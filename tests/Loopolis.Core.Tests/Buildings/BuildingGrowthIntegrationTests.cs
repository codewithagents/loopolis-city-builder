using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Buildings;

/// <summary>
/// Integration tests verifying BuildingGrowthSystem works end-to-end
/// through SimulationEngine (which wires Initialize + TryGrow each tick).
/// </summary>
[TestFixture]
public class BuildingGrowthIntegrationTests
{
    private static SimulationEngine MakeEngine(CityGrid grid)
    {
        var budget = new BudgetSystem();
        var pop = new PopulationSystem();
        var power = new PowerNetwork();
        var roads = new RoadNetwork();
        var demand = new DemandSystem();
        return new SimulationEngine(grid, budget, pop, power, roads, demand);
    }

    [Test]
    public void Engine_InitializesBuildings_OnFirstTick()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 4, ZoneType.PowerPlant);  // power plant at (5,4)
        grid.SetZone(5, 5, ZoneType.Road);         // road connected to power plant
        grid.SetZone(4, 5, ZoneType.Residential);  // residential tile adjacent to road
        grid.SetZone(6, 5, ZoneType.Residential);  // residential tile adjacent to road

        var engine = MakeEngine(grid);
        engine.Tick(); // first tick should call Initialize

        Assert.That(grid.Buildings.Count, Is.EqualTo(2),
            "Two road-adjacent residential tiles should each get a building after first tick");
        Assert.That(grid.GetTile(4, 5).BuildingId, Is.Not.Null);
        Assert.That(grid.GetTile(6, 5).BuildingId, Is.Not.Null);
    }

    [Test]
    public void Engine_GrowsTownhouse_When2x2BlockFillsUp()
    {
        // 2x2 residential block, road adjacent and powered
        var grid = new CityGrid(10, 10);

        // Power plant connected directly to the road
        grid.SetZone(2, 8, ZoneType.PowerPlant);  // PP at (2,8)
        grid.SetZone(3, 8, ZoneType.Road);         // road adjacent to PP
        grid.SetZone(4, 8, ZoneType.Road);

        // 2x2 residential block: bottom row adjacent to road
        grid.SetZone(3, 6, ZoneType.Residential);
        grid.SetZone(4, 6, ZoneType.Residential);
        grid.SetZone(3, 7, ZoneType.Residential);
        grid.SetZone(4, 7, ZoneType.Residential);

        var engine = MakeEngine(grid);

        // Run 200 ticks — road-adjacent tiles (3,7) and (4,7) at row 7 get buildings
        // and grow to full capacity, triggering 2x2 growth
        for (var i = 0; i < 200; i++) engine.Tick();

        var multiTile = grid.Buildings.Values.Where(b => b.TileCount > 1).ToList();
        Assert.That(multiTile.Count, Is.GreaterThanOrEqualTo(1),
            "After 200 ticks with 2x2 residential block adjacent to road, a 2x2 townhouse should form");
        Assert.That(multiTile[0].TypeId, Is.EqualTo("res_townhouse_2x2"));
    }

    [Test]
    public void Engine_InteriorTileWithoutBuilding_DoesNotDevelop()
    {
        var grid = new CityGrid(10, 10);

        // Power plant at (1,1), connected road at (1,2) running to (5,8)
        grid.SetZone(1, 1, ZoneType.PowerPlant);
        for (var y = 2; y <= 8; y++) grid.SetZone(1, y, ZoneType.Road);
        for (var x = 2; x <= 5; x++) grid.SetZone(x, 8, ZoneType.Road);

        // 1x3 column at x=5: tiles (5,5), (5,6), (5,7) — only (5,7) is road adjacent (road at y=8)
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetZone(5, 6, ZoneType.Residential);
        grid.SetZone(5, 7, ZoneType.Residential);

        var engine = MakeEngine(grid);

        // Run enough ticks for (5,7) to grow to cap
        for (var i = 0; i < 100; i++) engine.Tick();

        // (5,7) should have developed (road adjacent)
        Assert.That(grid.GetPopulation(5, 7), Is.GreaterThan(0),
            "Road-adjacent tile at (5,7) should develop");

        // (5,5) and (5,6) should remain at 0 (no road access, no building unless promoted via growth)
        // Note: they might get a building if the system grows (5,7) into a 1x3, but that type doesn't exist in catalog
        // so they stay at 0
        var topTilePop = grid.GetPopulation(5, 5);
        var midTilePop = grid.GetPopulation(5, 6);

        // Without multi-tile buildings reaching them, they should be 0
        // (the catalog has no 1x3 building type)
        Assert.That(topTilePop, Is.EqualTo(0),
            "Interior tile (5,5) with no road access and no multi-tile building should not develop");
    }
}
