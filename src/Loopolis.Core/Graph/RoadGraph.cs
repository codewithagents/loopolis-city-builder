using Loopolis.Core.Grid;

namespace Loopolis.Core.Graph;

/// <summary>
/// Weighted undirected graph of road tiles. Nodes are (x,y) road/avenue tile positions.
/// Edge weight between two adjacent nodes = average of their node weights.
/// Road node weight = 1.0; Avenue node weight = 0.5 (avenues are faster routes).
///
/// Used by future systems to compute road-network distances rather than Manhattan distance.
/// Thread safety: not required — single-threaded tick model.
/// </summary>
public class RoadGraph
{
    // node weight keyed by (x,y)
    private readonly Dictionary<(int x, int y), float> _nodes = new();

    // adjacency: each node maps to its neighbours + edge weight
    private readonly Dictionary<(int x, int y), Dictionary<(int x, int y), float>> _edges = new();

    // external anchor nodes (border connections)
    private readonly HashSet<(int x, int y)> _externalAnchors = new();

    // 4-directional offsets
    private static readonly (int dx, int dy)[] Directions = { (0, -1), (0, 1), (-1, 0), (1, 0) };

    // ── Diagnostics ────────────────────────────────────────────────────────────

    /// <summary>Total number of nodes in the graph.</summary>
    public int NodeCount => _nodes.Count;

    /// <summary>Returns true if (x,y) is a registered road/avenue node in this graph.</summary>
    public bool IsRoadNode(int x, int y) => _nodes.ContainsKey((x, y));

    /// <summary>
    /// Total number of undirected edges in the graph.
    /// Each edge is stored in both adjacency dictionaries, so we divide by 2.
    /// </summary>
    public int EdgeCount
    {
        get
        {
            var total = 0;
            foreach (var adj in _edges.Values)
                total += adj.Count;
            return total / 2;
        }
    }

    // ── External anchors (border connections) ─────────────────────────────────

    /// <summary>
    /// Mark a node as an external anchor (border connection).
    /// External anchors behave like ordinary road nodes for pathfinding but are tagged
    /// so future systems can treat them as job/migration sources.
    /// The node must already exist (call AddNode first) or be added afterward.
    /// </summary>
    public void SetExternalAnchor(int x, int y)
    {
        _externalAnchors.Add((x, y));
    }

    /// <summary>Returns true if (x,y) is an external anchor node.</summary>
    public bool IsExternalAnchor(int x, int y) => _externalAnchors.Contains((x, y));

    /// <summary>All positions tagged as external anchors.</summary>
    public IReadOnlyCollection<(int x, int y)> ExternalAnchors => _externalAnchors;

    // ── Mutation ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Add (or update) a road node at (x, y) with the given weight.
    /// Automatically wires edges to all 4-directional neighbours that are already nodes.
    /// Edge weight = average of the two endpoint node weights.
    /// </summary>
    public void AddNode(int x, int y, float weight = 1.0f)
    {
        _nodes[(x, y)] = weight;
        _edges.TryAdd((x, y), new Dictionary<(int x, int y), float>());

        foreach (var (dx, dy) in Directions)
        {
            var neighbour = (x + dx, y + dy);
            if (!_nodes.TryGetValue(neighbour, out var neighbourWeight)) continue;

            var edgeWeight = (weight + neighbourWeight) / 2f;
            _edges[(x, y)][neighbour]   = edgeWeight;
            _edges[neighbour][(x, y)]   = edgeWeight;
        }
    }

    /// <summary>
    /// Remove the node at (x, y) and all edges connected to it.
    /// No-op if the node does not exist.
    /// </summary>
    public void RemoveNode(int x, int y)
    {
        var key = (x, y);
        if (!_nodes.ContainsKey(key)) return;

        // Remove reverse edges from all neighbours
        if (_edges.TryGetValue(key, out var neighbours))
        {
            foreach (var neighbour in neighbours.Keys)
            {
                if (_edges.TryGetValue(neighbour, out var reverseAdj))
                    reverseAdj.Remove(key);
            }
        }

        _nodes.Remove(key);
        _edges.Remove(key);
        _externalAnchors.Remove(key);
    }

    // ── Queries ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dijkstra shortest path from (x1,y1) to (x2,y2).
    /// Returns the total road-network distance, or <c>float.MaxValue</c> if unreachable.
    /// </summary>
    public float GetDistance(int x1, int y1, int x2, int y2)
    {
        var source = (x1, y1);
        var target = (x2, y2);

        if (!_nodes.ContainsKey(source) || !_nodes.ContainsKey(target))
            return float.MaxValue;

        if (source == target) return 0f;

        var dist = ShortestPathSourceMap(x1, y1);
        return dist.TryGetValue(target, out var d) ? d : float.MaxValue;
    }

