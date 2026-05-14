using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

/// <summary>
/// Tests for Park zone type:
///   - Happiness bonus: +0.08 per park tile within Chebyshev-3, capped at +0.20 total
///   - Multiple parks stack up to the cap (+0.20)
///   - No bonus outside Chebyshev-3 radius
///   - Maintenance cost: $3/tick per park tile
///   - Parks do not require road access or power (IsReadyToDevelop = true always)
///   - Parks do not produce population
///   - Park bonus only applies to Residential tiles (not Commercial or Industrial)
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
    public void Parks_IncreaseHappinessOfNearbyResidential()
    {
        // Park at (5,5), residential directly adjacent at (5,6)
        var grid = new CityGrid(10, 10);
        MakeResidentialReady(grid, 5, 6);
        grid.SetZone(5, 5, ZoneType.Park);

        _happiness.Propagate(grid);

        // base=0.6 + park=0.08 - neglect≈0.001 = ~0.679
        var happiness = grid.GetTile(5, 6).Happiness;
        Assert.That(happiness, Is.GreaterThan(0.6),
            "Residential tile adjacent to a park should have happiness > base 0.6");
        Assert.That(happiness, Is.EqualTo(0.68).Within(0.002),
            "Residential adjacent to one park: base 0.6 + park bonus 0.08 ≈ 0.68");
    }

    [Test]
    public void Parks_HappinessBonusCappedAt0Point20()
    {
        // Place 4+ parks within Chebyshev-3. 4 × 0.08 = 0.32 > cap of 0.20.
        // Total park contribution should be exactly 0.20, not 0.32+.
        var grid = new CityGrid(15, 15);
        MakeResidentialReady(grid, 7, 7);
        grid.SetZone(6, 7, ZoneType.Park);  // distance 1
        grid.SetZone(8, 7, ZoneType.Park);  // distance 1
        grid.SetZone(7, 6, ZoneType.Park);  // distance 1
        grid.SetZone(7, 8, ZoneType.Park);  // distance 1

        _happiness.Propagate(grid);

        var happiness = grid.GetTile(7, 7).Happiness;
        // base=0.6 + capped park bonus 0.20 = 0.80 (not 0.92 = 0.6 + 4×0.08)
        Assert.That(happiness, Is.EqualTo(0.80).Within(0.002),
            "Park bonus must be capped at +0.20 regardless of how many park tiles are nearby");
        Assert.That(happiness, Is.LessThanOrEqualTo(0.82),
            "Park contribution must not exceed +0.20 cap even with many parks");
    }

    [Test]
    public void Parks_NoEffectOnNonResidential()
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

    [Test]
    public void BudgetSystem_ParkMaintenanceCosts3PerTile()
    {
        // Verify BudgetSystem.MaintenanceCostPerTile includes Park at $3.0/tick
        Assert.That(BudgetSystem.MaintenanceCostPerTile.ContainsKey(ZoneType.Park), Is.True,
            "MaintenanceCostPerTile dictionary must contain ZoneType.Park");
        Assert.That(BudgetSystem.MaintenanceCostPerTile[ZoneType.Park], Is.EqualTo(3.0),
            "Park maintenance cost must be $3.00 per tick");
    }

    [Test]
    public void ParkTile_NoBonus_OutsideRadius()
    {
        // Park at (5,5), residential at (9,5) — Chebyshev distance = 4 → outside radius 3
        var grid = new CityGrid(15, 15);
        MakeResidentialReady(grid, 9, 5);
        grid.SetZone(5, 5, ZoneType.Park);

        _happiness.Propagate(grid);

        var happiness = grid.GetTile(9, 5).Happiness;
        // base=0.6 + no park bonus, minus first-tick neglect (0.001)
        Assert.That(happiness, Is.EqualTo(0.6).Within(0.002),
            "Residential at Chebyshev distance 4 from park should NOT receive park bonus");
    }

    [Test]
    public void ParkTile_TwoParks_StackToDoubleBonus()
    {
        // Two parks within radius 3, each contributing +0.08 → total +0.16 (under cap)
        var grid = new CityGrid(15, 15);
        MakeResidentialReady(grid, 7, 7);
        grid.SetZone(6, 7, ZoneType.Park);  // distance 1
        grid.SetZone(8, 7, ZoneType.Park);  // distance 1

        _happiness.Propagate(grid);

        var happiness = grid.GetTile(7, 7).Happiness;
        // base=0.6 + 2 × 0.08 = 0.76 (under cap, no clipping)
        Assert.That(happiness, Is.EqualTo(0.76).Within(0.002),
            "Two park tiles each add +0.08: total park contribution should be +0.16");
    }

    [Test]
    public void ParkTile_ExactlyAtRadius3_ReceivesBonus()
    {
        // Park at (5,5), residential at (5,8) — Chebyshev distance = max(0, 3) = 3 → exactly at boundary
        var grid = new CityGrid(15, 15);
        MakeResidentialReady(grid, 5, 8);
        grid.SetZone(5, 5, ZoneType.Park);

        _happiness.Propagate(grid);

        var happiness = grid.GetTile(5, 8).Happiness;
        Assert.That(happiness, Is.GreaterThan(0.6),
            "Residential exactly at Chebyshev-3 boundary should receive park bonus");
        Assert.That(happiness, Is.EqualTo(0.68).Within(0.002),
            "Residential exactly at Chebyshev-3: base 0.6 + park bonus 0.08 ≈ 0.68");
    }

    [Test]
    public void ParkTile_MaintenanceCost_IncludedInCalculation()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Park);

        var budget = new BudgetSystem();
        var cost = budget.CalculateMaintenanceCost(grid);

        Assert.That(cost, Is.EqualTo(3.0).Within(0.001),
            "Grid with one park tile should have $3.00/tick maintenance");
    }

    [Test]
    public void TwoParkTiles_MaintenanceCost_Is6PerTick()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(3, 3, ZoneType.Park);
        grid.SetZone(6, 6, ZoneType.Park);

        var budget = new BudgetSystem();
        var cost = budget.CalculateMaintenanceCost(grid);

        Assert.That(cost, Is.EqualTo(6.0).Within(0.001),
            "Grid with two park tiles should have $6.00/tick maintenance (2 × $3)");
    }

    // ── Grid rules ───────────────────────────────────────────────────────────

    [Test]
    public void ParkTile_DoesNotRequireRoadAccess()
    {
        // Parks should be IsReadyToDevelop == true regardless of road access
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Park);

        var roads = new RoadNetwork();
        roads.Propagate(grid);

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
    public void ParkTile_CannotBePlacedOnWater()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(5, 5, 0); // water

        grid.SetZone(5, 5, ZoneType.Park);

        Assert.That(grid.GetTile(5, 5).Zone, Is.Not.EqualTo(ZoneType.Park),
            "Park tile should not be placeable on water (height=0)");
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
