using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

/// <summary>
/// Tests for M8 Phase 1: PoliceHQ, FireHQ, and Hospital service tiers.
///
/// Coverage:
///   - PoliceHQ covers radius 10 Manhattan
///   - FireHQ covers radius 10 Manhattan
///   - Hospital reduces EventPenalty by 50% for covered tiles
///   - Hospital coverage counted separately (hospitalCoveragePercent)
///   - HQ tiles count toward existing police/fire coverage (same category)
///   - City-milestone gate (population ≥ 5,000 required for HQ/Hospital placement)
/// </summary>
[TestFixture]
public class ServiceTierTests
{
    private HappinessSystem _happiness = null!;

    [SetUp]
    public void SetUp() => _happiness = new HappinessSystem();

    private static void MakeReady(CityGrid grid, int x, int y)
    {
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
    }

    // ── PoliceHQ ────────────────────────────────────────────────────────────────

    [Test]
    public void PoliceHQCoversRadius10()
    {
        // PoliceHQ at (0,0), residential at Manhattan distance 10 — should be covered
        var grid = new CityGrid(25, 5);
        grid.SetZone(0, 0, ZoneType.PoliceHQ);
        grid.SetZone(10, 0, ZoneType.Residential);
        MakeReady(grid, 10, 0);

        _happiness.Propagate(grid);

        // base 0.6 + 0.15 police coverage = 0.75 (covered, no neglect first tick)
        Assert.That(grid.GetTile(10, 0).Happiness, Is.EqualTo(0.75).Within(0.001),
            "PoliceHQ should provide +0.15 happiness to residential tiles at Manhattan distance 10");
    }

    [Test]
    public void PoliceHQDoesNotCoverBeyondRadius10()
    {
        // PoliceHQ at (0,0), residential at Manhattan distance 11 — should NOT be covered
        var grid = new CityGrid(25, 5);
        grid.SetZone(0, 0, ZoneType.PoliceHQ);
        grid.SetZone(11, 0, ZoneType.Residential);
        MakeReady(grid, 11, 0);

        _happiness.Propagate(grid);

        // base 0.6 only (uncovered, first-tick neglect -0.001 = 0.599)
        Assert.That(grid.GetTile(11, 0).Happiness, Is.EqualTo(0.6).Within(0.002),
            "PoliceHQ should not cover tiles beyond Manhattan distance 10");
    }

    [Test]
    public void PoliceHQExtendsPoliceStationCoverage()
    {
        // PoliceStation only covers radius 4. PoliceHQ covers radius 10.
        // A tile at distance 8 should be covered by PoliceHQ but not PoliceStation.
        var grid = new CityGrid(25, 5);
        grid.SetZone(0, 0, ZoneType.PoliceStation); // radius 4
        grid.SetZone(8, 0, ZoneType.Residential);
        MakeReady(grid, 8, 0);

        _happiness.Propagate(grid);
        var happinessWithStation = grid.GetTile(8, 0).Happiness;

        // Reset and use PoliceHQ instead
        var grid2 = new CityGrid(25, 5);
        grid2.SetZone(0, 0, ZoneType.PoliceHQ);     // radius 10
        grid2.SetZone(8, 0, ZoneType.Residential);
        MakeReady(grid2, 8, 0);
        var happiness2 = new HappinessSystem();
        happiness2.Propagate(grid2);
        var happinessWithHQ = grid2.GetTile(8, 0).Happiness;

        Assert.That(happinessWithStation, Is.EqualTo(0.6).Within(0.002),
            "PoliceStation at distance 8 should NOT cover the tile (radius only 4)");
        Assert.That(happinessWithHQ, Is.EqualTo(0.75).Within(0.001),
            "PoliceHQ at distance 8 SHOULD cover the tile (radius 10)");
    }

    // ── FireHQ ──────────────────────────────────────────────────────────────────

    [Test]
    public void FireHQCoversRadius10()
    {
        // FireHQ at (0,0), residential at Manhattan distance 10 — should be covered
        var grid = new CityGrid(25, 5);
        grid.SetZone(0, 0, ZoneType.FireHQ);
        grid.SetZone(10, 0, ZoneType.Residential);
        MakeReady(grid, 10, 0);

        _happiness.Propagate(grid);

        // base 0.6 + 0.15 fire coverage = 0.75
        Assert.That(grid.GetTile(10, 0).Happiness, Is.EqualTo(0.75).Within(0.001),
            "FireHQ should provide +0.15 happiness to residential tiles at Manhattan distance 10");
    }

