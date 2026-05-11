using System.Text.Json;
using System.Text.Json.Serialization;
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
            TaxIncome: Math.Round(budget.CalculateTaxIncome(), 2),
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

    var paused        = false;
    var speed         = initialSpeed;
    var skipRemaining = 0;
    var pauseAfterSkip = false;

    WriteState(tmpPath, stateFile, engine, grid, paused, sessionId);
    Console.WriteLine($"[server] Started. Scenario: {scenario}, Speed: {speed} t/s, Session: {sessionId}");
    Console.WriteLine($"[server] State: {stateFile}");
    Console.WriteLine($"[server] Commands: {commandFile}");

    try
    {
        while (true)
        {
            // 1. Read command
            ProcessCommand(commandFile, ref paused, ref speed, ref skipRemaining, ref pauseAfterSkip, ref grid, ref engine, sessionId);
            // grid/engine may have been replaced by new_game — use current references below

            // 2. Tick (or skip, or wait)
            if (skipRemaining > 0)
            {
                // Fast-forward: no writes, no sleep
                engine.Tick();
                skipRemaining--;
                if (skipRemaining == 0)
                {
                    Console.WriteLine(
                        $"[skip] complete. Tick: {engine.TickCount}, Pop: {engine.Population.Population}, Balance: ${engine.Budget.Balance:N2}");
                    if (pauseAfterSkip)
                    {
                        paused = true;
                        pauseAfterSkip = false;
                    }
                    WriteState(tmpPath, stateFile, engine, grid, paused, sessionId);
                }
            }
            else if (!paused)
            {
                engine.Tick();
                WriteState(tmpPath, stateFile, engine, grid, paused, sessionId);
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

static void ProcessCommand(
    string cmdPath,
    ref bool paused,
    ref double speed,
    ref int skipRemaining,
    ref bool pauseAfterSkip,
    ref CityGrid grid,
    ref SimulationEngine engine,
    string sessionId)
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
                    var placementCost = BudgetSystem.PlacementCosts.GetValueOrDefault(zone, 0.0);
                    if (!engine.Budget.CanAfford(placementCost))
                    {
                        Console.WriteLine($"[place_zone] insufficient_funds: need ${placementCost:N0}, have ${engine.Budget.Balance:N0}");
                        break;
                    }
                    if (Enum.TryParse<ZoneType>(zone, out var zoneType))
                    {
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
                    grid.SetZone(x, y, ZoneType.Empty);
                    Console.WriteLine($"[erase] ({x},{y}) => Empty");
                }
                break;

            case "new_game":
            {
                var newGrid   = new CityGrid(32, 32);
                var newBudget = new BudgetSystem(initialBalance: 10_000);
                var newPop    = new PopulationSystem();
                var newPower  = new PowerNetwork();
                var newRoads  = new RoadNetwork();
                var newDemand = new DemandSystem();
                grid   = newGrid;
                engine = new SimulationEngine(newGrid, newBudget, newPop, newPower, newRoads, newDemand);
                Console.WriteLine("[new_game] Reset to empty 32x32 grid, $10000 starting balance.");
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
                    skipRemaining = count;
                    pauseAfterSkip = root.TryGetProperty("pauseAfter", out var paProp) && paProp.GetBoolean();
                    Console.WriteLine($"[skip] Starting {count} ticks...");
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

            default:
                Console.WriteLine($"[command] Unknown: {cmd}");
                break;
        }
    }
}

static void WriteState(string tmpPath, string statePath, SimulationEngine engine, CityGrid grid, bool paused, string sessionId = "")
{
    var nonEmptyTiles = grid.AllTiles()
        .Where(t => t.Zone != ZoneType.Empty)
        .Select(t => new TileState(
            t.X, t.Y, t.Zone.ToString(), t.HasPower, t.HasRoadAccess,
            t.Zone == ZoneType.Residential ? grid.GetPopulation(t.X, t.Y) : 0,
            Math.Round(t.PollutionLevel, 3),
            Math.Round(t.Happiness, 3),
            t.HasDemandBoost))
        .ToList();

    var residentialCount = grid.TilesOfType(ZoneType.Residential).Count();
    var maxCapacity = residentialCount * 50;
    var milestone = engine.MilestoneSystem.LatestMilestone;

    // Compute next milestone inline: find first threshold > current population
    var currentPop = engine.Population.Population;
    (string Name, string Emoji, int Target)[] milestoneThresholds =
    {
        ("Town",       "🥉", 500),
        ("City",       "🥈", 5_000),
        ("Metropolis", "🥇", 25_000),
        ("Loopolis",   "🏆", 100_000),
    };
    var nextMilestone = milestoneThresholds.FirstOrDefault(m => m.Target > currentPop);
    var nextMilestoneName   = nextMilestone != default ? $"{nextMilestone.Name} {nextMilestone.Emoji}" : null;
    var nextMilestoneTarget = nextMilestone != default ? nextMilestone.Target : 0;

    var activeEvent = engine.EventSystem.ActiveEvent;

    var state = new ServerState(
        Tick:                      engine.TickCount,
        Paused:                    paused,
        Population:                engine.Population.Population,
        MaxCapacity:               maxCapacity,
        Balance:                   Math.Round(engine.Budget.Balance, 2),
        TaxPerTick:                Math.Round(engine.Budget.CalculateTaxIncome(), 2),
        CommercialIncomePerTick:   Math.Round(engine.Budget.CommercialIncomePerTick, 2),
        MaintenancePerTick:        Math.Round(engine.Budget.LastMaintenanceCost, 2),
        NetPerTick:                Math.Round(engine.Budget.NetIncomePerTick, 2),
        Happiness:                 Math.Round(engine.HappinessSystem.AverageHappiness(grid), 3),
        MilestoneReached:          milestone?.Name,
        Pollution:                 Math.Round(engine.PollutionSystem.AveragePollution(grid), 3),
        GameState:                 engine.MilestoneSystem.CurrentState.ToString(),
        Milestones:                engine.MilestoneSystem.Reached.Select(m => $"{m.Name} {m.Emoji} (tick {m.ReachedAtTick})").ToList(),
        Tiles:                     nonEmptyTiles,
        NextMilestoneName:         nextMilestoneName,
        NextMilestoneTarget:       nextMilestoneTarget,
        ActiveEventName:           activeEvent?.Name,
        ActiveEventDescription:    activeEvent?.Description,
        LatestEventBanner:         engine.LatestEventBanner,
        TaxModifier:               engine.Budget.TaxModifier,
        SessionId:                 sessionId.Length > 0 ? sessionId : null
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
    var budget     = new BudgetSystem(initialBalance: 10_000);
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
            // Industrial bottom-right quadrant
            for (var x = 17; x <= 24; x++)
            for (var y = 17; y <= 24; y++)
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

        default:
            // Wired starter city: plant → road → zones (all connected)
            grid.SetZone(10, 10, ZoneType.PowerPlant);
            grid.SetZone(10, 11, ZoneType.Road);
            grid.SetZone(10, 12, ZoneType.Road);
            grid.SetZone(9,  12, ZoneType.Residential);
            grid.SetZone(11, 12, ZoneType.Residential);
            grid.SetZone(9,  13, ZoneType.Residential);
            grid.SetZone(10, 13, ZoneType.Road);
            grid.SetZone(11, 13, ZoneType.Commercial);
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

record TileState(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("zone")] string Zone,
    [property: JsonPropertyName("hasPower")] bool HasPower,
    [property: JsonPropertyName("hasRoadAccess")] bool HasRoadAccess,
    [property: JsonPropertyName("population")] int Population,
    [property: JsonPropertyName("pollutionLevel")] double PollutionLevel,
    [property: JsonPropertyName("happiness")] double Happiness,
    [property: JsonPropertyName("hasDemandBoost")] bool HasDemandBoost);

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
    string? NextMilestoneName = null,
    int NextMilestoneTarget = 0,
    string? ActiveEventName = null,
    string? ActiveEventDescription = null,
    string? LatestEventBanner = null,
    double TaxModifier = 0.0,
    string? SessionId = null);

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
                    ZoneType.Residential => ("R", Green),
                    ZoneType.Commercial  => ("C", Blue),
                    ZoneType.Industrial  => ("I", Yellow),
                    ZoneType.Road        => ("░", Gray),
                    ZoneType.PowerPlant  => ("P", Red),
                    ZoneType.PowerLine   => ("╌", Cyan),
                    _                    => ("·", Gray),
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
        Console.WriteLine($"  │  Tax/tick   : ${budget.CalculateTaxIncome(),10:N2}                │");
        Console.WriteLine($"  │  Status     : {survived,-28}│");
        Console.WriteLine($"  │                                          │");
        Console.WriteLine($"  │  Zones ▸  {Green}R{Reset}:{grid.TilesOfType(ZoneType.Residential).Count(),3}  " +
                          $"{Blue}C{Reset}:{grid.TilesOfType(ZoneType.Commercial).Count(),3}  " +
                          $"{Yellow}I{Reset}:{grid.TilesOfType(ZoneType.Industrial).Count(),3}        │");
        Console.WriteLine($"  └──────────────────────────────────────────┘");
        Console.WriteLine();
    }
}
