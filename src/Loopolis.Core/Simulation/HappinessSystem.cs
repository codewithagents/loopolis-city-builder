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

    private const int ParkBonusRadius = 3;            // Chebyshev distance
    private const double ParkHappinessBonusPerTile = 0.08; // per park tile within radius
    private const double ParkHappinessBonusCap = 0.20;     // maximum total park contribution

    public void Propagate(CityGrid grid, double taxModifier = 0.0, double eventPenalty = 0.0,
        RoadTrafficSystem? trafficSystem = null, PowerCapacitySystem? powerCapacitySystem = null,
        int cityPopulation = 0, RoadGraph? roadGraph = null, double policyHappinessBonus = 0.0,
        float serviceCoverageRadiusBonus = 0f, double parkHappinessMultiplier = 1.0)
    {
        grid.ClearHappiness();

        // Pre-compute: which service tiles exist (only compute once per tick)
        var services = grid.AllTiles()
            .Where(t => ServiceRadius.ContainsKey(t.Zone))
            .ToList();

        // Pre-compute: park tile positions for Chebyshev-2 happiness bonus
        var parkTiles = grid.TilesOfType(ZoneType.Park).ToList();

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
                    // Civic Charter adds serviceCoverageRadiusBonus to all service radii.
                    var dist = roadGraph.GetDistanceViaRoads(grid, tile.X, tile.Y, svc.X, svc.Y);
                    covered = dist <= ServiceRadius[svc.Zone] + serviceCoverageRadiusBonus;
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
                neglect = Math.Min(0.20, neglect + 0.001); // accumulate 0.001/tick, cap at 0.20 (floors happiness at ~0.40)
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

            // Park bonus: +0.08 per park tile within Chebyshev distance 3, capped at +0.20 total
            // Civic Charter doubles the park happiness contribution (parkHappinessMultiplier = 2.0).
            if (parkTiles.Count > 0)
            {
                var nearbyParks = parkTiles.Count(p =>
                    Math.Abs(p.X - tile.X) <= ParkBonusRadius &&
                    Math.Abs(p.Y - tile.Y) <= ParkBonusRadius);
                if (nearbyParks > 0)
                {
                    var rawParkBonus = Math.Min(nearbyParks * ParkHappinessBonusPerTile, ParkHappinessBonusCap);
                    happiness += rawParkBonus * parkHappinessMultiplier;
                }
            }

            // Traffic congestion penalty: −0.10 if adjacent to an overloaded road/avenue
            if (trafficSystem != null)
                happiness += trafficSystem.GetHappinessModifier(grid, tile.X, tile.Y);

            // Commute penalty: only applies to developed tiles (BuildingId != null) with industry on the map
            if (poweredIndustrialPositions != null && poweredIndustrialPositions.Count > 0 && tile.BuildingId != null)
                happiness += ComputeCommutePenalty(tile, poweredIndustrialPositions, grid, roadGraph);

            // Policy happiness bonus (e.g. GreenCity +0.10)
            if (policyHappinessBonus != 0.0)
                happiness += policyHappinessBonus;

            // Clamp
            happiness = Math.Clamp(happiness, 0.1, 1.0);

            grid.SetHappiness(tile.X, tile.Y, happiness);
        }
    }

    /// <summary>Returns the current service neglect penalty for a tile (0.0 to 0.3).</summary>
    public double GetNeglect(int x, int y) =>
        _neglect.TryGetValue((x, y), out var v) ? v : 0;

    /// <summary>
    /// Returns the average neglect level across all developed residential tiles (0.0 to 0.20).
    /// Used by the Runner/HUD to warn the player when neglect is rising.
    /// Returns 0.0 when there are no developed residential tiles.
    /// </summary>
    public double AverageNeglect(CityGrid grid)
    {
        var developed = grid.TilesOfType(ZoneType.Residential)
            .Where(t => t.BuildingId != null)
            .ToList();
        if (developed.Count == 0) return 0.0;
        return developed.Average(t => GetNeglect(t.X, t.Y));
    }

    public double AverageHappiness(CityGrid grid)
    {
        var ready = grid.TilesOfType(ZoneType.Residential)
            .Where(t => t.IsReadyToDevelop).ToList();
        if (ready.Count == 0) return 1.0;
        return ready.Average(t => t.Happiness);
    }

    /// <summary>
    /// Computes capacity-aware service coverage for all four service types.
    ///
    /// For each service building:
    ///   1. Run Dijkstra from the building's road neighbour → distances to all road nodes.
    ///   2. Collect candidate tiles (residential for School/Police/Hospital; any developed tile for Fire).
    ///   3. Sort candidates by road-graph distance ascending (closest first).
    ///   4. Drain building capacity: mark tiles covered until capacity runs out.
    ///
    /// Multiple buildings of the same type stack: a tile covered by ANY building of that type is covered.
    /// Results are aggregated into a <see cref="ServiceCoverageResult"/> snapshot.
    /// </summary>
    public ServiceCoverageResult ComputeServiceCoverage(CityGrid grid, RoadGraph? roadGraph = null)
    {
        // Service types that use capacity model (base types only — HQ variants use same logic)
        var serviceGroups = new[]
        {
            new ServiceGroup(ZoneType.School,        "School"),
            new ServiceGroup(ZoneType.PoliceStation, "Police"),
            new ServiceGroup(ZoneType.FireStation,   "Fire"),
            new ServiceGroup(ZoneType.Hospital,      "Hospital"),
        };

        // Collect HQ types that map onto base types
        // PoliceHQ → PoliceStation category, FireHQ → FireStation category
        static ZoneType BaseCategory(ZoneType z) => z switch
        {
            ZoneType.PoliceHQ => ZoneType.PoliceStation,
            ZoneType.FireHQ   => ZoneType.FireStation,
            _                 => z,
        };

        // Find all service buildings per category
        var allServiceTiles = grid.AllTiles()
            .Where(t => ServiceCapacityModel.Capacity.ContainsKey(t.Zone))
            .ToList();

        // For FireStation/FireHQ: candidate tiles are all developed tiles (any zone with BuildingId)
        // For others: candidate tiles are residential tiles with road access
        var residentialCandidates = grid.TilesOfType(ZoneType.Residential)
            .Where(t => t.IsReadyToDevelop)
            .ToList();

        var developedCandidates = grid.AllTiles()
            .Where(t => t.BuildingId != null)
            .ToList();

        var results = new Dictionary<ZoneType, ServiceGroupResult>();

        foreach (var group in serviceGroups)
        {
            var serviceType    = group.ServiceType;
            var candidates     = (serviceType == ZoneType.FireStation)
                ? developedCandidates
                : residentialCandidates;

            // Gather all buildings in this category (including HQ variants)
            var buildings = allServiceTiles
                .Where(t => BaseCategory(t.Zone) == serviceType)
                .ToList();

            var seatsTotal = buildings.Sum(b => ServiceCapacityModel.Capacity[b.Zone]);

            if (buildings.Count == 0 || candidates.Count == 0)
            {
                results[serviceType] = new ServiceGroupResult(
                    CoveredTiles:  new HashSet<(int, int)>(),
                    SeatsUsed:     0,
                    SeatsTotal:    seatsTotal,
                    TotalCandidates: candidates.Count);
                continue;
            }

            var coveredSet = new HashSet<(int, int)>();
            var seatsUsed  = 0;

            foreach (var building in buildings)
            {
                if (roadGraph == null)
                {
                    // Fallback: Manhattan distance, no capacity drain — just pure radius coverage
                    var mRadius = ServiceRadius.TryGetValue(building.Zone, out var r)
                        ? r : 0;
                    var mRadiusManhattan = ServiceRadiusManhattan.TryGetValue(building.Zone, out var mr)
                        ? mr : 0;
                    var capacity   = ServiceCapacityModel.Capacity[building.Zone];
                    var remaining  = capacity;

                    var sorted = candidates
                        .OrderBy(c => Math.Abs(c.X - building.X) + Math.Abs(c.Y - building.Y))
                        .Take(ServiceCapacityModel.MaxTilesPerBuilding)
                        .ToList();

                    foreach (var candidate in sorted)
                    {
                        if (remaining <= 0) break;
                        var dist = Math.Abs(candidate.X - building.X) + Math.Abs(candidate.Y - building.Y);
                        if (dist > mRadiusManhattan) break; // sorted ascending, can break early

                        if (!coveredSet.Contains((candidate.X, candidate.Y)))
                        {
                            var demand = ServiceCapacityModel.GetDemandPerTile(serviceType, candidate);
                            if (demand > 0 && remaining >= demand)
                            {
                                coveredSet.Add((candidate.X, candidate.Y));
                                remaining  -= demand;
                                seatsUsed  += demand;
                            }
                            else if (demand == 0)
                            {
                                // Tile with 0 demand (e.g. unpopulated residential) — still mark covered
                                // but don't drain capacity for 0-demand tiles
                                coveredSet.Add((candidate.X, candidate.Y));
                            }
                        }
                    }
                }
                else
                {
                    // Road-graph distance coverage with capacity
                    var radius   = ServiceRadius.TryGetValue(building.Zone, out var r) ? r : 0f;
                    var capacity = ServiceCapacityModel.Capacity[building.Zone];
                    var remaining = capacity;

                    // Find the building's road neighbour
                    var buildingRoadNeighbour = FindRoadNeighbour(grid, roadGraph, building.X, building.Y);
                    if (buildingRoadNeighbour == null) continue; // building not road-accessible

                    // Run Dijkstra from the building's road node → distances to all reachable road nodes
                    var distMap = roadGraph.ShortestPathSourceMap(
                        buildingRoadNeighbour.Value.x,
                        buildingRoadNeighbour.Value.y);

                    // Compute road-graph distance to each candidate
                    var candidatesWithDist = new List<(Tile tile, float dist)>(
                        Math.Min(candidates.Count, ServiceCapacityModel.MaxTilesPerBuilding * 2));

                    foreach (var candidate in candidates)
                    {
                        var candRoadNeighbour = FindRoadNeighbour(grid, roadGraph, candidate.X, candidate.Y);
                        if (candRoadNeighbour == null) continue;

                        if (!distMap.TryGetValue(candRoadNeighbour.Value, out var d)) continue;
                        if (d > radius) continue; // beyond service radius

                        candidatesWithDist.Add((candidate, d));
                    }

                    // Sort by distance ascending — closest first
                    candidatesWithDist.Sort((a, b) => a.dist.CompareTo(b.dist));

                    // Take up to MaxTilesPerBuilding
                    var limit = Math.Min(candidatesWithDist.Count, ServiceCapacityModel.MaxTilesPerBuilding);

                    for (var i = 0; i < limit; i++)
                    {
                        if (remaining <= 0) break;
                        var candidate = candidatesWithDist[i].tile;
                        var key = (candidate.X, candidate.Y);

                        if (!coveredSet.Contains(key))
                        {
                            var demand = ServiceCapacityModel.GetDemandPerTile(serviceType, candidate);
                            if (demand == 0)
                            {
                                // Zero-demand tile (e.g. no-pop residential, no-building tile)
                                // Mark covered without draining capacity
                                coveredSet.Add(key);
                            }
                            else if (remaining >= demand)
                            {
                                coveredSet.Add(key);
                                remaining  -= demand;
                                seatsUsed  += demand;
                            }
                            // If demand > remaining, tile is NOT covered (out of capacity)
                        }
                    }
                }
            }

            results[serviceType] = new ServiceGroupResult(
                CoveredTiles:    coveredSet,
                SeatsUsed:       seatsUsed,
                SeatsTotal:      seatsTotal,
                TotalCandidates: candidates.Count);
        }

        // --- Build result ---
        var schoolResult  = results[ZoneType.School];
        var policeResult  = results[ZoneType.PoliceStation];
        var fireResult    = results[ZoneType.FireStation];
        var hospitalResult = results[ZoneType.Hospital];

        float CoveragePercent(ServiceGroupResult g) =>
            g.TotalCandidates == 0 ? 0f
            : (float)g.CoveredTiles.Count / g.TotalCandidates;

        return new ServiceCoverageResult(
            SchoolCoveragePercent:   CoveragePercent(schoolResult),
            PoliceCoveragePercent:   CoveragePercent(policeResult),
            FireCoveragePercent:     CoveragePercent(fireResult),
            HospitalCoveragePercent: CoveragePercent(hospitalResult),
            SchoolSeatsUsed:         schoolResult.SeatsUsed,
            SchoolSeatsTotal:        schoolResult.SeatsTotal,
            PoliceCapacityUsed:      policeResult.SeatsUsed,
            PoliceCapacityTotal:     policeResult.SeatsTotal,
            FireCapacityUsed:        fireResult.SeatsUsed,
            FireCapacityTotal:       fireResult.SeatsTotal,
            HospitalBedsUsed:        hospitalResult.SeatsUsed,
            HospitalBedsTotal:       hospitalResult.SeatsTotal
        );
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static readonly (int dx, int dy)[] CardinalDirections =
        { (0, -1), (0, 1), (-1, 0), (1, 0) };

    /// <summary>
    /// Returns the road-graph node for the tile at (x,y) (if it is itself a node)
    /// or the first cardinal road-adjacent neighbour, or null if none exists.
    /// </summary>
    private static (int x, int y)? FindRoadNeighbour(CityGrid grid, RoadGraph roadGraph, int x, int y)
    {
        // Check if the tile itself is a road node
        if (roadGraph.IsRoadNode(x, y)) return (x, y);

        foreach (var (dx, dy) in CardinalDirections)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (!grid.IsInBounds(nx, ny)) continue;
            if (roadGraph.IsRoadNode(nx, ny)) return (nx, ny);
        }
        return null;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private record ServiceGroup(ZoneType ServiceType, string Name);

    private record ServiceGroupResult(
        HashSet<(int, int)> CoveredTiles,
        int SeatsUsed,
        int SeatsTotal,
        int TotalCandidates);
}
