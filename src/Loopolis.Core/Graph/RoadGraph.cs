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

    // 4-directional offsets
    private static readonly (int dx, int dy)[] Directions = { (0, -1), (0, 1), (-1, 0), (1, 0) };

    // ── Diagnostics ────────────────────────────────────────────────────────────

    /// <summary>Total number of nodes in the graph.</summary>
    public int NodeCount => _nodes.Count;

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
}
