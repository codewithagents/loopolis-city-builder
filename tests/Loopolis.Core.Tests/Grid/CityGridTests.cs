using Loopolis.Core.Grid;

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
}
