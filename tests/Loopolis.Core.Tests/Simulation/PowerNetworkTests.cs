using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class PowerNetworkTests
{
    private PowerNetwork _power = null!;

    [SetUp]
    public void SetUp() => _power = new PowerNetwork();

    [Test]
    public void NoPlants_NothingGetsPower()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);

        _power.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).HasPower, Is.False);
        Assert.That(_power.PoweredTileCount, Is.EqualTo(0));
    }

    [Test]
    public void PowerPlant_ItselfGetsPower()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant);

        _power.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).HasPower, Is.True);
    }

    [Test]
    public void PowerPlant_AdjacentResidential_GetsPower()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant);
        grid.SetZone(6, 5, ZoneType.Residential);

        _power.Propagate(grid);

        Assert.That(grid.GetTile(6, 5).HasPower, Is.True);
    }

    [Test]
    public void PowerPlant_ConnectedViaRoad_PropagatesThrough()
    {
        // Plant → Road → Road → Residential
        var grid = new CityGrid(10, 10);
        grid.SetZone(1, 5, ZoneType.PowerPlant);
        grid.SetZone(2, 5, ZoneType.Road);
        grid.SetZone(3, 5, ZoneType.Road);
        grid.SetZone(4, 5, ZoneType.Residential);

        _power.Propagate(grid);

        Assert.That(grid.GetTile(2, 5).HasPower, Is.True);
        Assert.That(grid.GetTile(3, 5).HasPower, Is.True);
        Assert.That(grid.GetTile(4, 5).HasPower, Is.True);
    }

    [Test]
    public void EmptyTile_BreaksChain()
    {
        // Plant → Empty gap → Residential (no power)
        var grid = new CityGrid(10, 10);
        grid.SetZone(1, 5, ZoneType.PowerPlant);
        // tile (2,5) stays Empty — insulator
        grid.SetZone(3, 5, ZoneType.Residential);

        _power.Propagate(grid);

        Assert.That(grid.GetTile(3, 5).HasPower, Is.False);
    }

    [Test]
    public void PowerLine_ConduitAcrossEmptySpace()
    {
        // Plant → PowerLine → PowerLine → Residential
        var grid = new CityGrid(10, 10);
        grid.SetZone(1, 5, ZoneType.PowerPlant);
        grid.SetZone(2, 5, ZoneType.PowerLine);
        grid.SetZone(3, 5, ZoneType.PowerLine);
        grid.SetZone(4, 5, ZoneType.Residential);

        _power.Propagate(grid);

        Assert.That(grid.GetTile(4, 5).HasPower, Is.True);
    }

    [Test]
    public void MultiplePlants_EachPropagatesIndependently()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(0, 0, ZoneType.PowerPlant);
        grid.SetZone(1, 0, ZoneType.Residential); // powered by plant 1

        grid.SetZone(9, 9, ZoneType.PowerPlant);
        grid.SetZone(8, 9, ZoneType.Commercial);  // powered by plant 2

        _power.Propagate(grid);

        Assert.That(grid.GetTile(1, 0).HasPower, Is.True);
        Assert.That(grid.GetTile(8, 9).HasPower, Is.True);
    }

    [Test]
    public void Propagate_ClearsPreviousPowerState()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant);
        grid.SetZone(6, 5, ZoneType.Residential);

        _power.Propagate(grid); // first run — residential gets power

        // Now remove the plant (replace with empty)
        grid.SetZone(5, 5, ZoneType.Empty);

        _power.Propagate(grid); // second run — power should be gone

        Assert.That(grid.GetTile(6, 5).HasPower, Is.False);
    }

    [Test]
    public void PoweredTileCount_MatchesActualPoweredTiles()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant);
        grid.SetZone(6, 5, ZoneType.Residential);
        grid.SetZone(5, 6, ZoneType.Road);

        _power.Propagate(grid);

        var actualCount = grid.AllTiles().Count(t => t.HasPower);
        Assert.That(_power.PoweredTileCount, Is.EqualTo(actualCount));
    }

    [Test]
    public void FireStation_AdjacentToPowerLine_ReceivesPower()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(1, 5, ZoneType.PowerPlant);
        grid.SetZone(2, 5, ZoneType.PowerLine);
        grid.SetZone(3, 5, ZoneType.FireStation);

        _power.Propagate(grid);

        Assert.That(grid.GetTile(3, 5).HasPower, Is.True,
            "FireStation must receive power when connected to the grid");
    }

    [Test]
    public void PoliceStation_AdjacentToPowerLine_ReceivesPower()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(1, 5, ZoneType.PowerPlant);
        grid.SetZone(2, 5, ZoneType.PowerLine);
        grid.SetZone(3, 5, ZoneType.PoliceStation);

        _power.Propagate(grid);

        Assert.That(grid.GetTile(3, 5).HasPower, Is.True,
            "PoliceStation must receive power when connected to the grid");
    }

    [Test]
    public void School_AdjacentToPowerLine_ReceivesPower()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(1, 5, ZoneType.PowerPlant);
        grid.SetZone(2, 5, ZoneType.PowerLine);
        grid.SetZone(3, 5, ZoneType.School);

        _power.Propagate(grid);

        Assert.That(grid.GetTile(3, 5).HasPower, Is.True,
            "School must receive power when connected to the grid");
    }

    [Test]
    public void PowerPropagates_ThroughServiceBuildings()
    {
        // FireStation should act as a conductor, passing power to the zone behind it
        var grid = new CityGrid(10, 10);
        grid.SetZone(1, 5, ZoneType.PowerPlant);
        grid.SetZone(2, 5, ZoneType.FireStation);
        grid.SetZone(3, 5, ZoneType.Residential);

        _power.Propagate(grid);

        Assert.That(grid.GetTile(3, 5).HasPower, Is.True,
            "Power must propagate through service buildings to adjacent zones");
    }

    [Test]
    public void LargeConnectedCity_AllZonesGetPower()
    {
        // A realistic city block: plant in corner, roads forming a grid, zones filling blocks
        var grid = new CityGrid(10, 10);

        grid.SetZone(0, 0, ZoneType.PowerPlant);

        // Horizontal road across row 5
        for (var x = 0; x < 10; x++) grid.SetZone(x, 5, ZoneType.Road);

        // Vertical road down column 5
        for (var y = 0; y < 10; y++) grid.SetZone(5, y, ZoneType.Road);

        // Connect plant to road network via power line (start at y=1, plant stays at y=0)
        for (var y = 1; y <= 5; y++) grid.SetZone(0, y, ZoneType.PowerLine);

        // Residential blocks touching roads
        grid.SetZone(1, 4, ZoneType.Residential);
        grid.SetZone(6, 4, ZoneType.Residential);
        grid.SetZone(1, 6, ZoneType.Commercial);

        _power.Propagate(grid);

        Assert.That(grid.GetTile(1, 4).HasPower, Is.True);
        Assert.That(grid.GetTile(6, 4).HasPower, Is.True);
        Assert.That(grid.GetTile(1, 6).HasPower, Is.True);
    }
}
