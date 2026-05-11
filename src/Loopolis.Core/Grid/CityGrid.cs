namespace Loopolis.Core.Grid;

public enum ZoneType
{
    Empty,
    Residential,
    Commercial,
    Industrial,
    Road,
    PowerPlant,
    PowerLine,
    FireStation,    // coverage radius 4, happiness +0.15
    PoliceStation,  // coverage radius 4, happiness +0.15
    School,         // coverage radius 5, happiness +0.20
}

public record Tile(int X, int Y)
{
    public ZoneType Zone { get; init; } = ZoneType.Empty;
    public bool HasPower { get; init; } = false;
    public bool HasWater { get; init; } = false;
    public bool HasRoadAccess { get; init; } = false;
    public double DemandFactor { get; init; } = 1.0;
    public double PollutionLevel { get; init; } = 0.0;
    public double Happiness { get; init; } = 1.0;
    public int Population { get; init; } = 0;

    /// <summary>True when a commercial zone is adjacent and grants a demand boost to this residential tile.</summary>
    public bool HasDemandBoost => Zone == ZoneType.Residential && DemandFactor > 1.0;

    /// <summary>
    /// A zone is ready to develop when it has both power and road access.
    /// Infrastructure tiles (roads, power lines, power plants, service buildings) are always considered ready.
    /// </summary>
    public bool IsReadyToDevelop => Zone switch
    {
        ZoneType.Residential or ZoneType.Commercial or ZoneType.Industrial => HasPower && HasRoadAccess,
        ZoneType.Empty => false,
        _ => true  // infrastructure tiles (roads, power, services) don't need access to themselves
    };
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

    public void SetPower(int x, int y, bool hasPower)
    {
        AssertInBounds(x, y);
        _tiles[x, y] = _tiles[x, y] with { HasPower = hasPower };
    }

    public void ClearPower()
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            _tiles[x, y] = _tiles[x, y] with { HasPower = false };
    }

    public void SetRoadAccess(int x, int y, bool hasAccess)
    {
        AssertInBounds(x, y);
        _tiles[x, y] = _tiles[x, y] with { HasRoadAccess = hasAccess };
    }

    public void ClearRoadAccess()
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            _tiles[x, y] = _tiles[x, y] with { HasRoadAccess = false };
    }

    public void SetDemand(int x, int y, double factor)
    {
        AssertInBounds(x, y);
        _tiles[x, y] = _tiles[x, y] with { DemandFactor = factor };
    }

    public void ClearDemand()
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            _tiles[x, y] = _tiles[x, y] with { DemandFactor = 1.0 };
    }

    public void SetPollution(int x, int y, double level)
    {
        AssertInBounds(x, y);
        _tiles[x, y] = _tiles[x, y] with { PollutionLevel = Math.Clamp(level, 0.0, 1.0) };
    }

    public void AddPollution(int x, int y, double amount)
    {
        AssertInBounds(x, y);
        var newLevel = Math.Clamp(_tiles[x, y].PollutionLevel + amount, 0.0, 1.0);
        _tiles[x, y] = _tiles[x, y] with { PollutionLevel = newLevel };
    }

    public void ClearPollution()
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            _tiles[x, y] = _tiles[x, y] with { PollutionLevel = 0.0 };
    }

    public void SetHappiness(int x, int y, double happiness)
    {
        AssertInBounds(x, y);
        _tiles[x, y] = _tiles[x, y] with { Happiness = happiness };
    }

    public void ClearHappiness()
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            _tiles[x, y] = _tiles[x, y] with { Happiness = 1.0 };
    }

    public void SetPopulation(int x, int y, int pop)
    {
        AssertInBounds(x, y);
        _tiles[x, y] = _tiles[x, y] with { Population = Math.Max(0, pop) };
    }

    public int GetPopulation(int x, int y)
    {
        AssertInBounds(x, y);
        return _tiles[x, y].Population;
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
