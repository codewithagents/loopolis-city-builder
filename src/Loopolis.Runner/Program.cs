using System.Text.Json;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

// Loopolis SimulationRunner — the agent feedback loop tool
// Usage: dotnet run -- <ticks> [scenario]
// Output: JSON report for agent analysis

var ticks = args.Length > 0 ? int.Parse(args[0]) : 500;
var scenario = args.Length > 1 ? args[1] : "default";

var grid = new CityGrid(32, 32);
var budget = new BudgetSystem(initialBalance: 10_000);
var population = new PopulationSystem();

// --- Scenario Setup ---
switch (scenario)
{
    case "no_power":
        // Residential zones with no power — tests decline
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetZone(6, 5, ZoneType.Residential);
        grid.SetZone(7, 5, ZoneType.Residential);
        break;

    case "powered_start":
        // Residential zones with power — tests growth
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetZone(6, 5, ZoneType.Residential);
        grid.SetZone(7, 5, ZoneType.Residential);
        break;

    default:
        // Basic starter city
        grid.SetZone(10, 10, ZoneType.PowerPlant);
        grid.SetZone(10, 11, ZoneType.Road);
        grid.SetZone(10, 12, ZoneType.Residential);
        grid.SetZone(11, 12, ZoneType.Residential);
        grid.SetZone(10, 13, ZoneType.Commercial);
        break;
}

// --- Simulation Loop ---
var tickHistory = new List<TickSnapshot>();

for (var tick = 0; tick < ticks; tick++)
{
    population.Tick(grid);
    budget.SetPopulation(population.Population);
    budget.CollectTaxes();
    budget.DeductCost(50); // base operating cost per tick

    if (tick % 100 == 0 || tick == ticks - 1)
    {
        tickHistory.Add(new TickSnapshot(
            Tick: tick,
            Population: population.Population,
            Balance: Math.Round(budget.Balance, 2),
            IsInDeficit: budget.IsInDeficit,
            TaxIncome: Math.Round(budget.CalculateTaxIncome(), 2)
        ));
    }
}

// --- Report ---
var report = new SimulationReport(
    Scenario: scenario,
    TotalTicks: ticks,
    FinalPopulation: population.Population,
    FinalBalance: Math.Round(budget.Balance, 2),
    Survived: !budget.IsInDeficit,
    ResidentialZones: grid.TilesOfType(ZoneType.Residential).Count(),
    CommercialZones: grid.TilesOfType(ZoneType.Commercial).Count(),
    IndustrialZones: grid.TilesOfType(ZoneType.Industrial).Count(),
    History: tickHistory
);

Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

// --- Records ---
record TickSnapshot(int Tick, int Population, double Balance, bool IsInDeficit, double TaxIncome);

record SimulationReport(
    string Scenario,
    int TotalTicks,
    int FinalPopulation,
    double FinalBalance,
    bool Survived,
    int ResidentialZones,
    int CommercialZones,
    int IndustrialZones,
    List<TickSnapshot> History
);
