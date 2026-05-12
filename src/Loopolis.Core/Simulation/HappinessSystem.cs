using Loopolis.Core.Graph;
using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Calculates happiness for each ready residential zone.
///
/// Formula:
///   base = 0.6
///   + 0.25  if any adjacent tile is a ready Commercial zone (jobs nearby)
///   + 0.15  for each service type (Fire/Police/FireHQ/PoliceHQ/School/Hospital) covering this tile, max +0.30
///             (service coverage = road-graph distance ≤ coverage radius when a RoadGraph is supplied,
///              otherwise falls back to Manhattan distance)
///   - 0.4 * PollutionLevel  (pollution hurts happiness significantly)
///   - serviceNeglect        (accumulated penalty for lack of service coverage over time)
///   - commutePenalty        (road-graph distance to nearest powered industrial zone;
///                            ≤10.0=0, 10.1–20.0=−0.10, >20.0=−0.20, no-road-path=−0.25)
///                           (no penalty if city population &lt; 50 or no industrial on map)
///   = clamp(0.1, 1.0)
///
/// Service neglect accumulates at +0.001/tick when no service covers the tile (max 0.3),
/// and recovers at -0.002/tick when any service covers the tile (min 0.0).
/// This creates mid-game pressure: players must build services to maintain growth rate.
///
/// Hospital event penalty reduction: when an eventPenalty is active and a Hospital is within
/// road-graph radius 12 of the tile, the penalty is halved for that tile.
///
/// Commute penalty: punishes pure zone segregation (residential far from industrial jobs).
/// Pre-computed once per tick from powered industrial tiles. Grace period below pop 50.
///
/// Only ready residential zones get happiness calculated. Non-ready zones keep the default 1.0.
/// Call Propagate() each tick after PollutionSystem and DemandSystem.
/// </summary>
public class HappinessSystem
{
    /// <summary>
    /// Coverage radii in road-graph distance units.
    /// Road edges cost 1.0; Avenue edges cost 0.5.
    /// Used when a RoadGraph is passed to Propagate().
    /// </summary>
    private static readonly Dictionary<ZoneType, float> ServiceRadius = new()
    {
        { ZoneType.FireStation,    8.0f },
        { ZoneType.PoliceStation,  8.0f },
        { ZoneType.School,        10.0f },
        { ZoneType.PoliceHQ,       8.0f },   // HQ uses same radius — larger building, same coverage zone
        { ZoneType.FireHQ,         8.0f },
        { ZoneType.Hospital,      12.0f },
    };

    /// <summary>
    /// Coverage radii in Manhattan-distance tiles.
    /// Used as fallback when no RoadGraph is supplied (tests and standalone simulation without roads).
    /// </summary>
    private static readonly Dictionary<ZoneType, int> ServiceRadiusManhattan = new()
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

    /// <summary>
    /// Returns the average commute penalty across all developed residential tiles (those with BuildingId).
    /// Zero when there is no industrial or when population is below the grace threshold.
    /// </summary>
    public double AverageCommutePenalty(CityGrid grid, int population, RoadGraph? roadGraph = null)
    {
        if (population < CommutePenaltyGracePopulation) return 0.0;

        var poweredIndustrial = grid.TilesOfType(ZoneType.Industrial)
            .Where(t => t.HasPower)
            .ToList();
        if (poweredIndustrial.Count == 0) return 0.0;

        var developedResidential = grid.TilesOfType(ZoneType.Residential)
            .Where(t => t.BuildingId != null)
            .ToList();
        if (developedResidential.Count == 0) return 0.0;

        return developedResidential
            .Select(t => ComputeCommutePenalty(t, poweredIndustrial, grid, roadGraph))
            .Average();
    }

    private const int CommutePenaltyGracePopulation = 50;

