using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Calculates happiness for each ready residential zone.
///
/// Formula:
///   base = 0.6
///   + 0.25  if any adjacent tile is a ready Commercial zone (jobs nearby)
///   + 0.15  for each service type (Fire/Police/FireHQ/PoliceHQ/School/Hospital) covering this tile, max +0.30
///             (service coverage = within Manhattan distance of the service's coverage radius)
///   - 0.4 * PollutionLevel  (pollution hurts happiness significantly)
///   - serviceNeglect        (accumulated penalty for lack of service coverage over time)
///   = clamp(0.1, 1.0)
///
/// Service neglect accumulates at +0.001/tick when no service covers the tile (max 0.3),
/// and recovers at -0.002/tick when any service covers the tile (min 0.0).
/// This creates mid-game pressure: players must build services to maintain growth rate.
///
/// Hospital event penalty reduction: when an eventPenalty is active and a Hospital is within
/// radius 8 of the tile, the penalty is halved for that tile.
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
        { ZoneType.PoliceHQ,     10 },
        { ZoneType.FireHQ,       10 },
        { ZoneType.Hospital,      8 },
    };

    /// <summary>
    /// Maps HQ zone types to their base service category for coverage grouping.
    /// PoliceHQ counts as PoliceStation for the "max 2 types" bonus cap.
    /// FireHQ counts as FireStation. Hospital is its own category.
    /// </summary>
    private static ZoneType ServiceCategory(ZoneType zone) => zone switch
    {
        ZoneType.PoliceHQ => ZoneType.PoliceStation,
        ZoneType.FireHQ   => ZoneType.FireStation,
        _                 => zone,
    };

    // Option B: track neglect per tile in a dictionary — no CityGrid API change needed
    private readonly Dictionary<(int, int), double> _neglect = new();

    public void Propagate(CityGrid grid, double taxModifier = 0.0, double eventPenalty = 0.0,
        RoadTrafficSystem? trafficSystem = null, PowerCapacitySystem? powerCapacitySystem = null)
    {
        grid.ClearHappiness();

        // Pre-compute: which service tiles exist (only compute once per tick)
        var services = grid.AllTiles()
            .Where(t => ServiceRadius.ContainsKey(t.Zone))
            .ToList();

        // Pre-compute: which Hospital tiles exist for event penalty reduction
        var hospitals = services.Where(t => t.Zone == ZoneType.Hospital).ToList();

        // Brownout penalty: applies to BFS-powered tiles only
        var brownoutPenalty = powerCapacitySystem?.BrownoutHappinessPenalty ?? 0.0;

        foreach (var tile in grid.TilesOfType(ZoneType.Residential))
        {
            if (!tile.IsReadyToDevelop) continue; // only ready zones have happiness calculated

            var happiness = 0.6;

            // Commercial adjacency bonus (jobs nearby)
            var hasNearbyCommercial = grid.AdjacentTiles(tile.X, tile.Y)
                .Any(n => n.Zone == ZoneType.Commercial && n.IsReadyToDevelop);
            if (hasNearbyCommercial) happiness += 0.25;

            // Service coverage bonus (each unique service category, max 2 types = +0.30)
            // PoliceHQ counts as PoliceStation; FireHQ counts as FireStation; Hospital is its own category
            var coveredByCategories = new HashSet<ZoneType>();
            foreach (var svc in services)
            {
                var dist = Math.Abs(svc.X - tile.X) + Math.Abs(svc.Y - tile.Y); // Manhattan distance
                if (dist <= ServiceRadius[svc.Zone])
                    coveredByCategories.Add(ServiceCategory(svc.Zone));
            }
            happiness += Math.Min(coveredByCategories.Count, 2) * 0.15;

            // Pollution penalty
            happiness -= tile.PollutionLevel * 0.4;

            // Service neglect: accumulate when uncovered, recover when covered
            var key = (tile.X, tile.Y);
            var hasCoverage = coveredByCategories.Count > 0;
            _neglect.TryGetValue(key, out var neglect);
            if (hasCoverage)
                neglect = Math.Max(0.0, neglect - 0.002); // recover 0.002/tick
            else
                neglect = Math.Min(0.3, neglect + 0.001); // accumulate 0.001/tick
            _neglect[key] = neglect;

            // Apply neglect penalty after all other calculations
            happiness -= neglect;

            // Tax modifier: low taxes improve happiness; high taxes reduce it
            happiness += taxModifier;

            // Event penalty: active city events reduce happiness.
            // Hospital within radius 8 halves the penalty for covered tiles.
            if (eventPenalty != 0.0)
            {
                var coveredByHospital = hospitals.Any(h =>
                    Math.Abs(h.X - tile.X) + Math.Abs(h.Y - tile.Y) <= ServiceRadius[ZoneType.Hospital]);
                happiness += coveredByHospital ? eventPenalty * 0.5 : eventPenalty;
            }

            // Brownout penalty: applies only to BFS-powered tiles (already gated by IsReadyToDevelop).
            // brownoutPenalty is negative (e.g. −0.02), add it directly.
            if (brownoutPenalty != 0.0 && tile.HasPower)
                happiness += brownoutPenalty;

            // Traffic congestion penalty: −0.10 if adjacent to an overloaded road/avenue
            if (trafficSystem != null)
                happiness += trafficSystem.GetHappinessModifier(grid, tile.X, tile.Y);

            // Clamp
            happiness = Math.Clamp(happiness, 0.1, 1.0);

            grid.SetHappiness(tile.X, tile.Y, happiness);
        }
    }

    /// <summary>Returns the current service neglect penalty for a tile (0.0 to 0.3).</summary>
    public double GetNeglect(int x, int y) =>
        _neglect.TryGetValue((x, y), out var v) ? v : 0;

    public double AverageHappiness(CityGrid grid)
    {
        var ready = grid.TilesOfType(ZoneType.Residential)
            .Where(t => t.IsReadyToDevelop).ToList();
        if (ready.Count == 0) return 1.0;
        return ready.Average(t => t.Happiness);
    }
}
