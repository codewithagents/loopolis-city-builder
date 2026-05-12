using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Grid;

[TestFixture]
public class CityGridTests
{
    [Test]
    public void NewGrid_AllTilesAreEmpty()
    {
        var grid = new CityGrid(10, 10);

        Assert.That(grid.AllTiles().All(t => t.Zone == ZoneType.Empty), Is.True);
    }

    [Test]
    public void SetZone_TileReflectsNewZone()
    {
        var grid = new CityGrid(10, 10);

        grid.SetZone(3, 4, ZoneType.Residential);

        Assert.That(grid.GetTile(3, 4).Zone, Is.EqualTo(ZoneType.Residential));
    }

    [Test]
    public void SetZone_DoesNotAffectOtherTiles()
    {
        var grid = new CityGrid(10, 10);

        grid.SetZone(3, 4, ZoneType.Residential);

        Assert.That(grid.GetTile(0, 0).Zone, Is.EqualTo(ZoneType.Empty));
        Assert.That(grid.GetTile(3, 5).Zone, Is.EqualTo(ZoneType.Empty));
    }

    [Test]
    public void SetZone_OutOfBounds_Throws()
    {
        var grid = new CityGrid(10, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SetZone(10, 0, ZoneType.Residential));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SetZone(-1, 0, ZoneType.Residential));
    }

    [Test]
    public void TilesOfType_ReturnsCorrectCount()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(0, 0, ZoneType.Residential);
        grid.SetZone(1, 0, ZoneType.Residential);
        grid.SetZone(2, 0, ZoneType.Commercial);

        Assert.That(grid.TilesOfType(ZoneType.Residential).Count(), Is.EqualTo(2));
        Assert.That(grid.TilesOfType(ZoneType.Commercial).Count(), Is.EqualTo(1));
        Assert.That(grid.TilesOfType(ZoneType.Industrial).Count(), Is.EqualTo(0));
    }

    [Test]
    public void AdjacentTiles_CenterTile_ReturnsFourNeighbors()
    {
        var grid = new CityGrid(10, 10);
        var neighbors = grid.AdjacentTiles(5, 5).ToList();

        Assert.That(neighbors.Count, Is.EqualTo(4));
    }

    [Test]
    public void AdjacentTiles_CornerTile_ReturnsTwoNeighbors()
    {
        var grid = new CityGrid(10, 10);
        var neighbors = grid.AdjacentTiles(0, 0).ToList();

        Assert.That(neighbors.Count, Is.EqualTo(2));
    }

    // ── Height level tests ───────────────────────────────────────────────────

    [Test]
    public void SetHeightLevel_Water_BlocksZonePlacement()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(5, 5, 0); // water
        grid.SetZone(5, 5, ZoneType.Residential);
        Assert.That(grid.GetTile(5, 5).Zone, Is.EqualTo(ZoneType.Empty));
    }

    [Test]
    public void SetHeightLevel_Elevated_AllowsPlacement()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(5, 5, 3); // elevated
        grid.SetZone(5, 5, ZoneType.Residential);
        Assert.That(grid.GetTile(5, 5).Zone, Is.EqualTo(ZoneType.Residential));
    }

    [Test]
    public void GetHeightLevel_DefaultsToOne()
    {
        var grid = new CityGrid(10, 10);
        Assert.That(grid.GetHeightLevel(0, 0), Is.EqualTo(1));
    }

    [Test]
    public void GetHeightLevel_ReflectsSetHeightLevel()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(3, 7, 4);
        Assert.That(grid.GetHeightLevel(3, 7), Is.EqualTo(4));
    }

    [Test]
    public void GetTile_ReflectsHeightLevel()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(2, 2, 5);
        Assert.That(grid.GetTile(2, 2).HeightLevel, Is.EqualTo(5));
    }

    [Test]
    public void GetTile_DerivedTerrain_ReflectsHeight()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(2, 2, 3);  // elevated → Hill
        Assert.That(grid.GetTile(2, 2).Terrain, Is.EqualTo(TerrainType.Hill));
    }

    [Test]
    public void AllTiles_ReflectsHeightLevel()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(0, 0, 0); // water
        var waterTile = grid.AllTiles().First(t => t.X == 0 && t.Y == 0);
        Assert.That(waterTile.HeightLevel, Is.EqualTo(0));
        Assert.That(waterTile.Terrain, Is.EqualTo(TerrainType.Water));
    }

    [Test]
    public void GetHeightLevel_OutOfBounds_ReturnsOne()
    {
        var grid = new CityGrid(10, 10);
        Assert.That(grid.GetHeightLevel(-1, 0), Is.EqualTo(1));
        Assert.That(grid.GetHeightLevel(10, 0), Is.EqualTo(1));
    }

    [Test]
    public void SetForest_ReflectsOnTile()
    {
        var grid = new CityGrid(10, 10);
        grid.SetForest(3, 4, true);
        Assert.That(grid.GetTile(3, 4).HasForest, Is.True);
        Assert.That(grid.HasForestAt(3, 4), Is.True);
    }

    [Test]
    public void HasForestAt_DefaultIsFalse()
    {
        var grid = new CityGrid(10, 10);
        Assert.That(grid.HasForestAt(0, 0), Is.False);
    }

    [Test]
    public void DerivedTerrain_Forest_WhenHasForestAndElevatedFalse()
    {
        var grid = new CityGrid(10, 10);
        grid.SetForest(5, 5, true); // height=1 (flat) but has forest
        Assert.That(grid.GetTile(5, 5).Terrain, Is.EqualTo(TerrainType.Forest));
    }

    // ── Backward-compat shim tests (SetTerrain / GetTerrain) ────────────────

    [Test]
    public void SetTerrain_Water_BlocksZonePlacement()
    {
        var grid = new CityGrid(10, 10);
        grid.SetTerrain(5, 5, TerrainType.Water); // shim: sets HeightLevel=0
        grid.SetZone(5, 5, ZoneType.Residential);
        Assert.That(grid.GetTile(5, 5).Zone, Is.EqualTo(ZoneType.Empty));
    }

    [Test]
    public void SetTerrain_Hill_AllowsPlacement()
    {
        var grid = new CityGrid(10, 10);
        grid.SetTerrain(5, 5, TerrainType.Hill); // shim: sets HeightLevel=3
        grid.SetZone(5, 5, ZoneType.Residential);
        Assert.That(grid.GetTile(5, 5).Zone, Is.EqualTo(ZoneType.Residential));
    }

    [Test]
    public void GetTerrain_DefaultsToFlat()
    {
        var grid = new CityGrid(10, 10);
        Assert.That(grid.GetTerrain(0, 0), Is.EqualTo(TerrainType.Flat));
    }

    [Test]
    public void GetTerrain_ReflectsSetTerrain()
    {
        var grid = new CityGrid(10, 10);
        grid.SetTerrain(3, 7, TerrainType.Forest); // shim: sets HasForest=true
        Assert.That(grid.GetTerrain(3, 7), Is.EqualTo(TerrainType.Forest));
    }

    [Test]
    public void GetTile_IncludesTerrain()
    {
        var grid = new CityGrid(10, 10);
        grid.SetTerrain(2, 2, TerrainType.Hill); // shim: sets HeightLevel=3
        Assert.That(grid.GetTile(2, 2).Terrain, Is.EqualTo(TerrainType.Hill));
    }

    [Test]
    public void AllTiles_IncludesTerrainForEachTile()
    {
        var grid = new CityGrid(10, 10);
        grid.SetTerrain(0, 0, TerrainType.Water); // shim: sets HeightLevel=0
        var waterTile = grid.AllTiles().First(t => t.X == 0 && t.Y == 0);
        Assert.That(waterTile.Terrain, Is.EqualTo(TerrainType.Water));
    }

    [Test]
    public void GetTerrain_OutOfBounds_ReturnsFlat()
    {
        var grid = new CityGrid(10, 10);
        Assert.That(grid.GetTerrain(-1, 0), Is.EqualTo(TerrainType.Flat));
        Assert.That(grid.GetTerrain(10, 0), Is.EqualTo(TerrainType.Flat));
    }

    // ── IsPlateau / IsCliffEdge / CanPlaceRoad tests ─────────────────────────

    [Test]
    public void IsPlateau_AllNeighborsElevatedWithinOne_ReturnsTrue()
    {
        // 3×3 grid of height 3 tiles — center is a plateau (all neighbours diff ≤ 1)
        var grid = new CityGrid(10, 10);
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
            grid.SetHeightLevel(5 + dx, 5 + dy, 3);

        Assert.That(grid.IsPlateau(5, 5), Is.True,
            "A tile surrounded by same-height elevated tiles should be a plateau.");
    }

    [Test]
    public void IsPlateau_OneSteepNeighbor_ReturnsFalse()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(5, 5, 3);
        grid.SetHeightLevel(5, 4, 1); // diff of 2 → cliff
        grid.SetHeightLevel(5, 6, 3);
        grid.SetHeightLevel(4, 5, 3);
        grid.SetHeightLevel(6, 5, 3);

        Assert.That(grid.IsPlateau(5, 5), Is.False,
            "A tile with one steep neighbour should not be a plateau.");
    }

    [Test]
    public void IsPlateau_FlatTile_ReturnsFalse()
    {
        var grid = new CityGrid(10, 10);
        // height=1 (default) — not elevated, so not a plateau
        Assert.That(grid.IsPlateau(5, 5), Is.False,
            "A flat tile (height=1) is not a plateau.");
    }

    [Test]
    public void IsCliffEdge_NeighborDiffGreaterThanOne_ReturnsTrue()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(5, 5, 3);
        grid.SetHeightLevel(5, 6, 1); // diff of 2 → cliff

        Assert.That(grid.IsCliffEdge(5, 5), Is.True,
            "Tile adjacent to one with height diff > 1 should be a cliff edge.");
    }

    [Test]
    public void IsCliffEdge_AllNeighborsDiffAtMostOne_ReturnsFalse()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(5, 5, 3);
        grid.SetHeightLevel(5, 4, 2); // diff 1 — ok
        grid.SetHeightLevel(5, 6, 3); // diff 0 — ok
        grid.SetHeightLevel(4, 5, 3); // diff 0 — ok
        grid.SetHeightLevel(6, 5, 4); // diff 1 — ok

        Assert.That(grid.IsCliffEdge(5, 5), Is.False,
            "Tile with all neighbours within diff 1 should not be a cliff edge.");
    }

    [Test]
    public void CanPlaceRoad_GentleSlope_ReturnsTrue()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(5, 5, 2);
        grid.SetHeightLevel(5, 4, 1); // diff 1 — allowed
        grid.SetHeightLevel(5, 6, 2); // diff 0 — ok
        grid.SetHeightLevel(4, 5, 2); // diff 0 — ok
        grid.SetHeightLevel(6, 5, 3); // diff 1 — allowed

        var (ok, reason) = grid.CanPlaceRoad(5, 5);
        Assert.That(ok, Is.True, "Road placement on gentle slope (diff ≤ 1) should succeed.");
        Assert.That(reason, Is.Null);
    }

    [Test]
    public void CanPlaceRoad_CliffEdge_ReturnsFalse()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(5, 5, 3);
        grid.SetHeightLevel(5, 6, 1); // diff of 2 → cliff

        var (ok, reason) = grid.CanPlaceRoad(5, 5);
        Assert.That(ok, Is.False,
            "Road placement on cliff edge (diff > 1) should be rejected.");
        Assert.That(reason, Does.Contain("cliff"));
    }

    [Test]
    public void CanPlaceRoad_WaterTile_ReturnsFalse()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(5, 5, 0); // water

        var (ok, reason) = grid.CanPlaceRoad(5, 5);
        Assert.That(ok, Is.False, "Road cannot be placed on water.");
    }

    [Test]
    public void SetZone_CannotOverwriteOccupiedTileWithNonEmpty()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant);

        // Attempt to place Residential on top of PowerPlant — should be silently blocked
        grid.SetZone(5, 5, ZoneType.Residential);

        Assert.That(grid.GetTile(5, 5).Zone, Is.EqualTo(ZoneType.PowerPlant),
            "Occupied tiles cannot be overwritten with a non-empty zone");
    }

    [Test]
    public void SetZone_EmptyEraseAllowedOnOccupiedTile()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant);

        // Erase via ZoneType.Empty must always work
        grid.SetZone(5, 5, ZoneType.Empty);

        Assert.That(grid.GetTile(5, 5).Zone, Is.EqualTo(ZoneType.Empty),
            "Erasing (ZoneType.Empty) must always be permitted on occupied tiles");
    }

    [Test]
    public void GetPlacementCost_ForestAdds75()
    {
        var cost = BudgetSystem.GetPlacementCost("Road", TerrainType.Forest);
        Assert.That(cost, Is.EqualTo(25.0 + 75.0)); // Road base = $25
    }

    [Test]
    public void GetPlacementCost_HillAdds50()
    {
        var cost = BudgetSystem.GetPlacementCost("Residential", TerrainType.Hill);
        Assert.That(cost, Is.EqualTo(50.0 + 50.0)); // Residential base = $50
    }

    [Test]
    public void GetPlacementCost_FlatNoSurcharge()
    {
        var cost = BudgetSystem.GetPlacementCost("Road", TerrainType.Flat);
        Assert.That(cost, Is.EqualTo(25.0)); // Road base = $25, no surcharge
    }

    // ── EraseBuildingAt tests ─────────────────────────────────────────────────

    [Test]
    public void EraseBuildingAt_RemovesBuildingFromRegistry()
    {
        var grid = new CityGrid(10, 10);
        // Place a 2×2 residential building manually
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(3 + dx, 3 + dy, ZoneType.Residential);
            grid.SetPopulation(3 + dx, 3 + dy, 20);
        }
        var id = "bldg01";
        var building = new Loopolis.Core.Buildings.Building(id, "res_townhouse_2x2", ZoneType.Residential, 3, 3, 2, 2);
        grid.Buildings[id] = building;
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
            grid.SetBuildingId(3 + dx, 3 + dy, id);

        grid.EraseBuildingAt(3, 3);

        Assert.That(grid.Buildings.ContainsKey(id), Is.False,
            "Building should be removed from registry after EraseBuildingAt");
    }

    [Test]
    public void EraseBuildingAt_ClearsBuildingIdOnAllTiles()
    {
        var grid = new CityGrid(10, 10);
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(3 + dx, 3 + dy, ZoneType.Residential);
        }
        var id = "bldg02";
        var building = new Loopolis.Core.Buildings.Building(id, "res_townhouse_2x2", ZoneType.Residential, 3, 3, 2, 2);
        grid.Buildings[id] = building;
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
            grid.SetBuildingId(3 + dx, 3 + dy, id);

        grid.EraseBuildingAt(3, 4); // erase via non-anchor tile

        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
            Assert.That(grid.GetTile(3 + dx, 3 + dy).BuildingId, Is.Null,
                $"All tiles should have BuildingId=null after EraseBuildingAt");
    }

    [Test]
    public void EraseBuildingAt_ResetsPopulationOnAllTiles()
    {
        var grid = new CityGrid(10, 10);
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(3 + dx, 3 + dy, ZoneType.Residential);
            grid.SetPopulation(3 + dx, 3 + dy, 30);
        }
        var id = "bldg03";
        var building = new Loopolis.Core.Buildings.Building(id, "res_townhouse_2x2", ZoneType.Residential, 3, 3, 2, 2);
        grid.Buildings[id] = building;
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
            grid.SetBuildingId(3 + dx, 3 + dy, id);

        grid.EraseBuildingAt(3, 3);

        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
            Assert.That(grid.GetTile(3 + dx, 3 + dy).Population, Is.EqualTo(0),
                $"Population should be 0 after EraseBuildingAt");
    }

    [Test]
    public void EraseBuildingAt_NoBuildingId_IsNoOp()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        // No building registered

        Assert.DoesNotThrow(() => grid.EraseBuildingAt(5, 5),
            "EraseBuildingAt on a tile with no BuildingId should be a no-op");
    }

    [Test]
    public void SetZone_ErasingTileWithBuildingId_DemolishesBuilding()
    {
        // When SetZone(Empty) is called on a tile that is part of a multi-tile building,
        // the building should be demolished (removed from Buildings registry).
        var grid = new CityGrid(10, 10);
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(3 + dx, 3 + dy, ZoneType.Residential);
        }
        var id = "bldg04";
        var building = new Loopolis.Core.Buildings.Building(id, "res_townhouse_2x2", ZoneType.Residential, 3, 3, 2, 2);
        grid.Buildings[id] = building;
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
            grid.SetBuildingId(3 + dx, 3 + dy, id);

        // Erase the anchor tile via SetZone (as Godot standalone mode does)
        grid.SetZone(3, 3, ZoneType.Empty);

        Assert.That(grid.Buildings.ContainsKey(id), Is.False,
            "SetZone(Empty) on a building tile should demolish the building");
        // All tiles should have BuildingId cleared
        Assert.That(grid.GetTile(4, 4).BuildingId, Is.Null,
            "All tiles in the building footprint should have BuildingId=null");
    }

    [Test]
    public void SetZone_EraseOnTileWithOrphanBuildingId_ClearsReference()
    {
        // If tile has a BuildingId that is NOT in the Buildings registry (orphaned reference),
        // EraseBuildingAt should still clear the tile's BuildingId without crashing.
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetBuildingId(5, 5, "orphaned_id"); // no entry in Buildings dict

        Assert.DoesNotThrow(() => grid.SetZone(5, 5, ZoneType.Empty),
            "Erasing a tile with an orphaned BuildingId should not crash");
        Assert.That(grid.GetTile(5, 5).BuildingId, Is.Null,
            "Tile's BuildingId should be cleared even when building registry entry is missing");
    }
}
