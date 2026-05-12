using System.Text.Json;
using System.Text.Json.Serialization;
using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

// Loopolis SimulationRunner — the agent feedback loop tool
// Usage: dotnet run -- <ticks> [scenario] [--ascii]
//        dotnet run -- server <scenario> [--speed <n>]
// Output: JSON report for agent analysis, or ASCII city map

// ── Dispatch ────────────────────────────────────────────────────────────────

if (args.Length > 0 && args[0] == "server")
{
    var scenario     = args.Length > 1 ? args[1] : "default";
    var speedIndex   = Array.IndexOf(args, "--speed");
    var initialSpeed = speedIndex >= 0 && speedIndex + 1 < args.Length
        ? double.Parse(args[speedIndex + 1])
        : 1.0;

    RunServer(scenario, initialSpeed);
    return;
}

// ── Original CLI mode ────────────────────────────────────────────────────────

var ticks    = args.Length > 0 ? int.Parse(args[0]) : 500;
var cliScene = args.Length > 1 ? args[1] : "default";
var asciiMode = args.Contains("--ascii");

var (cliGrid, cliEngine) = SetupScenario(cliScene);
var budget     = cliEngine.Budget;
var population = cliEngine.Population;
var powerNetwork = cliEngine.PowerNetwork;

var tickHistory = new List<TickSnapshot>();

for (var tick = 0; tick < ticks; tick++)
{
    cliEngine.Tick();

    if (tick % 100 == 0 || tick == ticks - 1)
    {
        tickHistory.Add(new TickSnapshot(
            Tick: tick,
            Population: population.Population,
            Balance: Math.Round(budget.Balance, 2),
            IsInDeficit: budget.IsInDeficit,
            TaxIncome: Math.Round(budget.LastTaxIncome, 2),
            MaintenanceCost: Math.Round(budget.LastMaintenanceCost, 2),
            NetPerTick: Math.Round(budget.NetIncomePerTick, 2),
            AverageHappiness: Math.Round(cliEngine.HappinessSystem.AverageHappiness(cliGrid), 3),
            AveragePollution: Math.Round(cliEngine.PollutionSystem.AveragePollution(cliGrid), 3)
        ));
    }
}

// --- Output ---
if (asciiMode)
{
    AsciiRenderer.Render(cliGrid, budget, population, cliScene, ticks);
}
else
{
    var residentialTiles      = cliGrid.TilesOfType(ZoneType.Residential).ToList();
    var poweredResidential    = residentialTiles.Count(t => t.HasPower);
    var roadAccessResidential = residentialTiles.Count(t => t.HasRoadAccess);
    var readyResidential      = residentialTiles.Count(t => t.IsReadyToDevelop);

    var report = new SimulationReport(
        Scenario: cliScene,
        TotalTicks: ticks,
        FinalPopulation: population.Population,
        FinalBalance: Math.Round(budget.Balance, 2),
        Survived: !budget.IsInDeficit,
        ResidentialZones: residentialTiles.Count,
        PoweredResidentialZones: poweredResidential,
        RoadAccessResidentialZones: roadAccessResidential,
        ReadyResidentialZones: readyResidential,
        PoweredTiles: powerNetwork.PoweredTileCount,
        CommercialZones: cliGrid.TilesOfType(ZoneType.Commercial).Count(),
        IndustrialZones: cliGrid.TilesOfType(ZoneType.Industrial).Count(),
        AveragePollution: Math.Round(cliEngine.PollutionSystem.AveragePollution(cliGrid), 3),
        AverageHappiness: Math.Round(cliEngine.HappinessSystem.AverageHappiness(cliGrid), 3),
        GameState: cliEngine.MilestoneSystem.CurrentState.ToString(),
        MilestonesReached: cliEngine.MilestoneSystem.Reached.Select(m => $"{m.Name} {m.Emoji} (tick {m.ReachedAtTick})").ToList(),
        History: tickHistory
    );
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}

// ── Server mode ──────────────────────────────────────────────────────────────

static void RunServer(string scenario, double initialSpeed)
{
    var sharedDir  = Path.Combine(FindSolutionRoot(), "godot", "shared");
    Directory.CreateDirectory(sharedDir);

    var sessionId   = Guid.NewGuid().ToString("N")[..8]; // e.g. "a3f7c219"
    var stateFile   = Path.Combine(sharedDir, $"state-{sessionId}.json");
    var tmpPath     = Path.Combine(sharedDir, $"state-{sessionId}.tmp.json");
    var commandFile = Path.Combine(sharedDir, $"command-{sessionId}.json");

    Console.WriteLine($"[loopolis] session={sessionId}");

    var (grid, engine) = SetupScenario(scenario);

    var paused           = false;
    var speed            = initialSpeed;
    var skipRemaining    = 0;
    var skipRequested    = 0;    // total ticks requested in the last skip command
    var pauseAfterSkip   = false;
    var pauseOnEvent     = true;
    var skipPauseReason  = (string?)null;
    var recentEvents     = new List<string>();

    WriteState(tmpPath, stateFile, engine, grid, paused, sessionId, pauseReason: null, ticksRun: null, recentEvents: recentEvents);
    Console.WriteLine($"[server] Started. Scenario: {scenario}, Speed: {speed} t/s, Session: {sessionId}");
    Console.WriteLine($"[server] State: {stateFile}");
    Console.WriteLine($"[server] Commands: {commandFile}");

    try
    {
        while (true)
        {
            // 1. Read command
            var pausedBefore = paused;
            ProcessCommand(commandFile, sharedDir, tmpPath, stateFile, ref paused, ref speed,
                ref skipRemaining, ref skipRequested, ref pauseAfterSkip, ref pauseOnEvent,
                ref grid, ref engine, sessionId, recentEvents);
            // grid/engine may have been replaced by new_game — use current references below

            // If the game was just paused, immediately flush state so state.json reflects Paused: true
            if (!pausedBefore && paused)
                WriteState(tmpPath, stateFile, engine, grid, paused, sessionId, pauseReason: null, ticksRun: null, recentEvents: recentEvents);

            // 2. Tick (or skip, or wait)
            if (skipRemaining > 0)
            {
                // Fast-forward: no writes, no sleep
                var milestonesBefore = engine.MilestoneSystem.Reached.Count;
                engine.Tick();
                skipRemaining--;

                // Check early-pause conditions when pauseOnEvent is enabled
                if (pauseOnEvent)
                {
                    // Tier 2 passive events: log but don't pause
                    var passiveEvent = DetectPassiveEvent(engine);
                    if (passiveEvent != null && !recentEvents.Contains(passiveEvent))
                    {
                        recentEvents.Add(passiveEvent);
                        Console.WriteLine($"[skip] passive event (no pause): {passiveEvent} at tick {engine.TickCount}");
                    }

                    // Tier 1 actionable events: pause immediately
                    var earlyReason = DetectActionableSkipPauseReason(engine, grid, milestonesBefore);
                    if (earlyReason != null)
                    {
                        var ticksDone = skipRequested - skipRemaining;
                        skipPauseReason = earlyReason;
                        skipRemaining = 0;
                        Console.WriteLine(
                            $"[skip] paused early at tick {engine.TickCount}: {earlyReason} " +
                            $"(ran {ticksDone}/{skipRequested} ticks)");
                        paused = true;
                        pauseAfterSkip = false;
                        WriteState(tmpPath, stateFile, engine, grid, paused, sessionId,
                            pauseReason: earlyReason, ticksRun: ticksDone, recentEvents: recentEvents);
                        recentEvents = new List<string>();
                        skipPauseReason = null;
                        continue;
                    }
                }

                if (skipRemaining == 0)
                {
                    Console.WriteLine(
                        $"[skip] complete. Tick: {engine.TickCount}, Pop: {engine.Population.Population}, Balance: ${engine.Budget.Balance:N2}");
                    if (pauseAfterSkip)
                    {
                        paused = true;
                        pauseAfterSkip = false;
                    }
                    WriteState(tmpPath, stateFile, engine, grid, paused, sessionId,
                        pauseReason: null, ticksRun: skipRequested, recentEvents: recentEvents);
                    recentEvents = new List<string>();
                }
            }
            else if (!paused)
            {
                engine.Tick();
                WriteState(tmpPath, stateFile, engine, grid, paused, sessionId, pauseReason: null, ticksRun: null, recentEvents: recentEvents);
                var milestone = engine.MilestoneSystem.LatestMilestone;
                var milestoneTag = milestone != null ? $" [{milestone.Name} {milestone.Emoji}]" : "";
                Console.WriteLine(
                    $"[tick {engine.TickCount,4}] pop={engine.Population.Population} " +
                    $"happiness={engine.HappinessSystem.AverageHappiness(grid):F2} " +
                    $"pollution={engine.PollutionSystem.AveragePollution(grid):F2} " +
                    $"balance=${engine.Budget.Balance:N0} net=${engine.Budget.NetIncomePerTick:+0.#;-0.#}{milestoneTag}");
                var sleepMs = (int)(1000.0 / speed);
                Thread.Sleep(sleepMs);
            }
            else
            {
                // Paused — poll for commands at 20 Hz
                Thread.Sleep(50);
            }
        }
    }
    finally
    {
        // Clean up session state file on exit so Godot discovery does not pick up stale data
        if (File.Exists(stateFile)) File.Delete(stateFile);
        Console.WriteLine($"[server] Cleaned up {stateFile}");
    }
}