    /// <summary>
    /// Returns true when there is at least one road-network path between the two tiles.
    /// </summary>
    public bool IsReachable(int x1, int y1, int x2, int y2)
    {
        var source = (x1, y1);
        var target = (x2, y2);

        if (!_nodes.ContainsKey(source) || !_nodes.ContainsKey(target))
            return false;

        if (source == target) return true;

        // BFS is enough to check reachability (no need for full Dijkstra)
        var visited = new HashSet<(int, int)> { source };
        var queue   = new Queue<(int, int)>();
        queue.Enqueue(source);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target) return true;

            if (!_edges.TryGetValue(current, out var adj)) continue;
            foreach (var next in adj.Keys)
            {
                if (visited.Add(next))
                    queue.Enqueue(next);
            }
        }
        return false;
    }

    /// <summary>
    /// Returns all (x,y) tiles reachable from (x,y) in the same connected component,
    /// including (x,y) itself.
    /// Returns an empty collection if the source tile is not a node in the graph.
    /// </summary>
    public IReadOnlyCollection<(int x, int y)> GetConnectedComponent(int x, int y)
    {
        var source = (x, y);
        if (!_nodes.ContainsKey(source))
            return Array.Empty<(int, int)>();

        var visited = new HashSet<(int, int)>();
        var queue   = new Queue<(int, int)>();
        queue.Enqueue(source);
        visited.Add(source);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!_edges.TryGetValue(current, out var adj)) continue;
            foreach (var next in adj.Keys)
            {
                if (visited.Add(next))
                    queue.Enqueue(next);
            }
        }
        return visited;
    }

    /// <summary>
    /// Returns the road-graph distance between two non-road tiles via their nearest road neighbours.
    ///
    /// A non-road tile's "nearest road tile" is any of its 4 cardinal neighbours that is a node
    /// in this graph (i.e. a Road or Avenue tile). The first such neighbour found is used.
    ///
    /// Returns <c>float.MaxValue</c> if:
    ///   - either tile has no adjacent road node, OR
    ///   - no path exists between the two road neighbours.
    ///
    /// This is the primary entry-point for service coverage and commute penalty calculations
    /// once G2 road-based coverage is active.
    /// </summary>
    public float GetDistanceViaRoads(CityGrid grid, int x1, int y1, int x2, int y2)
    {
        var road1 = FindNearestRoadNeighbour(grid, x1, y1);
        if (road1 == null) return float.MaxValue;

        var road2 = FindNearestRoadNeighbour(grid, x2, y2);
        if (road2 == null) return float.MaxValue;

        return GetDistance(road1.Value.x, road1.Value.y, road2.Value.x, road2.Value.y);
    }

    /// <summary>
    /// Returns the first cardinal neighbour of (x, y) that is a node in this graph,
    /// or null if none exists. Checks North, South, West, East in that order.
    /// </summary>
    private (int x, int y)? FindNearestRoadNeighbour(CityGrid grid, int x, int y)
    {
        // If the tile itself is a road node, use it directly
        if (_nodes.ContainsKey((x, y))) return (x, y);

        foreach (var (dx, dy) in Directions)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (!grid.IsInBounds(nx, ny)) continue;
            if (_nodes.ContainsKey((nx, ny))) return (nx, ny);
        }
        return null;
    }

    /// <summary>
    /// Run Dijkstra from (sourceX, sourceY) and return a dictionary mapping each reachable
    /// (x,y) node to its shortest road-network distance from the source.
    /// The source itself is included with distance 0.
    /// Returns an empty dictionary if the source is not in the graph.
    /// </summary>
    public Dictionary<(int x, int y), float> ShortestPathSourceMap(int sourceX, int sourceY)
    {
        var source = (sourceX, sourceY);
        var result = new Dictionary<(int x, int y), float>();

        if (!_nodes.ContainsKey(source)) return result;

        result[source] = 0f;

        // PriorityQueue<node, priority> — min-heap on distance
        var pq = new PriorityQueue<(int x, int y), float>();
        pq.Enqueue(source, 0f);

        while (pq.Count > 0)
        {
            pq.TryDequeue(out var current, out var currentDist);

            // Skip stale entries (we may have enqueued the same node with a worse distance)
            if (result.TryGetValue(current, out var bestDist) && currentDist > bestDist)
                continue;

            if (!_edges.TryGetValue(current, out var adj)) continue;

            foreach (var (next, edgeWeight) in adj)
            {
                var newDist = currentDist + edgeWeight;
                if (!result.TryGetValue(next, out var existingDist) || newDist < existingDist)
                {
                    result[next] = newDist;
                    pq.Enqueue(next, newDist);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Run Dijkstra from (sourceX, sourceY) and return both a distance map and a parent map.
    /// The parent map records, for each node, which node it was reached from (i.e. the previous
    /// node on the shortest path from the source). The source maps to itself.
    /// Returns empty dictionaries if the source is not in the graph.
    /// </summary>
    public (Dictionary<(int x, int y), float> Distances, Dictionary<(int x, int y), (int x, int y)> Parents)
        ShortestPathWithParents(int sourceX, int sourceY)
    {
        var source  = (sourceX, sourceY);
        var dist    = new Dictionary<(int x, int y), float>();
        var parents = new Dictionary<(int x, int y), (int x, int y)>();

        if (!_nodes.ContainsKey(source))
            return (dist, parents);

        dist[source]    = 0f;
        parents[source] = source;

        var pq = new PriorityQueue<(int x, int y), float>();
        pq.Enqueue(source, 0f);

        while (pq.Count > 0)
        {
            pq.TryDequeue(out var current, out var currentDist);

            if (dist.TryGetValue(current, out var bestDist) && currentDist > bestDist)
                continue;

            if (!_edges.TryGetValue(current, out var adj)) continue;

            foreach (var (next, edgeWeight) in adj)
            {
                var newDist = currentDist + edgeWeight;
                if (!dist.TryGetValue(next, out var existingDist) || newDist < existingDist)
                {
                    dist[next]    = newDist;
                    parents[next] = current;
                    pq.Enqueue(next, newDist);
                }
            }
        }

        return (dist, parents);
    }

    /// <summary>
    /// Reconstruct the path from source to target using a parent map produced by
    /// <see cref="ShortestPathWithParents"/>. Returns the sequence of nodes from
    /// source (inclusive) to target (inclusive), or an empty list if target is not
    /// in the parent map (unreachable).
    /// </summary>
    public static List<(int x, int y)> ReconstructPath(
        Dictionary<(int x, int y), (int x, int y)> parents,
        (int x, int y) source,
        (int x, int y) target)
    {
        var path = new List<(int x, int y)>();

        if (!parents.ContainsKey(target))
            return path;

        var current = target;
        while (current != source)
        {
            path.Add(current);
            current = parents[current];
        }
        path.Add(source);
        path.Reverse();
        return path;
    }

    // ── Edge traffic tracking ──────────────────────────────────────────────────

    // Canonical edge key: pack smaller node first so (a,b) == (b,a).
    // Each node is packed as a 32-bit pair (x*65536+y) and the two halves are stored
    // as (long lo, long hi) where lo ≤ hi.
    private readonly Dictionary<(long lo, long hi), int> _edgeTraffic = new();

    private static (long lo, long hi) EdgeKey(int x1, int y1, int x2, int y2)
    {
        var a = (long)x1 * 65536 + y1;
        var b = (long)x2 * 65536 + y2;
        return a <= b ? (a, b) : (b, a);
    }

    /// <summary>
    /// Increment traffic on the undirected edge between two adjacent road nodes.
    /// Direction-agnostic: IncrementEdgeTraffic(a,b) == IncrementEdgeTraffic(b,a).
    /// </summary>
    public void IncrementEdgeTraffic(int x1, int y1, int x2, int y2, int amount = 1)
    {
        var key = EdgeKey(x1, y1, x2, y2);
        _edgeTraffic.TryGetValue(key, out var current);
        _edgeTraffic[key] = current + amount;
    }

    /// <summary>Get current traffic load on the undirected edge between two nodes.</summary>
    public int GetEdgeTraffic(int x1, int y1, int x2, int y2)
    {
        var key = EdgeKey(x1, y1, x2, y2);
        return _edgeTraffic.TryGetValue(key, out var v) ? v : 0;
    }

    /// <summary>Reset all edge traffic to 0. Call at the start of each tick.</summary>
    public void ResetEdgeTraffic() => _edgeTraffic.Clear();

    /// <summary>
    /// For a given road node, return the total traffic load — sum of traffic across all its edges,
    /// divided by 2 to avoid double-counting (each worker traverses both an entry and an exit edge).
    /// This is written to tile.TrafficLoad and shown in the HUD.
    /// Returns 0 for nodes not in the graph.
    /// </summary>
    public int GetNodeTraffic(int x, int y)
    {
        if (!_edges.TryGetValue((x, y), out var adj)) return 0;

        var total = 0;
        foreach (var (neighbour, _) in adj)
            total += GetEdgeTraffic(x, y, neighbour.x, neighbour.y);

        // Divide by 2: each worker contributes to both the edge entering this node
        // and the edge leaving, so summing all edges double-counts each worker.
        return total / 2;
    }
}
