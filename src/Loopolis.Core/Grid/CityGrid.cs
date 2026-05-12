namespace Loopolis.Core.Grid;

/// <summary>
/// Terrain type derived from HeightLevel and HasForest — do not persist.
/// This enum is computed, not stored. Use HeightLevel and HasForest for persistence and logic.
/// </summary>
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

    /// <summary>
    /// Per-tile elevation (0–10). 0 = Water (unbuildable), 1 = Flat (default), 2+ = Elevated.
    /// This is the source-of-truth for terrain height. TerrainType is derived from this.
    /// </summary>
    public int HeightLevel { get; init; } = 1;

    /// <summary>Whether this tile has forest vegetation. Independent of elevation.</summary>
    public bool HasForest { get; init; } = false;

    /// <summary>
    /// Derived terrain classification. Use HeightLevel and HasForest for logic and persistence.
    /// Kept for backward compatibility with existing callers.
    /// </summary>
    public TerrainType Terrain => HeightLevel <= 0 ? TerrainType.Water
        : HasForest                                 ? TerrainType.Forest
        : HeightLevel >= 2                          ? TerrainType.Hill
        : TerrainType.Flat;

    public string? BuildingId { get; init; } = null;
    /// <summary>Count of R/C/I zone tiles within Chebyshev distance 2. Set each tick by RoadTrafficSystem.</summary>
    public int TrafficLoad { get; init; } = 0;

    /// <summary>Land value score (0.0–1.0). Computed each tick by LandValueSystem. Water tiles have 0.</summary>
    public double LandValue { get; init; } = 0.0;

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
    private readonly int[,]  _heightLevel;
    private readonly bool[,] _hasForest;

    public int Width { get; }
    public int Height { get; }

    public Dictionary<string, Buildings.Building> Buildings { get; } = new();

    /// <summary>
    /// Cached average height across all tiles. Computed once by ComputeAverageHeight() after terrain generation.
    /// Defaults to 1.0 (flat) until explicitly computed.
    /// </summary>
    public float AverageHeight { get; private set; } = 1.0f;

    public CityGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _tiles       = new Tile[width, height];
        _heightLevel = new int[width, height];
        _hasForest   = new bool[width, height];

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            _heightLevel[x, y] = 1;           // default: flat
            _tiles[x, y] = new Tile(x, y);
        }
    }

    // ── Height / forest API ───────────────────────────────────────────────────

    /// <summary>Returns the height level for a tile (0–10). Returns 1 for out-of-bounds.</summary>
    public int GetHeightLevel(int x, int y)
    {
        if (!IsInBounds(x, y)) return 1;
        return _heightLevel[x, y];
    }

    /// <summary>Sets the height level for a tile (0–10). Silently ignored if out of bounds.</summary>
    public void SetHeightLevel(int x, int y, int level)
    {
        if (!IsInBounds(x, y)) return;
        _heightLevel[x, y] = Math.Clamp(level, 0, 10);
        // Sync the cached tile record
        _tiles[x, y] = _tiles[x, y] with { HeightLevel = _heightLevel[x, y] };
    }

    /// <summary>Returns whether the tile has forest vegetation. Returns false for out-of-bounds.</summary>
    public bool HasForestAt(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        return _hasForest[x, y];
    }

    /// <summary>Sets the forest flag for a tile. Silently ignored if out of bounds.</summary>
    public void SetForest(int x, int y, bool hasForest)
    {
        if (!IsInBounds(x, y)) return;
        _hasForest[x, y] = hasForest;
        _tiles[x, y] = _tiles[x, y] with { HasForest = hasForest };
    }

    /// <summary>
    /// Apply a generated height map array to the grid.
    /// Array must be at least [Width, Height].
    /// </summary>
    public void ApplyHeightMap(int[,] heightMap)
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            SetHeightLevel(x, y, heightMap[x, y]);
        ComputeAverageHeight();
    }

    /// <summary>
    /// Apply a generated forest map array to the grid.
    /// Array must be at least [Width, Height].
    /// </summary>
    public void ApplyForestMap(bool[,] forestMap)
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            SetForest(x, y, forestMap[x, y]);
    }

    /// <summary>
    /// Set all tiles to height=1 (flat) and clear all forests.
    /// Call this on scenarios that should have predictable flat terrain.
    /// </summary>
    public void SetFlatTerrain()
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
        {
            SetHeightLevel(x, y, 1);
            SetForest(x, y, false);
        }
        AverageHeight = 1.0f;
    }

    /// <summary>Compute and cache AverageHeight from current heightLevel array.</summary>
    public void ComputeAverageHeight()
    {
        long sum = 0;
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            sum += _heightLevel[x, y];
        AverageHeight = (float)sum / (Width * Height);
    }

    // ── Terrain classification helpers ────────────────────────────────────────

    /// <summary>
    /// Returns true if the tile is elevated relative to the map average.
    /// </summary>
    public bool IsElevated(int x, int y) =>
        IsInBounds(x, y) && _heightLevel[x, y] > AverageHeight;

    /// <summary>
    /// Returns true if any orthogonal neighbour has |height difference| > 1 — a cliff edge.
    /// </summary>
    public bool IsCliffEdge(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { -1, 1, 0, 0 };
        var h = _heightLevel[x, y];
        for (var i = 0; i < 4; i++)
        {
            var nx = x + dx[i]; var ny = y + dy[i];
            if (!IsInBounds(nx, ny)) continue;
            if (Math.Abs(h - _heightLevel[nx, ny]) > 1) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the tile is elevated (height ≥ 2) AND all 4 orthogonal neighbours
    /// differ by at most 1 in height — a plateau.
    /// </summary>
    public bool IsPlateau(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        if (_heightLevel[x, y] < 2) return false;
        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { -1, 1, 0, 0 };
        var h = _heightLevel[x, y];
        for (var i = 0; i < 4; i++)
        {
            var nx = x + dx[i]; var ny = y + dy[i];
            if (!IsInBounds(nx, ny)) continue;
            if (Math.Abs(h - _heightLevel[nx, ny]) > 1) return false;
        }
        return true;
    }

    /// <summary>
    /// Check if a Road or Avenue can be placed at (x, y).
    /// Returns (true, null) if allowed; (false, reason) if blocked by a cliff edge.
    /// Other zones are not constrained by height differences.
    /// </summary>
    public (bool Ok, string? Reason) CanPlaceRoad(int x, int y)
    {
        if (!IsInBounds(x, y)) return (false, $"Out of bounds: ({x},{y})");
        if (_heightLevel[x, y] <= 0) return (false, "Cannot place road on water");

        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { -1, 1, 0, 0 };
        var h = _heightLevel[x, y];
        for (var i = 0; i < 4; i++)
        {
            var nx = x + dx[i]; var ny = y + dy[i];
            if (!IsInBounds(nx, ny)) continue;
            if (Math.Abs(h - _heightLevel[nx, ny]) > 1)
                return (false, "Cannot build road here — cliff (height difference too large)");
        }
        return (true, null);
    }

    // ── Terrain backward-compat shims ─────────────────────────────────────────

    /// <summary>
    /// Returns the derived terrain type. Deprecated — use GetHeightLevel and HasForestAt directly.
    /// </summary>
    public TerrainType GetTerrain(int x, int y)
    {
        if (!IsInBounds(x, y)) return TerrainType.Flat;
        var h = _heightLevel[x, y];
        if (h <= 0) return TerrainType.Water;
        if (_hasForest[x, y]) return TerrainType.Forest;
        if (h >= 2) return TerrainType.Hill;
        return TerrainType.Flat;
    }

    /// <summary>
    /// Sets terrain via the legacy TerrainType enum.
    /// Hill → HeightLevel=3, Forest → HasForest=true (HeightLevel unchanged ≥1),
    /// Water → HeightLevel=0, Flat → HeightLevel=1, HasForest=false.
    /// Deprecated — use SetHeightLevel and SetForest directly.
    /// </summary>
    public void SetTerrain(int x, int y, TerrainType terrain)
    {
        if (!IsInBounds(x, y)) return;
        switch (terrain)
        {
            case TerrainType.Water:
                SetHeightLevel(x, y, 0);
                SetForest(x, y, false);
                break;
            case TerrainType.Hill:
                SetHeightLevel(x, y, 3);
                SetForest(x, y, false);
                break;
            case TerrainType.Forest:
                if (_heightLevel[x, y] <= 0) SetHeightLevel(x, y, 1);
                SetForest(x, y, true);
                break;
            case TerrainType.Flat:
            default:
                SetHeightLevel(x, y, 1);
                SetForest(x, y, false);
                break;
        }
    }

    // ── Zone placement ────────────────────────────────────────────────────────

    public Tile GetTile(int x, int y)
    {
        AssertInBounds(x, y);
        return _tiles[x, y];
    }

    public void SetZone(int x, int y, ZoneType zone)
    {
        AssertInBounds(x, y);
        if (_heightLevel[x, y] <= 0)
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

    public void SetLandValue(int x, int y, double value)
    {
        AssertInBounds(x, y);
        _tiles[x, y] = _tiles[x, y] with { LandValue = Math.Clamp(value, 0.0, 1.0) };
    }

    public void ClearLandValue()
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            if (_tiles[x, y].LandValue != 0.0)
                _tiles[x, y] = _tiles[x, y] with { LandValue = 0.0 };
    }

    private void AssertInBounds(int x, int y)
    {
        if (!IsInBounds(x, y))
            throw new ArgumentOutOfRangeException($"Tile ({x},{y}) is out of bounds ({Width}x{Height})");
    }
}
