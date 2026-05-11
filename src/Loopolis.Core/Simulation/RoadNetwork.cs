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
        ZoneType.FireStation,
        ZoneType.PoliceStation,
        ZoneType.School,
    };

    public int AccessibleZoneCount { get; private set; }

    public void Propagate(CityGrid grid)
    {
        grid.ClearRoadAccess();
        AccessibleZoneCount = 0;

        foreach (var tile in grid.AllTiles())
        {
            if (!ZonesThatNeedRoads.Contains(tile.Zone))
                continue;

            foreach (var neighbour in grid.AdjacentTiles(tile.X, tile.Y))
            {
                if (neighbour.Zone == ZoneType.Road || neighbour.Zone == ZoneType.Avenue)
                {
                    grid.SetRoadAccess(tile.X, tile.Y, true);
                    AccessibleZoneCount++;
                    break;
                }
            }
        }
    }
}
