using Loopolis.Core.Graph;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class HappinessSystemTests
{
    private HappinessSystem _happiness = null!;

    [SetUp]
    public void SetUp() => _happiness = new HappinessSystem();

    /// <summary>
    /// Helper: makes a residential tile fully ready (powered + road access).
    /// </summary>
    private static void MakeReady(CityGrid grid, int x, int y)
    {
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
    }

    /// <summary>
    /// Helper: makes a commercial tile fully ready (for adjacency bonus tests).
    /// </summary>
    private static void MakeCommercialReady(CityGrid grid, int x, int y)
    {
        grid.SetZone(x, y, ZoneType.Commercial);
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
    }

    [Test]
    public void ReadyResidentialWithNoModifiers_GetsBaseHappiness()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // No commercial adjacent, no services, no pollution
        // Note: first tick accumulates 0.001 neglect (no services), so happiness = 0.599

        _happiness.Propagate(grid);

        // Tolerance of 0.002 to account for first-tick neglect accumulation (0.001/tick)
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.6).Within(0.002));
    }

    [Test]
    public void AdjacentCommercial_IncreasesHappiness()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        MakeCommercialReady(grid, 5, 6); // adjacent

        _happiness.Propagate(grid);

        // base 0.6 + 0.25 commercial = 0.85 (minus 0.001 first-tick neglect = 0.849)
        // Tolerance of 0.002 to account for first-tick neglect accumulation
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.85).Within(0.002));
    }

    [Test]
    public void FireStationInRange_IncreasesHappiness()
    {
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // Fire station at Manhattan distance 3 (within radius 4)
        grid.SetZone(5, 8, ZoneType.FireStation);

        _happiness.Propagate(grid);

        // base 0.6 + 0.15 fire station = 0.75 (covered → no neglect on first tick)
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.75).Within(0.001));
    }

    [Test]
    public void PoliceStationInRange_IncreasesHappiness()
    {
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // Police station at Manhattan distance 4 (at edge of radius 4)
        grid.SetZone(9, 5, ZoneType.PoliceStation);

        _happiness.Propagate(grid);

        // base 0.6 + 0.15 police = 0.75 (covered → no neglect on first tick)
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.75).Within(0.001));
    }

    [Test]
    public void SchoolInRange_IncreasesHappiness()
    {
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // School at Manhattan distance 5 (at edge of radius 5)
        grid.SetZone(5, 10, ZoneType.School);

        _happiness.Propagate(grid);

        // base 0.6 + 0.15 school = 0.75 (covered → no neglect on first tick)
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.75).Within(0.001));
    }

    [Test]
    public void TwoServiceTypes_CappedBonus()
    {
        // Two different service types covering the zone → +0.30 (not +0.45 for three types)
        var grid = new CityGrid(15, 15);
        grid.SetZone(7, 7, ZoneType.Residential);
        MakeReady(grid, 7, 7);
        grid.SetZone(7, 10, ZoneType.FireStation);   // Manhattan distance 3, within 4
        grid.SetZone(7, 4, ZoneType.PoliceStation);  // Manhattan distance 3, within 4
        grid.SetZone(2, 7, ZoneType.School);         // Manhattan distance 5, within 5

        _happiness.Propagate(grid);

        // base 0.6 + min(3, 2) * 0.15 = 0.6 + 0.30 = 0.90 (covered → no neglect on first tick)
        Assert.That(grid.GetTile(7, 7).Happiness, Is.EqualTo(0.90).Within(0.001));
    }

    [Test]
    public void HighPollution_ReducesHappiness()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        grid.SetPollution(5, 5, 0.5); // 0.5 pollution → -0.2 happiness

        _happiness.Propagate(grid);

        // base 0.6 - 0.5 * 0.4 = 0.6 - 0.2 = 0.4 (minus 0.001 first-tick neglect = 0.399)
        // Tolerance of 0.002 to account for first-tick neglect accumulation
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.4).Within(0.002));
    }

    [Test]
    public void PollutionAndCommercial_Combined()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        MakeCommercialReady(grid, 5, 6); // +0.25
        grid.SetPollution(5, 5, 0.5);   // -0.2

        _happiness.Propagate(grid);

        // 0.6 + 0.25 - 0.2 = 0.65 (minus 0.001 first-tick neglect = 0.649)
        // Tolerance of 0.002 to account for first-tick neglect accumulation
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.65).Within(0.002));
    }

    [Test]
    public void HappinessClamped_NeverBelowFloor()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        grid.SetPollution(5, 5, 1.0); // max pollution → -0.4, so 0.6 - 0.4 = 0.2

        // Even setting pollution > 1 via direct path — test the clamp floor
        // 1.0 pollution → 0.6 - 0.4 = 0.2 (above floor)
        // Let's test the floor by using pollution on a zone without commercial bonus
        // For floor test: need happiness < 0.1 which would require pollution > 1.25
        // Since pollution is clamped to [0,1], min possible is 0.6 - 0.4 = 0.2
        // So test that pollution doesn't push below 0.2, and verify clamp is there

        _happiness.Propagate(grid);

        // 0.6 - 1.0*0.4 = 0.2 — above floor, but let's verify clamp is >= 0.1
        Assert.That(grid.GetTile(5, 5).Happiness, Is.GreaterThanOrEqualTo(0.1),
            "Happiness should never fall below the 0.1 floor");
        // Tolerance of 0.002 to account for first-tick neglect accumulation (0.001/tick)
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.2).Within(0.002));
    }

    [Test]
    public void NotReadyZone_GetsNoHappinessCalculation()
    {
        // Unready residential zone: Happiness should remain at default 1.0 (not calculated)
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        // NOT powered, NOT road-accessed → not ready

        _happiness.Propagate(grid);

        // Non-ready zones are skipped — they keep the ClearHappiness default of 1.0
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(1.0),
            "Non-ready residential zone should keep default happiness (1.0) after propagation");
    }

    [Test]
    public void ServiceOutsideRadius_NoBonus()
    {
        var grid = new CityGrid(20, 20);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // Fire station at Manhattan distance 5 (outside radius 4)
        grid.SetZone(5, 10, ZoneType.FireStation);

        _happiness.Propagate(grid);

        // base 0.6 only — fire station too far away (uncovered → first-tick neglect 0.001)
        // Tolerance of 0.002 to account for first-tick neglect accumulation
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.6).Within(0.002));
    }

    [Test]
    public void AverageHappiness_NoReadyZones_ReturnsOne()
    {
        var grid = new CityGrid(10, 10);
        // No residential at all

        _happiness.Propagate(grid);

        Assert.That(_happiness.AverageHappiness(grid), Is.EqualTo(1.0));
    }

    [Test]
    public void AverageHappiness_CalculatesCorrectly()
    {
        var grid = new CityGrid(10, 10);
        // Zone A: base only → 0.6 (uncovered, first-tick neglect 0.001 → 0.599)
        grid.SetZone(2, 2, ZoneType.Residential);
        MakeReady(grid, 2, 2);
        // Zone B: with commercial adjacent → 0.85 (uncovered, first-tick neglect 0.001 → 0.849)
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        MakeCommercialReady(grid, 5, 6);

        _happiness.Propagate(grid);

        // Average = (0.599 + 0.849) / 2 ≈ 0.724 — tolerance 0.002 for first-tick neglect
        Assert.That(_happiness.AverageHappiness(grid), Is.EqualTo(0.725).Within(0.002));
    }

    [Test]
    public void ServiceNeglect_AccumulatesWhenUncovered()
    {
        // Ready residential zone with no services → neglect should increase each tick
        // After 100 ticks, happiness should be lower than baseline (0.6)
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // No services anywhere

        for (var i = 0; i < 100; i++)
            _happiness.Propagate(grid);

        // After 100 ticks: neglect = 100 * 0.001 = 0.1 → happiness = 0.6 - 0.1 = 0.5
        Assert.That(grid.GetTile(5, 5).Happiness, Is.LessThan(0.6),
            "Happiness should have dropped below baseline after 100 uncovered ticks");
        Assert.That(_happiness.GetNeglect(5, 5), Is.EqualTo(0.1).Within(0.001),
            "Neglect should be 0.1 after 100 ticks at 0.001/tick");
    }

    [Test]
    public void ServiceNeglect_ResetsWhenCovered()
    {
        // Accumulate neglect for 100 ticks, then add service, then recover
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);

        // 100 ticks with no coverage → neglect = 0.1
        for (var i = 0; i < 100; i++)
            _happiness.Propagate(grid);

        var happinessAfterNeglect = grid.GetTile(5, 5).Happiness;

        // Add a fire station within range
        grid.SetZone(5, 8, ZoneType.FireStation); // Manhattan distance 3, within radius 4

        // 150 more ticks → recovery: 0.1 / 0.002 = 50 ticks to fully recover
        for (var i = 0; i < 150; i++)
            _happiness.Propagate(grid);

        var happinessAfterRecovery = grid.GetTile(5, 5).Happiness;

        Assert.That(happinessAfterRecovery, Is.GreaterThan(happinessAfterNeglect),
            "Happiness should recover after service coverage is established");
        Assert.That(_happiness.GetNeglect(5, 5), Is.EqualTo(0.0).Within(0.001),
            "Neglect should fully recover to 0 within 150 ticks (50 ticks needed at 0.002/tick)");
    }

    [Test]
    public void ServiceNeglect_CappedAt0Point20()
    {
        // After 400+ ticks with no coverage, neglect shouldn't exceed 0.20 (lowered from 0.30
        // to prevent guaranteed abandonment in no-service scenarios — floors happiness at ~0.40).
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);

        // Run 400 ticks — far past the 200-tick threshold to max neglect
        for (var i = 0; i < 400; i++)
            _happiness.Propagate(grid);

        Assert.That(_happiness.GetNeglect(5, 5), Is.EqualTo(0.20).Within(0.001),
            "Service neglect should be capped at 0.20");
        Assert.That(grid.GetTile(5, 5).Happiness, Is.GreaterThanOrEqualTo(0.39).Within(0.001),
            "Final happiness should be ~0.40 even at max neglect (0.6 base - 0.20 cap)");
    }

    // --- Tax modifier tests ---

    [Test]
    public void LowTaxModifier_IncreasesHappiness()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);

        _happiness.Propagate(grid, taxModifier: 0.0);
        var baseHappiness = grid.GetTile(5, 5).Happiness;

        _happiness.Propagate(grid, taxModifier: +0.05);
        var lowTaxHappiness = grid.GetTile(5, 5).Happiness;

        Assert.That(lowTaxHappiness, Is.GreaterThan(baseHappiness),
            "Low tax modifier (+0.05) should increase happiness above baseline");
    }

    [Test]
    public void HighTaxModifier_DecreasesHappiness()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);

        _happiness.Propagate(grid, taxModifier: 0.0);
        var baseHappiness = grid.GetTile(5, 5).Happiness;

        _happiness.Propagate(grid, taxModifier: -0.10);
        var highTaxHappiness = grid.GetTile(5, 5).Happiness;

        Assert.That(highTaxHappiness, Is.LessThan(baseHappiness),
            "High tax modifier (-0.10) should decrease happiness below baseline");
    }

    [Test]
    public void TaxModifier_ClampedTo1Point0Maximum()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // Place a fire station and commercial adjacent to push happiness near max
        MakeCommercialReady(grid, 5, 6);
        grid.SetZone(5, 8, ZoneType.FireStation);

        // Large positive tax modifier that would push beyond 1.0
        _happiness.Propagate(grid, taxModifier: +1.0);

        Assert.That(grid.GetTile(5, 5).Happiness, Is.LessThanOrEqualTo(1.0),
            "Happiness with tax modifier should be clamped to 1.0 maximum");
    }

    [Test]
    public void TaxModifier_ClampedTo0Point1Minimum()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        grid.SetPollution(5, 5, 1.0); // max pollution already reduces happiness

        // Large negative tax modifier pushing below 0.1
        _happiness.Propagate(grid, taxModifier: -1.0);

        Assert.That(grid.GetTile(5, 5).Happiness, Is.GreaterThanOrEqualTo(0.1),
            "Happiness with tax modifier should be clamped to 0.1 minimum");
    }

    // ── Road-graph coverage tests ───────────────────────────────────────────────

    /// <summary>
    /// Helper: build a RoadGraph and add Road nodes for every Road tile in the grid.
    /// </summary>
    private static RoadGraph BuildRoadGraph(CityGrid grid)
    {
        var roadGraph = new RoadGraph();
        foreach (var tile in grid.AllTiles())
        {
            if (tile.Zone == ZoneType.Road)    roadGraph.AddNode(tile.X, tile.Y, 1.0f);
            if (tile.Zone == ZoneType.Avenue)  roadGraph.AddNode(tile.X, tile.Y, 0.5f);
        }
        return roadGraph;
    }

    [Test]
    public void RoadGraph_ServiceWithNoRoadNeighbour_CoversNothing()
    {
        // Service placed with no adjacent road tile — even 1 tile away, coverage = 0
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // Road adjacent to residential so it has road access, but NOT adjacent to fire station
        grid.SetZone(5, 6, ZoneType.Road);
        // Fire station at (5, 3) — no road adjacent to it
        grid.SetZone(5, 3, ZoneType.FireStation);

        var roadGraph = BuildRoadGraph(grid);
        _happiness.Propagate(grid, roadGraph: roadGraph);

        // FireStation has no road neighbor → GetDistanceViaRoads returns MaxValue → no coverage
        // happiness = 0.6 only (minus first-tick neglect 0.001)
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.6).Within(0.002),
            "Fire station with no road access should provide no coverage bonus");
    }

    [Test]
    public void RoadGraph_ResidentialWithNoRoadNeighbour_CoveredByNothing()
    {
        // Residential tile with no road adjacent — even service 1 tile away cannot cover it
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // Fire station at (5, 6) with a road at (5, 7) — service has road access but residential doesn't
        grid.SetZone(5, 6, ZoneType.FireStation);
        grid.SetZone(5, 7, ZoneType.Road);

        var roadGraph = BuildRoadGraph(grid);
        _happiness.Propagate(grid, roadGraph: roadGraph);

        // Residential at (5,5) has no road neighbor (road is at (5,7) — not adjacent to (5,5))
        // → GetDistanceViaRoads returns MaxValue → no coverage
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.6).Within(0.002),
            "Residential with no road access should receive no service coverage via road graph");
    }

    [Test]
    public void RoadGraph_ServiceReachableViaLongRoad_IsCoveredWithinRadius()
    {
        // Residential at (0,5), Fire station at (9,5), 8 road tiles connecting them
        // road neighbor of (0,5) = (1,5); road neighbor of (9,5) = (8,5)
        // graph distance (1,5)→(8,5) = 7 edges × 1.0 = 7.0 ≤ 8.0 (FireStation radius) → COVERED
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();
        grid.SetZone(0, 5, ZoneType.Residential);
        MakeReady(grid, 0, 5);
        for (var x = 1; x <= 8; x++)
            grid.SetZone(x, 5, ZoneType.Road);
        grid.SetZone(9, 5, ZoneType.FireStation);

        var roadGraph = BuildRoadGraph(grid);
        _happiness.Propagate(grid, roadGraph: roadGraph);

        // base 0.6 + 0.15 fire coverage = 0.75 (covered → no neglect)
        Assert.That(grid.GetTile(0, 5).Happiness, Is.EqualTo(0.75).Within(0.001),
            "Fire station reachable within road-graph radius should provide coverage bonus");
    }

    [Test]
    public void RoadGraph_ServiceTooFarViaRoad_NotCovered()
    {
        // Residential at (0,5), Fire station at (11,5) — 10 road tiles between them
        // road neighbor of (0,5) = (1,5); road neighbor of (11,5) = (10,5)
        // graph distance (1,5)→(10,5) = 9 edges = 9.0 > 8.0 (FireStation radius) → NOT covered
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();
        grid.SetZone(0, 5, ZoneType.Residential);
        MakeReady(grid, 0, 5);
        for (var x = 1; x <= 10; x++)
            grid.SetZone(x, 5, ZoneType.Road);
        grid.SetZone(11, 5, ZoneType.FireStation);

        var roadGraph = BuildRoadGraph(grid);
        _happiness.Propagate(grid, roadGraph: roadGraph);

        // base 0.6 only — service too far via road (minus first-tick neglect 0.001)
        Assert.That(grid.GetTile(0, 5).Happiness, Is.EqualTo(0.6).Within(0.002),
            "Fire station beyond road-graph radius should not provide coverage bonus");
    }

    [Test]
    public void RoadGraph_AvenueShortcut_AllowsCoverageWithinRadius()
    {
        // 8 avenue tiles (weight 0.5 each) between residential and fire station
        // road neighbor of (0,5) = (1,5); road neighbor of (9,5) = (8,5)
        // graph distance: 7 avenue edges × 0.5 = 3.5 ≤ 8.0 → COVERED
        // Even though Manhattan distance is 9 > old radius 4, road graph shows it's close
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();
        grid.SetZone(0, 5, ZoneType.Residential);
        MakeReady(grid, 0, 5);
        for (var x = 1; x <= 8; x++)
            grid.SetZone(x, 5, ZoneType.Avenue);
        grid.SetZone(9, 5, ZoneType.FireStation);

        var roadGraph = BuildRoadGraph(grid);
        _happiness.Propagate(grid, roadGraph: roadGraph);

        // base 0.6 + 0.15 fire coverage = 0.75
        Assert.That(grid.GetTile(0, 5).Happiness, Is.EqualTo(0.75).Within(0.001),
            "Avenue shortcut should allow service coverage within road-graph radius");
    }

    [Test]
    public void RoadGraph_CommutePenalty_DisconnectedIndustrial_ReturnsMaxValue()
    {
        // Road-graph distance between residential and industrial when industrial has no road neighbor
        // should return float.MaxValue, triggering the -0.25 penalty.
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();
        grid.SetZone(2, 5, ZoneType.Residential);
        grid.SetZone(2, 4, ZoneType.Road);
        grid.SetZone(9, 9, ZoneType.Industrial);

        var roadGraph = new RoadGraph();
        roadGraph.AddNode(2, 4, 1.0f); // only road near residential — industrial (9,9) has no road neighbor

        var dist = roadGraph.GetDistanceViaRoads(grid, 2, 5, 9, 9);

        Assert.That(dist, Is.EqualTo(float.MaxValue),
            "Disconnected industrial (no road neighbor) should return MaxValue distance");
    }

    [Test]
    public void RoadGraph_CommutePenalty_NearbyIndustrialViaRoad_NoPenalty()
    {
        // Residential and industrial connected via short road (distance ≤ 10.0) — no commute penalty
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();
        grid.SetZone(2, 5, ZoneType.Residential);
        for (var x = 3; x <= 7; x++)
            grid.SetZone(x, 5, ZoneType.Road);
        grid.SetZone(8, 5, ZoneType.Industrial);
        MakeReady(grid, 2, 5);
        grid.SetPower(8, 5, true);

        var roadGraph = BuildRoadGraph(grid);

        // road neighbor of (2,5) = (3,5); road neighbor of (8,5) = (7,5)
        // graph distance (3,5)→(7,5) = 4 edges = 4.0 ≤ 10.0 → no penalty
        var dist = roadGraph.GetDistanceViaRoads(grid, 2, 5, 8, 5);

        Assert.That(dist, Is.EqualTo(4.0f).Within(0.001f));
        Assert.That(dist, Is.LessThanOrEqualTo(10.0f),
            "Short road commute distance should not trigger commute penalty");
    }

    // ── AverageNeglect tests ──────────────────────────────────────────────────

    [Test]
    public void AverageNeglect_NoDevelopedTiles_ReturnsZero()
    {
        var grid = new CityGrid(10, 10);
        // No residential tiles at all

        var result = _happiness.AverageNeglect(grid);

        Assert.That(result, Is.EqualTo(0.0),
            "AverageNeglect should be 0.0 when there are no developed residential tiles");
    }

    [Test]
    public void AverageNeglect_NoBuildingId_ReturnsZero()
    {
        // Residential tile that is ready but has no BuildingId (not yet developed)
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // No BuildingId set — tile is ready but not developed

        _happiness.Propagate(grid); // accumulates neglect in _neglect dict but tile not BuildingId-gated

        var result = _happiness.AverageNeglect(grid);

        Assert.That(result, Is.EqualTo(0.0),
            "AverageNeglect should be 0.0 when no tiles have a BuildingId");
    }

    [Test]
    public void AverageNeglect_AfterTicks_ReflectsAccumulatedNeglect()
    {
        // Developed residential tile with no services → neglect accumulates 0.001/tick
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        grid.SetBuildingId(5, 5, "test_building");

        for (var i = 0; i < 50; i++)
            _happiness.Propagate(grid);

        // After 50 ticks: neglect = 50 * 0.001 = 0.05
        var result = _happiness.AverageNeglect(grid);

        Assert.That(result, Is.EqualTo(0.05).Within(0.001),
            "AverageNeglect should be 0.05 after 50 uncovered ticks at 0.001/tick");
    }

    [Test]
    public void AverageNeglect_WithServiceCoverage_StaysLow()
    {
        // Developed tile covered by a service — neglect does not accumulate
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        grid.SetBuildingId(5, 5, "test_building");
        // Fire station within Manhattan radius 4
        grid.SetZone(5, 8, ZoneType.FireStation);

        for (var i = 0; i < 50; i++)
            _happiness.Propagate(grid);

        var result = _happiness.AverageNeglect(grid);

        Assert.That(result, Is.EqualTo(0.0).Within(0.001),
            "AverageNeglect should stay at 0.0 when service coverage is maintained from the start");
    }

    // ── Bug-fix regression tests ──────────────────────────────────────────────

    /// <summary>
    /// Bug: erasing a residential tile (e.g. via fire damage) leaves a stale _neglect entry.
    /// A new zone placed at the same coordinates should start with zero neglect, not inherit
    /// the demolished tile's accumulated penalty.
    /// </summary>
    [Test]
    public void ClearNeglect_NewTileAtSamePosition_StartsWithZeroNeglect()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);

        // Accumulate significant neglect on the tile (200 ticks → cap at 0.20)
        for (var i = 0; i < 250; i++)
            _happiness.Propagate(grid);

        Assert.That(_happiness.GetNeglect(5, 5), Is.EqualTo(0.20).Within(0.001),
            "Neglect should be at cap (0.20) after 250 uncovered ticks");

        // Clear the neglect (simulates EraseTile)
        _happiness.ClearNeglect(5, 5);

        Assert.That(_happiness.GetNeglect(5, 5), Is.EqualTo(0.0),
            "After ClearNeglect, tile should report 0.0 neglect");
    }

    [Test]
    public void ClearNeglect_AfterErase_NewTileDoesNotInheritPenalty()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);

        // Build up neglect over 100 ticks
        for (var i = 0; i < 100; i++)
            _happiness.Propagate(grid);

        // Erase the tile (fire damage path) and clear its neglect
        grid.SetZone(5, 5, ZoneType.Empty);
        _happiness.ClearNeglect(5, 5);

        // Place a new residential zone at the same position
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);

        // Propagate once — the new tile should start at 0.001 neglect (one tick), not inherited 0.1
        _happiness.Propagate(grid);

        Assert.That(_happiness.GetNeglect(5, 5), Is.EqualTo(0.001).Within(0.0001),
            "Rebuilt tile should start with only one tick of neglect (0.001), not the old tile's 0.1");
    }
}
