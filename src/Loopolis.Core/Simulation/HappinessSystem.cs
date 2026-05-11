using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Calculates happiness for each ready residential zone.
///
/// Formula:
///   base = 0.6
///   + 0.25  if any adjacent tile is a ready Commercial zone (jobs nearby)
///   + 0.15  for each service type (Fire/Police/School) covering this tile, max +0.30
///             (service coverage = within Manhattan distance of the service's coverage radius)
///   - 0.4 * PollutionLevel  (pollution hurts happiness significantly)
///   = clamp(0.1, 1.0)
///
/// Only ready residential zones get happiness calculated. Non-ready zones keep the default 1.0.
/// Call Propagate() each tick after PollutionSystem and DemandSystem.
/// </summary>
public class HappinessSystem
{
    private static readonly Dictionary<ZoneType, int> ServiceRadius = new()
    {
        { ZoneType.FireStation,   4 },
        { ZoneType.PoliceStation, 4 },
        { ZoneType.School,        5 },
    };

    public void Propagate(CityGrid grid)
    {
        grid.ClearHappiness();

        // Pre-compute: which service tiles exist (only compute once per tick)
        var services = grid.AllTiles()
            .Where(t => ServiceRadius.ContainsKey(t.Zone))
            .ToList();

        foreach (var tile in grid.TilesOfType(ZoneType.Residential))
        {
            if (!tile.IsReadyToDevelop) continue; // only ready zones have happiness calculated

            var happiness = 0.6;

            // Commercial adjacency bonus (jobs nearby)
            var hasNearbyCommercial = grid.AdjacentTiles(tile.X, tile.Y)
                .Any(n => n.Zone == ZoneType.Commercial && n.IsReadyToDevelop);
            if (hasNearbyCommercial) happiness += 0.25;

            // Service coverage bonus (each unique service type, max 2 types = +0.30)
            var coveredByTypes = new HashSet<ZoneType>();
            foreach (var svc in services)
            {
                var dist = Math.Abs(svc.X - tile.X) + Math.Abs(svc.Y - tile.Y); // Manhattan distance
                if (dist <= ServiceRadius[svc.Zone])
                    coveredByTypes.Add(svc.Zone);
            }
            happiness += Math.Min(coveredByTypes.Count, 2) * 0.15;

            // Pollution penalty
            happiness -= tile.PollutionLevel * 0.4;

            // Clamp
            happiness = Math.Clamp(happiness, 0.1, 1.0);

            grid.SetHappiness(tile.X, tile.Y, happiness);
        }
    }

    public double AverageHappiness(CityGrid grid)
    {
        var ready = grid.TilesOfType(ZoneType.Residential)
            .Where(t => t.IsReadyToDevelop).ToList();
        if (ready.Count == 0) return 1.0;
        return ready.Average(t => t.Happiness);
    }
}
