using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class SimulationEngineTests
{
    private SimulationEngine BuildEngine(CityGrid? grid = null)
    {
        grid ??= new CityGrid(10, 10);
        return new SimulationEngine(
            grid,
            new BudgetSystem(initialBalance: 10_000),
            new PopulationSystem(),
            new PowerNetwork(),
            new RoadNetwork(),
            new DemandSystem()
        );
    }

    [Test]
    public void Tick_IncrementsTick()
    {
        var engine = BuildEngine();

        engine.Tick();

        Assert.That(engine.TickCount, Is.EqualTo(1));
    }

    [Test]
    public void MultipleTicks_TickCountAccumulates()
    {
        var engine = BuildEngine();

        engine.Tick();
        engine.Tick();
        engine.Tick();

        Assert.That(engine.TickCount, Is.EqualTo(3));
    }

    [Test]
    public void Tick_PropagatesPowerAndGrowsPopulation()
    {
        // Wired city: plant → road → residential
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant);
        grid.SetZone(5, 6, ZoneType.Road);
        grid.SetZone(4, 6, ZoneType.Residential);
        grid.SetZone(6, 6, ZoneType.Residential);

        var engine = BuildEngine(grid);

        for (var i = 0; i < 10; i++) engine.Tick();

        Assert.That(engine.Population.Population, Is.GreaterThan(0),
            "Wired residential zones should produce population after 10 ticks");
    }

    [Test]
    public void Tick_NoPower_NoPopulationGrowth()
    {
        // Zones with road but no power
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(6, 5, ZoneType.Residential);

        var engine = BuildEngine(grid);

        for (var i = 0; i < 50; i++) engine.Tick();

        Assert.That(engine.Population.Population, Is.EqualTo(0),
            "Residential zones without power should not grow");
    }

    [Test]
    public void Tick_UpdatesBudgetWithPopulation()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant);
        grid.SetZone(5, 6, ZoneType.Road);
        grid.SetZone(4, 6, ZoneType.Residential);

        var engine = BuildEngine(grid);

        for (var i = 0; i < 20; i++) engine.Tick();

        // After ticks, Budget.Population should match Population.Population
        Assert.That(engine.Budget.Population, Is.EqualTo(engine.Population.Population));
    }

    [Test]
    public void Tick_WithDemandBoost_GrowsFasterThanBaseline()
    {
        // Scenario A: residential with adjacent commercial (boosted)
        // Layout:
        //   (5,5) PowerPlant
        //   (5,6) Road  — provides road access to (4,6) R, (6,6) R, (5,7) C
        //   (4,6) Residential
        //   (6,6) Residential
        //   (5,7) Commercial — adjacent to (4,6) R and (6,6) R via road, also adjacent to road
        // Power flows: plant→road→residential, plant→road→commercial
        // Road access: R at (4,6) touches road at (5,6); C at (5,7) touches road at (5,6)
        // Adjacency: R at (4,6) is NOT adjacent to C at (5,7). Need to place C directly adjacent to R.
        //
        // Revised layout:
        //   (5,5) PowerPlant
        //   (5,6) Road
        //   (4,6) Residential   ← adjacent to road (5,6) and commercial (4,7)
        //   (4,7) Commercial    ← adjacent to residential (4,6) and road (5,7)
        //   (5,7) Road          ← gives (4,7) commercial road access

        var boostedGrid = new CityGrid(10, 10);
        boostedGrid.SetZone(5, 5, ZoneType.PowerPlant);
        boostedGrid.SetZone(5, 6, ZoneType.Road);
        boostedGrid.SetZone(4, 6, ZoneType.Residential); // powered via plant→road; road access via (5,6)
        boostedGrid.SetZone(5, 7, ZoneType.Road);
        boostedGrid.SetZone(4, 7, ZoneType.Commercial);  // adjacent to residential (4,6), road access via (5,7)

        var boostedEngine = new SimulationEngine(
            boostedGrid,
            new BudgetSystem(10_000),
            new PopulationSystem(),
            new PowerNetwork(),
            new RoadNetwork(),
            new DemandSystem()
        );

        // Scenario B: only residential, no commercial nearby (identical layout minus commercial)
        var baselineGrid = new CityGrid(10, 10);
        baselineGrid.SetZone(5, 5, ZoneType.PowerPlant);
        baselineGrid.SetZone(5, 6, ZoneType.Road);
        baselineGrid.SetZone(4, 6, ZoneType.Residential);
        // No commercial

        var baselineEngine = new SimulationEngine(
            baselineGrid,
            new BudgetSystem(10_000),
            new PopulationSystem(),
            new PowerNetwork(),
            new RoadNetwork(),
            new DemandSystem()
        );

        const int ticks = 10;
        for (var i = 0; i < ticks; i++)
        {
            boostedEngine.Tick();
            baselineEngine.Tick();
        }

        Assert.That(boostedEngine.Population.Population, Is.GreaterThan(baselineEngine.Population.Population),
            "Residential adjacent to commercial should grow faster than isolated residential");
    }

    [Test]
    public void Tick_MaintenanceCostsDeducted()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant); // costs 10/tick
        grid.SetZone(5, 6, ZoneType.Road);       // costs 1/tick

        var engine = BuildEngine(grid);
        var startBalance = engine.Budget.Balance;

        engine.Tick();

        // After 1 tick with no population: taxes = 0, maintenance > 0 → balance decreases
        Assert.That(engine.Budget.Balance, Is.LessThan(startBalance),
            "Maintenance costs should reduce balance each tick");
    }

    [Test]
    public void Tick_ExposedSystemsMatchExpectedState()
    {
        var grid = new CityGrid(10, 10);
        var engine = BuildEngine(grid);

        // All systems are exposed as public properties
        Assert.That(engine.Grid, Is.SameAs(grid));
        Assert.That(engine.Budget, Is.Not.Null);
        Assert.That(engine.Population, Is.Not.Null);
        Assert.That(engine.PowerNetwork, Is.Not.Null);
        Assert.That(engine.RoadNetwork, Is.Not.Null);
        Assert.That(engine.DemandSystem, Is.Not.Null);
    }
}
