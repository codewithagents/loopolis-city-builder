using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Buildings;

[TestFixture]
public class BuildingGrowthSystemTests
{
    private BuildingGrowthSystem _system = null!;

    [SetUp]
    public void SetUp() => _system = new BuildingGrowthSystem();

    // Helper: give a tile power + road access (makes it eligible for initialization)
    private static void MakeRoadAccessible(CityGrid grid, int x, int y)
    {
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
    }

    // ── Test 1: Road-adjacent tile → initialized as 1×1 building ─────────────

    [Test]
    public void RoadAdjacent_ResidentialTile_GetsInitializedAsBuilding()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeRoadAccessible(grid, 5, 5);

        _system.Initialize(grid);

        Assert.That(grid.GetTile(5, 5).BuildingId, Is.Not.Null,
            "Road-adjacent residential tile should get a building ID after Initialize()");
        Assert.That(grid.Buildings.Count, Is.EqualTo(1),
            "Exactly one building should exist in the grid");

        var building = grid.Buildings.Values.First();
        Assert.That(building.Width, Is.EqualTo(1));
        Assert.That(building.Height, Is.EqualTo(1));
        Assert.That(building.TypeId, Is.EqualTo("res_house_1x1"));
    }

    // ── Test 2: Interior tile (no road access) → NOT initialized ─────────────

    [Test]
    public void Interior_Tile_WithNoRoadAccess_IsNotInitialized()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPower(5, 5, true);
        // Intentionally NO SetRoadAccess — interior tile

        _system.Initialize(grid);

        Assert.That(grid.GetTile(5, 5).BuildingId, Is.Null,
            "Interior tile without road access should NOT get a building ID");
        Assert.That(grid.Buildings.Count, Is.EqualTo(0));
    }

    // ── Test 3: Building at <80% capacity → does NOT grow ────────────────────

    [Test]
    public void Building_AtLessThan80Percent_DoesNotGrow()
    {
        var grid = new CityGrid(10, 10);
        // Set up 4 tiles of residential in a 2x2 block with road access
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            MakeRoadAccessible(grid, 5 + dx, 5 + dy);
        }

        _system.Initialize(grid);
        // Buildings initialized as 1x1 each
        Assert.That(grid.Buildings.Count, Is.EqualTo(4));

        // Set population at (5,5) to 30 out of 50 = 60% (below 80% threshold)
        var buildingId = grid.GetTile(5, 5).BuildingId!;
        grid.SetPopulation(5, 5, 30);

        var buildingCountBefore = grid.Buildings.Count;
        _system.TryGrow(grid, GameState.Active);

        Assert.That(grid.Buildings.Count, Is.EqualTo(buildingCountBefore),
            "Building at 60% capacity should not attempt to grow");
    }

    // ── Test 4: Building at ≥80% capacity → grows to 2×2 (Townhouse) ─────────

    [Test]
    public void Building_AtOrAbove80Percent_GrowsTo2x2()
    {
        var grid = new CityGrid(10, 10);
        // Set up a 2x2 zone block with road access on all tiles
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            MakeRoadAccessible(grid, 5 + dx, 5 + dy);
        }

        _system.Initialize(grid);
        Assert.That(grid.Buildings.Count, Is.EqualTo(4), "Should start with 4 separate 1x1 buildings");

        // Fill (5,5) to 41/50 = 82% capacity
        grid.SetPopulation(5, 5, 41);

        _system.TryGrow(grid, GameState.Active);

        // A 2x2 building should have formed (townhouse)
        var multiTileBuildings = grid.Buildings.Values.Where(b => b.TileCount > 1).ToList();
        Assert.That(multiTileBuildings.Count, Is.GreaterThanOrEqualTo(1),
            "At least one 2x2 building (townhouse) should have formed");
        Assert.That(multiTileBuildings[0].TypeId, Is.EqualTo("res_townhouse_2x2"));
    }

    // ── Test 5: Growing building absorbs smaller building in footprint ─────────

    [Test]
    public void GrowingBuilding_AbsorbsSmallerBuildingInFootprint()
    {
        var grid = new CityGrid(10, 10);
        // Four tiles, all with road access
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            MakeRoadAccessible(grid, 5 + dx, 5 + dy);
        }

        _system.Initialize(grid);
        var initialCount = grid.Buildings.Count;
        Assert.That(initialCount, Is.EqualTo(4));

        // Fill the trigger building at (5,5) to above 80%
        grid.SetPopulation(5, 5, 45); // 90%

        _system.TryGrow(grid, GameState.Active);

        // The new 2x2 building should have absorbed the others in its footprint
        // Total buildings should be less than 4
        Assert.That(grid.Buildings.Count, Is.LessThan(initialCount),
            "Smaller buildings within the new footprint should be absorbed");

        // All tiles in the 2x2 area should now point to the same building
        var b00 = grid.GetTile(5, 5).BuildingId;
        var b10 = grid.GetTile(6, 5).BuildingId;
        var b01 = grid.GetTile(5, 6).BuildingId;
        var b11 = grid.GetTile(6, 6).BuildingId;

        Assert.That(b00, Is.Not.Null);
        Assert.That(b00, Is.EqualTo(b10));
        Assert.That(b00, Is.EqualTo(b01));
        Assert.That(b00, Is.EqualTo(b11));
    }

    // ── Test 6: Villa (2×3) requires forest within 3 tiles ──────────────────

    [Test]
    public void Villa_RequiresForestNearby_FailsWithoutForest()
    {
        var grid = new CityGrid(20, 20);
        // Set up a 2x3 block with road access
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 3; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            MakeRoadAccessible(grid, 5 + dx, 5 + dy);
        }

        _system.Initialize(grid);

        // Fill all 1x1 buildings to 100% — they're eligible to grow
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 3; dy++)
            grid.SetPopulation(5 + dx, 5 + dy, 50);

        // Advance to Town milestone so villa is unlocked by milestone
        // but there is NO forest nearby

        _system.TryGrow(grid, GameState.Town);

        // No villa should have formed (no forest)
        var villas = grid.Buildings.Values
            .Where(b => b.TypeId == "res_villa_2x3" || b.TypeId == "res_villa_3x2")
            .ToList();
        Assert.That(villas.Count, Is.EqualTo(0),
            "Villa requires forest nearby — should not form without it");
    }

    [Test]
    public void Villa_RequiresForestNearby_SucceedsWithForestPresent()
    {
        var grid = new CityGrid(20, 20);
        // Set up a 2x3 block with road access
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 3; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            MakeRoadAccessible(grid, 5 + dx, 5 + dy);
        }

        _system.Initialize(grid);

        // Place forest within 3 tiles of the footprint (Chebyshev distance)
        grid.SetTerrain(3, 5, TerrainType.Forest); // 2 tiles away from column 5

        // Fill all 1x1 buildings to 100%
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 3; dy++)
            grid.SetPopulation(5 + dx, 5 + dy, 50);

        _system.TryGrow(grid, GameState.Town);

        var villas = grid.Buildings.Values
            .Where(b => b.TypeId == "res_villa_2x3" || b.TypeId == "res_villa_3x2")
            .ToList();
        Assert.That(villas.Count, Is.GreaterThanOrEqualTo(1),
            "Villa should form when forest is within 3 tiles");
    }

    // ── Test 7: ApartmentBlock (4×4) requires City milestone ─────────────────

    [Test]
    public void ApartmentBlock_RequiresCityMilestone_FailsAtActiveMilestone()
    {
        var grid = new CityGrid(20, 20);
        // Set up a 4x4 block with road access and all services
        for (var dx = 0; dx < 4; dx++)
        for (var dy = 0; dy < 4; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            MakeRoadAccessible(grid, 5 + dx, 5 + dy);
        }
        // Services in range
        grid.SetZone(1, 1, ZoneType.School);
        grid.SetZone(1, 2, ZoneType.PoliceStation);
        grid.SetZone(1, 3, ZoneType.FireStation);

        _system.Initialize(grid);

        // Fill all tiles to 100%
        for (var dx = 0; dx < 4; dx++)
        for (var dy = 0; dy < 4; dy++)
            grid.SetPopulation(5 + dx, 5 + dy, 50);

        _system.TryGrow(grid, GameState.Active); // only Active milestone

        var apartments = grid.Buildings.Values
            .Where(b => b.TypeId == "res_apartment_4x4")
            .ToList();
        Assert.That(apartments.Count, Is.EqualTo(0),
            "Apartment block requires City milestone — should not form at Active state");
    }

    [Test]
    public void ApartmentBlock_RequiresCityMilestone_SucceedsAtCityMilestone()
    {
        var grid = new CityGrid(20, 20);
        // Set up a 4x4 block with road access
        for (var dx = 0; dx < 4; dx++)
        for (var dy = 0; dy < 4; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            MakeRoadAccessible(grid, 5 + dx, 5 + dy);
        }
        // Place services within range (10 tiles Manhattan distance)
        grid.SetZone(1, 1, ZoneType.School);
        grid.SetZone(1, 2, ZoneType.PoliceStation);
        grid.SetZone(1, 3, ZoneType.FireStation);

        _system.Initialize(grid);

        // Fill all tiles to 100%
        for (var dx = 0; dx < 4; dx++)
        for (var dy = 0; dy < 4; dy++)
            grid.SetPopulation(5 + dx, 5 + dy, 50);

        _system.TryGrow(grid, GameState.City); // City milestone reached

        var apartments = grid.Buildings.Values
            .Where(b => b.TypeId == "res_apartment_4x4")
            .ToList();
        Assert.That(apartments.Count, Is.GreaterThanOrEqualTo(1),
            "Apartment block should form at City milestone with all services in range");
    }

    // ── Test 8: ApartmentBlock requires service coverage (school+police+fire) ──

    [Test]
    public void ApartmentBlock_RequiresAllServices_FailsWhenMissingService()
    {
        var grid = new CityGrid(20, 20);
        // 4x4 residential block
        for (var dx = 0; dx < 4; dx++)
        for (var dy = 0; dy < 4; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            MakeRoadAccessible(grid, 5 + dx, 5 + dy);
        }
        // Only School and Police — missing FireStation
        grid.SetZone(1, 1, ZoneType.School);
        grid.SetZone(1, 2, ZoneType.PoliceStation);
        // No FireStation

        _system.Initialize(grid);

        for (var dx = 0; dx < 4; dx++)
        for (var dy = 0; dy < 4; dy++)
            grid.SetPopulation(5 + dx, 5 + dy, 50);

        _system.TryGrow(grid, GameState.City);

        var apartments = grid.Buildings.Values
            .Where(b => b.TypeId == "res_apartment_4x4")
            .ToList();
        Assert.That(apartments.Count, Is.EqualTo(0),
            "Apartment block requires all three services — should not form when FireStation is missing");
    }

    // ── Test 9: Partial footprint off-grid → rejected ─────────────────────────

    [Test]
    public void Footprint_PartiallyOffGrid_IsRejected()
    {
        var grid = new CityGrid(6, 6); // small grid
        // Place a residential tile at (5,5) — rightmost corner
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeRoadAccessible(grid, 5, 5);

        _system.Initialize(grid);

        Assert.That(grid.Buildings.Count, Is.EqualTo(1));

        // Fill to 100% — normally would try to grow to 2x2 or larger
        grid.SetPopulation(5, 5, 50);

        _system.TryGrow(grid, GameState.Active);

        // The 2x2 footprint would go off-grid — should be rejected
        // Building might stay 1x1 (no valid anchor in bounds)
        var b = grid.Buildings.Values.First();
        // If it tries to grow to 2x2 from (5,5), anchor at (4,4) would be valid if grid has room
        // At corner (5,5) in a 6x6 grid, anchor must be ≤ 4 and ≤ 4 to fit 2x2
        // (5,5) can anchor at (4,4): tiles (4,4),(5,4),(4,5),(5,5) — but those tiles must be residential
        // Since only (5,5) is residential, all other tiles in footprint are Empty → rejected
        Assert.That(b.TileCount, Is.EqualTo(1),
            "Building at corner with no adjacent residential should stay 1x1");
    }

    // ── P1: Power-as-Density Unlock tests ─────────────────────────────────────

    [Test]
    public void ResHouse1x1_GrowsWithRoadOnly_NoPowerNeeded()
    {
        // res_house_1x1 should be initialized from road access alone — no power required.
        // This is the P1 design: basic cottage can form without power.
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetRoadAccess(5, 5, true);
        // Intentionally NO SetPower — should still initialize as res_house_1x1

        _system.Initialize(grid);

        Assert.That(grid.GetTile(5, 5).BuildingId, Is.Not.Null,
            "Residential tile with road access (no power) should get a building ID");
        Assert.That(grid.Buildings.Count, Is.EqualTo(1));
        Assert.That(grid.Buildings.Values.First().TypeId, Is.EqualTo("res_house_1x1"),
            "Should be initialized as res_house_1x1 even without power");
    }

    [Test]
    public void ResHouse1x1_Capacity25_WhenUnpowered()
    {
        // Unpowered cottage: effective capacity = 25 (half of normal 50).
        Assert.That(BuildingGrowthSystem.GetEffectiveCapacity("res_house_1x1", hasPower: false), Is.EqualTo(25),
            "Unpowered res_house_1x1 should have capacity 25");
    }

    [Test]
    public void ResHouse1x1_Capacity50_WhenPowered()
    {
        // Powered cottage: full capacity = 50.
        Assert.That(BuildingGrowthSystem.GetEffectiveCapacity("res_house_1x1", hasPower: true), Is.EqualTo(50),
            "Powered res_house_1x1 should have capacity 50");
    }

    [Test]
    public void ResTownhouse_RequiresPower_NoGrowthWithoutIt()
    {
        // 2×2 townhouse requires ALL tiles powered. Without power, TryGrow should refuse to create it.
        var grid = new CityGrid(10, 10);
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            // Road access but NO power — cottage can form, townhouse cannot
            grid.SetRoadAccess(5 + dx, 5 + dy, true);
        }

        _system.Initialize(grid);
        Assert.That(grid.Buildings.Count, Is.EqualTo(4), "Should have 4 unpowered cottages");

        // Fill all cottages to capacity to trigger growth attempt
        foreach (var b in grid.Buildings.Values)
            foreach (var (tx, ty) in b.Tiles())
                grid.SetPopulation(tx, ty, 50);

        _system.TryGrow(grid, GameState.Active);

        // No townhouse should have formed — all tiles lack power
        var townhouses = grid.Buildings.Values.Where(b => b.TypeId == "res_townhouse_2x2").ToList();
        Assert.That(townhouses.Count, Is.EqualTo(0),
            "Townhouse (2×2) requires all tiles powered — should not form without power");

        // All buildings should still be 1×1 cottages
        Assert.That(grid.Buildings.Values.All(b => b.TileCount == 1), Is.True,
            "All buildings should remain 1×1 cottages when tiles are unpowered");
    }

    [Test]
    public void GetEffectiveCapacity_OtherBuildings_UseStandardFormula()
    {
        // All buildings other than res_house_1x1 use the standard formula (tiles × 50),
        // regardless of power status (they require power to form in the first place).
        Assert.That(BuildingGrowthSystem.GetEffectiveCapacity("res_townhouse_2x2", hasPower: false), Is.EqualTo(200),
            "2×2 townhouse should always have capacity 200 (4 tiles × 50)");
        Assert.That(BuildingGrowthSystem.GetEffectiveCapacity("res_townhouse_2x2", hasPower: true), Is.EqualTo(200),
            "Powered 2×2 townhouse should also have capacity 200");
        Assert.That(BuildingGrowthSystem.GetEffectiveCapacity("com_shop_1x1", hasPower: false), Is.EqualTo(50),
            "Commercial 1×1 should have capacity 50 regardless of power");
        Assert.That(BuildingGrowthSystem.GetEffectiveCapacity("ind_factory_1x1", hasPower: true), Is.EqualTo(50),
            "Industrial 1×1 should have capacity 50 regardless of power");
    }
}

