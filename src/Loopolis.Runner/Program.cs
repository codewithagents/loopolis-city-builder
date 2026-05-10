using System.Text.Json;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

// Loopolis SimulationRunner — the agent feedback loop tool
// Usage: dotnet run -- <ticks> [scenario] [--ascii]
// Output: JSON report for agent analysis, or ASCII city map

var ticks = args.Length > 0 ? int.Parse(args[0]) : 500;
var scenario = args.Length > 1 ? args[1] : "default";
var asciiMode = args.Contains("--ascii");

var grid = new CityGrid(32, 32);
var budget = new BudgetSystem(initialBalance: 10_000);
var population = new PopulationSystem();
var powerNetwork = new PowerNetwork();
var roadNetwork  = new RoadNetwork();

// --- Scenario Setup ---
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
        // Plant at (8,8) → line along y=8 from x=9 to x=15 (hits the road, power floods the network)
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
        grid.SetZone(10, 11, ZoneType.Road);   // road connects plant to zones
        grid.SetZone(10, 12, ZoneType.Road);
        grid.SetZone(9,  12, ZoneType.Residential);
        grid.SetZone(11, 12, ZoneType.Residential);
        grid.SetZone(9,  13, ZoneType.Residential);
        grid.SetZone(10, 13, ZoneType.Road);
        grid.SetZone(11, 13, ZoneType.Commercial);
        break;
}

// --- Simulation Loop ---
var tickHistory = new List<TickSnapshot>();

for (var tick = 0; tick < ticks; tick++)
{
    powerNetwork.Propagate(grid);
    roadNetwork.Propagate(grid);
    population.Tick(grid);
    budget.SetPopulation(population.Population);
    budget.CollectTaxes();
    budget.DeductMaintenance(grid);

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
    AsciiRenderer.Render(grid, budget, population, scenario, ticks);
}
else
{
    var residentialTiles       = grid.TilesOfType(ZoneType.Residential).ToList();
    var poweredResidential     = residentialTiles.Count(t => t.HasPower);
    var roadAccessResidential  = residentialTiles.Count(t => t.HasRoadAccess);
    var readyResidential       = residentialTiles.Count(t => t.IsReadyToDevelop);

    var report = new SimulationReport(
        Scenario: scenario,
        TotalTicks: ticks,
        FinalPopulation: population.Population,
        FinalBalance: Math.Round(budget.Balance, 2),
        Survived: !budget.IsInDeficit,
        ResidentialZones: residentialTiles.Count,
        PoweredResidentialZones: poweredResidential,
        RoadAccessResidentialZones: roadAccessResidential,
        ReadyResidentialZones: readyResidential,
        PoweredTiles: powerNetwork.PoweredTileCount,
        CommercialZones: grid.TilesOfType(ZoneType.Commercial).Count(),
        IndustrialZones: grid.TilesOfType(ZoneType.Industrial).Count(),
        History: tickHistory
    );
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}

// --- Records ---
record TickSnapshot(int Tick, int Population, double Balance, bool IsInDeficit, double TaxIncome, double MaintenanceCost, double NetPerTick);

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
    List<TickSnapshot> History
);

// --- ASCII Renderer ---
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
