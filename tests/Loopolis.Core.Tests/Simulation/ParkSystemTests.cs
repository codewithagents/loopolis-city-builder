using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

/// <summary>
/// Tests for Park zone type:
///   - Happiness bonus (+0.10) granted to ready residential tiles within Chebyshev-2
///   - Bonus does not stack with multiple parks
///   - No bonus outside Chebyshev-2 radius
///   - Maintenance cost ($1/tick)
///   - Parks are not subject to road access requirements
///   - Parks do not produce population
/// </summary>
[TestFixture]
public class ParkSystemTests
{
    private HappinessSystem _happiness = null!;

    [SetUp]
    public void SetUp() => _happiness = new HappinessSystem();

    /// <summary>Helper: makes a residential tile fully ready (powered + road access).</summary>
    private static void MakeResidentialReady(CityGrid grid, int x, int y)
    {
        grid.SetZone(x, y, ZoneType.Residential);
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
    }

    // ── Happiness bonus ──────────────────────────────────────────────────────

    [Test]
    public void ParkTile_GivesHappinessBonus_ToAdjacentResidential()
    {
        // Park at (5,5), residential directly adjacent at (5,6)
        var grid = new CityGrid(10, 10);
        MakeResidentialReady(grid, 5, 6);
        grid.SetZone(5, 5, ZoneType.Park);

        _happiness.Propagate(grid);

        // base=0.6 + park=0.10 - neglect≈0.001 = ~0.699
        var happiness = grid.GetTile(5, 6).Happiness;
        Assert.That(happiness, Is.GreaterThan(0.6),
            "Residential tile adjacent to a park should have happiness > base 0.6");
        Assert.That(happiness, Is.EqualTo(0.7).Within(0.002),
            "Residential adjacent to park: base 0.6 + park bonus 0.10 ≈ 0.70");
    }

    [Test]
    public void ParkTile_GivesHappinessBonus_WithinChebyshev2()
    {
        // Park at (5,5), residential at (7,7) — Chebyshev distance = max(|7-5|, |7-5|) = 2 → within radius
        var grid = new CityGrid(15, 15);
        MakeResidentialReady(grid, 7, 7);
        grid.SetZone(5, 5, ZoneType.Park);

        _happiness.Propagate(grid);

        var happiness = grid.GetTile(7, 7).Happiness;
        Assert.That(happiness, Is.GreaterThan(0.6),
            "Residential at Chebyshev-2 from park should still receive +0.10 bonus");
    }

    [Test]
    public void ParkTile_NoBonus_OutsideRadius()
    {
        // Park at (5,5), residential at (8,5) — Chebyshev distance = 3 → outside radius
        var grid = new CityGrid(15, 15);
        MakeResidentialReady(grid, 8, 5);
        grid.SetZone(5, 5, ZoneType.Park);

        _happiness.Propagate(grid);

        var happiness = grid.GetTile(8, 5).Happiness;
        // base=0.6 + no park bonus, minus first-tick neglect (0.001)
        Assert.That(happiness, Is.EqualTo(0.6).Within(0.002),
            "Residential at Chebyshev distance 3 from park should NOT receive park bonus");
    }

    [Test]
    public void ParkTile_BonusDoesNotStack_MultiplePark()
    {
        // Two parks at distance 1 and distance 2 from the residential tile.
        // The bonus should be applied at most once.
        var grid = new CityGrid(15, 15);
        MakeResidentialReady(grid, 7, 7);
        grid.SetZone(6, 7, ZoneType.Park);  // distance 1
        grid.SetZone(5, 7, ZoneType.Park);  // distance 2

        _happiness.Propagate(grid);

        var happiness = grid.GetTile(7, 7).Happiness;
        // base=0.6 + ONE park bonus 0.10 = 0.70 (not 0.80)
        Assert.That(happiness, Is.EqualTo(0.7).Within(0.002),
            "Park bonus must not stack: two parks within radius should still give exactly +0.10");
        Assert.That(happiness, Is.LessThan(0.75),
            "Park bonus must not stack — second park should add nothing extra");
    }

