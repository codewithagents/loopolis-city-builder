using Loopolis.Core.Graph;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

/// <summary>
/// Tests for G4 Service Capacity Model (ServiceCapacityModel + HappinessSystem.ComputeServiceCoverage).
///
/// Design: each service building has finite capacity. Coverage drains from closest tiles first.
/// When capacity runs out the remaining tiles are uncovered even if within road-graph radius.
/// </summary>
[TestFixture]
public class ServiceCapacityTests
{
    private HappinessSystem _happiness = null!;

    [SetUp]
    public void SetUp() => _happiness = new HappinessSystem();

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Build a RoadGraph from all Road/Avenue tiles in a grid.</summary>
    private static RoadGraph BuildRoadGraph(CityGrid grid)
    {
        var rg = new RoadGraph();
        foreach (var tile in grid.AllTiles())
        {
            if (tile.Zone == ZoneType.Road)   rg.AddNode(tile.X, tile.Y, 1.0f);
            if (tile.Zone == ZoneType.Avenue) rg.AddNode(tile.X, tile.Y, 0.5f);
        }
        return rg;
    }

    /// <summary>Set road access + power so a residential tile is IsReadyToDevelop.</summary>
    private static void MakeReady(CityGrid grid, int x, int y)
    {
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
    }

    /// <summary>
    /// Assign a BuildingId to a tile and set its population.
    /// The BuildingId is needed for FireStation capacity checks (developed tile = has BuildingId).
    /// </summary>
    private static void SetPopulation(CityGrid grid, int x, int y, int pop)
    {
        var buildingId = $"b_{x}_{y}";
        var building = new Loopolis.Core.Buildings.Building(
            buildingId, "res_house_1x1", ZoneType.Residential, x, y, 1, 1);
        grid.Buildings[buildingId] = building;
        grid.SetBuildingId(x, y, buildingId);
        grid.SetPopulation(x, y, pop);
    }

    // ── ServiceCapacityModel unit tests ────────────────────────────────────────

    [Test]
    public void Capacity_SchoolHas200Seats()
    {
        Assert.That(ServiceCapacityModel.Capacity[ZoneType.School], Is.EqualTo(200));
    }

    [Test]
    public void Capacity_PoliceStationHas300()
    {
        Assert.That(ServiceCapacityModel.Capacity[ZoneType.PoliceStation], Is.EqualTo(300));
    }

    [Test]
    public void Capacity_FireStationHas400()
    {
        Assert.That(ServiceCapacityModel.Capacity[ZoneType.FireStation], Is.EqualTo(400));
    }

    [Test]
    public void Capacity_HospitalHas80Beds()
    {
        Assert.That(ServiceCapacityModel.Capacity[ZoneType.Hospital], Is.EqualTo(80));
    }

    [Test]
    public void GetDemandPerTile_FireStation_DevelopedTile_ReturnsOne()
    {
        var grid = new CityGrid(5, 5);
        grid.SetZone(2, 2, ZoneType.Residential);
        SetPopulation(grid, 2, 2, 10); // sets BuildingId
        var tile = grid.GetTile(2, 2);

        var demand = ServiceCapacityModel.GetDemandPerTile(ZoneType.FireStation, tile);

        Assert.That(demand, Is.EqualTo(1),
            "FireStation should consume 1 capacity unit per developed tile (any tile with BuildingId)");
    }

    [Test]
    public void GetDemandPerTile_FireStation_UndevelopedTile_ReturnsZero()
    {
        var grid = new CityGrid(5, 5);
        grid.SetZone(2, 2, ZoneType.Residential);
        // No BuildingId set
        var tile = grid.GetTile(2, 2);

        var demand = ServiceCapacityModel.GetDemandPerTile(ZoneType.FireStation, tile);

        Assert.That(demand, Is.EqualTo(0),
            "FireStation should consume 0 for undeveloped tiles (no BuildingId)");
    }

    [Test]
    public void GetDemandPerTile_School_ReturnsTilePopulation()
    {
        var grid = new CityGrid(5, 5);
        grid.SetZone(2, 2, ZoneType.Residential);
        SetPopulation(grid, 2, 2, 42);
        var tile = grid.GetTile(2, 2);

        var demand = ServiceCapacityModel.GetDemandPerTile(ZoneType.School, tile);

        Assert.That(demand, Is.EqualTo(42));
    }