/// <summary>
/// Tier 1 — actionable events that require immediate player attention.
/// Returns a pause-reason string if the skip should stop early, or null to continue.
/// </summary>
static string? DetectActionableSkipPauseReason(SimulationEngine engine, CityGrid grid, int milestoneCountBefore)
{
    // Milestone reached this tick
    if (engine.MilestoneSystem.Reached.Count > milestoneCountBefore)
        return "MilestoneReached";

    // Actionable events: FireBreak and PowerOutage need immediate player attention
    if (engine.LatestEventBanner != null)
    {
        var eventType = engine.EventSystem.ActiveEvent?.Type;
        if (eventType == CityEventType.FireBreak || eventType == CityEventType.PowerOutage)
            return eventType.ToString();
    }

    // Bankruptcy warning: balance < 1 tick of total costs
    var oneTick = engine.Budget.LastMaintenanceCost;
    if (engine.Budget.Balance < oneTick && engine.Budget.Balance < 0)
        return "BankruptcyWarning";

    // Brownout: supply dropped below demand this tick
    if (engine.PowerCapacitySystem.IsBrownout)
        return "Brownout";

    // Abandonment warning: happiness dropping below threshold
    var avgHappiness = engine.HappinessSystem.AverageHappiness(grid);
    if (avgHappiness < 0.30)
        return "AbandonmentWarning";

    return null;
}

/// <summary>
/// Tier 2 — passive events that are informational only; no player action required.
/// Returns the event name if a passive event just fired, or null if nothing new.
/// </summary>
static string? DetectPassiveEvent(SimulationEngine engine)
{
    if (engine.LatestEventBanner == null) return null;
    var eventType = engine.EventSystem.ActiveEvent?.Type;
    if (eventType == CityEventType.DemandSlump || eventType == CityEventType.CrimeWave)
        return eventType.ToString();
    return null;
}

static void WriteOverlay(
    string sharedDir,
    string sessionId,
    SimulationEngine engine,
    CityGrid grid,
    string overlayType)
{
    var width  = grid.Width;
    var height = grid.Height;
    var tick   = engine.TickCount;

    // Pre-compute service tile list + radii (same logic as WriteState / HappinessSystem)
    var serviceRadii = new Dictionary<ZoneType, int>
    {
        { ZoneType.FireStation,   4 },
        { ZoneType.PoliceStation, 4 },
        { ZoneType.School,        5 },
        { ZoneType.PoliceHQ,     10 },
        { ZoneType.FireHQ,       10 },
        { ZoneType.Hospital,      8 },
    };
    var services = grid.AllTiles()
        .Where(t => serviceRadii.ContainsKey(t.Zone))
        .ToList();

    var overlayTiles = new List<OverlayTile>(width * height);

    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    {
        var tile = grid.GetTile(x, y);
        double value;

        switch (overlayType)
        {
            case "power":
                value = tile.HasPower ? 1.0 : 0.0;
                break;

            case "police":
            {
                // PoliceStation (radius 4) or PoliceHQ (radius 10) both count as police coverage
                var covered = services.Any(s =>
                    (s.Zone == ZoneType.PoliceStation || s.Zone == ZoneType.PoliceHQ) &&
                    Math.Abs(s.X - x) + Math.Abs(s.Y - y) <= serviceRadii[s.Zone]);
                value = covered ? 1.0 : 0.0;
                break;
            }

            case "fire":
            {
                // FireStation (radius 4) or FireHQ (radius 10) both count as fire coverage
                var covered = services.Any(s =>
                    (s.Zone == ZoneType.FireStation || s.Zone == ZoneType.FireHQ) &&
                    Math.Abs(s.X - x) + Math.Abs(s.Y - y) <= serviceRadii[s.Zone]);
                value = covered ? 1.0 : 0.0;
                break;
            }

            case "school":
            {
                var covered = services.Any(s =>
                    s.Zone == ZoneType.School &&
                    Math.Abs(s.X - x) + Math.Abs(s.Y - y) <= serviceRadii[ZoneType.School]);
                value = covered ? 1.0 : 0.0;
                break;
            }

            case "hospital":
            {
                var covered = services.Any(s =>
                    s.Zone == ZoneType.Hospital &&
                    Math.Abs(s.X - x) + Math.Abs(s.Y - y) <= serviceRadii[ZoneType.Hospital]);
                value = covered ? 1.0 : 0.0;
                break;
            }

            case "pollution":
                value = Math.Round(tile.PollutionLevel, 4);
                break;

            case "happiness":
                value = Math.Round(tile.Happiness, 4);
                break;

            case "traffic":
            {
                // Only road/avenue tiles have meaningful traffic load.
                // Value = TrafficLoad / overloadThreshold clamped to 0.0–1.0.
                // 1.0 means at capacity; tiles > threshold are clamped to 1.0 (overloaded).
                if (tile.Zone == ZoneType.Road || tile.Zone == ZoneType.Avenue)
                {
                    var threshold = tile.Zone == ZoneType.Avenue ? 16.0 : 8.0;
                    value = Math.Clamp(tile.TrafficLoad / threshold, 0.0, 1.0);
                    value = Math.Round(value, 4);
                }
                else
                {
                    value = 0.0;
                }
                break;
            }

            case "landvalue":
                // Non-water tiles only; sparse encoding (skip value=0).
                value = tile.Terrain != TerrainType.Water
                    ? Math.Round(tile.LandValue, 4)
                    : 0.0;
                break;

            default:
                value = 0.0;
                break;
        }

        // Sparse encoding: only emit tiles where value > 0
        if (value > 0.0)
            overlayTiles.Add(new OverlayTile(x, y, value));
    }

    // overlayTiles is sparse — zero-value tiles are omitted for compactness.
    // Readers should treat absent tiles as value=0.
    var overlayState = new OverlayState(overlayType, tick, width, height, overlayTiles);
    var options = new JsonSerializerOptions
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    var overlayJson = JsonSerializer.Serialize(overlayState, options);

    var overlayFile = Path.Combine(sharedDir, $"overlay-{sessionId}.json");
    var overlayTmp  = Path.Combine(sharedDir, $"overlay-{sessionId}.tmp.json");
    File.WriteAllText(overlayTmp, overlayJson);
    File.Move(overlayTmp, overlayFile, overwrite: true);

    Console.WriteLine($"[query_overlay] overlay={overlayType}, tick={tick}, tiles={overlayTiles.Count} (sparse, non-zero only), written to {overlayFile}");
}