    /// <summary>
    /// Computes the commute penalty for a single residential tile.
    /// When a RoadGraph is provided, uses road-graph distance (buckets: ≤10.0=0, 10.1–20.0=−0.10, >20.0=−0.20).
    /// Disconnected (no road path) = −0.25.
    /// Falls back to Manhattan distance (buckets: ≤8=0, ≤14=−0.10, >14=−0.20) when no graph is supplied.
    /// </summary>
    private static double ComputeCommutePenalty(Tile tile, List<Tile> poweredIndustrial,
        CityGrid grid, RoadGraph? roadGraph)
    {
        if (roadGraph != null)
        {
            var minDist = poweredIndustrial
                .Select(ind => roadGraph.GetDistanceViaRoads(grid, tile.X, tile.Y, ind.X, ind.Y))
                .Min();

            if (minDist >= float.MaxValue) return -0.25;   // no road connection at all

            return minDist switch
            {
                <= 10.0f => 0.0,
                <= 20.0f => -0.10,
                _        => -0.20,
            };
        }

        // Fallback: Manhattan distance (used when RoadGraph is not available, e.g. in tests)
        var minManhattan = poweredIndustrial
            .Select(ind => Math.Abs(ind.X - tile.X) + Math.Abs(ind.Y - tile.Y))
            .Min();

        return minManhattan switch
        {
            <= 8  => 0.0,
            <= 14 => -0.10,
            _     => -0.20,
        };
    }

    public void Propagate(CityGrid grid, double taxModifier = 0.0, double eventPenalty = 0.0,
        RoadTrafficSystem? trafficSystem = null, PowerCapacitySystem? powerCapacitySystem = null,
        int cityPopulation = 0, RoadGraph? roadGraph = null)
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

        // Pre-compute commute penalty: powered industrial positions (once per tick, not per tile)
        var poweredIndustrialPositions = (cityPopulation >= CommutePenaltyGracePopulation)
            ? grid.TilesOfType(ZoneType.Industrial).Where(t => t.HasPower).ToList()
            : null;

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
                bool covered;
                if (roadGraph != null)
                {
                    // Road-graph distance: tile must have road access to its neighbour, and
                    // service must also have road access, with graph distance ≤ coverage radius.
                    var dist = roadGraph.GetDistanceViaRoads(grid, tile.X, tile.Y, svc.X, svc.Y);
                    covered = dist <= ServiceRadius[svc.Zone];
                }
                else
                {
                    // Fallback: Manhattan distance (used in tests that don't supply a graph)
                    var dist = Math.Abs(svc.X - tile.X) + Math.Abs(svc.Y - tile.Y);
                    covered = ServiceRadiusManhattan.TryGetValue(svc.Zone, out var mRadius) && dist <= mRadius;
                }

                if (covered)
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
            // Hospital within road-graph radius 12 halves the penalty for covered tiles.
            if (eventPenalty != 0.0)
            {
                bool coveredByHospital;
                if (roadGraph != null)
                {
                    coveredByHospital = hospitals.Any(h =>
                        roadGraph.GetDistanceViaRoads(grid, tile.X, tile.Y, h.X, h.Y) <= ServiceRadius[ZoneType.Hospital]);
                }
                else
                {
                    coveredByHospital = hospitals.Any(h =>
                        Math.Abs(h.X - tile.X) + Math.Abs(h.Y - tile.Y) <= ServiceRadiusManhattan[ZoneType.Hospital]);
                }
                happiness += coveredByHospital ? eventPenalty * 0.5 : eventPenalty;
            }

            // Brownout penalty: applies only to BFS-powered tiles (already gated by IsReadyToDevelop).
            // brownoutPenalty is negative (e.g. −0.02), add it directly.
            if (brownoutPenalty != 0.0 && tile.HasPower)
                happiness += brownoutPenalty;

            // Traffic congestion penalty: −0.10 if adjacent to an overloaded road/avenue
            if (trafficSystem != null)
                happiness += trafficSystem.GetHappinessModifier(grid, tile.X, tile.Y);

            // Commute penalty: only applies to developed tiles (BuildingId != null) with industry on the map
            if (poweredIndustrialPositions != null && poweredIndustrialPositions.Count > 0 && tile.BuildingId != null)
                happiness += ComputeCommutePenalty(tile, poweredIndustrialPositions, grid, roadGraph);

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