    [Test]
    public void FireHQDoesNotCoverBeyondRadius10()
    {
        var grid = new CityGrid(25, 5);
        grid.SetZone(0, 0, ZoneType.FireHQ);
        grid.SetZone(11, 0, ZoneType.Residential);
        MakeReady(grid, 11, 0);

        _happiness.Propagate(grid);

        Assert.That(grid.GetTile(11, 0).Happiness, Is.EqualTo(0.6).Within(0.002),
            "FireHQ should not cover tiles beyond Manhattan distance 10");
    }

    // ── Hospital ────────────────────────────────────────────────────────────────

    [Test]
    public void HospitalCoversRadius8_WithHappinessBonus()
    {
        // Hospital at (0,0), residential at Manhattan distance 8
        var grid = new CityGrid(20, 5);
        grid.SetZone(0, 0, ZoneType.Hospital);
        grid.SetZone(8, 0, ZoneType.Residential);
        MakeReady(grid, 8, 0);

        _happiness.Propagate(grid);

        // base 0.6 + 0.15 hospital = 0.75
        Assert.That(grid.GetTile(8, 0).Happiness, Is.EqualTo(0.75).Within(0.001),
            "Hospital should provide +0.15 happiness bonus to tiles within radius 8");
    }

    [Test]
    public void HospitalReducesEventPenalty_ForCoveredTiles()
    {
        // Hospital-covered tile should receive half the event penalty
        var grid = new CityGrid(20, 5);
        grid.SetZone(0, 0, ZoneType.Hospital);
        grid.SetZone(5, 0, ZoneType.Residential); // within radius 8
        MakeReady(grid, 5, 0);

        // Simulate a FireBreak event penalty of -0.10
        const double eventPenalty = -0.10;
        _happiness.Propagate(grid, eventPenalty: eventPenalty);

        // base 0.6 + 0.15 (hospital) + (-0.10 * 0.5) (halved penalty) - 0 neglect
        // = 0.6 + 0.15 - 0.05 = 0.70
        Assert.That(grid.GetTile(5, 0).Happiness, Is.EqualTo(0.70).Within(0.001),
            "Hospital-covered tile should receive only half the event penalty");
    }

    [Test]
    public void HospitalReducesEventPenalty_UncoveredTileGetsFullPenalty()
    {
        // A tile NOT covered by a hospital should receive the full event penalty
        var grid = new CityGrid(25, 5);
        grid.SetZone(0, 0, ZoneType.Hospital); // hospital at origin
        grid.SetZone(15, 0, ZoneType.Residential); // distance 15 — outside radius 8
        MakeReady(grid, 15, 0);

        const double eventPenalty = -0.10;
        _happiness.Propagate(grid, eventPenalty: eventPenalty);

        // base 0.6 + (-0.10) full penalty - neglect ~0.001 ≈ 0.499
        // Uncovered by hospital, so full penalty applies
        Assert.That(grid.GetTile(15, 0).Happiness, Is.EqualTo(0.5).Within(0.002),
            "Tile outside Hospital radius should receive the full event penalty");
    }

    [Test]
    public void HospitalReducesEventPenalty_ZeroPenalty_NoEffect()
    {
        // With no active event (penalty = 0), hospital does not change anything
        var grid = new CityGrid(20, 5);
        grid.SetZone(0, 0, ZoneType.Hospital);
        grid.SetZone(5, 0, ZoneType.Residential);
        MakeReady(grid, 5, 0);

        _happiness.Propagate(grid, eventPenalty: 0.0);

        // base 0.6 + 0.15 hospital = 0.75 (no penalty to halve)
        Assert.That(grid.GetTile(5, 0).Happiness, Is.EqualTo(0.75).Within(0.001),
            "With no event penalty, hospital just provides the standard +0.15 happiness bonus");
    }

    // ── HQ counts toward existing coverage categories ────────────────────────

    [Test]
    public void PoliceHQCountsAsPoliceForCoverageCategory()
    {
        // PoliceHQ should count as the Police category, so Fire + PoliceHQ = 2 service types = +0.30
        var grid = new CityGrid(25, 5);
        grid.SetZone(5, 0, ZoneType.Residential);
        MakeReady(grid, 5, 0);
        grid.SetZone(0, 0, ZoneType.PoliceHQ);   // covers distance 10 → covers tile at x=5
        grid.SetZone(2, 0, ZoneType.FireStation); // covers distance 3 → covers tile at x=5

        _happiness.Propagate(grid);

        // base 0.6 + min(2, 2) * 0.15 = 0.6 + 0.30 = 0.90
        Assert.That(grid.GetTile(5, 0).Happiness, Is.EqualTo(0.90).Within(0.001),
            "PoliceHQ + FireStation should both contribute to the service coverage cap of 2 types (+0.30)");
    }

