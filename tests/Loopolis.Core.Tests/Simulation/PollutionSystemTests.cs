using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class PollutionSystemTests
{
    private PollutionSystem _pollution = null!;

    [SetUp]
    public void SetUp() => _pollution = new PollutionSystem();

    [Test]
    public void NoPollutionWithoutIndustrial()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetZone(3, 3, ZoneType.Commercial);

        _pollution.Propagate(grid);

        foreach (var tile in grid.AllTiles())
            Assert.That(tile.PollutionLevel, Is.EqualTo(0.0),
                $"Tile ({tile.X},{tile.Y}) should have no pollution without industrial zones");
    }

    [Test]
    public void IndustrialCenterTileHasMaxPollution()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Industrial);
        grid.SetPower(5, 5, true); // powered industrial emits pollution

        _pollution.Propagate(grid);

        // The source tile itself (distance=0) gets strength=1.0
        Assert.That(grid.GetTile(5, 5).PollutionLevel, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void PollutionDecaysWithDistance()
    {
        var grid = new CityGrid(15, 15);
        grid.SetZone(7, 7, ZoneType.Industrial);
        grid.SetPower(7, 7, true); // powered industrial emits pollution

        _pollution.Propagate(grid);

        // distance 1 (Manhattan=1, Euclidean=1.0): strength = 1 - 1/3 ≈ 0.667
        var at1 = grid.GetTile(7, 8).PollutionLevel;
        // distance 2 (Euclidean=2.0): strength = 1 - 2/3 ≈ 0.333
        var at2 = grid.GetTile(7, 9).PollutionLevel;

        Assert.That(at1, Is.GreaterThan(at2),
            "Pollution at distance 1 should be greater than at distance 2");
        Assert.That(at2, Is.GreaterThan(0.0),
            "Tile within radius should have non-zero pollution");
    }

    [Test]
    public void PollutionBeyondRadiusIsZero()
    {
        var grid = new CityGrid(15, 15);
        grid.SetZone(7, 7, ZoneType.Industrial);
        grid.SetPower(7, 7, true); // powered industrial emits pollution

        _pollution.Propagate(grid);

        // Distance 4 is beyond radius 3 — tile at (7, 11) is distance 4 from (7, 7)
        Assert.That(grid.GetTile(7, 11).PollutionLevel, Is.EqualTo(0.0),
            "Tile beyond pollution radius should have 0 pollution");

        // Also check diagonal — (7+3, 7+3) = (10, 10) has Euclidean distance sqrt(18) ≈ 4.24 > 3
        Assert.That(grid.GetTile(10, 10).PollutionLevel, Is.EqualTo(0.0),
            "Diagonal tile beyond radius should have 0 pollution");
    }

    [Test]
    public void MultipleSources_Accumulate()
    {
        // Single industrial source (powered)
        var gridSingle = new CityGrid(15, 15);
        gridSingle.SetZone(5, 5, ZoneType.Industrial);
        gridSingle.SetPower(5, 5, true);
        _pollution.Propagate(gridSingle);
        var singlePollution = gridSingle.GetTile(5, 6).PollutionLevel;

        // Two industrial sources, both reach tile (5, 6)
        var gridDouble = new CityGrid(15, 15);
        gridDouble.SetZone(5, 5, ZoneType.Industrial);
        gridDouble.SetPower(5, 5, true);
        gridDouble.SetZone(5, 7, ZoneType.Industrial); // also covers (5, 6)
        gridDouble.SetPower(5, 7, true);
        _pollution.Propagate(gridDouble);
        var doublePollution = gridDouble.GetTile(5, 6).PollutionLevel;

        Assert.That(doublePollution, Is.GreaterThan(singlePollution),
            "Two industrial sources should produce more pollution than one");
    }

    [Test]
    public void PollutionClearedOnRepropagate()
    {
        var grid = new CityGrid(15, 15);
        grid.SetZone(7, 7, ZoneType.Industrial);
        grid.SetPower(7, 7, true); // powered industrial emits pollution

        // First propagate — tiles get pollution
        _pollution.Propagate(grid);
        Assert.That(grid.GetTile(7, 7).PollutionLevel, Is.GreaterThan(0.0),
            "Industrial tile should have pollution after propagation");

        // Remove industrial, repropagate — pollution should be gone
        grid.SetZone(7, 7, ZoneType.Empty);
        _pollution.Propagate(grid);

        foreach (var tile in grid.AllTiles())
            Assert.That(tile.PollutionLevel, Is.EqualTo(0.0),
                $"Tile ({tile.X},{tile.Y}) should have no pollution after industrial removed");
    }

    [Test]
    public void AveragePollution_ReturnsZeroWithNoResidential()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Industrial);

        _pollution.Propagate(grid);

        // No residential zones → average is 0 by convention
        Assert.That(_pollution.AveragePollution(grid), Is.EqualTo(0.0));
    }

    [Test]
    public void AveragePollution_ResidentialNearIndustrial_IsPositive()
    {
        var grid = new CityGrid(15, 15);
        grid.SetZone(7, 7, ZoneType.Industrial);
        grid.SetPower(7, 7, true); // powered industrial emits pollution
        grid.SetZone(7, 9, ZoneType.Residential); // within radius

        _pollution.Propagate(grid);

        Assert.That(_pollution.AveragePollution(grid), Is.GreaterThan(0.0),
            "Residential zone within industrial radius should have positive average pollution");
    }
}
