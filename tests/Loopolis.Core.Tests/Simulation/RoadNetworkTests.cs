using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class RoadNetworkTests
{
    private RoadNetwork _roads = null!;

    [SetUp]
    public void SetUp() => _roads = new RoadNetwork();

    [Test]
    public void NoRoads_ZonesHaveNoAccess()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);

        _roads.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).HasRoadAccess, Is.False);
        Assert.That(_roads.AccessibleZoneCount, Is.EqualTo(0));
    }

    [Test]
    public void ResidentialAdjacentToRoad_GetsAccess()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(6, 5, ZoneType.Residential);

        _roads.Propagate(grid);

        Assert.That(grid.GetTile(6, 5).HasRoadAccess, Is.True);
    }

    [Test]
    public void CommercialAdjacentToRoad_GetsAccess()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(5, 6, ZoneType.Commercial);

        _roads.Propagate(grid);

        Assert.That(grid.GetTile(5, 6).HasRoadAccess, Is.True);
    }

    [Test]
    public void IndustrialAdjacentToRoad_GetsAccess()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(4, 5, ZoneType.Industrial);

        _roads.Propagate(grid);

        Assert.That(grid.GetTile(4, 5).HasRoadAccess, Is.True);
    }

    [Test]
    public void ZoneNotAdjacentToRoad_NoAccess()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(7, 5, ZoneType.Residential); // 2 tiles away — not adjacent

        _roads.Propagate(grid);

        Assert.That(grid.GetTile(7, 5).HasRoadAccess, Is.False);
    }

    [Test]
    public void RoadTile_DoesNotGetRoadAccessFlag()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(6, 5, ZoneType.Road);

        _roads.Propagate(grid);

        // Roads don't need road access — they ARE roads
        Assert.That(grid.GetTile(5, 5).HasRoadAccess, Is.False);
        Assert.That(grid.GetTile(6, 5).HasRoadAccess, Is.False);
    }

    [Test]
    public void PowerPlant_DoesNotGetRoadAccessFlag()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(6, 5, ZoneType.PowerPlant);

        _roads.Propagate(grid);

        Assert.That(grid.GetTile(6, 5).HasRoadAccess, Is.False);
    }

    [Test]
    public void Propagate_ClearsPreviousAccessState()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(6, 5, ZoneType.Residential);

        _roads.Propagate(grid); // first — has access

        grid.SetZone(5, 5, ZoneType.Empty); // remove road

        _roads.Propagate(grid); // second — access gone

        Assert.That(grid.GetTile(6, 5).HasRoadAccess, Is.False);
    }

    [Test]
    public void AccessibleZoneCount_MatchesActualAccessibleZones()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(4, 5, ZoneType.Residential); // adjacent — has access
        grid.SetZone(6, 5, ZoneType.Commercial);  // adjacent — has access
        grid.SetZone(9, 9, ZoneType.Industrial);  // isolated — no access

        _roads.Propagate(grid);

        Assert.That(_roads.AccessibleZoneCount, Is.EqualTo(2));
    }

    [Test]
    public void IsReadyToDevelop_RequiresBothPowerAndRoadAccess()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);

        // Neither power nor road — not ready
        Assert.That(grid.GetTile(5, 5).IsReadyToDevelop, Is.False);

        // Road access only — not ready
        grid.SetRoadAccess(5, 5, true);
        Assert.That(grid.GetTile(5, 5).IsReadyToDevelop, Is.False);

        // Both power and road — ready
        grid.SetPower(5, 5, true);
        Assert.That(grid.GetTile(5, 5).IsReadyToDevelop, Is.True);
    }

    [Test]
    public void IsReadyToDevelop_PowerOnlyIsNotEnough()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPower(5, 5, true); // power but no road

        Assert.That(grid.GetTile(5, 5).IsReadyToDevelop, Is.False);
    }

    [Test]
    public void FullStreet_AllZonesOnBothSidesGetAccess()
    {
        var grid = new CityGrid(10, 10);

        // Road down the middle
        for (var y = 0; y < 10; y++) grid.SetZone(5, y, ZoneType.Road);

        // Residential on both sides
        for (var y = 0; y < 10; y++)
        {
            grid.SetZone(4, y, ZoneType.Residential);
            grid.SetZone(6, y, ZoneType.Residential);
        }

        _roads.Propagate(grid);

        for (var y = 0; y < 10; y++)
        {
            Assert.That(grid.GetTile(4, y).HasRoadAccess, Is.True,
                $"Left side tile (4,{y}) should have road access");
            Assert.That(grid.GetTile(6, y).HasRoadAccess, Is.True,
                $"Right side tile (6,{y}) should have road access");
        }

        Assert.That(_roads.AccessibleZoneCount, Is.EqualTo(20));
    }

    [Test]
    public void ClusterAccess_EntireClusterAccessible_WhenOneTileTouchesRoad()
    {
        var grid = new CityGrid(10, 10);
        // 2x2 Residential block at (2,2)-(3,3)
        grid.SetZone(2, 2, ZoneType.Residential);
        grid.SetZone(3, 2, ZoneType.Residential);
        grid.SetZone(2, 3, ZoneType.Residential);
        grid.SetZone(3, 3, ZoneType.Residential);
        // Road only touches (2,2) from the left
        grid.SetZone(1, 2, ZoneType.Road);

        _roads.Propagate(grid);

        // All four residential tiles should have road access via the cluster
        Assert.That(grid.GetTile(2, 2).HasRoadAccess, Is.True, "Tile (2,2) adjacent to road — should have access");
        Assert.That(grid.GetTile(3, 2).HasRoadAccess, Is.True, "Tile (3,2) not adjacent but in cluster — should have access");
        Assert.That(grid.GetTile(2, 3).HasRoadAccess, Is.True, "Tile (2,3) not adjacent but in cluster — should have access");
        Assert.That(grid.GetTile(3, 3).HasRoadAccess, Is.True, "Tile (3,3) corner of cluster — should have access");
        Assert.That(_roads.AccessibleZoneCount, Is.EqualTo(4));
    }

    [Test]
    public void ClusterAccess_IsolatedCluster_HasNoAccess()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetZone(6, 5, ZoneType.Residential);
        // No road anywhere near

        _roads.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).HasRoadAccess, Is.False);
        Assert.That(grid.GetTile(6, 5).HasRoadAccess, Is.False);
        Assert.That(_roads.AccessibleZoneCount, Is.EqualTo(0));
    }
}