static void ProcessCommand(
    string cmdPath,
    string sharedDir,
    string tmpPath,
    string statePath,
    ref bool paused,
    ref double speed,
    ref int skipRemaining,
    ref int skipRequested,
    ref bool pauseAfterSkip,
    ref bool pauseOnEvent,
    ref CityGrid grid,
    ref SimulationEngine engine,
    string sessionId,
    List<string> recentEvents)
{
    string json;
    try
    {
        if (!File.Exists(cmdPath)) return;
        json = File.ReadAllText(cmdPath);
        File.Delete(cmdPath);
    }
    catch (IOException)
    {
        return;
    }

    JsonDocument doc;
    try { doc = JsonDocument.Parse(json); }
    catch (JsonException) { return; }

    using (doc)
    {
        var root = doc.RootElement;

        // Session ID validation: if command carries a sessionId that doesn't match ours, ignore it
        if (root.TryGetProperty("sessionId", out var cmdSessionProp))
        {
            var cmdSession = cmdSessionProp.GetString();
            if (cmdSession != null && cmdSession != sessionId)
            {
                Console.WriteLine($"[command] Warning: session mismatch (got {cmdSession}, expected {sessionId}) — ignoring stale command.");
                return;
            }
        }

        if (!root.TryGetProperty("cmd", out var cmdProp)) return;
        var cmd = cmdProp.GetString() ?? "";

        switch (cmd)
        {
            case "pause":
                paused = true;
                Console.WriteLine($"[paused] Tick {engine.TickCount}");
                break;

            case "resume":
                paused = false;
                Console.WriteLine("[resumed]");
                break;

            case "place_zone":
                if (root.TryGetProperty("x", out var xProp) &&
                    root.TryGetProperty("y", out var yProp) &&
                    root.TryGetProperty("zone", out var zoneProp))
                {
                    var x    = xProp.GetInt32();
                    var y    = yProp.GetInt32();
                    var zone = zoneProp.GetString() ?? "";
                    if (!grid.IsInBounds(x, y))
                    {
                        var errMsg = $"Placement out of bounds: ({x},{y}) is outside grid ({grid.Width}x{grid.Height})";
                        Console.WriteLine($"[place_zone] {errMsg}");
                        WriteStateWithError(tmpPath, statePath, engine, grid, paused, sessionId, errMsg, recentEvents);
                        break;
                    }
                    var placementCost = BudgetSystem.PlacementCosts.GetValueOrDefault(zone, 0.0);
                    if (!engine.Budget.CanAfford(placementCost))
                    {
                        Console.WriteLine($"[place_zone] insufficient_funds: need ${placementCost:N0}, have ${engine.Budget.Balance:N0}");
                        break;
                    }
                    if (Enum.TryParse<ZoneType>(zone, out var zoneType))
                    {
                        // Reject before charging if tile is occupied and this is not an erase
                        if (zoneType != ZoneType.Empty && grid.GetTile(x, y).Zone != ZoneType.Empty)
                        {
                            Console.WriteLine($"[place_zone] ({x},{y}) occupied by {grid.GetTile(x,y).Zone} — use erase first");
                            break;
                        }
                        // Milestone gate: PoliceHQ, FireHQ, Hospital require City milestone
                        var (allowed, gateError) = engine.MilestoneSystem.CanPlace(zoneType, engine.Population.Population);
                        if (!allowed)
                        {
                            Console.WriteLine($"[place_zone] milestone_gate: {gateError}");
                            WriteStateWithError(tmpPath, statePath, engine, grid, paused, sessionId, gateError!, recentEvents);
                            break;
                        }
                        engine.Budget.Charge(placementCost);
                        grid.SetZone(x, y, zoneType);
                        Console.WriteLine($"[place_zone] ({x},{y}) => {zoneType} (cost: ${placementCost:N0})");
                    }
                    else
                    {
                        Console.WriteLine($"[place_zone] Unknown zone type: {zone}");
                    }
                }
                break;

            case "erase":
                if (root.TryGetProperty("x", out var exProp) &&
                    root.TryGetProperty("y", out var eyProp))
                {
                    var x = exProp.GetInt32();
                    var y = eyProp.GetInt32();
                    if (!grid.IsInBounds(x, y))
                    {
                        var errMsg = $"Placement out of bounds: ({x},{y}) is outside grid ({grid.Width}x{grid.Height})";
                        Console.WriteLine($"[erase] {errMsg}");
                        WriteStateWithError(tmpPath, statePath, engine, grid, paused, sessionId, errMsg, recentEvents);
                        break;
                    }
                    grid.SetZone(x, y, ZoneType.Empty);
                    Console.WriteLine($"[erase] ({x},{y}) => Empty");
                }
                break;

            case "place_rect":
                if (root.TryGetProperty("x1", out var rx1Prop) &&
                    root.TryGetProperty("y1", out var ry1Prop) &&
                    root.TryGetProperty("x2", out var rx2Prop) &&
                    root.TryGetProperty("y2", out var ry2Prop) &&
                    root.TryGetProperty("zone", out var rzProp))
                {
                    var rZone = rzProp.GetString() ?? "";
                    // Normalise so x1 ≤ x2, y1 ≤ y2
                    var rx1 = Math.Min(rx1Prop.GetInt32(), rx2Prop.GetInt32());
                    var rx2 = Math.Max(rx1Prop.GetInt32(), rx2Prop.GetInt32());
                    var ry1 = Math.Min(ry1Prop.GetInt32(), ry2Prop.GetInt32());
                    var ry2 = Math.Max(ry1Prop.GetInt32(), ry2Prop.GetInt32());

                    if (!Enum.TryParse<ZoneType>(rZone, out var rZoneType))
                    {
                        Console.WriteLine($"[place_rect] Unknown zone type: {rZone}");
                        break;
                    }

                    var placedCount       = 0;
                    var skippedOob        = 0;
                    var skippedMilestone  = 0;
                    var skippedOccupied   = 0;
                    var skippedFunds      = 0;
                    var placementCost     = BudgetSystem.PlacementCosts.GetValueOrDefault(rZone, 0.0);

                    for (var ry = ry1; ry <= ry2; ry++)
                    for (var rx = rx1; rx <= rx2; rx++)
                    {
                        // Out-of-bounds: skip but continue
                        if (!grid.IsInBounds(rx, ry)) { skippedOob++; continue; }

                        // Occupied tile: skip
                        if (rZoneType != ZoneType.Empty && grid.GetTile(rx, ry).Zone != ZoneType.Empty)
                        { skippedOccupied++; continue; }

                        // Milestone gate
                        var (rAllowed, _) = engine.MilestoneSystem.CanPlace(rZoneType, engine.Population.Population);
                        if (!rAllowed) { skippedMilestone++; continue; }

                        // Funds check per tile
                        if (!engine.Budget.CanAfford(placementCost)) { skippedFunds++; continue; }

                        engine.Budget.Charge(placementCost);
                        grid.SetZone(rx, ry, rZoneType);
                        placedCount++;
                    }

                    var rectMsg = $"place_rect placed {placedCount} tiles ({rZone}) in ({rx1},{ry1})–({rx2},{ry2})";
                    var warnings = new List<string>();
                    if (skippedOob       > 0) warnings.Add($"{skippedOob} tiles skipped (out of bounds)");
                    if (skippedMilestone > 0) warnings.Add($"{skippedMilestone} tiles skipped (milestone gate)");
                    if (skippedOccupied  > 0) warnings.Add($"{skippedOccupied} tiles skipped (occupied)");
                    if (skippedFunds     > 0) warnings.Add($"{skippedFunds} tiles skipped (insufficient funds)");
                    var warnStr = warnings.Count > 0 ? " | warnings: " + string.Join("; ", warnings) : "";
                    Console.WriteLine($"[place_rect] {rectMsg}{warnStr}");
                    WriteState(tmpPath, statePath, engine, grid, paused, sessionId,
                        pauseReason: null, ticksRun: null, recentEvents: recentEvents,
                        lastCommand: rectMsg + (warnings.Count > 0 ? " | " + string.Join("; ", warnings) : null));
                }
                break;

            case "erase_rect":
                if (root.TryGetProperty("x1", out var erx1Prop) &&
                    root.TryGetProperty("y1", out var ery1Prop) &&
                    root.TryGetProperty("x2", out var erx2Prop) &&
                    root.TryGetProperty("y2", out var ery2Prop))
                {
                    // Normalise so x1 ≤ x2, y1 ≤ y2
                    var erx1 = Math.Min(erx1Prop.GetInt32(), erx2Prop.GetInt32());
                    var erx2 = Math.Max(erx1Prop.GetInt32(), erx2Prop.GetInt32());
                    var ery1 = Math.Min(ery1Prop.GetInt32(), ery2Prop.GetInt32());
                    var ery2 = Math.Max(ery1Prop.GetInt32(), ery2Prop.GetInt32());

                    var erasedCount = 0;
                    var erSkippedOob = 0;

                    for (var ery = ery1; ery <= ery2; ery++)
                    for (var erx = erx1; erx <= erx2; erx++)
                    {
                        if (!grid.IsInBounds(erx, ery)) { erSkippedOob++; continue; }
                        grid.SetZone(erx, ery, ZoneType.Empty);
                        erasedCount++;
                    }

                    var eraseMsg = $"erase_rect erased {erasedCount} tiles in ({erx1},{ery1})–({erx2},{ery2})";
                    var erWarnStr = erSkippedOob > 0 ? $" | {erSkippedOob} tiles skipped (out of bounds)" : "";
                    Console.WriteLine($"[erase_rect] {eraseMsg}{erWarnStr}");
                    WriteState(tmpPath, statePath, engine, grid, paused, sessionId,
                        pauseReason: null, ticksRun: null, recentEvents: recentEvents,
                        lastCommand: eraseMsg + (erSkippedOob > 0 ? $" | {erSkippedOob} tiles skipped (out of bounds)" : null));
                }
                break;

            case "new_game":
            {
                var newGrid   = new CityGrid(32, 32);
                var newBudget = new BudgetSystem(); // uses default $4,000
                var newPop    = new PopulationSystem();
                var newPower  = new PowerNetwork();
                var newRoads  = new RoadNetwork();
                var newDemand = new DemandSystem();
                grid   = newGrid;
                engine = new SimulationEngine(newGrid, newBudget, newPop, newPower, newRoads, newDemand);
                Console.WriteLine("[new_game] Reset to empty 32x32 grid, $4000 starting balance.");
                break;
            }

            case "set_speed":
                if (root.TryGetProperty("ticksPerSecond", out var tpsProp))
                {
                    speed = tpsProp.GetDouble();
                    Console.WriteLine($"[set_speed] {speed} t/s");
                }
                break;

            case "skip":
                if (root.TryGetProperty("ticks", out var ticksProp))
                {
                    var count = ticksProp.GetInt32();
                    skipRemaining  = count;
                    skipRequested  = count;
                    pauseAfterSkip = root.TryGetProperty("pauseAfter", out var paProp) && paProp.GetBoolean();
                    // pauseOnEvent defaults to true when absent
                    pauseOnEvent   = !root.TryGetProperty("pauseOnEvent", out var poeProp) || poeProp.GetBoolean();
                    Console.WriteLine($"[skip] Starting {count} ticks... pauseOnEvent={pauseOnEvent}");
                }
                break;

            case "set_tax":
                if (root.TryGetProperty("level", out var levelProp))
                {
                    var level = levelProp.GetString() ?? "normal";
                    engine.Budget.SetTaxRate(level);
                    Console.WriteLine($"[set_tax] level={level} taxRate={engine.Budget.TaxRate:P0} modifier={engine.Budget.TaxModifier:+0.00;-0.00;0.00}");
                }
                break;

            case "query_overlay":
                if (root.TryGetProperty("overlay", out var overlayProp))
                {
                    var overlayType = overlayProp.GetString() ?? "";
                    WriteOverlay(sharedDir, sessionId, engine, grid, overlayType);
                }
                else
                {
                    Console.WriteLine("[query_overlay] Missing 'overlay' property");
                }
                break;

            default:
                Console.WriteLine($"[command] Unknown: {cmd}");
                break;
        }
    }
}