    [Test]
    public void FireHQCountsAsFireForCoverageCategory()
    {
        // FireHQ should count as the Fire category, so PoliceStation + FireHQ = 2 types = +0.30
        var grid = new CityGrid(25, 5);
        grid.SetZone(5, 0, ZoneType.Residential);
        MakeReady(grid, 5, 0);
        grid.SetZone(0, 0, ZoneType.FireHQ);      // covers distance 10
        grid.SetZone(2, 0, ZoneType.PoliceStation); // covers distance 3

        _happiness.Propagate(grid);

        Assert.That(grid.GetTile(5, 0).Happiness, Is.EqualTo(0.90).Within(0.001),
            "FireHQ + PoliceStation should both contribute to the service coverage cap (+0.30)");
    }

    [Test]
    public void PoliceHQAndPoliceStation_DoNotDoubleCountAsOneSameCategory()
    {
        // PoliceHQ and PoliceStation covering the same tile count as ONE police category, not two
        var grid = new CityGrid(15, 5);
        grid.SetZone(5, 0, ZoneType.Residential);
        MakeReady(grid, 5, 0);
        grid.SetZone(0, 0, ZoneType.PoliceHQ);      // radius 10, covers tile
        grid.SetZone(3, 0, ZoneType.PoliceStation);  // radius 4, also covers tile

        _happiness.Propagate(grid);

        // Both are police — they collapse to 1 category: +0.15 only (not +0.30)
        Assert.That(grid.GetTile(5, 0).Happiness, Is.EqualTo(0.75).Within(0.001),
            "PoliceHQ and PoliceStation together count as ONE police category for the happiness bonus");
    }

    // ── Power conduction ────────────────────────────────────────────────────────

    [Test]
    public void PoliceHQ_ConductsPower()
    {
        var grid = new CityGrid(10, 5);
        grid.SetZone(0, 0, ZoneType.PowerPlant);
        grid.SetZone(1, 0, ZoneType.PoliceHQ);
        grid.SetZone(2, 0, ZoneType.Residential);

        var power = new PowerNetwork();
        power.Propagate(grid);

        Assert.That(grid.GetTile(1, 0).HasPower, Is.True, "PoliceHQ should conduct power");
        Assert.That(grid.GetTile(2, 0).HasPower, Is.True, "Residential adjacent to powered PoliceHQ should have power");
    }

    [Test]
    public void FireHQ_ConductsPower()
    {
        var grid = new CityGrid(10, 5);
        grid.SetZone(0, 0, ZoneType.PowerPlant);
        grid.SetZone(1, 0, ZoneType.FireHQ);
        grid.SetZone(2, 0, ZoneType.Residential);

        var power = new PowerNetwork();
        power.Propagate(grid);

        Assert.That(grid.GetTile(1, 0).HasPower, Is.True, "FireHQ should conduct power");
        Assert.That(grid.GetTile(2, 0).HasPower, Is.True, "Residential adjacent to powered FireHQ should have power");
    }

    [Test]
    public void Hospital_ConductsPower()
    {
        var grid = new CityGrid(10, 5);
        grid.SetZone(0, 0, ZoneType.PowerPlant);
        grid.SetZone(1, 0, ZoneType.Hospital);
        grid.SetZone(2, 0, ZoneType.Residential);

        var power = new PowerNetwork();
        power.Propagate(grid);

        Assert.That(grid.GetTile(1, 0).HasPower, Is.True, "Hospital should conduct power");
        Assert.That(grid.GetTile(2, 0).HasPower, Is.True, "Residential adjacent to powered Hospital should have power");
    }

    // ── Budget costs ─────────────────────────────────────────────────────────────

    [Test]
    public void PoliceHQ_HasCorrectPlacementCost()
    {
        Assert.That(BudgetSystem.PlacementCosts["PoliceHQ"], Is.EqualTo(2_000.0).Within(0.001));
    }

    [Test]
    public void FireHQ_HasCorrectPlacementCost()
    {
        Assert.That(BudgetSystem.PlacementCosts["FireHQ"], Is.EqualTo(2_000.0).Within(0.001));
    }

