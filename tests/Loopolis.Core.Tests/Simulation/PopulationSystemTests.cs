using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class PopulationSystemTests
{
    private PopulationSystem _pop = null!;

    [SetUp]
    public void SetUp() => _pop = new PopulationSystem();

    // Helper: mark a tile as fully ready (powered + road access) AND give it a BuildingId
    private static void MakeReady(CityGrid grid, int x, int y)
    {
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
        grid.SetBuildingId(x, y, "test");
    }

    [Test]
    public void EmptyGrid_PopulationStaysZero()
    {
        var grid = new CityGrid(10, 10);

        _pop.Tick(grid);

        Assert.That(_pop.Population, Is.EqualTo(0));
    }

    [Test]
    public void ReadyZone_PopulationGrows()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);

        _pop.Tick(grid);

        Assert.That(_pop.Population, Is.GreaterThan(0));
    }

    [Test]
    public void UnpoweredZone_PopulationDoesNotGrow()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetRoadAccess(5, 5, true); // road but no power
        grid.SetBuildingId(5, 5, "test");

        _pop.Tick(grid);

        Assert.That(_pop.Population, Is.EqualTo(0));
    }

    [Test]
    public void ZoneWithoutRoad_PopulationDoesNotGrow()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPower(5, 5, true); // power but no road — BuildingGrowthSystem would NOT give it a building
        // Intentionally no SetBuildingId — simulates interior tile without road access

        _pop.Tick(grid);

        Assert.That(_pop.Population, Is.EqualTo(0));
    }

    [Test]
    public void InactiveZones_DoNotCauseDecline()
    {
        // Key regression: unconnected zones sitting empty shouldn't drag down the city
        var grid = new CityGrid(10, 10);

        // 1 ready zone
        grid.SetZone(1, 1, ZoneType.Residential);
        MakeReady(grid, 1, 1);

        // 5 completely unserviced zones (no power, no road, no BuildingId)
        for (var x = 3; x <= 7; x++)
            grid.SetZone(x, 5, ZoneType.Residential); // no power, no road

        // Run many ticks — population should grow, not stay at zero
        for (var i = 0; i < 50; i++) _pop.Tick(grid);

        Assert.That(_pop.Population, Is.GreaterThan(0),
            "Inactive zones should not prevent growth of ready zones");
    }

    [Test]
    public void PopulationCapsAtCapacity()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);

        // Run long enough to hit capacity
        for (var i = 0; i < 500; i++) _pop.Tick(grid);

        Assert.That(_pop.Population, Is.EqualTo(50)); // 1 zone × 50 residents
    }

    [Test]
    public void LostServices_PopulationDeclines()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);

        // Grow to capacity
        for (var i = 0; i < 200; i++) _pop.Tick(grid);
        Assert.That(_pop.Population, Is.EqualTo(50));

        // Remove all services — zone is no longer ready
        grid.SetPower(5, 5, false);
        grid.SetRoadAccess(5, 5, false);
        grid.SetBuildingId(5, 5, null);

        // Run more ticks — population should decline toward new capacity (0)
        for (var i = 0; i < 100; i++) _pop.Tick(grid);

        Assert.That(_pop.Population, Is.EqualTo(0),
            "Population should decline when services are lost");
    }

    [Test]
    public void MoreReadyZones_HigherCapacity()
    {
        var grid = new CityGrid(10, 10);

        for (var x = 0; x < 5; x++)
        {
            grid.SetZone(x, 5, ZoneType.Residential);
            MakeReady(grid, x, 5);
        }

        for (var i = 0; i < 500; i++) _pop.Tick(grid);

        Assert.That(_pop.Population, Is.EqualTo(250)); // 5 zones × 50 residents
    }

    [Test]
    public void AddingZones_IncreasesCapacityAndGrowth()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);

        for (var i = 0; i < 200; i++) _pop.Tick(grid); // reach capacity: 50
        Assert.That(_pop.Population, Is.EqualTo(50));

        // Add a second zone
        grid.SetZone(6, 5, ZoneType.Residential);
        MakeReady(grid, 6, 5);

        for (var i = 0; i < 300; i++) _pop.Tick(grid); // grow to new capacity: 100

        Assert.That(_pop.Population, Is.EqualTo(100));
    }

    [Test]
    public void InteriorTile_CanGrow_WhenAdjacentNeighbourHasSufficientPopulation()
    {
        // With the BuildingGrowthSystem, interior tiles get a BuildingId when they
        // are absorbed into a multi-tile building. This test verifies that a tile
        // with a BuildingId (simulating being part of a multi-tile building) DOES grow.
        var grid = new CityGrid(10, 10);

        // Tile (5,5): road-adjacent, powered, has BuildingId — will grow
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetZone(4, 5, ZoneType.Road);
        MakeReady(grid, 5, 5);

        // Tile (6,5): interior — no direct road, but powered AND has a BuildingId
        // (simulates being part of a 2x1 building that was grown to cover this tile)
        grid.SetZone(6, 5, ZoneType.Residential);
        grid.SetPower(6, 5, true);
        grid.SetBuildingId(6, 5, "test"); // has building assignment → can develop

        // Both tiles should develop
        for (var i = 0; i < 200; i++) _pop.Tick(grid);

        Assert.That(grid.GetPopulation(5, 5), Is.GreaterThanOrEqualTo(25),
            "Road-adjacent tile must reach pop 25");
        Assert.That(grid.GetPopulation(6, 5), Is.GreaterThan(0),
            "Interior tile with BuildingId should develop");
    }

    [Test]
    public void Commercial_GrowsWhenAdjacentToResidents()
    {
        // Arrange: commercial tile powered + road-adjacent, residential neighbour with pop 30
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Commercial);
        grid.SetZone(5, 6, ZoneType.Road);
        grid.SetZone(5, 4, ZoneType.Residential);
        grid.SetPower(5, 5, true);
        grid.SetPower(5, 4, true);
        grid.SetRoadAccess(5, 5, true);
        grid.SetBuildingId(5, 5, "test");
        grid.SetPopulation(5, 4, 30); // residential neighbours present

        _pop.Tick(grid);

        Assert.That(grid.GetTile(5, 5).Population, Is.GreaterThan(0));
    }

    [Test]
    public void Commercial_GrowsWithoutAdjacentResidents_AtMinimumRate()
    {
        // Regression: decline check was overwriting growth when adjacentResidential < 5.
        // Commercial tiles with no residential neighbours were stuck oscillating at 0-1 activity.
        // Fix: decline is only applied as else-if, never overwriting a positive growth step.
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Commercial);
        grid.SetPower(5, 5, true);
        grid.SetRoadAccess(5, 5, true);
        grid.SetBuildingId(5, 5, "test");
        // No residential neighbours at all

        for (var i = 0; i < 60; i++) _pop.Tick(grid);

        Assert.That(grid.GetTile(5, 5).Population, Is.GreaterThanOrEqualTo(40),
            "Commercial must grow to near-capacity at minimum rate even without residential neighbours");
    }

    [Test]
    public void Commercial_DeclineDoesNotOverwriteGrowth_WhenResidentialBelowThreshold()
    {
        // Regression: the old if-then-overwrite pattern meant that any time
        // adjacentResidential < 5, the decline branch ran AFTER the growth branch,
        // resetting newPop back to current*(1-0.02) and erasing the growth.
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Commercial);
        grid.SetPower(5, 5, true);
        grid.SetRoadAccess(5, 5, true);
        grid.SetBuildingId(5, 5, "test");
        grid.SetPopulation(5, 5, 10); // pre-seeded activity

        // One tick with no adjacent residents (adjacentResidential = 0 < threshold 5)
        _pop.Tick(grid);

        // With the fix: growth runs (current < capacity), so pop should increase
        // With the bug: decline overwrites growth, pop drops from 10 to 9
        Assert.That(grid.GetTile(5, 5).Population, Is.GreaterThan(10),
            "Commercial with existing activity must grow even when no adjacent residential (<threshold)");
    }

    [Test]
    public void Industrial_GrowsWithPowerAndRoad()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Industrial);
        grid.SetZone(5, 6, ZoneType.Road);
        grid.SetPower(5, 5, true);
        grid.SetRoadAccess(5, 5, true);
        grid.SetBuildingId(5, 5, "test");

        _pop.Tick(grid);

        Assert.That(grid.GetTile(5, 5).Population, Is.GreaterThan(0));
    }

    [Test]
    public void Commercial_DoesNotCountTowardTotalPopulation()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Commercial);
        grid.SetPopulation(5, 5, 30);

        // Tick without any residential zones — Population should stay 0
        _pop.Tick(grid);

        Assert.That(_pop.Population, Is.EqualTo(0),
            "Commercial activity should not inflate the residential population count");
    }
}
