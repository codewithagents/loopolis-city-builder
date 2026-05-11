using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class DemandSystemTests
{
    private DemandSystem _demand = null!;

    [SetUp]
    public void SetUp() => _demand = new DemandSystem();

    // Helper: make a tile fully ready (powered + road access)
    private static void MakeReady(CityGrid grid, int x, int y)
    {
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
    }

    [Test]
    public void NoCommercialNearby_ResidentialGetsBaselineDemand()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);

        _demand.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.0));
    }

    [Test]
    public void CommercialAdjacent_ResidentialGetsBoost()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        grid.SetZone(6, 5, ZoneType.Commercial);
        MakeReady(grid, 6, 5);

        _demand.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.5));
    }

    [Test]
    public void CommercialNotReady_NoBoost()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // Commercial placed adjacent but NOT powered (not ready)
        grid.SetZone(6, 5, ZoneType.Commercial);
        grid.SetRoadAccess(6, 5, true); // road only — no power

        _demand.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.0),
            "Only ready commercial zones should boost residential demand");
    }

    [Test]
    public void CommercialNoRoad_NoBoost()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // Commercial adjacent but no road access (not ready)
        grid.SetZone(6, 5, ZoneType.Commercial);
        grid.SetPower(6, 5, true); // power only — no road

        _demand.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.0),
            "Commercial without road access is not ready and should not boost demand");
    }

    [Test]
    public void DemandClearedOnRepropagate()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        grid.SetZone(6, 5, ZoneType.Commercial);
        MakeReady(grid, 6, 5);

        _demand.Propagate(grid);
        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.5));

        // Remove commercial zone
        grid.SetZone(6, 5, ZoneType.Empty);

        _demand.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.0),
            "Demand should reset to baseline after commercial is removed");
    }

    [Test]
    public void IndustrialAdjacentToCommercial_CommercialGetsBoost()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Commercial);
        MakeReady(grid, 5, 5);
        grid.SetZone(6, 5, ZoneType.Industrial);
        MakeReady(grid, 6, 5);

        _demand.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.5),
            "Commercial adjacent to ready industrial should get a demand boost");
    }

    [Test]
    public void IndustrialNotReady_CommercialNoBoost()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Commercial);
        MakeReady(grid, 5, 5);
        // Industrial adjacent but not ready (no power)
        grid.SetZone(6, 5, ZoneType.Industrial);
        grid.SetRoadAccess(6, 5, true);

        _demand.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.0),
            "Unready industrial should not boost commercial demand");
    }

    [Test]
    public void ResidentialNotReady_NoBoostApplied()
    {
        var grid = new CityGrid(10, 10);
        // Residential with no services (not ready) adjacent to ready commercial
        grid.SetZone(5, 5, ZoneType.Residential);
        // Not making it ready — zone not powered/no road
        grid.SetZone(6, 5, ZoneType.Commercial);
        MakeReady(grid, 6, 5);

        _demand.Propagate(grid);

        // Unready residential doesn't get demand checked (stays at 1.0)
        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.0),
            "Unready residential tiles should not have demand applied to them");
    }

    [Test]
    public void MultipleResidentialZones_OnlyWithinRadius3GetsBoost()
    {
        var grid = new CityGrid(15, 15);
        // Residential at (5,5) — within Chebyshev-3 of commercial at (6,5) (distance = 1)
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // Residential at (1,5) — Chebyshev distance 5 from commercial at (6,5) → outside radius
        grid.SetZone(1, 5, ZoneType.Residential);
        MakeReady(grid, 1, 5);
        // Commercial at (6,5)
        grid.SetZone(6, 5, ZoneType.Commercial);
        MakeReady(grid, 6, 5);

        _demand.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.5),
            "Residential within Chebyshev-3 of commercial should get boosted");
        Assert.That(grid.GetTile(1, 5).DemandFactor, Is.EqualTo(1.0),
            "Residential more than 3 tiles away from commercial should stay at baseline");
    }

    [Test]
    public void EmptyGrid_PropagateDoesNotThrow()
    {
        var grid = new CityGrid(10, 10);

        Assert.DoesNotThrow(() => _demand.Propagate(grid));
    }

    [Test]
    public void NewGrid_AllDemandFactorsAreBaseline()
    {
        var grid = new CityGrid(5, 5);

        Assert.That(grid.AllTiles().All(t => t.DemandFactor == 1.0), Is.True);
    }
}