static void WriteStateWithError(
    string tmpPath,
    string statePath,
    SimulationEngine engine,
    CityGrid grid,
    bool paused,
    string sessionId,
    string errorMessage,
    List<string> recentEvents)
{
    // Write state with an error field so the caller can surface what went wrong
    WriteState(tmpPath, statePath, engine, grid, paused, sessionId,
        pauseReason: null, ticksRun: null, recentEvents: recentEvents, error: errorMessage);
}

static void WriteState(
    string tmpPath,
    string statePath,
    SimulationEngine engine,
    CityGrid grid,
    bool paused,
    string sessionId = "",
    string? pauseReason = null,
    int? ticksRun = null,
    List<string>? recentEvents = null,
    string? error = null,
    string? lastCommand = null)
{
    // Build a lookup from buildingId → typeId for tile population
    var buildingTypeLookup = grid.Buildings.ToDictionary(
        kvp => kvp.Key,
        kvp => kvp.Value.TypeId);

    var nonEmptyTiles = grid.AllTiles()
        .Where(t => t.Zone != ZoneType.Empty || t.Terrain != TerrainType.Flat)
        .Select(t => new TileState(
            t.X, t.Y, t.Zone.ToString(), t.HasPower, t.HasRoadAccess,
            t.Zone == ZoneType.Residential ? grid.GetPopulation(t.X, t.Y) : 0,
            Math.Round(t.PollutionLevel, 3),
            Math.Round(t.Happiness, 3),
            t.HasDemandBoost,
            t.BuildingId,
            t.BuildingId != null ? buildingTypeLookup.GetValueOrDefault(t.BuildingId) : null,
            t.TrafficLoad,
            t.Terrain != TerrainType.Flat ? t.Terrain.ToString() : null))
        .ToList();

    // --- Enriched building info ---
    // Sum tile populations per building and look up capacity from catalog
    var buildingTilePopulations = new Dictionary<string, int>();
    foreach (var tile in grid.AllTiles())
    {
        if (tile.BuildingId == null || tile.Zone != ZoneType.Residential) continue;
        buildingTilePopulations.TryGetValue(tile.BuildingId, out var existing);
        buildingTilePopulations[tile.BuildingId] = existing + grid.GetPopulation(tile.X, tile.Y);
    }

    var enrichedBuildings = grid.Buildings.Values
        .Select(b =>
        {
            var typeDef = BuildingCatalog.Find(b.TypeId);
            var capacity = typeDef?.MaxPopulation ?? (b.TileCount * 50);
            buildingTilePopulations.TryGetValue(b.Id, out var pop);
            return new BuildingStateInfo(b.TypeId, b.AnchorX, b.AnchorY, b.Width, b.Height, pop, capacity);
        })
        .ToArray();

    // Building summary: typeId → count
    var buildingSummary = enrichedBuildings
        .GroupBy(b => b.TypeId)
        .ToDictionary(g => g.Key, g => g.Count());

    var residentialCount = grid.TilesOfType(ZoneType.Residential).Count();
    var maxCapacity = residentialCount * 50;
    var milestone = engine.MilestoneSystem.LatestMilestone;

    // Compute next milestone: find first threshold > current population
    var currentPop = engine.Population.Population;
    (string Name, string Emoji, int Target)[] milestoneThresholds =
    {
        ("Town",       "🥉", 500),
        ("City",       "🥈", 5_000),
        ("Metropolis", "🥇", 25_000),
        ("Loopolis",   "🏆", 100_000),
    };
    var nextMilestoneData = milestoneThresholds.FirstOrDefault(m => m.Target > currentPop);
    var nextMilestoneName   = nextMilestoneData != default ? $"{nextMilestoneData.Name} {nextMilestoneData.Emoji}" : null;
    var nextMilestoneTarget = nextMilestoneData != default ? nextMilestoneData.Target : 0;

    // --- Happiness breakdown (average across all ready residential tiles) ---
    var readyResidential = grid.TilesOfType(ZoneType.Residential)
        .Where(t => t.IsReadyToDevelop).ToList();

    double avgServiceCoverage    = 0;
    double avgNeglectDecay       = 0;
    if (readyResidential.Count > 0)
    {
        // Service coverage contribution: each covered service category adds +0.15, max 2 categories
        // PoliceHQ counts as PoliceStation, FireHQ counts as FireStation
        var services = grid.AllTiles()
            .Where(t => t.Zone is ZoneType.FireStation or ZoneType.PoliceStation or ZoneType.School
                             or ZoneType.PoliceHQ or ZoneType.FireHQ or ZoneType.Hospital)
            .ToList();
        var serviceRadii = new Dictionary<ZoneType, int>
        {
            { ZoneType.FireStation,   4 },
            { ZoneType.PoliceStation, 4 },
            { ZoneType.School,        5 },
            { ZoneType.PoliceHQ,     10 },
            { ZoneType.FireHQ,       10 },
            { ZoneType.Hospital,      8 },
        };
        static ZoneType ServiceCat(ZoneType z) => z switch
        {
            ZoneType.PoliceHQ => ZoneType.PoliceStation,
            ZoneType.FireHQ   => ZoneType.FireStation,
            _                 => z,
        };

        foreach (var tile in readyResidential)
        {
            var coveredCategories = new HashSet<ZoneType>();
            foreach (var svc in services)
            {
                var dist = Math.Abs(svc.X - tile.X) + Math.Abs(svc.Y - tile.Y);
                if (serviceRadii.TryGetValue(svc.Zone, out var radius) && dist <= radius)
                    coveredCategories.Add(ServiceCat(svc.Zone));
            }
            avgServiceCoverage += Math.Min(coveredCategories.Count, 2) * 0.15;
            avgNeglectDecay    += engine.HappinessSystem.GetNeglect(tile.X, tile.Y);
        }
        avgServiceCoverage    /= readyResidential.Count;
        avgNeglectDecay       /= readyResidential.Count;
    }

    // Average commute penalty: sum of per-tile commute penalties / count of developed residential tiles
    var avgCommutePenalty = engine.HappinessSystem.AverageCommutePenalty(grid, currentPop);

    var happinessBreakdown = new HappinessBreakdown(
        ServiceCoverage:     Math.Round(avgServiceCoverage, 4),
        TaxModifier:         Math.Round(engine.Budget.TaxModifier, 4),
        UnemploymentPenalty: 0.0,   // employment affects growth rate, not happiness directly
        EventPenalty:        Math.Round(engine.EventSystem.HappinessPenalty, 4),
        NeglectDecay:        Math.Round(-avgNeglectDecay, 4),
        CommutePenalty:      Math.Round(avgCommutePenalty, 4)
    );

    // --- Coverage summary (power + services + pollution + happiness across all zoned tiles) ---
    var zonedTiles       = grid.AllTiles().Where(t => t.Zone is ZoneType.Residential or ZoneType.Commercial or ZoneType.Industrial).ToList();
    var poweredZoned     = zonedTiles.Count(t => t.HasPower);
    var unpoweredZoned   = zonedTiles.Count - poweredZoned;

    // Pre-compute service tiles and radii for coverage percentage
    // PoliceHQ (radius 10) counts as police coverage; FireHQ (radius 10) counts as fire coverage
    var covServiceRadii = new Dictionary<ZoneType, int>
    {
        { ZoneType.FireStation,   4 },
        { ZoneType.PoliceStation, 4 },
        { ZoneType.School,        5 },
        { ZoneType.PoliceHQ,     10 },
        { ZoneType.FireHQ,       10 },
        { ZoneType.Hospital,      8 },
    };
    var covServices = grid.AllTiles()
        .Where(t => covServiceRadii.ContainsKey(t.Zone))
        .ToList();

    int policeCovered = 0, fireCovered = 0, schoolCovered = 0, hospitalCovered = 0;
    double totalPollution = 0, totalHappiness = 0;
    foreach (var zt in zonedTiles)
    {
        if (covServices.Any(s => (s.Zone == ZoneType.PoliceStation || s.Zone == ZoneType.PoliceHQ)
                                 && Math.Abs(s.X - zt.X) + Math.Abs(s.Y - zt.Y) <= covServiceRadii[s.Zone]))
            policeCovered++;
        if (covServices.Any(s => (s.Zone == ZoneType.FireStation || s.Zone == ZoneType.FireHQ)
                                 && Math.Abs(s.X - zt.X) + Math.Abs(s.Y - zt.Y) <= covServiceRadii[s.Zone]))
            fireCovered++;
        if (covServices.Any(s => s.Zone == ZoneType.School
                                 && Math.Abs(s.X - zt.X) + Math.Abs(s.Y - zt.Y) <= covServiceRadii[ZoneType.School]))
            schoolCovered++;
        if (covServices.Any(s => s.Zone == ZoneType.Hospital
                                 && Math.Abs(s.X - zt.X) + Math.Abs(s.Y - zt.Y) <= covServiceRadii[ZoneType.Hospital]))
            hospitalCovered++;
        totalPollution  += zt.PollutionLevel;
        totalHappiness  += zt.Happiness;
    }
    var zonedCount = zonedTiles.Count;
    var coverageSummary = new CoverageSummary(
        PoweredZonedTilesCount:   poweredZoned,
        UnpoweredZonedTilesCount: unpoweredZoned,
        PoliceCoveragePercent:    zonedCount > 0 ? Math.Round((double)policeCovered    / zonedCount, 4) : 0.0,
        FireCoveragePercent:      zonedCount > 0 ? Math.Round((double)fireCovered      / zonedCount, 4) : 0.0,
        SchoolCoveragePercent:    zonedCount > 0 ? Math.Round((double)schoolCovered    / zonedCount, 4) : 0.0,
        HospitalCoveragePercent:  zonedCount > 0 ? Math.Round((double)hospitalCovered  / zonedCount, 4) : 0.0,
        AvgPollution:             zonedCount > 0 ? Math.Round(totalPollution  / zonedCount, 4) : 0.0,
        AvgHappiness:             zonedCount > 0 ? Math.Round(totalHappiness  / zonedCount, 4) : 0.0,
        OverloadedRoadCount:      engine.RoadTrafficSystem.OverloadedRoadCount,
        AvgTrafficLoad:           Math.Round(engine.RoadTrafficSystem.AvgTrafficLoad, 4),
        LandValueAvg:             Math.Round(engine.LandValueSystem.AverageLandValue(grid), 4),
        LandValueMax:             Math.Round(engine.LandValueSystem.MaxLandValue(grid), 4)
    );

    // --- Employment ---
    var employmentState = new EmploymentState(
        Jobs:             engine.EmploymentSystem.AvailableJobs,
        Workers:          engine.EmploymentSystem.RequiredJobs,
        UnemploymentRate: Math.Round(1.0 - engine.EmploymentSystem.EmploymentRatio, 3)
    );

    // --- Next milestone object ---
    var nextMilestoneInfo = nextMilestoneData != default
        ? new NextMilestoneInfo(
            Name:                nextMilestoneData.Name,
            RequiredPopulation:  nextMilestoneData.Target,
            CurrentPopulation:   currentPop)
        : null;

    var activeEvent = engine.EventSystem.ActiveEvent;

    // --- Power capacity ---
    var pcs = engine.PowerCapacitySystem;
    var powerState = new PowerState(
        SupplyMW:      pcs.TotalSupplyMW,
        DemandMW:      pcs.TotalDemandMW,
        CapacityRatio: Math.Round(pcs.CapacityRatio, 4),
        IsBrownout:    pcs.IsBrownout);

    var state = new ServerState(
        Tick:                      engine.TickCount,
        Paused:                    paused,
        Population:                currentPop,
        MaxCapacity:               maxCapacity,
        Balance:                   Math.Round(engine.Budget.Balance, 2),
        TaxPerTick:                Math.Round(engine.Budget.LastTaxIncome, 2),
        CommercialIncomePerTick:   Math.Round(engine.Budget.CommercialIncomePerTick, 2),
        MaintenancePerTick:        Math.Round(engine.Budget.LastMaintenanceCost, 2),
        NetPerTick:                Math.Round(engine.Budget.NetIncomePerTick, 2),
        Happiness:                 Math.Round(engine.HappinessSystem.AverageHappiness(grid), 3),
        MilestoneReached:          milestone?.Name,
        Pollution:                 Math.Round(engine.PollutionSystem.AveragePollution(grid), 3),
        GameState:                 engine.MilestoneSystem.CurrentState.ToString(),
        Milestones:                engine.MilestoneSystem.Reached.Select(m => $"{m.Name} {m.Emoji} (tick {m.ReachedAtTick})").ToList(),
        Tiles:                     nonEmptyTiles,
        BuildingList:              enrichedBuildings,
        BuildingSummary:           buildingSummary,
        NextMilestoneName:         nextMilestoneName,
        NextMilestoneTarget:       nextMilestoneTarget,
        NextMilestone:             nextMilestoneInfo,
        ActiveEventName:           activeEvent?.Name,
        ActiveEventDescription:    activeEvent?.Description,
        LatestEventBanner:         engine.LatestEventBanner,
        TaxModifier:               engine.Budget.TaxModifier,
        SessionId:                 sessionId.Length > 0 ? sessionId : null,
        AvailableJobs:             engine.EmploymentSystem.AvailableJobs,
        RequiredJobs:              engine.EmploymentSystem.RequiredJobs,
        EmploymentRatio:           Math.Round(engine.EmploymentSystem.EmploymentRatio, 3),
        EventHappinessPenalty:     engine.EventSystem.HappinessPenalty,
        HappinessBreakdown:        happinessBreakdown,
        Employment:                employmentState,
        CoverageSummary:           coverageSummary,
        PauseReason:               pauseReason,
        TicksRun:                  ticksRun,
        RecentEvents:              recentEvents ?? new List<string>(),
        Error:                     error,
        Power:                     powerState,
        LastCommand:               lastCommand
    );

    var options = new JsonSerializerOptions
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
    };
    var json = JsonSerializer.Serialize(state, options);
    File.WriteAllText(tmpPath, json);
    File.Move(tmpPath, statePath, overwrite: true);
}

