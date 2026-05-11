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
            NetPerTick: Math.Round(budget.NetIncomePerTick, 2)
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
        History: tickHistory
    );
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}

// ── Server mode ──────────────────────────────────────────────────────────────

static void RunServer(string scenario, double initialSpeed)
{
    var sharedDir = Path.Combine(FindSolutionRoot(), "godot", "shared");
    Directory.CreateDirectory(sharedDir);
    var statePath = Path.Combine(sharedDir, "state.json");
    var tmpPath   = Path.Combine(sharedDir, "state.tmp.json");
    var cmdPath   = Path.Combine(sharedDir, "command.json");

    var (grid, engine) = SetupScenario(scenario);

    var paused        = false;
    var speed         = initialSpeed;
    var skipRemaining = 0;
    var pauseAfterSkip = false;

    WriteState(tmpPath, statePath, engine, grid, paused);
    Console.WriteLine($"[server] Started. Scenario: {scenario}, Speed: {speed} t/s");
    Console.WriteLine($"[server] State: {statePath}");
    Console.WriteLine($"[server] Commands: {cmdPath}");

    while (true)
    {
        // 1. Read command
        ProcessCommand(cmdPath, ref paused, ref speed, ref skipRemaining, ref pauseAfterSkip, grid, engine);

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
                WriteState(tmpPath, statePath, engine, grid, paused);
            }
        }
        else if (!paused)
        {
            engine.Tick();
            WriteState(tmpPath, statePath, engine, grid, paused);
            Console.WriteLine(
                $"[tick {engine.TickCount}] pop={engine.Population.Population} balance=${engine.Budget.Balance:N2}");
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

static void ProcessCommand(
    string cmdPath,
    ref bool paused,
    ref double speed,
    ref int skipRemaining,
    ref bool pauseAfterSkip,
    CityGrid grid,
    SimulationEngine engine)
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
                    if (Enum.TryParse<ZoneType>(zone, out var zoneType))
                    {
                        grid.SetZone(x, y, zoneType);
                        Console.WriteLine($"[place_zone] ({x},{y}) => {zoneType}");
                    }
                    else
                    {
                        Console.WriteLine($"[place_zone] Unknown zone type: {zone}");
                    }
                }
                break;

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

            default:
                Console.WriteLine($"[command] Unknown: {cmd}");
                break;
        }
    }
}

static void WriteState(string tmpPath, string statePath, SimulationEngine engine, CityGrid grid, bool paused)
{
    var nonEmptyTiles = grid.AllTiles()
        .Where(t => t.Zone != ZoneType.Empty)
        .Select(t => new TileState(t.X, t.Y, t.Zone.ToString(), t.HasPower, t.HasRoadAccess))
        .ToList();

    var state = new ServerState(
        Tick:               engine.TickCount,
        Paused:             paused,
        Population:         engine.Population.Population,
        Balance:            Math.Round(engine.Budget.Balance, 2),
        TaxPerTick:         Math.Round(engine.Budget.CalculateTaxIncome(), 2),
        MaintenancePerTick: Math.Round(engine.Budget.LastMaintenanceCost, 2),
        NetPerTick:         Math.Round(engine.Budget.NetIncomePerTick, 2),
        Tiles:              nonEmptyTiles
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
    double NetPerTick);

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
    List<TickSnapshot> History);

record TileState(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("zone")] string Zone,
    [property: JsonPropertyName("hasPower")] bool HasPower,
    [property: JsonPropertyName("hasRoadAccess")] bool HasRoadAccess);

record ServerState(
    int Tick,
    bool Paused,
    int Population,
    double Balance,
    double TaxPerTick,
    double MaintenancePerTick,
    double NetPerTick,
    List<TileState> Tiles);

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
