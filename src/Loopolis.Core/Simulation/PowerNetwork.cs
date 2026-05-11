using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Propagates power from CoalPlant/NuclearPlant (and legacy PowerPlant) tiles to all
/// connected non-empty tiles via BFS.
///
/// Conductors: CoalPlant, NuclearPlant, PowerPlant (legacy), PowerLine, Road, Residential, Commercial, Industrial
/// Insulators: Empty (breaks the chain — you must connect zones with roads or power lines)
///
/// Call Propagate() each simulation tick or whenever the grid changes.
/// </summary>
public class PowerNetwork
{
    private static readonly HashSet<ZoneType> Conductors = new()
    {
        ZoneType.PowerPlant,    // legacy alias
        ZoneType.CoalPlant,
        ZoneType.NuclearPlant,
        ZoneType.PowerLine,
        ZoneType.Road,
        ZoneType.Avenue,
        ZoneType.Residential,
        ZoneType.Commercial,
        ZoneType.Industrial,
        ZoneType.FireStation,
        ZoneType.PoliceStation,
        ZoneType.School,
        ZoneType.PoliceHQ,
        ZoneType.FireHQ,
        ZoneType.Hospital,
    };

    private static readonly HashSet<ZoneType> PowerSources = new()
    {
        ZoneType.PowerPlant,    // legacy alias
        ZoneType.CoalPlant,
        ZoneType.NuclearPlant,
    };

    public int PoweredTileCount { get; private set; }

    /// <summary>
    /// Clears all power state on the grid, then BFS-floods from every power source.
    /// All reachable conductor tiles are marked as powered.
    /// </summary>
    public void Propagate(CityGrid grid)
    {
        grid.ClearPower();
        PoweredTileCount = 0;

        var visited = new bool[grid.Width, grid.Height];
        var queue = new Queue<(int x, int y)>();

        // Seed BFS from every power plant (CoalPlant, NuclearPlant, and legacy PowerPlant)
        foreach (var tile in grid.AllTiles())
        {
            if (PowerSources.Contains(tile.Zone) && !visited[tile.X, tile.Y])
            {
                queue.Enqueue((tile.X, tile.Y));
                visited[tile.X, tile.Y] = true;
            }
        }

        // BFS flood-fill through conductors
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();

            grid.SetPower(x, y, true);
            PoweredTileCount++;

            foreach (var neighbor in grid.AdjacentTiles(x, y))
            {
                if (!visited[neighbor.X, neighbor.Y] && Conductors.Contains(neighbor.Zone))
                {
                    visited[neighbor.X, neighbor.Y] = true;
                    queue.Enqueue((neighbor.X, neighbor.Y));
                }
            }
        }
    }
}