    [Test]
    public void GetDemandPerTile_Hospital_ReturnsTilePopulation()
    {
        var grid = new CityGrid(5, 5);
        grid.SetZone(2, 2, ZoneType.Residential);
        SetPopulation(grid, 2, 2, 15);
        var tile = grid.GetTile(2, 2);

        var demand = ServiceCapacityModel.GetDemandPerTile(ZoneType.Hospital, tile);

        Assert.That(demand, Is.EqualTo(15));
    }

    // ── Coverage result — no services ─────────────────────────────────────────

    [Test]
    public void NoServiceBuildings_AllCoveragePercentsAreZero()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(3, 5, ZoneType.Road);
        grid.SetZone(4, 5, ZoneType.Residential);
        MakeReady(grid, 4, 5);

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        Assert.That(result.SchoolCoveragePercent,   Is.EqualTo(0f));
        Assert.That(result.PoliceCoveragePercent,   Is.EqualTo(0f));
        Assert.That(result.FireCoveragePercent,     Is.EqualTo(0f));
        Assert.That(result.HospitalCoveragePercent, Is.EqualTo(0f));
    }

    [Test]
    public void NoServiceBuildings_AllSeatsAndBedsAreZero()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(3, 5, ZoneType.Road);
        grid.SetZone(4, 5, ZoneType.Residential);
        MakeReady(grid, 4, 5);

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        Assert.That(result.SchoolSeatsTotal,    Is.EqualTo(0));
        Assert.That(result.PoliceCapacityTotal, Is.EqualTo(0));
        Assert.That(result.FireCapacityTotal,   Is.EqualTo(0));
        Assert.That(result.HospitalBedsTotal,   Is.EqualTo(0));
    }

    // ── School capacity tests ──────────────────────────────────────────────────

    [Test]
    public void SchoolWith200Seats_100PopInRange_FullCoverage_100SeatsUsed()
    {
        // 1 school (200 seats), 2 residential tiles, each with pop 50 → total demand = 100
        // Both tiles should be covered; seats used = 100
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();

        // Road spine
        for (var x = 2; x <= 12; x++) grid.SetZone(x, 7, ZoneType.Road);

        // School road-adjacent
        grid.SetZone(2, 6, ZoneType.School);  // adj to (2,7) road

        // Two residential tiles road-adjacent
        grid.SetZone(5, 6, ZoneType.Residential);
        MakeReady(grid, 5, 6);
        SetPopulation(grid, 5, 6, 50);

        grid.SetZone(8, 6, ZoneType.Residential);
        MakeReady(grid, 8, 6);
        SetPopulation(grid, 8, 6, 50);

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        Assert.That(result.SchoolSeatsTotal, Is.EqualTo(200), "One school = 200 total seats");
        Assert.That(result.SchoolSeatsUsed,  Is.EqualTo(100), "50 + 50 = 100 seats used");
        Assert.That(result.SchoolCoveragePercent, Is.EqualTo(1.0f), "Both tiles covered = 100%");
    }

    [Test]
    public void SchoolWith200Seats_300PopInRange_CoverageNotFull_CapacityLimited()
    {
        // 1 school (200 seats), 3 residential tiles each with pop 100 → total demand = 300
        // Closest 2 tiles should be covered (100+100=200 seats used), third uncovered
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();

        for (var x = 0; x <= 12; x++) grid.SetZone(x, 7, ZoneType.Road);

        // School at left end, road-adjacent
        grid.SetZone(0, 6, ZoneType.School);  // adj to (0,7)

        // Three residential tiles — sorted by road distance: x=2, x=5, x=9
        grid.SetZone(2, 6, ZoneType.Residential);
        MakeReady(grid, 2, 6);
        SetPopulation(grid, 2, 6, 100);

        grid.SetZone(5, 6, ZoneType.Residential);
        MakeReady(grid, 5, 6);
        SetPopulation(grid, 5, 6, 100);

        grid.SetZone(9, 6, ZoneType.Residential);
        MakeReady(grid, 9, 6);
        SetPopulation(grid, 9, 6, 100);

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        Assert.That(result.SchoolSeatsTotal, Is.EqualTo(200));
        Assert.That(result.SchoolSeatsUsed,  Is.EqualTo(200), "School fills up exactly");
        // 2 of 3 tiles covered → 66.7%
        Assert.That(result.SchoolCoveragePercent, Is.LessThan(1.0f),
            "Coverage should be less than 100% when capacity runs out");
        Assert.That(result.SchoolCoveragePercent, Is.GreaterThan(0.5f),
            "Two of three tiles covered");
    }

    [Test]
    public void TwoSchools_CombinedCapacity_MoreTilesCovered()
    {
        // 2 schools (400 combined seats), 3 residential tiles each pop 100 → 300 total demand
        // All 3 tiles should be covered
        var grid = new CityGrid(20, 15);
        grid.SetFlatTerrain();

        for (var x = 0; x <= 15; x++) grid.SetZone(x, 7, ZoneType.Road);

        // School 1 at left, school 2 in middle
        grid.SetZone(0, 6, ZoneType.School);
        grid.SetZone(8, 6, ZoneType.School);

        // Three residential
        grid.SetZone(2, 6, ZoneType.Residential);
        MakeReady(grid, 2, 6);
        SetPopulation(grid, 2, 6, 100);

        grid.SetZone(5, 6, ZoneType.Residential);
        MakeReady(grid, 5, 6);
        SetPopulation(grid, 5, 6, 100);

        grid.SetZone(12, 6, ZoneType.Residential);
        MakeReady(grid, 12, 6);
        SetPopulation(grid, 12, 6, 100);

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        Assert.That(result.SchoolSeatsTotal, Is.EqualTo(400), "2 schools × 200 = 400 seats total");
        Assert.That(result.SchoolCoveragePercent, Is.EqualTo(1.0f),
            "All 3 tiles covered: combined capacity (400) > total demand (300)");
    }

    // ── FireStation counts buildings, not residents ────────────────────────────

    [Test]
    public void FireStation_CountsBuildings_NotResidents()
    {
        // FireStation capacity = 400 buildings.
        // 3 developed tiles (each with BuildingId) → each costs 1 unit → 3 used, all covered.
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();

        for (var x = 0; x <= 10; x++) grid.SetZone(x, 7, ZoneType.Road);

        grid.SetZone(0, 6, ZoneType.FireStation);  // adj to (0,7)

        // Three residential tiles, large populations but that shouldn't matter for fire
        for (var x = 2; x <= 4; x++)
        {
            grid.SetZone(x, 6, ZoneType.Residential);
            MakeReady(grid, x, 6);
            SetPopulation(grid, x, 6, 150); // 450 population total — would fill school x2
        }

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        // Fire capacity is in buildings, not pop. 3 developed tiles × 1 each = 3 units used.
        Assert.That(result.FireCapacityUsed, Is.EqualTo(3),
            "FireStation should use 1 unit per developed tile, not tile.Population");
        Assert.That(result.FireCoveragePercent, Is.EqualTo(1.0f),
            "All 3 buildings covered — far below 400-building limit");
    }

    [Test]
    public void FireStation_UndevelopedTiles_NotCountedAsLoad()
    {
        // Undeveloped residential (no BuildingId) costs 0 fire capacity units
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();

        for (var x = 0; x <= 8; x++) grid.SetZone(x, 5, ZoneType.Road);
        grid.SetZone(0, 4, ZoneType.FireStation);

        // Residential tile but no building assigned
        grid.SetZone(5, 4, ZoneType.Residential);
        MakeReady(grid, 5, 4);
        // DO NOT call SetPopulation → no BuildingId, Population = 0

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        // Undeveloped residential has no BuildingId → demand = 0 → not a fire candidate
        Assert.That(result.FireCapacityUsed, Is.EqualTo(0),
            "FireStation should not consume capacity for tiles with no building");
    }

    // ── Road-graph distance gates capacity ─────────────────────────────────────

    [Test]
    public void TileBeyondRoadGraphRadius_NotCoveredBySchool()
    {
        // School has radius 10. Tile road-graph distance > 10 → not covered even if close in Manhattan.
        // Road: x=0..12 at y=7, school at (0,6) adj to (0,7)
        // Tile at (12,6) adj to (12,7): distance (0,7)→(12,7) = 12 > 10 → NOT covered
        var grid = new CityGrid(20, 15);
        grid.SetFlatTerrain();

        for (var x = 0; x <= 13; x++) grid.SetZone(x, 7, ZoneType.Road);

        grid.SetZone(0, 6, ZoneType.School);

        // Near tile (should be covered)
        grid.SetZone(3, 6, ZoneType.Residential);
        MakeReady(grid, 3, 6);
        SetPopulation(grid, 3, 6, 50);

        // Far tile: road distance (0,7)→(12,7) = 12 edges = 12.0 > 10.0 (School radius)
        grid.SetZone(12, 6, ZoneType.Residential);
        MakeReady(grid, 12, 6);
        SetPopulation(grid, 12, 6, 50);

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        // Only 1 of 2 tiles covered (the close one)
        Assert.That(result.SchoolCoveragePercent, Is.EqualTo(0.5f).Within(0.001f),
            "Tile beyond road-graph radius should not be covered even if capacity remains");
        Assert.That(result.SchoolSeatsUsed, Is.EqualTo(50),
            "Only the near tile's population consumes school seats");
    }

    // ── Hospital coverage ──────────────────────────────────────────────────────

    [Test]
    public void Hospital_WithinCapacity_AllCovered_BedsUsedMatchesPop()
    {
        // Hospital has 80 beds. Two tiles with pop 30 each → 60 beds used, both covered.
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();

        for (var x = 0; x <= 10; x++) grid.SetZone(x, 7, ZoneType.Road);

        grid.SetZone(0, 6, ZoneType.Hospital);  // adj to (0,7)

        grid.SetZone(3, 6, ZoneType.Residential);
        MakeReady(grid, 3, 6);
        SetPopulation(grid, 3, 6, 30);

        grid.SetZone(6, 6, ZoneType.Residential);
        MakeReady(grid, 6, 6);
        SetPopulation(grid, 6, 6, 30);

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        Assert.That(result.HospitalBedsTotal, Is.EqualTo(80));
        Assert.That(result.HospitalBedsUsed,  Is.EqualTo(60), "30 + 30 = 60 beds used");
        Assert.That(result.HospitalCoveragePercent, Is.EqualTo(1.0f));
    }

    [Test]
    public void Hospital_OverCapacity_CoverageIsCapacityLimited()
    {
        // Hospital: 80 beds. 3 tiles × 40 pop = 120 demand → only 2 covered (80 beds used)
        var grid = new CityGrid(20, 15);
        grid.SetFlatTerrain();

        for (var x = 0; x <= 15; x++) grid.SetZone(x, 7, ZoneType.Road);

        grid.SetZone(0, 6, ZoneType.Hospital);

        for (var x = 2; x <= 6; x += 2)
        {
            grid.SetZone(x, 6, ZoneType.Residential);
            MakeReady(grid, x, 6);
            SetPopulation(grid, x, 6, 40);
        }

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        Assert.That(result.HospitalBedsUsed,  Is.EqualTo(80), "Hospital fills exactly to capacity");
        Assert.That(result.HospitalCoveragePercent, Is.LessThan(1.0f),
            "Hospital should not cover all tiles once capacity is exhausted");
    }

    // ── Service capacity totals reflect building count ─────────────────────────

    [Test]
    public void SchoolSeatsTotal_ReflectsNumberOfBuildings()
    {
        // 3 schools → 600 total seats
        var grid = new CityGrid(20, 10);
        grid.SetFlatTerrain();

        for (var x = 0; x <= 18; x++) grid.SetZone(x, 5, ZoneType.Road);

        grid.SetZone(0, 4,  ZoneType.School);
        grid.SetZone(7, 4,  ZoneType.School);
        grid.SetZone(14, 4, ZoneType.School);

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        Assert.That(result.SchoolSeatsTotal, Is.EqualTo(600), "3 schools × 200 = 600 total seats");
    }

    [Test]
    public void PoliceCapacityTotal_ReflectsNumberOfBuildings()
    {
        // 2 police stations → 600 total capacity
        var grid = new CityGrid(15, 10);
        grid.SetFlatTerrain();

        for (var x = 0; x <= 12; x++) grid.SetZone(x, 5, ZoneType.Road);

        grid.SetZone(0, 4, ZoneType.PoliceStation);
        grid.SetZone(8, 4, ZoneType.PoliceStation);

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        Assert.That(result.PoliceCapacityTotal, Is.EqualTo(600), "2 stations × 300 = 600 total capacity");
    }

    // ── No road-adjacent service → covers nothing ──────────────────────────────

    [Test]
    public void ServiceWithNoRoadAccess_CoversNoTilesInCapacityModel()
    {
        // School placed with no adjacent road — should cover 0 tiles
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();

        // Road runs along y=7 but school is at y=3 — not adjacent to any road
        for (var x = 3; x <= 10; x++) grid.SetZone(x, 7, ZoneType.Road);

        grid.SetZone(5, 3, ZoneType.School);  // no road neighbour

        grid.SetZone(5, 6, ZoneType.Residential);
        MakeReady(grid, 5, 6);
        SetPopulation(grid, 5, 6, 50);

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        Assert.That(result.SchoolCoveragePercent, Is.EqualTo(0f),
            "School without road access cannot reach any tiles via road graph");
        Assert.That(result.SchoolSeatsUsed, Is.EqualTo(0));
    }

    // ── PoliceHQ / FireHQ use same capacity as base ───────────────────────────

    [Test]
    public void PoliceHQ_CapacityMatchesPoliceStation()
    {
        Assert.That(ServiceCapacityModel.Capacity[ZoneType.PoliceHQ],
            Is.EqualTo(ServiceCapacityModel.Capacity[ZoneType.PoliceStation]),
            "PoliceHQ should have same capacity as PoliceStation (300)");
    }

    [Test]
    public void FireHQ_CapacityMatchesFireStation()
    {
        Assert.That(ServiceCapacityModel.Capacity[ZoneType.FireHQ],
            Is.EqualTo(ServiceCapacityModel.Capacity[ZoneType.FireStation]),
            "FireHQ should have same capacity as FireStation (400)");
    }

    // ── ComputeServiceCoverage without road graph (fallback) ──────────────────

    [Test]
    public void NoRoadGraph_SchoolInManhattanRange_CoverageWorks()
    {
        // When no RoadGraph is provided, falls back to Manhattan distance.
        var grid = new CityGrid(15, 15);

        grid.SetZone(5, 5, ZoneType.School);  // no road needed in fallback mode

        grid.SetZone(5, 8, ZoneType.Residential);
        MakeReady(grid, 5, 8);
        SetPopulation(grid, 5, 8, 50);  // Manhattan distance 3 ≤ 5 (School Manhattan radius)

        // No road graph supplied
        var result = _happiness.ComputeServiceCoverage(grid, roadGraph: null);

        Assert.That(result.SchoolCoveragePercent, Is.EqualTo(1.0f),
            "School within Manhattan radius should cover tile in fallback mode");
        Assert.That(result.SchoolSeatsUsed, Is.EqualTo(50));
    }

    [Test]
    public void NoRoadGraph_SchoolBeyondManhattanRange_NoCoverage()
    {
        // Manhattan radius for School fallback = 5. Tile at distance 6 → not covered.
        var grid = new CityGrid(20, 15);

        grid.SetZone(5, 5, ZoneType.School);

        grid.SetZone(5, 12, ZoneType.Residential);  // Manhattan distance = 7 > 5
        MakeReady(grid, 5, 12);
        SetPopulation(grid, 5, 12, 50);

        var result = _happiness.ComputeServiceCoverage(grid, roadGraph: null);

        Assert.That(result.SchoolCoveragePercent, Is.EqualTo(0f),
            "School should not cover tile beyond Manhattan radius 5 in fallback mode");
    }

    // ── Zero-population residential tiles ────────────────────────────────────

    [Test]
    public void ZeroPopResidential_SchoolCoversItWithoutDrainingSeats()
    {
        // Residential tiles with Population=0 cost 0 school seats.
        // The tile should be marked covered, but seats remain available for other tiles.
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();

        for (var x = 0; x <= 10; x++) grid.SetZone(x, 5, ZoneType.Road);
        grid.SetZone(0, 4, ZoneType.School);

        // Tile 1: pop 0 (no building)
        grid.SetZone(3, 4, ZoneType.Residential);
        MakeReady(grid, 3, 4);

        // Tile 2: pop 150 (real demand)
        grid.SetZone(7, 4, ZoneType.Residential);
        MakeReady(grid, 7, 4);
        SetPopulation(grid, 7, 4, 150);

        var rg = BuildRoadGraph(grid);
        var result = _happiness.ComputeServiceCoverage(grid, rg);

        // Zero-pop tile is covered but costs 0; tile with 150 pop costs 150 seats
        Assert.That(result.SchoolSeatsUsed, Is.EqualTo(150),
            "Zero-pop tile should not consume school seats");
        Assert.That(result.SchoolCoveragePercent, Is.EqualTo(1.0f),
            "Both tiles should be marked covered");
    }
}
