using Loopolis.Core.Graph;
using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Routes workers from residential tiles to industrial tiles via the road graph.
/// Each road segment traversed accumulates edge traffic on the <see cref="RoadGraph"/>.
///
/// Routing algorithm:
///   1. Collect all industrial tiles that have a building (BuildingId != null) and road access.
///      Cap at 10 industrial source tiles (most-populated first) for performance.
///   2. For each industrial source tile, run Dijkstra (ShortestPathWithParents) from its nearest
///      road neighbour — producing a shortest-path tree from that industrial entry point.
///   3. For each residential tile with population &gt; 0 and road access, find its nearest road
///      neighbour and look up the path back to each industrial entry point.
///   4. Route workers (Population / 4, min 1) along the shortest available path, calling
///      IncrementEdgeTraffic for each consecutive pair of road nodes.
///
/// Results are returned as a <see cref="WorkerFlowResult"/> record and stored by
/// <see cref="SimulationEngine"/> as <c>LastWorkerFlow</c>.
/// </summary>
public class WorkerFlowSystem
{
    private const int MaxIndustrialSources = 10;
    private const int WorkersPerPopulation  = 4;   // workers = population / 4

    /// <summary>
    /// Route workers from residential tiles to industrial tiles.
    /// Accumulates edge traffic on <paramref name="roadGraph"/>.
    /// </summary>
    public WorkerFlowResult Route(CityGrid grid, RoadGraph roadGraph)
    {
        // ── 1. Collect industrial source tiles ────────────────────────────────
        var industrialSources = new List<(int x, int y)>();
        foreach (var tile in grid.AllTiles())
        {
            if (tile.Zone == ZoneType.Industrial && tile.BuildingId != null && tile.HasRoadAccess)
                industrialSources.Add((tile.X, tile.Y));
        }

        if (industrialSources.Count == 0)
        {
            // Count unrouted workers (all of them)
            var allUnrouted = 0;
            foreach (var tile in grid.AllTiles())
            {
                if (tile.Zone == ZoneType.Residential && tile.Population > 0 && tile.HasRoadAccess)
                    allUnrouted += Math.Max(1, tile.Population / WorkersPerPopulation);
            }
            return new WorkerFlowResult(0, 0f, allUnrouted, 0);
        }

        // ── 2. Find road neighbours for each industrial source ─────────────────
        //    Build Dijkstra trees (one per industrial entry road node).
        //    Deduplicate: multiple industrial tiles on the same road node share one tree.

        var industrialEntries = new List<(int rx, int ry)>();   // road-node entries
        foreach (var (ix, iy) in industrialSources)
        {
            var roadNeighbour = FindNearestRoadNode(grid, roadGraph, ix, iy);
            if (roadNeighbour.HasValue && !industrialEntries.Contains(roadNeighbour.Value))
                industrialEntries.Add(roadNeighbour.Value);
        }

        // Cap to MaxIndustrialSources unique entry nodes
        if (industrialEntries.Count > MaxIndustrialSources)
            industrialEntries = industrialEntries.Take(MaxIndustrialSources).ToList();

        // Pre-compute shortest-path trees from each industrial entry node
        var pathTrees = new List<(
            (int rx, int ry) Entry,
            Dictionary<(int x, int y), float> Dist,
            Dictionary<(int x, int y), (int x, int y)> Parents)>();

        foreach (var (rx, ry) in industrialEntries)
        {
            var (dist, parents) = roadGraph.ShortestPathWithParents(rx, ry);
            pathTrees.Add(((rx, ry), dist, parents));
        }

        // ── 3. Route each residential tile ────────────────────────────────────
        var totalWorkersRouted    = 0;
        var totalUnrouted         = 0;
        var totalCommuteDistance  = 0f;
        var overloadedEdges       = 0;

        foreach (var tile in grid.AllTiles())
        {
            if (tile.Zone != ZoneType.Residential || tile.Population <= 0)
                continue;

            var workers = Math.Max(1, tile.Population / WorkersPerPopulation);

            // Without road access the workers can't commute at all
            if (!tile.HasRoadAccess)
            {
                totalUnrouted += workers;
                continue;
            }

            var roadEntry = FindNearestRoadNode(grid, roadGraph, tile.X, tile.Y);

            if (roadEntry == null)
            {
                totalUnrouted += workers;
                continue;
            }

            var (rEx, rEy) = roadEntry.Value;

            // Find the industrial entry that gives the shortest path from this residential entry
            var bestDist    = float.MaxValue;
            List<(int x, int y)>? bestPath = null;

            foreach (var (entry, dist, parents) in pathTrees)
            {
                if (!dist.TryGetValue((rEx, rEy), out var d)) continue;
                if (d >= bestDist) continue;

                // Reconstruct the path from the residential entry to this industrial entry
                var path = RoadGraph.ReconstructPath(parents, entry, (rEx, rEy));
                if (path.Count == 0) continue;

                bestDist = d;
                bestPath = path;
            }

            if (bestPath == null || bestPath.Count == 0)
            {
                totalUnrouted += workers;
                continue;
            }

            // Accumulate edge traffic along the path
            for (var i = 0; i < bestPath.Count - 1; i++)
            {
                var (ax, ay) = bestPath[i];
                var (bx, by) = bestPath[i + 1];
                roadGraph.IncrementEdgeTraffic(ax, ay, bx, by, workers);
            }

            totalWorkersRouted   += workers;
            totalCommuteDistance += bestDist * workers;
        }

        // Count overloaded edges (traffic > capacity)
        // Capacity per edge: Road=80, Avenue=200
        // We scan all edges that have any traffic and check against the lower-capacity endpoint.
        // Approximate: count distinct edges with traffic > 80 (Road threshold — conservative).
        overloadedEdges = CountOverloadedEdges(grid, roadGraph);

        var avgCommute = totalWorkersRouted > 0
            ? totalCommuteDistance / totalWorkersRouted
            : 0f;

        return new WorkerFlowResult(totalWorkersRouted, avgCommute, totalUnrouted, overloadedEdges);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static readonly (int dx, int dy)[] Directions = { (0, -1), (0, 1), (-1, 0), (1, 0) };

    /// <summary>
    /// Returns the first 4-directional road-graph neighbour of (x,y), or (x,y) itself if it's
    /// already a graph node. Returns null if no road neighbour exists.
    /// </summary>
    private static (int x, int y)? FindNearestRoadNode(CityGrid grid, RoadGraph roadGraph, int x, int y)
    {
        // If the tile itself is a road node (e.g. it IS a road tile), use it directly
        if (roadGraph.NodeCount > 0)
        {
            // Check the tile itself first (handles road tiles)
            var zone = grid.GetTile(x, y).Zone;
            if (zone == ZoneType.Road || zone == ZoneType.Avenue)
            {
                // Verify it's actually in the graph
                if (roadGraph.GetDistance(x, y, x, y) == 0f)
                    return (x, y);
            }
        }

        // Check 4-directional cardinal neighbours
        foreach (var (dx, dy) in Directions)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (!grid.IsInBounds(nx, ny)) continue;
            var zone = grid.GetTile(nx, ny).Zone;
            if (zone == ZoneType.Road || zone == ZoneType.Avenue)
            {
                // Verify this node is in the graph
                if (roadGraph.GetDistance(nx, ny, nx, ny) == 0f)
                    return (nx, ny);
            }
        }

        return null;
    }