static string FindSolutionRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new InvalidOperationException(
        "Could not find solution root (no .slnx/.sln found walking up from AppContext.BaseDirectory)");
}

// ── Scenario setup (shared between CLI and server mode) ──────────────────────

static (CityGrid grid, SimulationEngine engine) SetupScenario(string scenario)
{
    var grid       = new CityGrid(32, 32);
    var budget     = new BudgetSystem(); // default $4,000 starting balance
    var population = new PopulationSystem();
    var power      = new PowerNetwork();
    var roads      = new RoadNetwork();
    var demand     = new DemandSystem();

    switch (scenario)
    {
        case "no_power":
            // Zones with roads but no power → no growth
            grid.SetZone(5, 5, ZoneType.Road);
            grid.SetZone(6, 5, ZoneType.Residential);
            grid.SetZone(7, 5, ZoneType.Residential);
            grid.SetZone(8, 5, ZoneType.Residential);
            break;

        case "no_roads":
            // Zones with power but no road access → no growth
            grid.SetZone(5, 5, ZoneType.PowerPlant);
            grid.SetZone(6, 5, ZoneType.PowerLine);
            grid.SetZone(7, 5, ZoneType.Residential);  // powered but no road adjacent
            grid.SetZone(7, 6, ZoneType.Residential);
            grid.SetZone(7, 7, ZoneType.Residential);
            break;

        case "town":
            // Roads form a cross
            for (var x = 5; x <= 25; x++) grid.SetZone(x, 15, ZoneType.Road);
            for (var y = 5; y <= 25; y++) grid.SetZone(15, y, ZoneType.Road);
            // Power plant + power line running all the way to the vertical road
            grid.SetZone(8, 8, ZoneType.PowerPlant);
            for (var x = 9; x <= 15; x++) grid.SetZone(x, 8, ZoneType.PowerLine);
            // Residential blocks (top-left quadrant, touching the vertical road at x=15)
            for (var x = 6; x <= 14; x++)
            for (var y = 10; y <= 14; y++)
                if (grid.GetTile(x, y).Zone == ZoneType.Empty)
                    grid.SetZone(x, y, ZoneType.Residential);
            // Commercial strip along horizontal road (right side)
            for (var x = 16; x <= 24; x++)
                grid.SetZone(x, 14, ZoneType.Commercial);
            // Industrial bottom-right quadrant — start at y=16 so it touches the road at y=15
            for (var x = 17; x <= 24; x++)
            for (var y = 16; y <= 24; y++)
                grid.SetZone(x, y, ZoneType.Industrial);
            break;

        case "mixed":
            // Residential mixed with industrial nearby — tests pollution penalty on growth.
            //
            // Layout (32x32 grid):
            //   Power plant at (5,5), power line runs east to vertical road at x=15
            //   Main road: vertical at x=15 (y=3..28), horizontal at y=15 (x=5..28)
            //   Industrial block: south section (x=16..24, y=17..24) — near the road
            //   Residential north: safe distance from industrial (y=5..13)
            //   Residential polluted: immediately adjacent to industrial block (x=16..18, y=16 — one row away)
            //
            // Expected: northern R zones grow at base happiness; southern R zones near industrial
            //           suffer heavy pollution penalty → much slower growth

            // Power
            grid.SetZone(5, 5, ZoneType.PowerPlant);
            for (var x = 6; x <= 15; x++) grid.SetZone(x, 5, ZoneType.PowerLine);

            // Roads: vertical at x=15, horizontal at y=15
            for (var y = 3; y <= 28; y++) grid.SetZone(15, y, ZoneType.Road);
            for (var x = 5; x <= 28; x++) grid.SetZone(x, 15, ZoneType.Road);

            // Industrial block: south-east (y=17..24)
            for (var x = 16; x <= 24; x++)
            for (var y = 17; y <= 24; y++)
                grid.SetZone(x, y, ZoneType.Industrial);

            // Residential north (safe from pollution — ~8+ tiles from nearest industrial)
            for (var x = 6; x <= 14; x++)
            for (var y = 6; y <= 13; y++)
                if (grid.GetTile(x, y).Zone == ZoneType.Empty)
                    grid.SetZone(x, y, ZoneType.Residential);

            // Polluted residential: just north of industrial, touching the road at y=15 (y=16 but road is there)
            // Place them at y=14 (one tile north of road), adjacent to industrial via road gap
            // Actually: industrial starts at y=17, so at y=16 is still fine; road is at y=15
            // R at y=14 is 3 tiles from industrial (y=17), which is right at the pollution radius edge
            // For stronger effect, place some R adjacent to x=15 road at y=16 (just below road)
            for (var x = 16; x <= 18; x++)
                grid.SetZone(x, 16, ZoneType.Residential); // just 1 tile from industrial at y=17

            break;

        case "services":
            // City with school and fire station — tests service coverage happiness bonus.
            //
            // Layout:
            //   Power plant, roads forming a grid
            //   Residential blocks — some covered by services, some not
            //   One school covering the north block
            //   One fire station covering the east block
            //
            // Expected: covered zones reach happiness ~0.75+, uncovered zones stay at 0.6

            // Power
            grid.SetZone(5, 5, ZoneType.PowerPlant);
            for (var x = 6; x <= 20; x++) grid.SetZone(x, 5, ZoneType.PowerLine);

            // Roads: horizontal at y=10 and y=20, vertical at x=10 and x=20
            for (var x = 5; x <= 26; x++) grid.SetZone(x, 10, ZoneType.Road);
            for (var x = 5; x <= 26; x++) grid.SetZone(x, 20, ZoneType.Road);
            for (var y = 10; y <= 20; y++) grid.SetZone(10, y, ZoneType.Road);
            for (var y = 10; y <= 20; y++) grid.SetZone(20, y, ZoneType.Road);

            // Residential: north block (y=6..9), south block (y=21..24)
            for (var x = 11; x <= 19; x++)
            for (var y = 6; y <= 9; y++)
                if (grid.GetTile(x, y).Zone == ZoneType.Empty)
                    grid.SetZone(x, y, ZoneType.Residential);

            for (var x = 11; x <= 19; x++)
            for (var y = 21; y <= 24; y++)
                if (grid.GetTile(x, y).Zone == ZoneType.Empty)
                    grid.SetZone(x, y, ZoneType.Residential);

            // School: covers north block (Manhattan radius 5 from center of north block ~(15,7))
            grid.SetZone(15, 12, ZoneType.School); // within 5 of all north residential

            // Fire station: covers south block
            grid.SetZone(15, 18, ZoneType.FireStation); // within 4 of south residential at y=21

            // Commercial strip on the east road
            for (var y = 11; y <= 19; y++)
                grid.SetZone(21, y, ZoneType.Commercial);

            break;

        case "city_path":
            // Compact mixed-use foundation designed to reach City milestone (5,000 pop).
            //
            // Layout (32x32 grid):
            //   CoalPlant at (2,15) — moved west so nearest residential (7,14) is Euclidean ≈ 5.1 tiles away
            //                         (safely outside pollution radius 3).
            //   Main E-W road:  y=15, x=3..24 (22 tiles — extended to keep plant road-adjacent)
            //   North spur:     x=16, y=11..14 (4 tiles, branches off main road)
            //
            //   Residential west row  (y=14, x=7..15):  9 tiles — adjacent to main road at y=15
            //   Residential east col  (x=17, y=11..14): 4 tiles — adjacent to north spur at x=16
            //   Total: 13 residential tiles, capacity 650 pop, all road-accessible and powered.
            //
            //   Commercial   (y=16, x=9..12):  4 tiles — south of road, Chebyshev-3 demand boost to west residential
            //   Industrial   (y=16, x=7):      1 tile  — adjacent to road, covers west residential (commute ≤ 8)
            //   Industrial   (y=16, x=16):     1 tile  — adjacent to road, covers east residential (commute ≤ 6)
            //
            //   Commute distances (all ≤ 8, penalty = 0):
            //     (7,14)→(7,16):   0+2=2  ✓     (13,14)→nearest: min(6+2=8, 3+2=5)=5  ✓
            //     (15,14)→(16,16): 1+2=3  ✓     (17,11)→(16,16): 1+5=6  ✓
            //
            //   Services:
            //     FireStation   at (8,16)   — radius 4: covers residential x=7..12, y=14
            //     PoliceStation at (14,16)  — radius 4: covers residential x=10..18, y=12..14
            //     School        at (16,10)  — adjacent to north spur at (16,11); radius 5: covers x=17,y=11..14 + x=11..16,y=14

            // Power — plant at (2,15), road starts at (3,15) immediately adjacent
            grid.SetZone(2, 15, ZoneType.CoalPlant);

            // Roads
            for (var x = 3; x <= 24; x++) grid.SetZone(x, 15, ZoneType.Road);    // main E-W spine
            for (var y = 11; y <= 14; y++) grid.SetZone(16, y, ZoneType.Road);    // north spur

            // Residential — west row (road access via y=15 main road)
            for (var x = 7; x <= 15; x++) grid.SetZone(x, 14, ZoneType.Residential);

            // Residential — east column (road access via x=16 north spur)
            for (var y = 11; y <= 14; y++) grid.SetZone(17, y, ZoneType.Residential);

            // Commercial south of main road — Chebyshev-3 demand boost to west + center residential
            for (var x = 9; x <= 12; x++) grid.SetZone(x, 16, ZoneType.Commercial);

            // Industrial — two tiles placed so every residential tile is within 8 Manhattan distance
            // (7,16): covers west residential; (16,16): covers east residential + center
            grid.SetZone(7,  16, ZoneType.Industrial);
            grid.SetZone(16, 16, ZoneType.Industrial);

            // Services
            grid.SetZone(8,  16, ZoneType.FireStation);    // covers west residential cluster (y=15 road adjacent)
            grid.SetZone(14, 16, ZoneType.PoliceStation);  // covers center + east residential (y=15 road adjacent)
            grid.SetZone(16, 10, ZoneType.School);         // covers east column + center row (x=16 spur adjacent at (16,11))
            break;

        default:
            // Teaching layout: plant at west end → road runs east → homes on north side → shops + factory south
            // Coal plant at (5,12), road x=6..14 y=12, residential y=11 x=9..12, commercial y=13 x=9..10,
            // industrial y=13 x=6..7.
            // Plant→nearest residential (9,11): Euclidean ≈ 4.12 (> pollution radius 3) — no pollution on start zones.
            // Industrial (7,13)→residential (9,11): Manhattan 4 — well within commute range.
            grid.SetZone(5, 12, ZoneType.CoalPlant);
            for (var x = 6; x <= 14; x++) grid.SetZone(x, 12, ZoneType.Road);
            // Residential north of road
            grid.SetZone(9,  11, ZoneType.Residential);
            grid.SetZone(10, 11, ZoneType.Residential);
            grid.SetZone(11, 11, ZoneType.Residential);
            grid.SetZone(12, 11, ZoneType.Residential);
            // Commercial south of road (demand boost for residential — Chebyshev ≤ 3)
            grid.SetZone(9,  13, ZoneType.Commercial);
            grid.SetZone(10, 13, ZoneType.Commercial);
            // Industrial near plant, south of road
            grid.SetZone(6,  13, ZoneType.Industrial);
            grid.SetZone(7,  13, ZoneType.Industrial);
            break;
    }

    var engine = new SimulationEngine(grid, budget, population, power, roads, demand);
    return (grid, engine);
}

