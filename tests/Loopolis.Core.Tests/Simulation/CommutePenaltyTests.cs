using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

/// <summary>
/// Tests for the commute happiness penalty (Change 1) and the wider mixed-use bonus (Change 2).
///
/// Commute penalty: residential tiles with BuildingId set, more than 8 Manhattan tiles from
/// the nearest powered industrial zone, suffer −0.10 (9–14 tiles) or −0.20 (≥15 tiles).
/// Grace period: no penalty when city population &lt; 50, or when no industrial exists.
///
/// Wider mixed-use bonus: commercial within Chebyshev-3 (was Chebyshev-1) boosts residential
/// demand by 1.5×. Chebyshev-4 is outside range and should produce no boost.
/// </summary>
[TestFixture]
public class CommutePenaltyTests
{
    private HappinessSystem _happiness = null!;
    private DemandSystem _demand = null!;

    [SetUp]
    public void SetUp()
    {
        _happiness = new HappinessSystem();
        _demand = new DemandSystem();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Makes a residential tile fully ready and developed (has a building).</summary>
    private static void MakeDeveloped(CityGrid grid, int x, int y)
    {
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
        grid.SetBuildingId(x, y, "res_house_1x1");
    }

    /// <summary>Places a powered industrial tile.</summary>
    private static void PlacePoweredIndustrial(CityGrid grid, int x, int y)
    {
        grid.SetZone(x, y, ZoneType.Industrial);
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
    }

    // ── Commute penalty tests ─────────────────────────────────────────────────

    [Test]
    public void ResidentialWithin8TilesOfIndustrial_NoPenalty()
    {
        // Residential at (5,5), industrial at (5,13) — Manhattan distance = 8 → no penalty
        var grid = new CityGrid(20, 20);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeDeveloped(grid, 5, 5);
        PlacePoweredIndustrial(grid, 5, 13);

        _happiness.Propagate(grid, cityPopulation: 100);

        // With no other modifiers (no services, no pollution), base = 0.6
        // Distance 8 → commute penalty = 0.0
        // first-tick neglect = 0.001 → 0.599
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.6).Within(0.002),
            "Residential ≤8 tiles from industrial should have no commute penalty");
    }

    [Test]
    public void ResidentialAt10TilesFromIndustrial_Gets010Penalty()
    {
        // Residential at (5,5), industrial at (5,15) — Manhattan distance = 10 → −0.10 penalty
        var grid = new CityGrid(20, 20);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeDeveloped(grid, 5, 5);
        PlacePoweredIndustrial(grid, 5, 15);

        _happiness.Propagate(grid, cityPopulation: 100);

        // base 0.6 − 0.10 commute = 0.50 (minus first-tick neglect 0.001 = 0.499)
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.5).Within(0.002),
            "Residential 9–14 tiles from industrial should get −0.10 commute penalty");
    }

    [Test]
    public void ResidentialAt15TilesFromIndustrial_Gets020Penalty()
    {
        // Residential at (2,2), industrial at (2,17) — Manhattan distance = 15 → −0.20 penalty
        var grid = new CityGrid(25, 25);
        grid.SetZone(2, 2, ZoneType.Residential);
        MakeDeveloped(grid, 2, 2);
        PlacePoweredIndustrial(grid, 2, 17);

        _happiness.Propagate(grid, cityPopulation: 100);

        // base 0.6 − 0.20 commute = 0.40 (minus first-tick neglect 0.001 = 0.399)
        Assert.That(grid.GetTile(2, 2).Happiness, Is.EqualTo(0.4).Within(0.002),
            "Residential ≥15 tiles from industrial should get −0.20 commute penalty");
    }

    [Test]
    public void NoIndustrialOnMap_NoPenalty()
    {
        // No industrial tiles at all → no penalty regardless of population
        var grid = new CityGrid(20, 20);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeDeveloped(grid, 5, 5);
        // No industrial anywhere

        _happiness.Propagate(grid, cityPopulation: 200);

        // base 0.6 only (no commute penalty because no industrial on map)
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.6).Within(0.002),
            "No industrial on map should produce no commute penalty");
    }

    [Test]
    public void PopBelow50_NoPenalty()
    {
        // Industrial is far away, but population is below grace threshold of 50
        var grid = new CityGrid(25, 25);
        grid.SetZone(2, 2, ZoneType.Residential);
        MakeDeveloped(grid, 2, 2);
        PlacePoweredIndustrial(grid, 2, 20); // distance 18, would be −0.20 penalty

        _happiness.Propagate(grid, cityPopulation: 49); // below grace threshold

        // base 0.6 — no penalty because pop < 50
        Assert.That(grid.GetTile(2, 2).Happiness, Is.EqualTo(0.6).Within(0.002),
            "Population below 50 should be exempt from commute penalty (early-game grace)");
    }

    [Test]
    public void OnlyCountsPoweredIndustrial()
    {
        // Industrial placed but NOT powered → should not count as a commute destination
        // Residential at (2,2), industrial at (2,5) — distance 3, normally no penalty
        // But industrial is unpowered → no industrial counts → no penalty anyway
        // Let's verify: place residential FAR from powered industrial, but close to unpowered industrial
        var grid = new CityGrid(30, 30);
        grid.SetZone(2, 2, ZoneType.Residential);
        MakeDeveloped(grid, 2, 2);

        // Unpowered industrial close by (distance 3)
        grid.SetZone(2, 5, ZoneType.Industrial);
        grid.SetRoadAccess(2, 5, true);
        // No power set on this tile

        // Powered industrial very far (distance 20)
        PlacePoweredIndustrial(grid, 2, 22);

        _happiness.Propagate(grid, cityPopulation: 100);

        // Closest POWERED industrial is at distance 20 → penalty = −0.20
        // (unpowered industrial at distance 3 must not be counted)
        Assert.That(grid.GetTile(2, 2).Happiness, Is.EqualTo(0.4).Within(0.002),
            "Commute penalty should only consider powered industrial tiles");
    }

    [Test]
    public void CommutePenalty_DoesNotApplyToUndevelopedTiles()
    {
        // Undeveloped residential (no BuildingId) should not receive commute penalty
        var grid = new CityGrid(25, 25);
        grid.SetZone(2, 2, ZoneType.Residential);
        grid.SetPower(2, 2, true);
        grid.SetRoadAccess(2, 2, true);
        // No BuildingId — tile is ready but not developed

        PlacePoweredIndustrial(grid, 2, 20); // distance 18, would be −0.20 if applied

        _happiness.Propagate(grid, cityPopulation: 100);

        // base 0.6 — no commute penalty because tile has no BuildingId
        Assert.That(grid.GetTile(2, 2).Happiness, Is.EqualTo(0.6).Within(0.002),
            "Commute penalty should not apply to residential tiles without a building (undeveloped)");
    }

    [Test]
    public void AverageCommutePenalty_ReturnsCorrectAverage()
    {
        // Two developed residential tiles: one close (no penalty), one far (−0.20)
        var grid = new CityGrid(25, 25);

        grid.SetZone(5, 5, ZoneType.Residential);
        MakeDeveloped(grid, 5, 5); // distance to industry = 5 → no penalty

        grid.SetZone(5, 20, ZoneType.Residential);
        MakeDeveloped(grid, 5, 20); // distance to industry at (5,5+5=10) = too far
        // Actually let me compute: industrial at (5,10), residential A at (5,5) dist=5, B at (5,20) dist=10

        PlacePoweredIndustrial(grid, 5, 10);

        // Tile A: dist=5 → 0.0 penalty
        // Tile B: dist=10 → −0.10 penalty
        // Average = −0.05

        var avg = _happiness.AverageCommutePenalty(grid, population: 100);

        Assert.That(avg, Is.EqualTo(-0.05).Within(0.001),
            "Average commute penalty should be the mean of per-tile penalties");
    }

    [Test]
    public void AverageCommutePenalty_PopBelow50_ReturnsZero()
    {
        var grid = new CityGrid(25, 25);
        grid.SetZone(2, 2, ZoneType.Residential);
        MakeDeveloped(grid, 2, 2);
        PlacePoweredIndustrial(grid, 2, 20);

        var avg = _happiness.AverageCommutePenalty(grid, population: 30);

        Assert.That(avg, Is.EqualTo(0.0),
            "AverageCommutePenalty should return 0 when population < 50");
    }

    [Test]
    public void AverageCommutePenalty_NoIndustrial_ReturnsZero()
    {
        var grid = new CityGrid(25, 25);
        grid.SetZone(2, 2, ZoneType.Residential);
        MakeDeveloped(grid, 2, 2);
        // No industrial at all

        var avg = _happiness.AverageCommutePenalty(grid, population: 100);

        Assert.That(avg, Is.EqualTo(0.0),
            "AverageCommutePenalty should return 0 when no industrial exists");
    }

    // ── Wider mixed-use bonus tests ───────────────────────────────────────────

    [Test]
    public void WiderMixedUseBonus_CommercialAt3Tiles_StillBoostsGrowth()
    {
        // Commercial at Chebyshev distance 3 should boost residential demand (1.5×)
        // Chebyshev distance 3 means max(|dx|,|dy|) = 3
        // Residential at (5,5), commercial at (8,5) → Chebyshev dist = max(3,0) = 3 → within radius
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPower(5, 5, true);
        grid.SetRoadAccess(5, 5, true);

        grid.SetZone(8, 5, ZoneType.Commercial);
        grid.SetPower(8, 5, true);
        grid.SetRoadAccess(8, 5, true);

        _demand.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.5),
            "Commercial at Chebyshev distance 3 should boost residential demand");
    }

    [Test]
    public void WiderMixedUseBonus_CommercialAt4Tiles_NoBoost()
    {
        // Commercial at Chebyshev distance 4 is outside the radius — no boost
        // Residential at (5,5), commercial at (9,5) → Chebyshev dist = max(4,0) = 4 → outside radius
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPower(5, 5, true);
        grid.SetRoadAccess(5, 5, true);

        grid.SetZone(9, 5, ZoneType.Commercial);
        grid.SetPower(9, 5, true);
        grid.SetRoadAccess(9, 5, true);

        _demand.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.0),
            "Commercial at Chebyshev distance 4 (outside radius 3) should NOT boost residential demand");
    }

    [Test]
    public void WiderMixedUseBonus_CommercialAt2Tiles_StillBoostsGrowth()
    {
        // Chebyshev distance 2 is also within radius 3
        // Residential at (5,5), commercial at (7,5) → dist = 2
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPower(5, 5, true);
        grid.SetRoadAccess(5, 5, true);

        grid.SetZone(7, 5, ZoneType.Commercial);
        grid.SetPower(7, 5, true);
        grid.SetRoadAccess(7, 5, true);

        _demand.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.5),
            "Commercial at Chebyshev distance 2 should also boost residential demand");
    }

    [Test]
    public void WiderMixedUseBonus_DiagonalDistance3_StillBoostsGrowth()
    {
        // Diagonal at Chebyshev distance 3 (dx=3, dy=3) should still boost
        // Residential at (5,5), commercial at (8,8) → max(3,3) = 3 → within radius
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPower(5, 5, true);
        grid.SetRoadAccess(5, 5, true);

        grid.SetZone(8, 8, ZoneType.Commercial);
        grid.SetPower(8, 8, true);
        grid.SetRoadAccess(8, 8, true);

        _demand.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).DemandFactor, Is.EqualTo(1.5),
            "Commercial at diagonal Chebyshev distance 3 should boost residential demand");
    }
}