    /// <summary>
    /// Count edges with traffic above their capacity threshold.
    /// Edge capacity is determined by the lower-capacity of the two endpoint zones.
    ///   Road endpoint   → capacity 80
    ///   Avenue endpoint → capacity 200
    /// Uses a set to avoid counting each undirected edge twice.
    /// </summary>
    private static int CountOverloadedEdges(CityGrid grid, RoadGraph roadGraph)
    {
        var counted = new HashSet<(long lo, long hi)>();
        var overloaded = 0;

        foreach (var tile in grid.AllTiles())
        {
            if (tile.Zone != ZoneType.Road && tile.Zone != ZoneType.Avenue) continue;

            var capacity = tile.Zone == ZoneType.Avenue ? 200 : 80;

            foreach (var (dx, dy) in Directions)
            {
                var nx = tile.X + dx;
                var ny = tile.Y + dy;
                if (!grid.IsInBounds(nx, ny)) continue;

                var neighbourZone = grid.GetTile(nx, ny).Zone;
                if (neighbourZone != ZoneType.Road && neighbourZone != ZoneType.Avenue) continue;

                // Canonical edge key to avoid double-counting
                var a = (long)tile.X * 65536 + tile.Y;
                var b = (long)nx      * 65536 + ny;
                var key = a <= b ? (a, b) : (b, a);

                if (!counted.Add(key)) continue;

                var edgeTraffic = roadGraph.GetEdgeTraffic(tile.X, tile.Y, nx, ny);
                var neighbourCap = neighbourZone == ZoneType.Avenue ? 200 : 80;
                var effectiveCap = Math.Min(capacity, neighbourCap);

                if (edgeTraffic > effectiveCap)
                    overloaded++;
            }
        }

        return overloaded;
    }
}

/// <summary>
/// Summary of one worker-routing pass.
/// </summary>
public record WorkerFlowResult(
    int   WorkersRouted,
    float AverageCommuteDistance,
    int   UnroutedWorkers,
    int   OverloadedEdges
);
