using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Determines road access for every zone tile on the grid.
///
/// A zone (Residential / Commercial / Industrial) has road access when
/// at least one orthogonally adjacent tile is a Road.
///
/// Roads themselves and infrastructure tiles don't receive a road-access flag —
/// only zones that need it to develop.
///
/// Call Propagate() each tick or whenever the grid changes.
/// </summary>
public class RoadNetwork
{
    private static readonly HashSet<ZoneType> ZonesThatNeedRoads = new()
    {
        ZoneType.Residential,
        ZoneType.Commercial,
        ZoneType.Industrial,
    };

    public int AccessibleZoneCount { get; private set; }

    public void Propagate(CityGrid grid)
    {
        grid.ClearRoadAccess();
        AccessibleZoneCount = 0;

        // Pass 1 — find all zone tiles that directly touch a road
        var roadAdjacent = new HashSet<(int x, int y)>();
        foreach (var tile in grid.AllTiles())
        {
            if (!ZonesThatNeedRoads.Contains(tile.Zone))
                continue;

            foreach (var neighbour in grid.AdjacentTiles(tile.X, tile.Y))
            {
                if (neighbour.Zone == ZoneType.Road)
                {
                    roadAdjacent.Add((tile.X, tile.Y));
                    break;
                }
            }
        }

        // Pass 2 — flood-fill same-zone clusters; mark entire cluster accessible if any tile touches a road
        var visited = new HashSet<(int x, int y)>();
        var accessibleTiles = new HashSet<(int x, int y)>();

        foreach (var tile in grid.AllTiles())
        {
            if (!ZonesThatNeedRoads.Contains(tile.Zone))
                continue;

            var pos = (tile.X, tile.Y);
            if (visited.Contains(pos))
                continue;

            // BFS flood-fill to collect the entire contiguous same-zone cluster
            var clusterZone = tile.Zone;
            var cluster = new List<(int x, int y)>();
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue(pos);
            visited.Add(pos);

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                cluster.Add((cx, cy));
                foreach (var n in grid.AdjacentTiles(cx, cy))
                {
                    var np = (n.X, n.Y);
                    if (!visited.Contains(np) && n.Zone == clusterZone)
                    {
                        visited.Add(np);
                        queue.Enqueue(np);
                    }
                }
            }

            // If any tile in the cluster directly touches a road, the whole cluster gets access
            bool clusterHasAccess = cluster.Any(p => roadAdjacent.Contains(p));
            if (clusterHasAccess)
                foreach (var p in cluster)
                    accessibleTiles.Add(p);
        }

        // Apply road access to all tiles in accessible clusters
        foreach (var (x, y) in accessibleTiles)
        {
            grid.SetRoadAccess(x, y, true);
            AccessibleZoneCount++;
        }
    }
}
