using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Calculates traffic density pressure on road and avenue tiles.
///
/// For each road/avenue tile, counts the number of R/C/I zone tiles within
/// Chebyshev distance 2 (a 5×5 square centred on the tile, excluding the tile itself).
/// This count is the tile's TrafficLoad, written back to the grid each tick.
///
/// Overload thresholds:
///   Road:   overloaded if TrafficLoad > 8
///   Avenue: overloaded if TrafficLoad > 16
///
/// When a road tile is overloaded, adjacent R/C/I zones receive:
///   - Growth multiplier: ×0.7
///   - Happiness modifier: −0.10
///
/// Avenue tiles have double the capacity, rewarding the player's higher upfront
/// cost ($50 placement, $2/tick maintenance vs Road's $25/$1).
/// </summary>
public class RoadTrafficSystem
{
    private const int RoadOverloadThreshold   = 8;
    private const int AvenueOverloadThreshold = 16;
    private const double OverloadGrowthMultiplier  = 0.7;
    private const double OverloadHappinessPenalty  = -0.10;

    private static readonly HashSet<ZoneType> ZoneTileTypes = new()
    {
        ZoneType.Residential,
        ZoneType.Commercial,
        ZoneType.Industrial,
    };

    private static readonly HashSet<ZoneType> RoadTileTypes = new()
    {
        ZoneType.Road,
        ZoneType.Avenue,
    };

    /// <summary>Number of road/avenue tiles currently overloaded.</summary>
    public int OverloadedRoadCount { get; private set; }

    /// <summary>Average traffic load across all road and avenue tiles (0.0 if no roads).</summary>
    public double AvgTrafficLoad { get; private set; }

    // Per-tile traffic load cache (keyed by (x,y)) — populated during Propagate.
    // Used by IsOverloaded() and GetGrowthMultiplier() / GetHappinessModifier().
    private readonly Dictionary<(int x, int y), int> _loadCache = new();

    /// <summary>
    /// Recalculates traffic load for every road/avenue tile and writes results to the grid.
    /// Call once per simulation tick, after RoadNetwork.Propagate.
    /// </summary>
    public void Propagate(CityGrid grid)
    {
        grid.ClearTrafficLoad();
        _loadCache.Clear();
        _overloaded.Clear();
        OverloadedRoadCount = 0;

        var roadTiles = new List<(int x, int y, ZoneType zone)>();
        foreach (var tile in grid.AllTiles())
        {
            if (RoadTileTypes.Contains(tile.Zone))
                roadTiles.Add((tile.X, tile.Y, tile.Zone));
        }

        int totalLoad = 0;

        foreach (var (rx, ry, zone) in roadTiles)
        {
            var load = CountZonesInChebyshev2(grid, rx, ry);
            grid.SetTrafficLoad(rx, ry, load);
            _loadCache[(rx, ry)] = load;
            totalLoad += load;

            var threshold = zone == ZoneType.Avenue ? AvenueOverloadThreshold : RoadOverloadThreshold;
            if (load > threshold)
            {
                OverloadedRoadCount++;
                _overloaded.Add((rx, ry));
            }
        }

        AvgTrafficLoad = roadTiles.Count > 0 ? (double)totalLoad / roadTiles.Count : 0.0;
    }

    /// <summary>Returns the cached traffic load for a road/avenue tile. Returns 0 for non-road tiles.</summary>
    public int GetTrafficLoad(int x, int y) =>
        _loadCache.TryGetValue((x, y), out var v) ? v : 0;

    /// <summary>Returns true if the road/avenue tile at (x,y) is currently overloaded.</summary>
    public bool IsOverloaded(int x, int y) => _overloaded.Contains((x, y));

    /// <summary>
    /// Returns the growth multiplier for a zone tile, factoring in overloaded adjacent roads.
    /// 0.7 if any adjacent road/avenue is overloaded; 1.0 otherwise.
    /// Only meaningful for R/C/I tiles.
    /// </summary>
    public double GetGrowthMultiplier(CityGrid grid, int x, int y)
    {
        foreach (var neighbour in grid.AdjacentTiles(x, y))
        {
            if (RoadTileTypes.Contains(neighbour.Zone) && _overloaded.Contains((neighbour.X, neighbour.Y)))
                return OverloadGrowthMultiplier;
        }
        return 1.0;
    }

    /// <summary>
    /// Returns the happiness modifier for a residential tile due to adjacent overloaded roads.
    /// −0.10 if any adjacent road/avenue is overloaded; 0.0 otherwise.
    /// </summary>
    public double GetHappinessModifier(CityGrid grid, int x, int y)
    {
        foreach (var neighbour in grid.AdjacentTiles(x, y))
        {
            if (RoadTileTypes.Contains(neighbour.Zone) && _overloaded.Contains((neighbour.X, neighbour.Y)))
                return OverloadHappinessPenalty;
        }
        return 0.0;
    }

    // Separate set tracking overloaded road coordinates — avoids having to re-lookup zone type.
    private readonly HashSet<(int x, int y)> _overloaded = new();

    private static int CountZonesInChebyshev2(CityGrid grid, int cx, int cy)
    {
        var count = 0;
        for (var dx = -2; dx <= 2; dx++)
        for (var dy = -2; dy <= 2; dy++)
        {
            if (dx == 0 && dy == 0) continue; // exclude the road tile itself
            var nx = cx + dx;
            var ny = cy + dy;
            if (!grid.IsInBounds(nx, ny)) continue;
            var zone = grid.GetTile(nx, ny).Zone;
            if (ZoneTileTypes.Contains(zone))
                count++;
        }
        return count;
    }
}