// ── Records ──────────────────────────────────────────────────────────────────

record TickSnapshot(
    int Tick,
    int Population,
    double Balance,
    bool IsInDeficit,
    double TaxIncome,
    double MaintenanceCost,
    double NetPerTick,
    double AverageHappiness,
    double AveragePollution);

record SimulationReport(
    string Scenario,
    int TotalTicks,
    int FinalPopulation,
    double FinalBalance,
    bool Survived,
    int ResidentialZones,
    int PoweredResidentialZones,
    int RoadAccessResidentialZones,
    int ReadyResidentialZones,
    int PoweredTiles,
    int CommercialZones,
    int IndustrialZones,
    double AveragePollution,
    double AverageHappiness,
    string GameState,
    List<string> MilestonesReached,
    List<TickSnapshot> History);

/// <summary>Enriched building entry in state.json — includes population and capacity.</summary>
record BuildingStateInfo(
    [property: JsonPropertyName("typeId")]     string TypeId,
    [property: JsonPropertyName("x")]          int    X,
    [property: JsonPropertyName("y")]          int    Y,
    [property: JsonPropertyName("width")]      int    Width,
    [property: JsonPropertyName("height")]     int    Height,
    [property: JsonPropertyName("population")] int    Population,
    [property: JsonPropertyName("capacity")]   int    Capacity);