    [Test]
    public void Hospital_HasCorrectPlacementCost()
    {
        Assert.That(BudgetSystem.PlacementCosts["Hospital"], Is.EqualTo(3_000.0).Within(0.001));
    }

    [Test]
    public void PoliceHQ_HasCorrectMaintenanceCost()
    {
        var budget = new BudgetSystem();
        var grid = new CityGrid(5, 5);
        grid.SetZone(0, 0, ZoneType.PoliceHQ);

        Assert.That(budget.CalculateMaintenanceCost(grid), Is.EqualTo(25.0).Within(0.001));
    }

    [Test]
    public void FireHQ_HasCorrectMaintenanceCost()
    {
        var budget = new BudgetSystem();
        var grid = new CityGrid(5, 5);
        grid.SetZone(0, 0, ZoneType.FireHQ);

        Assert.That(budget.CalculateMaintenanceCost(grid), Is.EqualTo(25.0).Within(0.001));
    }

    [Test]
    public void Hospital_HasCorrectMaintenanceCost()
    {
        var budget = new BudgetSystem();
        var grid = new CityGrid(5, 5);
        grid.SetZone(0, 0, ZoneType.Hospital);

        Assert.That(budget.CalculateMaintenanceCost(grid), Is.EqualTo(35.0).Within(0.001));
    }

    // ── City milestone gate ───────────────────────────────────────────────────

    [Test]
    public void HQBlockedBeforeCityMilestone_PoliceHQ()
    {
        var milestones = new MilestoneSystem();
        var (allowed, error) = milestones.CanPlace(ZoneType.PoliceHQ, currentPopulation: 4_999);

        Assert.That(allowed, Is.False, "PoliceHQ should be blocked before City milestone");
        Assert.That(error, Does.Contain("5,000"), "Error message should mention the required population");
        Assert.That(error, Does.Contain("PoliceHQ"), "Error message should name the zone type");
    }

    [Test]
    public void HQBlockedBeforeCityMilestone_FireHQ()
    {
        var milestones = new MilestoneSystem();
        var (allowed, error) = milestones.CanPlace(ZoneType.FireHQ, currentPopulation: 0);

        Assert.That(allowed, Is.False, "FireHQ should be blocked before City milestone");
        Assert.That(error, Does.Contain("5,000"));
    }

    [Test]
    public void HQBlockedBeforeCityMilestone_Hospital()
    {
        var milestones = new MilestoneSystem();
        var (allowed, error) = milestones.CanPlace(ZoneType.Hospital, currentPopulation: 3_000);

        Assert.That(allowed, Is.False, "Hospital should be blocked before City milestone");
        Assert.That(error, Does.Contain("5,000"));
    }

    [Test]
    public void HQAllowedAtCityMilestone_PoliceHQ()
    {
        var milestones = new MilestoneSystem();
        var (allowed, error) = milestones.CanPlace(ZoneType.PoliceHQ, currentPopulation: 5_000);

        Assert.That(allowed, Is.True, "PoliceHQ should be allowed at exactly 5,000 population");
        Assert.That(error, Is.Null);
    }

    [Test]
    public void HQAllowedAboveCityMilestone_FireHQ()
    {
        var milestones = new MilestoneSystem();
        var (allowed, error) = milestones.CanPlace(ZoneType.FireHQ, currentPopulation: 10_000);

        Assert.That(allowed, Is.True, "FireHQ should be allowed above City milestone");
        Assert.That(error, Is.Null);
    }

    [Test]
    public void HQAllowedAboveCityMilestone_Hospital()
    {
        var milestones = new MilestoneSystem();
        var (allowed, error) = milestones.CanPlace(ZoneType.Hospital, currentPopulation: 5_001);

        Assert.That(allowed, Is.True, "Hospital should be allowed above City milestone");
        Assert.That(error, Is.Null);
    }

    [Test]
    public void NonGatedZones_AlwaysAllowed()
    {
        var milestones = new MilestoneSystem();

        foreach (var zone in new[] { ZoneType.Road, ZoneType.Residential, ZoneType.FireStation,
                                     ZoneType.PoliceStation, ZoneType.School, ZoneType.PowerPlant })
        {
            var (allowed, error) = milestones.CanPlace(zone, currentPopulation: 0);
            Assert.That(allowed, Is.True, $"{zone} should not be gated by milestone");
            Assert.That(error, Is.Null);
        }
    }
}
