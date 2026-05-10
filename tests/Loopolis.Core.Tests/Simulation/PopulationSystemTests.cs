using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class PopulationSystemTests
{
    private PopulationSystem _pop = null!;

    [SetUp]
    public void SetUp() => _pop = new PopulationSystem();

    // Helper: mark a tile as fully ready (powered + road access)
    private static void MakeReady(CityGrid grid, int x, int y)
    {
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
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

        _pop.Tick(grid);

        Assert.That(_pop.Population, Is.EqualTo(0));
    }

    [Test]
    public void ZoneWithoutRoad_PopulationDoesNotGrow()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPower(5, 5, true); // power but no road

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

        // 5 completely unserviced zones
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
}
