namespace Loopolis.Core.Grid;

public enum ZoneType
{
    Empty,
    Residential,
    Commercial,
    Industrial,
    Road,
    PowerPlant,
    PowerLine
}

public record Tile(int X, int Y)
{
    public ZoneType Zone { get; init; } = ZoneType.Empty;
    public bool HasPower { get; init; } = false;
    public bool HasWater { get; init; } = false;
}

public class CityGrid
{
    private readonly Tile[,] _tiles;

    public int Width { get; }
    public int Height { get; }

    public CityGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _tiles = new Tile[width, height];

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
            _tiles[x, y] = new Tile(x, y);
    }

    public Tile GetTile(int x, int y)
    {
        AssertInBounds(x, y);
        return _tiles[x, y];
    }

    public void SetZone(int x, int y, ZoneType zone)
    {
        AssertInBounds(x, y);
        _tiles[x, y] = _tiles[x, y] with { Zone = zone };
    }

    public bool IsInBounds(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height;

    public IEnumerable<Tile> AllTiles()
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            yield return _tiles[x, y];
    }

    public IEnumerable<Tile> TilesOfType(ZoneType zone) =>
        AllTiles().Where(t => t.Zone == zone);

    public IEnumerable<Tile> AdjacentTiles(int x, int y)
    {
        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { -1, 1, 0, 0 };

        for (var i = 0; i < 4; i++)
        {
            var nx = x + dx[i];
            var ny = y + dy[i];
            if (IsInBounds(nx, ny))
                yield return _tiles[nx, ny];
        }
    }

    private void AssertInBounds(int x, int y)
    {
        if (!IsInBounds(x, y))
            throw new ArgumentOutOfRangeException($"Tile ({x},{y}) is out of bounds ({Width}x{Height})");
    }
}
