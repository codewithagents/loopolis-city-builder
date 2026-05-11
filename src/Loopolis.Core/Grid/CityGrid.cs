namespace Loopolis.Core.Grid;

public enum ZoneType
{
    Empty,
    Residential,
    Commercial,
    Industrial,
    Road,
    PowerPlant,     // kept for backward compatibility — alias for CoalPlant
    PowerLine,
    FireStation,    // coverage radius 4, happiness +0.15
    PoliceStation,  // coverage radius 4, happiness +0.15
    School,         // coverage radius 5, happiness +0.20
    Avenue,         // wider road: cost $20, maintenance $2/tick, overload threshold 16 (vs Road: $10/$1/8)
    PoliceHQ,       // coverage radius 10, happiness +0.15 — unlocks at City milestone (pop ≥ 5,000)
    FireHQ,         // coverage radius 10, happiness +0.15 — unlocks at City milestone (pop ≥ 5,000)
    Hospital,       // coverage radius 8, happiness +0.15 — halves EventPenalty for covered tiles — unlocks at City milestone
    CoalPlant,      // 500 MW output, $500 placement, $8/tick maintenance, emits pollution radius 3 strength 0.4
    NuclearPlant,   // 3,000 MW output, $8,000 placement, $50/tick maintenance, zero pollution — unlocks at Town milestone (pop ≥ 500)
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
    public TerrainType Terrain { get; init; } = TerrainType.Flat;
    public string? BuildingId { get; init; } = null;
    /// <summary>Count of R/C/I zone tiles within Chebyshev distance 2. Set each tick by RoadTrafficSystem.</summary>
    public int TrafficLoad { get; init; } = 0;

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
    private readonly TerrainType[,] _terrain;

    public int Width { get; }
    public int Height { get; }

    public Dictionary<string, Buildings.Building> Buildings { get; } = new();

    public CityGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _tiles = new Tile[width, height];
        _terrain = new TerrainType[width, height];

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            _terrain[x, y] = TerrainType.Flat;
            _tiles[x, y] = new Tile(x, y);
        }
    }

    public Tile GetTile(int x, int y)
    {
        AssertInBounds(x, y);
        return _tiles[x, y] with { Terrain = _terrain[x, y] };
    }

    public TerrainType GetTerrain(int x, int y)
    {
        if (!IsInBounds(x, y)) return TerrainType.Flat;
        return _terrain[x, y];
    }

    public void SetTerrain(int x, int y, TerrainType terrain)
    {
        if (!IsInBounds(x, y)) return;
        _terrain[x, y] = terrain;
    }

    public void SetZone(int x, int y, ZoneType zone)
    {
        AssertInBounds(x, y);
        if (GetTerrain(x, y) == TerrainType.Water)
            return; // cannot build on water
        if (zone != ZoneType.Empty && _tiles[x, y].Zone != ZoneType.Empty)
            return; // cannot overwrite occupied tile — use Erase (Empty) first
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
            yield return _tiles[x, y] with { Terrain = _terrain[x, y] };
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
                yield return _tiles[nx, ny] with { Terrain = _terrain[nx, ny] };
        }
    }

    public void SetBuildingId(int x, int y, string? id)
    {
        AssertInBounds(x, y);
        _tiles[x, y] = _tiles[x, y] with { BuildingId = id };
    }

    public void ClearBuildings()
    {
        Buildings.Clear();
        for (var x = 0; x < Width; x++)
            for (var y = 0; y < Height; y++)
                if (_tiles[x, y].BuildingId != null)
                    _tiles[x, y] = _tiles[x, y] with { BuildingId = null };
    }

    public void SetTrafficLoad(int x, int y, int load)
    {
        AssertInBounds(x, y);
        _tiles[x, y] = _tiles[x, y] with { TrafficLoad = Math.Max(0, load) };
    }

    public void ClearTrafficLoad()
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            if (_tiles[x, y].TrafficLoad != 0)
                _tiles[x, y] = _tiles[x, y] with { TrafficLoad = 0 };
    }

    private void AssertInBounds(int x, int y)
    {
        if (!IsInBounds(x, y))
            throw new ArgumentOutOfRangeException($"Tile ({x},{y}) is out of bounds ({Width}x{Height})");
    }
}
