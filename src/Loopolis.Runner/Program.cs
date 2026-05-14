using System.Text.Json;
using System.Text.Json.Serialization;
using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;
using Loopolis.Core.Policies;
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
    var seedIndex    = Array.IndexOf(args, "--seed");
    var terrainSeed  = seedIndex >= 0 && seedIndex + 1 < args.Length
        ? int.Parse(args[seedIndex + 1])
        : new Random().Next();

    RunServer(scenario, initialSpeed, terrainSeed);
    return;
}

// ── Original CLI mode ────────────────────────────────────────────────────────

var ticks    = args.Length > 0 ? int.Parse(args[0]) : 500;
var cliScene = args.Length > 1 ? args[1] : "default";
var asciiMode = args.Contains("--ascii");
var cliSeedIdx  = Array.IndexOf(args, "--seed");
var cliSeed     = cliSeedIdx >= 0 && cliSeedIdx + 1 < args.Length ? int.Parse(args[cliSeedIdx + 1]) : 0;

var (cliGrid, cliEngine) = SetupScenario(cliScene, cliSeed);
var budget     = cliEngine.Budget;
var population = cliEngine.Population;
var powerNetwork = cliEngine.PowerNetwork;

var tickHistory = new List<TickSnapshot>();

for (var tick = 0; tick < ticks; tick++)
{
    // stress_test: after 200 ticks remove the power plant to trigger degradation
    if (cliScene == "stress_test" && tick == 200)
    {
        cliEngine.EraseTile(0, 0); // erase CoalPlant at (0,0)
        Console.Error.WriteLine($"[stress_test] Removed power plant at tick {tick}");
    }

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

static void RunServer(string scenario, double initialSpeed, int terrainSeed = 0)
{
    var sharedDir  = Path.Combine(FindSolutionRoot(), "godot", "shared");
    Directory.CreateDirectory(sharedDir);

    var sessionId   = Guid.NewGuid().ToString("N")[..8]; // e.g. "a3f7c219"
    var stateFile   = Path.Combine(sharedDir, $"state-{sessionId}.json");
    var tmpPath     = Path.Combine(sharedDir, $"state-{sessionId}.tmp.json");
    var commandFile = Path.Combine(sharedDir, $"command-{sessionId}.json");

    Console.WriteLine($"[loopolis] session={sessionId}");

    var (grid, engine) = SetupScenario(scenario, terrainSeed);

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

    // Brownout: supply dropped below demand this tick.
    // Only fire when there IS a power plant (IsActiveBrownout) AND the city is past the
    // 30-tick grace period.  No-plant early-game states (supply == 0) are NOT actionable.
    if (engine.PowerCapacitySystem.IsActiveBrownout && engine.TickCount > 30)
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

    // Pre-compute service tile list + radii (road-graph distance units)
    var serviceRadii = new Dictionary<ZoneType, float>
    {
        { ZoneType.FireStation,    8.0f },
        { ZoneType.PoliceStation,  8.0f },
        { ZoneType.School,        10.0f },
        { ZoneType.PoliceHQ,       8.0f },
        { ZoneType.FireHQ,         8.0f },
        { ZoneType.Hospital,      12.0f },
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
                // PoliceStation or PoliceHQ — road-graph distance coverage
                var covered = services.Any(s =>
                    (s.Zone == ZoneType.PoliceStation || s.Zone == ZoneType.PoliceHQ) &&
                    engine.RoadGraph.GetDistanceViaRoads(grid, x, y, s.X, s.Y) <= serviceRadii[s.Zone]);
                value = covered ? 1.0 : 0.0;
                break;
            }

            case "fire":
            {
                // FireStation or FireHQ — road-graph distance coverage
                var covered = services.Any(s =>
                    (s.Zone == ZoneType.FireStation || s.Zone == ZoneType.FireHQ) &&
                    engine.RoadGraph.GetDistanceViaRoads(grid, x, y, s.X, s.Y) <= serviceRadii[s.Zone]);
                value = covered ? 1.0 : 0.0;
                break;
            }

            case "school":
            {
                var covered = services.Any(s =>
                    s.Zone == ZoneType.School &&
                    engine.RoadGraph.GetDistanceViaRoads(grid, x, y, s.X, s.Y) <= serviceRadii[ZoneType.School]);
                value = covered ? 1.0 : 0.0;
                break;
            }

            case "hospital":
            {
                var covered = services.Any(s =>
                    s.Zone == ZoneType.Hospital &&
                    engine.RoadGraph.GetDistanceViaRoads(grid, x, y, s.X, s.Y) <= serviceRadii[ZoneType.Hospital]);
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
                value = grid.GetHeightLevel(x, y) > 0
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
                        // Cliff guard: Road and Avenue cannot span height differences > 1
                        if (zoneType == ZoneType.Road || zoneType == ZoneType.Avenue)
                        {
                            var (roadOk, roadErr) = grid.CanPlaceRoad(x, y);
                            if (!roadOk)
                            {
                                Console.WriteLine($"[place_zone] cliff_guard: {roadErr}");
                                WriteStateWithError(tmpPath, statePath, engine, grid, paused, sessionId, roadErr!, recentEvents);
                                break;
                            }
                        }
                        engine.Budget.Charge(placementCost);
                        engine.PlaceTile(x, y, zoneType);
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
                    engine.EraseTile(x, y);
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
                    var skippedCliff      = 0;
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

                        // Cliff guard for roads
                        if (rZoneType == ZoneType.Road || rZoneType == ZoneType.Avenue)
                        {
                            var (roadOk, _) = grid.CanPlaceRoad(rx, ry);
                            if (!roadOk) { skippedCliff++; continue; }
                        }

                        // Funds check per tile
                        if (!engine.Budget.CanAfford(placementCost)) { skippedFunds++; continue; }

                        engine.Budget.Charge(placementCost);
                        engine.PlaceTile(rx, ry, rZoneType);
                        placedCount++;
                    }

                    var rectMsg = $"place_rect placed {placedCount} tiles ({rZone}) in ({rx1},{ry1})–({rx2},{ry2})";
                    var warnings = new List<string>();
                    if (skippedOob       > 0) warnings.Add($"{skippedOob} tiles skipped (out of bounds)");
                    if (skippedMilestone > 0) warnings.Add($"{skippedMilestone} tiles skipped (milestone gate)");
                    if (skippedOccupied  > 0) warnings.Add($"{skippedOccupied} tiles skipped (occupied)");
                    if (skippedFunds     > 0) warnings.Add($"{skippedFunds} tiles skipped (insufficient funds)");
                    if (skippedCliff     > 0) warnings.Add($"{skippedCliff} tiles skipped (cliff — height diff too large)");
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
                        engine.EraseTile(erx, ery);
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

            case "set_policy":
                // {"cmd":"set_policy","policy":"GreenCity","active":true}
                if (root.TryGetProperty("policy", out var policyProp) &&
                    root.TryGetProperty("active", out var activeProp))
                {
                    var policyName = policyProp.GetString() ?? "";
                    var isActive   = activeProp.GetBoolean();
                    if (Enum.TryParse<PolicyType>(policyName, out var policyType))
                    {
                        if (isActive)
                            engine.PolicySystem.ActivatePolicy(policyType);
                        else
                            engine.PolicySystem.DeactivatePolicy(policyType);
                        Console.WriteLine($"[set_policy] {policyName}={isActive}, total cost={engine.PolicySystem.GetCostPerTick()}/tick");
                    }
                    else
                    {
                        Console.WriteLine($"[set_policy] Unknown policy: {policyName}");
                    }
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
        .Where(t => t.Zone != ZoneType.Empty || t.HeightLevel != 1 || t.HasForest || t.IsBorderConnection)
        .Select(t => new TileState(
            t.X, t.Y, t.Zone.ToString(), t.HasPower, t.HasRoadAccess,
            t.Zone == ZoneType.Residential ? grid.GetPopulation(t.X, t.Y) : 0,
            Math.Round(t.PollutionLevel, 3),
            Math.Round(t.Happiness, 3),
            t.HasDemandBoost,
            t.BuildingId,
            t.BuildingId != null ? buildingTypeLookup.GetValueOrDefault(t.BuildingId) : null,
            t.TrafficLoad,
            t.Terrain != TerrainType.Flat ? t.Terrain.ToString() : null,
            t.HeightLevel,
            t.HasForest,
            t.IsBorderConnection))
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
        var serviceRadii = new Dictionary<ZoneType, float>
        {
            { ZoneType.FireStation,    8.0f },
            { ZoneType.PoliceStation,  8.0f },
            { ZoneType.School,        10.0f },
            { ZoneType.PoliceHQ,       8.0f },
            { ZoneType.FireHQ,         8.0f },
            { ZoneType.Hospital,      12.0f },
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
                var dist = engine.RoadGraph.GetDistanceViaRoads(grid, tile.X, tile.Y, svc.X, svc.Y);
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
    var avgCommutePenalty = engine.HappinessSystem.AverageCommutePenalty(grid, currentPop, engine.RoadGraph);

    var happinessBreakdown = new HappinessBreakdown(
        ServiceCoverage:     Math.Round(avgServiceCoverage, 4),
        TaxModifier:         Math.Round(engine.Budget.TaxModifier, 4),
        UnemploymentPenalty: 0.0,   // employment affects growth rate, not happiness directly
        EventPenalty:        Math.Round(engine.EventSystem.HappinessPenalty, 4),
        NeglectDecay:        Math.Round(-avgNeglectDecay, 4),
        CommutePenalty:      Math.Round(avgCommutePenalty, 4),
        AverageNeglect:      Math.Round(engine.HappinessSystem.AverageNeglect(grid), 4)
    );

    // --- Coverage summary (power + services + pollution + happiness across all zoned tiles) ---
    var zonedTiles       = grid.AllTiles().Where(t => t.Zone is ZoneType.Residential or ZoneType.Commercial or ZoneType.Industrial).ToList();
    var poweredZoned     = zonedTiles.Count(t => t.HasPower);
    var unpoweredZoned   = zonedTiles.Count - poweredZoned;

    // Pre-compute service tiles and radii for coverage percentage
    // PoliceHQ (radius 8) counts as police coverage; FireHQ (radius 8) counts as fire coverage
    // Radii are in road-graph distance units (Road=1.0, Avenue=0.5 per edge)
    var covServiceRadii = new Dictionary<ZoneType, float>
    {
        { ZoneType.FireStation,    8.0f },
        { ZoneType.PoliceStation,  8.0f },
        { ZoneType.School,        10.0f },
        { ZoneType.PoliceHQ,       8.0f },
        { ZoneType.FireHQ,         8.0f },
        { ZoneType.Hospital,      12.0f },
    };
    var covServices = grid.AllTiles()
        .Where(t => covServiceRadii.ContainsKey(t.Zone))
        .ToList();

    int policeCovered = 0, fireCovered = 0, schoolCovered = 0, hospitalCovered = 0;
    double totalPollution = 0, totalHappiness = 0;
    foreach (var zt in zonedTiles)
    {
        if (covServices.Any(s => (s.Zone == ZoneType.PoliceStation || s.Zone == ZoneType.PoliceHQ)
                                 && engine.RoadGraph.GetDistanceViaRoads(grid, zt.X, zt.Y, s.X, s.Y) <= covServiceRadii[s.Zone]))
            policeCovered++;
        if (covServices.Any(s => (s.Zone == ZoneType.FireStation || s.Zone == ZoneType.FireHQ)
                                 && engine.RoadGraph.GetDistanceViaRoads(grid, zt.X, zt.Y, s.X, s.Y) <= covServiceRadii[s.Zone]))
            fireCovered++;
        if (covServices.Any(s => s.Zone == ZoneType.School
                                 && engine.RoadGraph.GetDistanceViaRoads(grid, zt.X, zt.Y, s.X, s.Y) <= covServiceRadii[ZoneType.School]))
            schoolCovered++;
        if (covServices.Any(s => s.Zone == ZoneType.Hospital
                                 && engine.RoadGraph.GetDistanceViaRoads(grid, zt.X, zt.Y, s.X, s.Y) <= covServiceRadii[ZoneType.Hospital]))
            hospitalCovered++;
        totalPollution  += zt.PollutionLevel;
        totalHappiness  += zt.Happiness;
    }
    var zonedCount = zonedTiles.Count;
    // G4: capacity-aware coverage from the last service coverage snapshot
    var capacityCoverage = engine.LastServiceCoverage;
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
        LandValueMax:             Math.Round(engine.LandValueSystem.MaxLandValue(grid), 4),
        SchoolSeatsUsed:          capacityCoverage?.SchoolSeatsUsed    ?? 0,
        SchoolSeatsTotal:         capacityCoverage?.SchoolSeatsTotal   ?? 0,
        PoliceCapacityUsed:       capacityCoverage?.PoliceCapacityUsed ?? 0,
        PoliceCapacityTotal:      capacityCoverage?.PoliceCapacityTotal ?? 0,
        FireCapacityUsed:         capacityCoverage?.FireCapacityUsed   ?? 0,
        FireCapacityTotal:        capacityCoverage?.FireCapacityTotal  ?? 0,
        HospitalBedsUsed:         capacityCoverage?.HospitalBedsUsed   ?? 0,
        HospitalBedsTotal:        capacityCoverage?.HospitalBedsTotal  ?? 0
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

    // --- Terrain summary ---
    var waterTileCount   = 0;
    var elevatedCount    = 0;
    var plateauCount     = 0;
    for (var ty = 0; ty < grid.Height; ty++)
    for (var tx = 0; tx < grid.Width; tx++)
    {
        var hl = grid.GetHeightLevel(tx, ty);
        if (hl <= 0) waterTileCount++;
        else if (hl >= 2)
        {
            elevatedCount++;
            if (grid.IsPlateau(tx, ty)) plateauCount++;
        }
    }
    var terrainSummary = new TerrainSummary(
        AverageHeight:    Math.Round(grid.AverageHeight, 3),
        WaterTileCount:   waterTileCount,
        ElevatedTileCount: elevatedCount,
        PlateauTileCount:  plateauCount);

    var activeEvent = engine.EventSystem.ActiveEvent;

    // --- Power capacity ---
    var pcs = engine.PowerCapacitySystem;
    var powerState = new PowerState(
        SupplyMW:      pcs.TotalSupplyMW,
        DemandMW:      pcs.TotalDemandMW,
        CapacityRatio: Math.Round(pcs.CapacityRatio, 4),
        IsBrownout:    pcs.IsBrownout);

    // --- Worker flow ---
    WorkerFlowState? workerFlowState = null;
    if (engine.LastWorkerFlow != null)
    {
        var wf = engine.LastWorkerFlow;
        workerFlowState = new WorkerFlowState(
            WorkersRouted:          wf.WorkersRouted,
            AverageCommuteDistance: Math.Round(wf.AverageCommuteDistance, 2),
            UnroutedWorkers:        wf.UnroutedWorkers,
            OverloadedEdges:        wf.OverloadedEdges);
    }

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
        WorkingAge:                currentPop,
        EmploymentRatio:           Math.Round(engine.EmploymentSystem.EmploymentRatio, 3),
        EmploymentWarning:         engine.EmploymentSystem.EmploymentRatio < 0.40 && currentPop > 50,
        RequiredJobs:              engine.EmploymentSystem.RequiredJobs,
        EventHappinessPenalty:     engine.EventSystem.HappinessPenalty,
        HappinessBreakdown:        happinessBreakdown,
        Employment:                employmentState,
        CoverageSummary:           coverageSummary,
        PauseReason:               pauseReason,
        TicksRun:                  ticksRun,
        RecentEvents:              recentEvents ?? new List<string>(),
        Error:                     error,
        Power:                     powerState,
        LastCommand:               lastCommand,
        Terrain:                   terrainSummary,
        RoadGraphNodes:            engine.RoadGraph.NodeCount,
        WorkerFlow:                workerFlowState,
        EventTileX:                engine.EventSystem.FireTileX,
        EventTileY:                engine.EventSystem.FireTileY,
        LastDegradedBuildings:     engine.LastDegradedBuildings?.ToArray(),
        LastNewBuildingTypeIds:    engine.LastNewBuildingTypeIds?.ToArray(),
        // Scenario tracking
        ActiveScenarioId:          engine.ActiveScenario?.Id,
        ActiveScenarioName:        engine.ActiveScenario?.Name,
        ScenarioTargetPopulation:  engine.ActiveScenario?.Goal.TargetPopulation ?? 0,
        ScenarioTickLimit:         engine.ActiveScenario?.TickLimit ?? 0,
        ScenarioBronzeTick:        engine.ActiveScenario?.Medals.Bronze ?? 0,
        ScenarioSilverTick:        engine.ActiveScenario?.Medals.Silver ?? 0,
        ScenarioGoldTick:          engine.ActiveScenario?.Medals.Gold   ?? 0,
        ScenarioComplete:          engine.ScenarioComplete,
        MedalEarned:               engine.MedalEarned,
        ScenarioFailed:            engine.ScenarioFailed,
        ParkTiles:                 grid.TilesOfType(ZoneType.Park).Count(),
        // Policy system state
        PolicyGreenCity:           engine.PolicySystem.IsActive(PolicyType.GreenCity),
        PolicyIndustrialHub:       engine.PolicySystem.IsActive(PolicyType.IndustrialHub),
        PolicyCommercialBoost:     engine.PolicySystem.IsActive(PolicyType.CommercialBoost),
        PolicyOpenCity:            engine.PolicySystem.IsActive(PolicyType.OpenCity),
        PolicyTotalCostPerTick:    engine.PolicySystem.GetCostPerTick()
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

static (CityGrid grid, SimulationEngine engine) SetupScenario(string scenario, int terrainSeed = 0)
{
    var grid       = new CityGrid(32, 32);
    var budget     = new BudgetSystem(); // default $4,000 starting balance
    var population = new PopulationSystem();
    var power      = new PowerNetwork();
    var roads      = new RoadNetwork();
    var demand     = new DemandSystem();

    switch (scenario)
    {
        case "generated_128":
        {
            // Procedurally generated 128×128 terrain using diamond-square.
            // Starts with only a border connection road at center of south edge.
            var seed = terrainSeed != 0 ? terrainSeed : 42;
            var g128 = new CityGrid(128, 128);
            var heightMap128 = Loopolis.Core.Grid.HeightMapGenerator.Generate(128, 128, seed);
            var forestMap128 = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(128, 128, seed);
            g128.ApplyHeightMap(heightMap128);
            g128.ApplyForestMap(forestMap128);
            // Border connection at center of south edge — force flat terrain so it can always be placed
            g128.SetHeightLevel(64, 127, 1);
            g128.PlaceBorderConnection(64, 127);
            // Starter road spine heading north — force flat terrain on each tile
            g128.SetHeightLevel(64, 126, 1); g128.SetZone(64, 126, ZoneType.Road);
            g128.SetHeightLevel(64, 125, 1); g128.SetZone(64, 125, ZoneType.Road);
            g128.SetHeightLevel(64, 124, 1); g128.SetZone(64, 124, ZoneType.Road);
            Console.WriteLine($"[generated_128] Border connection at (64,127), starter spine (64,126–124), seed={seed}");
            var engine128 = new SimulationEngine(g128, budget, population, power, roads, demand);
            engine128.SeedRoadGraphFromGrid();
            return (g128, engine128);
        }

        case "generated_map":
        {
            // Procedurally generated 64×64 terrain using diamond-square. Seed from CLI --seed arg.
            // Starts with only a border connection road at center of south edge.
            var seed = terrainSeed != 0 ? terrainSeed : 42;
            var g64 = new CityGrid(64, 64);
            var heightMap = Loopolis.Core.Grid.HeightMapGenerator.Generate(64, 64, seed);
            var forestMap = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(64, 64, seed);
            g64.ApplyHeightMap(heightMap);
            g64.ApplyForestMap(forestMap);
            // Border connection at center of south edge — force flat terrain so it can always be placed
            g64.SetHeightLevel(32, 63, 1);
            g64.PlaceBorderConnection(32, 63);
            // Starter road spine heading north — force flat terrain on each tile
            g64.SetHeightLevel(32, 62, 1); g64.SetZone(32, 62, ZoneType.Road);
            g64.SetHeightLevel(32, 61, 1); g64.SetZone(32, 61, ZoneType.Road);
            g64.SetHeightLevel(32, 60, 1); g64.SetZone(32, 60, ZoneType.Road);
            Console.WriteLine($"[generated_map] Border connection at (32,63), starter spine (32,62–60), seed={seed}");
            var engine64 = new SimulationEngine(g64, budget, population, power, roads, demand);
            engine64.SeedRoadGraphFromGrid();
            return (g64, engine64);
        }

        case "no_power":
            // Zones with roads but no power → no growth
            // Explicit flat terrain so road cliff constraint never fires
            grid.SetFlatTerrain();
            grid.SetZone(5, 5, ZoneType.Road);
            grid.SetZone(6, 5, ZoneType.Residential);
            grid.SetZone(7, 5, ZoneType.Residential);
            grid.SetZone(8, 5, ZoneType.Residential);
            break;

        case "no_roads":
            // Zones with power but no road access → no growth
            grid.SetFlatTerrain();
            grid.SetZone(5, 5, ZoneType.PowerPlant);
            grid.SetZone(6, 5, ZoneType.PowerLine);
            grid.SetZone(7, 5, ZoneType.Residential);  // powered but no road adjacent
            grid.SetZone(7, 6, ZoneType.Residential);
            grid.SetZone(7, 7, ZoneType.Residential);
            break;

        case "town":
            grid.SetFlatTerrain();
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

            grid.SetFlatTerrain();
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
            // City with school and fire station — tests road-graph service coverage bonus.
            //
            // Layout: compact road network with services placed on road-adjacent tiles
            //   CoalPlant at (2,15) powers the whole grid via road adjacency
            //   Main E-W road at y=15 (x=3..26)
            //   North spur at x=15 (y=9..15)
            //   Residential row north: (x=6..14, y=14) — adjacent to y=15 road
            //   Residential row south: (x=6..14, y=16) — adjacent to y=15 road
            //   School at (15,8): adjacent to north spur (15,9) — covers north residents
            //   FireStation at (16,15): adjacent to main road at (15,15) — covers south residents
            //
            // Road-graph coverage:
            //   North resident (x,14) road-neighbor = (x,15); school road-neighbor = (15,9)
            //   graph distance (x,15)→(15,15)→...→(15,9) ≤ 10.0 (School radius) → covered
            //   South resident (x,16) road-neighbor = (x,15); fire road-neighbor = (15,15)
            //   graph distance (x,15)→(15,15) ≤ 8.0 (FireStation radius) → covered
            //
            // Expected: north residents covered by School, south by FireStation → happiness 0.75+

            grid.SetFlatTerrain();
            // Power
            grid.SetZone(2, 15, ZoneType.CoalPlant);

            // Roads
            for (var x = 3; x <= 26; x++) grid.SetZone(x, 15, ZoneType.Road); // main E-W
            for (var y = 9; y <= 14; y++) grid.SetZone(15, y, ZoneType.Road);  // north spur

            // Residential: single row north and south of main road (all road-adjacent)
            for (var x = 6; x <= 14; x++) grid.SetZone(x, 14, ZoneType.Residential); // north of road, adj to road
            for (var x = 6; x <= 14; x++) grid.SetZone(x, 16, ZoneType.Residential); // south of road, adj to road

            // School: adjacent to top of north spur (15,9), covers all north residents via road graph
            // Road-graph: school road-neighbor=(15,9); north resident (x,14) road-neighbor=(x,15)
            // distance (x,15)→(15,15): |x-15| road edges, then (15,15)→(15,9): 6 road edges
            // max distance for (6,14): (6,15)→(15,15) = 9 + (15,9) = 6 → total 15.0 > 10.0 (School)
            // so place school closer: use PoliceStation at (15,8) adjacent to north spur (15,9)
            // but north residents at x=11..14 are within 0..4 + 6 = 6..10 → borderline
            // Use only x=11..14 for north to ensure coverage:
            for (var x = 6;  x <= 14; x++) grid.SetZone(x, 14, ZoneType.Empty); // clear previous
            for (var x = 11; x <= 14; x++) grid.SetZone(x, 14, ZoneType.Residential); // near x=15 spur
            grid.SetZone(15, 8, ZoneType.School);  // adj to (15,9); from (11,15): distance=4+(15,15→15,9)=4+6=10 ≤ 10

            // Fire station: on road at (16,15) (adjacent to (15,15)), covers south residents
            // From (11,16) road-neighbor=(11,15): distance (11,15)→(16,15)=5 → adjacent to station→0 (same neighbor)
            // Wait: fire station at (16,15) is itself a Road? No—place it ADJACENT to road.
            // FireStation at (16,14): adj to (16,15) road. From south resident (11,16) road-neighbor=(11,15)
            // dist (11,15)→(16,15)=5; then fire station neighbor=(16,15); total 5 ≤ 8 → covered
            grid.SetZone(16, 14, ZoneType.FireStation); // adj to main road (16,15)

            // Commercial east of road junction for demand boost
            for (var x = 17; x <= 22; x++) grid.SetZone(x, 16, ZoneType.Commercial);

            break;

        case "city_path":
            // Compact mixed-use foundation designed to reach City milestone (5,000 pop).
            grid.SetFlatTerrain();
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
            grid.SetZone(8,  16, ZoneType.FireStation);    // covers west residential cluster (road-adjacent to spine)
            grid.SetZone(14, 16, ZoneType.PoliceStation);  // covers center + east residential (road-adjacent to spine)
            // School at (13,16): road-adjacent to (13,15). Graph distance to west residential (7,14) ≈ 7.
            // Graph distance to east residential (17,14) ≈ 5. Both well within School radius 10.0.
            grid.SetZone(13, 16, ZoneType.School);

            // Park near residential center — gives +0.08/tile happiness to Chebyshev-3 neighbours (cap +0.20)
            grid.SetZone(11, 13, ZoneType.Park);
            break;

        case "powered_start":
            // Like default but pre-built with fire+police coverage — tests mid-game growth without neglect cascade.
            grid.SetFlatTerrain();
            grid.SetZone(5, 12, ZoneType.CoalPlant);
            for (var x = 6; x <= 16; x++) grid.SetZone(x, 12, ZoneType.Road);
            // Residential north of road — wider strip
            for (var x = 9; x <= 14; x++) grid.SetZone(x, 11, ZoneType.Residential);
            // Commercial south of road
            grid.SetZone(9,  13, ZoneType.Commercial);
            grid.SetZone(10, 13, ZoneType.Commercial);
            grid.SetZone(11, 13, ZoneType.Commercial);
            // Industrial far west, south of road (away from residential)
            grid.SetZone(6,  13, ZoneType.Industrial);
            grid.SetZone(7,  13, ZoneType.Industrial);
            // Services — pre-built so players see what covered growth looks like
            grid.SetZone(8,  13, ZoneType.FireStation);
            grid.SetZone(13, 13, ZoneType.PoliceStation);
            grid.SetZone(15, 11, ZoneType.School);
            break;

        case "stress_test":
        {
            // Dense 16×16 grid of R/C/I zones with roads + power.
            // Designed to exercise BuildingGrowthSystem and BuildingDegradationSystem under real pressure.
            // Phase 1 (ticks 0–199): city grows and buildings tier up.
            // Phase 2 (ticks 200–399): power plant removed → degradation fires for all multi-tile buildings.
            //
            // Layout (grid 32×32, zones in top-left 16×16 block):
            //   Power plant at (0,0)
            //   Horizontal road spine at y=1 (x=0..16)
            //   Vertical road spine at x=0 (y=0..16)
            //   Residential: x=1..8, y=2..9  (8×8 = 64 tiles)
            //   Commercial:  x=9..12, y=2..5  (4×4 = 16 tiles adjacent to R for demand boost)
            //   Industrial:  x=9..12, y=6..9  (4×4 = 16 tiles)
            //
            // Note: this scenario is used by "dotnet run -- 400 stress_test" where the runner
            // runs all 400 ticks. Power removal is done by the SimulationRunner wrapping this scenario.
            // For the base 400-tick run we keep the power plant in place so buildings grow first,
            // then use SetupStressTestPhase2 for the second half.
            grid.SetFlatTerrain();
            // Power plant + lines running along road spines
            grid.SetZone(0, 0, ZoneType.CoalPlant);
            for (var x = 1; x <= 16; x++) grid.SetZone(x, 0, ZoneType.PowerLine);
            // Roads: horizontal at y=1, vertical at x=0
            for (var x = 0; x <= 16; x++) grid.SetZone(x, 1, ZoneType.Road);
            for (var y = 2; y <= 16; y++) grid.SetZone(0, y, ZoneType.Road);
            // Residential block
            for (var x = 1; x <= 8; x++)
            for (var y = 2; y <= 9; y++)
                grid.SetZone(x, y, ZoneType.Residential);
            // Commercial strip (demand boost for residential)
            for (var x = 9; x <= 12; x++)
            for (var y = 2; y <= 5; y++)
                grid.SetZone(x, y, ZoneType.Commercial);
            // Industrial block
            for (var x = 9; x <= 12; x++)
            for (var y = 6; y <= 9; y++)
                grid.SetZone(x, y, ZoneType.Industrial);
            // Services — placed just below residential block, adjacent to vertical road at x=0
            grid.SetZone(1, 10, ZoneType.FireStation);
            grid.SetZone(1, 11, ZoneType.PoliceStation);
            grid.SetZone(1, 12, ZoneType.School);
            break;
        }

        case "cottage_start":
            // Tests P1: road-only cottage growth, then power upgrade payoff.
            // NO power plant — residential grows as unpowered cottages (cap 25, 0.7× tax).
            grid.SetFlatTerrain();
            grid.PlaceBorderConnection(16, 31);
            for (var x = 13; x <= 19; x++) grid.SetZone(x, 30, ZoneType.Road);   // E-W road
            for (var x = 13; x <= 19; x++) grid.SetZone(x, 29, ZoneType.Residential); // R north of road
            for (var x = 13; x <= 19; x++) grid.SetZone(x, 31, ZoneType.Commercial);  // C south of road
            // No power plant — cottages should still appear
            break;

        case "island_chain":
        {
            // Island Chain challenge: 64×64 archipelago with ~40% water.
            var g64ic = new CityGrid(64, 64);
            var heightMapIc = Loopolis.Core.Grid.HeightMapGenerator.GenerateNamed("island_chain", 64, 64);
            var forestMapIc = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(64, 64, seed: 0xC0FFEE);
            g64ic.ApplyHeightMap(heightMapIc);
            g64ic.ApplyForestMap(forestMapIc);
            // Border connection at center of south edge — force flat terrain
            g64ic.SetHeightLevel(32, 63, 1);
            g64ic.PlaceBorderConnection(32, 63);
            // Starter road spine heading north
            g64ic.SetHeightLevel(32, 62, 1); g64ic.SetZone(32, 62, ZoneType.Road);
            g64ic.SetHeightLevel(32, 61, 1); g64ic.SetZone(32, 61, ZoneType.Road);
            g64ic.SetHeightLevel(32, 60, 1); g64ic.SetZone(32, 60, ZoneType.Road);
            var budgetIc = new BudgetSystem(initialBalance: 6_500);
            Console.WriteLine("[island_chain] Border connection at (32,63), starter spine (32,62–60)");
            var engineIc = new SimulationEngine(g64ic, budgetIc, population, power, roads, demand);
            engineIc.SeedRoadGraphFromGrid();
            return (g64ic, engineIc);
        }

        case "narrow_valley":
        {
            // Narrow Valley challenge: 128×128 map with mountain walls on east/west.
            var g128nv = new CityGrid(128, 128);
            var heightMapNv = Loopolis.Core.Grid.HeightMapGenerator.GenerateNamed("narrow_valley", 128, 128);
            var forestMapNv = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(128, 128, seed: 0xBADC0DE);
            g128nv.ApplyHeightMap(heightMapNv);
            g128nv.ApplyForestMap(forestMapNv);
            // Border connection at center of south edge — force flat terrain
            g128nv.SetHeightLevel(64, 127, 1);
            g128nv.PlaceBorderConnection(64, 127);
            // Starter road spine heading north
            g128nv.SetHeightLevel(64, 126, 1); g128nv.SetZone(64, 126, ZoneType.Road);
            g128nv.SetHeightLevel(64, 125, 1); g128nv.SetZone(64, 125, ZoneType.Road);
            g128nv.SetHeightLevel(64, 124, 1); g128nv.SetZone(64, 124, ZoneType.Road);
            var budgetNv = new BudgetSystem(initialBalance: 7_000);
            Console.WriteLine("[narrow_valley] Border connection at (64,127), starter spine (64,126–124)");
            var engineNv = new SimulationEngine(g128nv, budgetNv, population, power, roads, demand);
            engineNv.SeedRoadGraphFromGrid();
            return (g128nv, engineNv);
        }

        case "river_delta":
        {
            // River Delta challenge: 64×64 mostly flat with diagonal water channels.
            var g64rd = new CityGrid(64, 64);
            var heightMapRd = Loopolis.Core.Grid.HeightMapGenerator.GenerateNamed("river_delta", 64, 64);
            var forestMapRd = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(64, 64, seed: 0xDE17A1);
            g64rd.ApplyHeightMap(heightMapRd);
            g64rd.ApplyForestMap(forestMapRd);
            // Border connection at center of south edge — force flat terrain
            g64rd.SetHeightLevel(32, 63, 1);
            g64rd.PlaceBorderConnection(32, 63);
            // Starter road spine heading north
            g64rd.SetHeightLevel(32, 62, 1); g64rd.SetZone(32, 62, ZoneType.Road);
            g64rd.SetHeightLevel(32, 61, 1); g64rd.SetZone(32, 61, ZoneType.Road);
            g64rd.SetHeightLevel(32, 60, 1); g64rd.SetZone(32, 60, ZoneType.Road);
            var budgetRd = new BudgetSystem(initialBalance: 5_000);
            Console.WriteLine("[river_delta] Border connection at (32,63), starter spine (32,62–60)");
            var engineRd = new SimulationEngine(g64rd, budgetRd, population, power, roads, demand);
            engineRd.SeedRoadGraphFromGrid();
            return (g64rd, engineRd);
        }

        default:
            // Empty new-game start with a border connection road from the south edge.
            // Player must build their own infrastructure.
            grid.SetFlatTerrain();
            // Border connection — center of south edge, unerasable Regional Highway
            grid.PlaceBorderConnection(16, 31);
            // Starter spine — 3 road tiles heading north from border
            grid.SetZone(16, 30, ZoneType.Road);
            grid.SetZone(16, 29, ZoneType.Road);
            grid.SetZone(16, 28, ZoneType.Road);
            break;
    }

    var engine = new SimulationEngine(grid, budget, population, power, roads, demand);
    engine.SeedRoadGraphFromGrid();   // seed road graph from any roads placed during scenario setup
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
    [property: JsonPropertyName("commutePenalty")]      double CommutePenalty = 0.0,
    [property: JsonPropertyName("averageNeglect")]      double AverageNeglect = 0.0);

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
    [property: JsonPropertyName("terrain")] string? Terrain = null,
    [property: JsonPropertyName("height")] int HeightLevel = 1,
    [property: JsonPropertyName("hasForest")] bool HasForest = false,
    [property: JsonPropertyName("isBorderConnection")] bool IsBorderConnection = false);

record TerrainSummary(
    [property: JsonPropertyName("averageHeight")]    double AverageHeight,
    [property: JsonPropertyName("waterTileCount")]   int    WaterTileCount,
    [property: JsonPropertyName("elevatedTileCount")] int   ElevatedTileCount,
    [property: JsonPropertyName("plateauTileCount")] int    PlateauTileCount);

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
    [property: JsonPropertyName("landValueMax")]             double LandValueMax = 0.0,
    // G4: capacity model fields
    [property: JsonPropertyName("schoolSeatsUsed")]          int    SchoolSeatsUsed = 0,
    [property: JsonPropertyName("schoolSeatsTotal")]         int    SchoolSeatsTotal = 0,
    [property: JsonPropertyName("policeCapacityUsed")]       int    PoliceCapacityUsed = 0,
    [property: JsonPropertyName("policeCapacityTotal")]      int    PoliceCapacityTotal = 0,
    [property: JsonPropertyName("fireCapacityUsed")]         int    FireCapacityUsed = 0,
    [property: JsonPropertyName("fireCapacityTotal")]        int    FireCapacityTotal = 0,
    [property: JsonPropertyName("hospitalBedsUsed")]         int    HospitalBedsUsed = 0,
    [property: JsonPropertyName("hospitalBedsTotal")]        int    HospitalBedsTotal = 0);

record PowerState(
    [property: JsonPropertyName("supplyMW")]       int    SupplyMW,
    [property: JsonPropertyName("demandMW")]       int    DemandMW,
    [property: JsonPropertyName("capacityRatio")]  double CapacityRatio,
    [property: JsonPropertyName("isBrownout")]     bool   IsBrownout);

record WorkerFlowState(
    [property: JsonPropertyName("workersRouted")]          int    WorkersRouted,
    [property: JsonPropertyName("averageCommuteDistance")] double AverageCommuteDistance,
    [property: JsonPropertyName("unroutedWorkers")]        int    UnroutedWorkers,
    [property: JsonPropertyName("overloadedEdges")]        int    OverloadedEdges);

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
    int WorkingAge = 0,
    double EmploymentRatio = 1.0,
    bool EmploymentWarning = false,
    int RequiredJobs = 0,
    double EventHappinessPenalty = 0.0,
    HappinessBreakdown? HappinessBreakdown = null,
    EmploymentState? Employment = null,
    CoverageSummary? CoverageSummary = null,
    string? PauseReason = null,
    int? TicksRun = null,
    List<string>? RecentEvents = null,
    string? Error = null,
    PowerState? Power = null,
    string? LastCommand = null,
    TerrainSummary? Terrain = null,
    int RoadGraphNodes = 0,
    WorkerFlowState? WorkerFlow = null,
    int EventTileX = -1,   // X coord of tile currently on fire (-1 = none)
    int EventTileY = -1,   // Y coord of tile currently on fire (-1 = none)
    string[]? LastDegradedBuildings = null,  // typeIds demolished by BuildingDegradationSystem this tick
    string[]? LastNewBuildingTypeIds = null, // typeIds created by BuildingGrowthSystem this tick
    // Scenario tracking (null/0 when sandbox)
    string? ActiveScenarioId = null,
    string? ActiveScenarioName = null,
    int ScenarioTargetPopulation = 0,
    int ScenarioTickLimit = 0,
    int ScenarioBronzeTick = 0,
    int ScenarioSilverTick = 0,
    int ScenarioGoldTick = 0,
    bool ScenarioComplete = false,
    string? MedalEarned = null,
    bool ScenarioFailed = false,
    int ParkTiles = 0,                      // count of Park zone tiles
    string? PersonalBestMedal = null,       // personal best medal from leaderboard
    int PersonalBestTick = 0,              // tick count of personal best run
    // Policy system
    bool PolicyGreenCity = false,
    bool PolicyIndustrialHub = false,
    bool PolicyCommercialBoost = false,
    bool PolicyOpenCity = false,
    int PolicyTotalCostPerTick = 0);

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
                    ZoneType.Park         => ("P", Green),
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