/// <summary>
/// Tests for unpowered industrial and pollution behaviour (Part 5 / Part 5 design).
/// </summary>
[TestFixture]
public class UnpoweredSystemsTests
{
    [Test]
    public void UnpoweredIndustrial_Has2Jobs()
    {
        // Unpowered industrial tile with road access provides exactly UnpoweredIndustrialJobs (2) jobs.
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Industrial);
        grid.SetRoadAccess(5, 5, true);
        // HasPower = false (default)
        grid.SetPopulation(5, 5, 50); // full activity, but power gated

        var employment = new EmploymentSystem();
        employment.Propagate(grid, totalPopulation: 0);

        Assert.That(employment.AvailableJobs, Is.EqualTo(EmploymentSystem.UnpoweredIndustrialJobs),
            "Unpowered industrial should provide exactly 2 placeholder jobs");
    }

    [Test]
    public void UnpoweredIndustrial_HasZeroPollution()
    {
        // Unpowered industrial: no production, no smoke.
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Industrial);
        // HasPower = false (default)

        var pollution = new PollutionSystem();
        pollution.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).PollutionLevel, Is.EqualTo(0.0),
            "Unpowered industrial tile should emit zero pollution (no production = no smoke)");

        // Neighboring tiles should also be clean
        Assert.That(grid.GetTile(5, 6).PollutionLevel, Is.EqualTo(0.0),
            "Neighbor of unpowered industrial should also have zero pollution");
    }

    [Test]
    public void PoweredIndustrial_StillEmitsPollution()
    {
        // Powered industrial still emits at full strength 1.0.
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Industrial);
        grid.SetPower(5, 5, true); // powered

        var pollution = new PollutionSystem();
        pollution.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).PollutionLevel, Is.EqualTo(1.0).Within(0.001),
            "Powered industrial should emit full pollution");
    }

    [Test]
    public void UnpoweredIndustrial_WithRoad_Provides2Jobs_NotActivityBased()
    {
        // Even with very high activity on the tile, unpowered = only 2 jobs (not activity-scaled).
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Industrial);
        grid.SetRoadAccess(5, 5, true);
        grid.SetPopulation(5, 5, 50); // if activity-scaled: 50 × 0.4 = 20 jobs; but unpowered = 2

        var employment = new EmploymentSystem();
        employment.Propagate(grid, totalPopulation: 0);

        Assert.That(employment.AvailableJobs, Is.EqualTo(2),
            "Unpowered industrial jobs should be 2 regardless of activity level");
        Assert.That(employment.AvailableJobs, Is.Not.EqualTo(20),
            "Should NOT scale by activity when unpowered");
    }
}
