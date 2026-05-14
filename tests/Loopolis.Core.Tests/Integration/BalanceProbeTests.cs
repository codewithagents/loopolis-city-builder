using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Integration;

/// <summary>Balance probe for petition, fatigue, and charter analysis.</summary>
[TestFixture]
public class BalanceProbeTests
{
    private static SimulationEngine BuildPoweredStart()
    {
        var grid = new CityGrid(32, 32);
        var budget = new BudgetSystem(4_000);
        var engine = new SimulationEngine(grid, budget, new PopulationSystem(), new PowerNetwork(), new RoadNetwork(), new DemandSystem());
        grid.SetFlatTerrain();
        grid.SetZone(5, 12, ZoneType.CoalPlant);
        for (var x = 6; x <= 16; x++) grid.SetZone(x, 12, ZoneType.Road);
        for (var x = 9; x <= 14; x++) grid.SetZone(x, 11, ZoneType.Residential);
        for (var x = 9; x <= 14; x++) grid.SetZone(x, 10, ZoneType.Residential);
        grid.SetZone(9,  13, ZoneType.Commercial);
        grid.SetZone(10, 13, ZoneType.Commercial);
        grid.SetZone(11, 13, ZoneType.Commercial);
        grid.SetZone(6,  13, ZoneType.Industrial);
        grid.SetZone(7,  13, ZoneType.Industrial);
        grid.SetZone(8,  13, ZoneType.FireStation);
        grid.SetZone(13, 13, ZoneType.PoliceStation);
        grid.SetZone(15, 11, ZoneType.School);
        engine.SeedRoadGraphFromGrid();
        return engine;
    }

    [Test]
    public void ProbeAllScenarios()
    {
        // ─── powered_start 1000 ticks ───────────────────────────────────────────
        var e1 = BuildPoweredStart();
        int cumulativePetitions = 0;
        var petitionLog = new List<string>();
        for (var tick = 0; tick < 1000; tick++)
        {
            e1.Tick();
            foreach (var p in e1.PetitionSystem.NewThisTick)
            {
                cumulativePetitions++;
                petitionLog.Add($"  t={tick} ISSUED [{p.Category}] {p.DistrictName} dl={p.DeadlineTick}");
            }
        }
        var g1 = e1.Grid;
        Console.WriteLine("=== powered_start 1000 ticks ===");
        Console.WriteLine($"Pop: {e1.Population.Population} | Balance: {e1.Budget.Balance:N0} | Happiness: {e1.HappinessSystem.AverageHappiness(g1):F3}");
        Console.WriteLine($"GameState: {e1.MilestoneSystem.CurrentState} | NetPerTick: {e1.Budget.NetIncomePerTick:N2}");
        Console.WriteLine($"Employment: AvailableJobs={e1.EmploymentSystem.AvailableJobs} RequiredJobs={e1.EmploymentSystem.RequiredJobs} Ratio={e1.EmploymentSystem.EmploymentRatio:P1}");
        Console.WriteLine($"Petitions issued total: {cumulativePetitions}");
        Console.WriteLine($"Expired unresolved: {e1.PetitionSystem.ExpiredPetitions.Count}");
        Console.WriteLine($"Active at end: {e1.PetitionSystem.ActivePetitions.Count}");
        foreach (var p in e1.PetitionSystem.ActivePetitions)
            Console.WriteLine($"  [{p.Category}] {p.DistrictName} issued={p.IssuedTick}");
        Console.WriteLine("Petition log (first 15):");
        foreach (var entry in petitionLog.Take(15)) Console.WriteLine(entry);
        if (petitionLog.Count > 15) Console.WriteLine($"  ...and {petitionLog.Count - 15} more");
        Console.WriteLine($"ServiceFatigue active: {e1.ServiceFatigue.IsActive}");
        Console.WriteLine($"Degraded tiles: {e1.ServiceFatigue.DeprecatedTiles.Count()}");
        var snap = e1.ServiceFatigue.GetSnapshot();
        Console.WriteLine($"All tracked service tiles ({snap.Count}):");
        foreach (var kv in snap)
        {
            var z = g1.GetTile(kv.Key.x, kv.Key.y).Zone;
            Console.WriteLine($"  ({kv.Key.x},{kv.Key.y}) {z}: {kv.Value:P1}");
        }
        Console.WriteLine($"Advisory: [{e1.CurrentAdvice.Category}] {e1.CurrentAdvice.Text}");

        // ─── no_power 400 ticks ───────────────────────────────────────────────
        var grid4 = new CityGrid(32, 32);
        grid4.SetFlatTerrain();
        grid4.SetZone(5, 5, ZoneType.Road);
        grid4.SetZone(6, 5, ZoneType.Residential);
        grid4.SetZone(7, 5, ZoneType.Residential);
        grid4.SetZone(8, 5, ZoneType.Residential);
        var e4 = new SimulationEngine(grid4, new BudgetSystem(4_000), new PopulationSystem(), new PowerNetwork(), new RoadNetwork(), new DemandSystem());
        e4.SeedRoadGraphFromGrid();
        int pets4 = 0;
        for (var tick = 0; tick < 400; tick++) { e4.Tick(); pets4 += e4.PetitionSystem.NewThisTick.Count; }
        Console.WriteLine("\n=== no_power 400 ticks ===");
        Console.WriteLine($"Pop: {e4.Population.Population} | Balance: {e4.Budget.Balance:N0} | Happiness: {e4.HappinessSystem.AverageHappiness(grid4):F3}");
        Console.WriteLine($"GameState: {e4.MilestoneSystem.CurrentState}");
        Console.WriteLine($"Petitions issued: {pets4}");
        Console.WriteLine($"Active: {e4.PetitionSystem.ActivePetitions.Count} | Expired: {e4.PetitionSystem.ExpiredPetitions.Count}");
        Console.WriteLine($"ServiceFatigue active: {e4.ServiceFatigue.IsActive}");
        Console.WriteLine($"Advisory: [{e4.CurrentAdvice.Category}] {e4.CurrentAdvice.Text}");

        // ─── boom_town / city_challenge: headless note ─────────────────────────
        Console.WriteLine("\n=== boom_town / city_challenge note ===");
        Console.WriteLine("These scenarios start empty (64x64 grid + 3 roads, no zones placed).");
        Console.WriteLine("The headless runner has no player agent to place tiles.");
        Console.WriteLine("Result: 0 pop for entire run — not a simulation bug, but runner limitation.");

        Assert.Pass("Probe complete");
    }
}