record HappinessBreakdown(
    [property: JsonPropertyName("serviceCoverage")]     double ServiceCoverage,
    [property: JsonPropertyName("taxModifier")]         double TaxModifier,
    [property: JsonPropertyName("unemploymentPenalty")] double UnemploymentPenalty,
    [property: JsonPropertyName("eventPenalty")]        double EventPenalty,
    [property: JsonPropertyName("neglectDecay")]        double NeglectDecay,
    [property: JsonPropertyName("commutePenalty")]      double CommutePenalty = 0.0);

record EmploymentState(
    [property: JsonPropertyName("jobs")]             int    Jobs,
    [property: JsonPropertyName("workers")]          int    Workers,
    [property: JsonPropertyName("unemploymentRate")] double UnemploymentRate);

record NextMilestoneInfo(
    [property: JsonPropertyName("name")]                string Name,
    [property: JsonPropertyName("requiredPopulation")]  int    RequiredPopulation,
    [property: JsonPropertyName("currentPopulation")]   int    CurrentPopulation);

record TileState(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("zone")] string Zone,
    [property: JsonPropertyName("hasPower")] bool HasPower,
    [property: JsonPropertyName("hasRoadAccess")] bool HasRoadAccess,
    [property: JsonPropertyName("population")] int Population,
    [property: JsonPropertyName("pollutionLevel")] double PollutionLevel,
    [property: JsonPropertyName("happiness")] double Happiness,
    [property: JsonPropertyName("hasDemandBoost")] bool HasDemandBoost,
    [property: JsonPropertyName("buildingId")] string? BuildingId = null,
    [property: JsonPropertyName("buildingType")] string? BuildingType = null,
    [property: JsonPropertyName("trafficLoad")] int TrafficLoad = 0,
    [property: JsonPropertyName("terrain")] string? Terrain = null);

