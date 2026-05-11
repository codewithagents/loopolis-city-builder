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

    [Test]
    public void SetTerrain_Water_BlocksZonePlacement()
    {
        var grid = new CityGrid(10, 10);
        grid.SetTerrain(5, 5, TerrainType.Water);
        grid.SetZone(5, 5, ZoneType.Residential);
        Assert.That(grid.GetTile(5, 5).Zone, Is.EqualTo(ZoneType.Empty));
    }

    [Test]
    public void SetTerrain_Hill_AllowsPlacement()
    {
        var grid = new CityGrid(10, 10);
        grid.SetTerrain(5, 5, TerrainType.Hill);
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
        grid.SetTerrain(3, 7, TerrainType.Forest);
        Assert.That(grid.GetTerrain(3, 7), Is.EqualTo(TerrainType.Forest));
    }

    [Test]
    public void GetTile_IncludesTerrain()
    {
        var grid = new CityGrid(10, 10);
        grid.SetTerrain(2, 2, TerrainType.Hill);
        Assert.That(grid.GetTile(2, 2).Terrain, Is.EqualTo(TerrainType.Hill));
    }

    [Test]
    public void AllTiles_IncludesTerrainForEachTile()
    {
        var grid = new CityGrid(10, 10);
        grid.SetTerrain(0, 0, TerrainType.Water);
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
}