    [Test]
    public void ParkTile_ExactlyAtRadius_ReceivesBonus()
    {
        // Park at (5,5), residential at (5,7) — Chebyshev distance = max(0, 2) = 2 → exactly at boundary
        var grid = new CityGrid(15, 15);
        MakeResidentialReady(grid, 5, 7);
        grid.SetZone(5, 5, ZoneType.Park);

        _happiness.Propagate(grid);

        var happiness = grid.GetTile(5, 7).Happiness;
        Assert.That(happiness, Is.GreaterThan(0.6),
            "Residential exactly at Chebyshev-2 boundary should receive park bonus");
    }

    [Test]
    public void ParkTile_NoHappiness_ForNonResidential()
    {
        // Commercial and industrial tiles don't use the happiness formula at all.
        // Park bonus applies only to residential tiles.
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Park);
        grid.SetZone(5, 6, ZoneType.Commercial);
        grid.SetPower(5, 6, true);
        grid.SetRoadAccess(5, 6, true);

        _happiness.Propagate(grid);

        // Commercial tile happiness stays at default (1.0 — happiness is only computed for Residential)
        var happiness = grid.GetTile(5, 6).Happiness;
        Assert.That(happiness, Is.EqualTo(1.0),
            "Commercial tiles should not be affected by the park happiness bonus");
    }

    // ── Budget maintenance ───────────────────────────────────────────────────

    [Test]
    public void ParkTile_HasMaintenanceCost()
    {
        // Verify BudgetSystem.MaintenanceCostPerTile includes Park at $1.0/tick
        Assert.That(BudgetSystem.MaintenanceCostPerTile.ContainsKey(ZoneType.Park), Is.True,
            "MaintenanceCostPerTile dictionary must contain ZoneType.Park");
        Assert.That(BudgetSystem.MaintenanceCostPerTile[ZoneType.Park], Is.EqualTo(1.0),
            "Park maintenance cost must be $1.00 per tick");
    }

    [Test]
    public void ParkTile_MaintenanceCost_IncludedInCalculation()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Park);

        var budget = new BudgetSystem();
        var cost = budget.CalculateMaintenanceCost(grid);

        Assert.That(cost, Is.EqualTo(1.0).Within(0.001),
            "Grid with one park tile should have $1.00/tick maintenance");
    }

    // ── Grid rules ───────────────────────────────────────────────────────────

    [Test]
    public void ParkTile_DoesNotRequireRoadAccess()
    {
        // Parks should not appear in ZonesThatNeedRoads —
        // verify by checking that after RoadNetwork.Propagate, a park with no adjacent road
        // doesn't get HasRoadAccess = true, but more importantly RoadNetwork doesn't ERROR
        // and the park tile IsReadyToDevelop == true regardless.
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Park);

        var roads = new RoadNetwork();
        roads.Propagate(grid);

        // Park has no road adjacent — HasRoadAccess will be false (road network doesn't set it)
        // but IsReadyToDevelop for Park is always true (doesn't require road access)
        Assert.That(grid.GetTile(5, 5).IsReadyToDevelop, Is.True,
            "Park IsReadyToDevelop should be true even without road access");
    }

    [Test]
    public void ParkTile_DoesNotRequirePower()
    {
        // Park tiles are always ready — no power needed.
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Park);
        // HasPower defaults to false — park should still be ready.

        Assert.That(grid.GetTile(5, 5).IsReadyToDevelop, Is.True,
            "Park IsReadyToDevelop should be true even without power");
    }

    [Test]
    public void ParkTile_CanBePlacedOnFlatTerrain()
    {
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();

        grid.SetZone(5, 5, ZoneType.Park);

        Assert.That(grid.GetTile(5, 5).Zone, Is.EqualTo(ZoneType.Park),
            "Park tile should be placeable on flat terrain");
    }
}