record CoverageSummary(
    [property: JsonPropertyName("poweredZonedTilesCount")]   int    PoweredZonedTilesCount,
    [property: JsonPropertyName("unpoweredZonedTilesCount")] int    UnpoweredZonedTilesCount,
    [property: JsonPropertyName("policeCoveragePercent")]    double PoliceCoveragePercent,
    [property: JsonPropertyName("fireCoveragePercent")]      double FireCoveragePercent,
    [property: JsonPropertyName("schoolCoveragePercent")]    double SchoolCoveragePercent,
    [property: JsonPropertyName("hospitalCoveragePercent")]  double HospitalCoveragePercent,
    [property: JsonPropertyName("avgPollution")]             double AvgPollution,
    [property: JsonPropertyName("avgHappiness")]             double AvgHappiness,
    [property: JsonPropertyName("overloadedRoadCount")]      int    OverloadedRoadCount = 0,
    [property: JsonPropertyName("avgTrafficLoad")]           double AvgTrafficLoad = 0.0,
    [property: JsonPropertyName("landValueAvg")]             double LandValueAvg = 0.0,
    [property: JsonPropertyName("landValueMax")]             double LandValueMax = 0.0);

record PowerState(
    [property: JsonPropertyName("supplyMW")]       int    SupplyMW,
    [property: JsonPropertyName("demandMW")]       int    DemandMW,
    [property: JsonPropertyName("capacityRatio")]  double CapacityRatio,
    [property: JsonPropertyName("isBrownout")]     bool   IsBrownout);

record OverlayTile(
    [property: JsonPropertyName("x")]     int    X,
    [property: JsonPropertyName("y")]     int    Y,
    [property: JsonPropertyName("value")] double Value);

/// <summary>
/// Sparse overlay snapshot. <c>tiles</c> contains only entries where value &gt; 0;
/// absent tiles should be treated as value = 0 by the reader.
/// </summary>
record OverlayState(
    [property: JsonPropertyName("overlay")] string            Overlay,
    [property: JsonPropertyName("tick")]    int               Tick,
    [property: JsonPropertyName("width")]   int               Width,
    [property: JsonPropertyName("height")]  int               Height,
    [property: JsonPropertyName("tiles")]   List<OverlayTile> Tiles);

record ServerState(
    int Tick,
    bool Paused,
    int Population,
    int MaxCapacity,
    double Balance,
    double TaxPerTick,
    double CommercialIncomePerTick,
    double MaintenancePerTick,
    double NetPerTick,
    double Happiness,
    [property: JsonPropertyName("milestoneReached")] string? MilestoneReached,
    double Pollution,
    string GameState,
    List<string> Milestones,
    List<TileState> Tiles,
    BuildingStateInfo[]? BuildingList = null,
    Dictionary<string, int>? BuildingSummary = null,
    string? NextMilestoneName = null,
    int NextMilestoneTarget = 0,
    NextMilestoneInfo? NextMilestone = null,
    string? ActiveEventName = null,
    string? ActiveEventDescription = null,
    string? LatestEventBanner = null,
    double TaxModifier = 0.0,
    string? SessionId = null,
    int AvailableJobs = 0,
    int RequiredJobs = 0,
    double EmploymentRatio = 1.0,
    double EventHappinessPenalty = 0.0,
    HappinessBreakdown? HappinessBreakdown = null,
    EmploymentState? Employment = null,
    CoverageSummary? CoverageSummary = null,
    string? PauseReason = null,
    int? TicksRun = null,
    List<string>? RecentEvents = null,
    string? Error = null,
    PowerState? Power = null,
    string? LastCommand = null);

// ── ASCII Renderer ────────────────────────────────────────────────────────────

static class AsciiRenderer
{
    private const string Reset  = "\x1b[0m";
    private const string Green  = "\x1b[32m";
    private const string Blue   = "\x1b[34m";
    private const string Yellow = "\x1b[33m";
    private const string Red    = "\x1b[31m";
    private const string Cyan   = "\x1b[36m";
    private const string White  = "\x1b[37m";
    private const string Gray   = "\x1b[90m";
    private const string BgGray = "\x1b[100m";

    public static void Render(CityGrid grid, BudgetSystem budget, PopulationSystem pop, string scenario, int ticks)
    {
        Console.Clear();
        Console.WriteLine($"  ╔══════════════════════════════════════╗");
        Console.WriteLine($"  ║  L O O P O L I S  — {scenario,-14} ║");
        Console.WriteLine($"  ╚══════════════════════════════════════╝");
        Console.WriteLine();

        // Grid
        for (var y = 0; y < grid.Height; y++)
        {
            Console.Write("  ");
            for (var x = 0; x < grid.Width; x++)
            {
                var tile = grid.GetTile(x, y);
                var (symbol, color) = tile.Zone switch
                {
                    ZoneType.Residential  => ("R", Green),
                    ZoneType.Commercial   => ("C", Blue),
                    ZoneType.Industrial   => ("I", Yellow),
                    ZoneType.Road         => ("░", Gray),
                    ZoneType.PowerPlant   => ("P", Red),
                    ZoneType.CoalPlant    => ("K", Red),
                    ZoneType.NuclearPlant => ("N", White),
                    ZoneType.PowerLine    => ("╌", Cyan),
                    _                     => ("·", Gray),
                };
                Console.Write($"{color}{symbol}{Reset}");
            }
            Console.WriteLine();
        }

        // Legend
        Console.WriteLine();
        Console.WriteLine($"  {Green}R{Reset} Residential  {Blue}C{Reset} Commercial  {Yellow}I{Reset} Industrial");
        Console.WriteLine($"  {Red}P{Reset} Power Plant  {Cyan}╌{Reset} Power Line  {Gray}░{Reset} Road  {Gray}·{Reset} Empty");
        Console.WriteLine();

        // Stats
        var balanceColor = budget.IsInDeficit ? Red : Green;
        var survived     = budget.IsInDeficit ? $"{Red}✗ BANKRUPT{Reset}" : $"{Green}✓ SURVIVED{Reset}";

        Console.WriteLine($"  ┌─── After {ticks} ticks ──────────────────┐");
        Console.WriteLine($"  │  Population : {pop.Population,8}                │");
        Console.WriteLine($"  │  Balance    : {balanceColor}${budget.Balance,+12:N0}{Reset}          │");
        Console.WriteLine($"  │  Tax/tick   : ${budget.LastTaxIncome,10:N2}                │");
        Console.WriteLine($"  │  Status     : {survived,-28}│");
        Console.WriteLine($"  │                                          │");
        Console.WriteLine($"  │  Zones ▸  {Green}R{Reset}:{grid.TilesOfType(ZoneType.Residential).Count(),3}  " +
                          $"{Blue}C{Reset}:{grid.TilesOfType(ZoneType.Commercial).Count(),3}  " +
                          $"{Yellow}I{Reset}:{grid.TilesOfType(ZoneType.Industrial).Count(),3}        │");
        Console.WriteLine($"  └──────────────────────────────────────────┘");
        Console.WriteLine();
    }
}
