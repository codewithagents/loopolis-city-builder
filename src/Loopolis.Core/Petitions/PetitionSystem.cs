using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Petitions;

/// <summary>
/// Manages the lifecycle of citizen petitions.
///
/// Petitions are filed when simulation thresholds are breached:
///   Happiness   — cluster of ≥3 contiguous residential tiles all with happiness &lt; 0.45
///   Power       — ≥3 zoned tiles without power AND a power plant exists in the city
///   Employment  — EmploymentRatio &lt; 0.40 AND Population > 150
///   Services    — ≥4 residential tiles in a cluster with no fire or police coverage
///   Pollution   — ≥3 residential tiles in a cluster with PollutionLevel > 0.6, OR
///                 ≥2 direct industrial neighbours (if no PollutionLevel data)
///   Overcrowding — ≥3 residential buildings at ≥95% capacity
///
/// Lifecycle constraints:
///   - Max 3 active (unresolved) petitions citywide at any time
///   - Max 1 active petition per DistrictName (no spamming the same area)
///   - Deadline = IssuedTick + 75 ticks
///   - Unresolved at deadline → PenaltyApplied = true, 100-tick district penalty of -0.05 happiness
///
/// Call Tick() once per simulation tick, AFTER all simulation systems have run.
/// </summary>
public class PetitionSystem
{
    private const int DeadlineTicks    = 75;
    private const int MaxActive        = 3;
    private const int PenaltyDuration  = 100;  // ticks
    private const float DistrictPenalty = 0.05f;

    private readonly List<Petition> _active   = new();
    private readonly List<Petition> _expired  = new();
    private readonly List<Petition> _newThisTick      = new();
    private readonly List<Petition> _recentlyResolved = new();

    // districtName → remaining ticks of -0.05 happiness penalty
    private readonly Dictionary<string, int> _penaltyTicks = new();

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>All unresolved, non-penalized petitions currently open.</summary>
    public IReadOnlyList<Petition> ActivePetitions => _active;

    /// <summary>Petitions issued this tick (for toast delivery). Cleared at start of next Tick().</summary>
    public IReadOnlyList<Petition> NewThisTick => _newThisTick;

    /// <summary>Petitions resolved this tick (for celebration toast). Cleared at start of next Tick().</summary>
    public IReadOnlyList<Petition> RecentlyResolved => _recentlyResolved;

    /// <summary>Petitions that expired without being resolved (historical log).</summary>
    public IReadOnlyList<Petition> ExpiredPetitions => _expired;

    /// <summary>
    /// Returns the happiness penalty (0.05f) for the given district if that district has an
    /// active penalty from an expired unresolved petition.  Returns 0.0f otherwise.
    /// </summary>
    public float GetDistrictPenalty(string districtName) =>
        _penaltyTicks.TryGetValue(districtName, out var remaining) && remaining > 0
            ? DistrictPenalty
            : 0.0f;

    // ── Main tick ────────────────────────────────────────────────────────────

    /// <summary>
    /// Run petition lifecycle checks for one simulation tick.
    /// Must be called after all other simulation systems (population, happiness, employment, etc.).
    /// </summary>
    public void Tick(CityGrid grid, SimulationEngine engine)
    {
        _newThisTick.Clear();
        _recentlyResolved.Clear();

        var tick = engine.TickCount;  // current tick (after increment by engine, so this is the JUST-completed tick)

        // Step 1: Decay penalty timers
        foreach (var key in _penaltyTicks.Keys.ToList())
        {
            _penaltyTicks[key]--;
            if (_penaltyTicks[key] <= 0)
                _penaltyTicks.Remove(key);
        }

        // Step 2: Check deadlines — expire overdue petitions
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var p = _active[i];
            if (tick >= p.DeadlineTick && !p.Resolved)
            {
                var expired = p with { PenaltyApplied = true };
                _active.RemoveAt(i);
                _expired.Add(expired);
                AddPenaltyDistrict(expired.DistrictName);
            }
        }

        // Step 3: Resolve petitions whose triggering condition has cleared
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var p = _active[i];
            if (IsConditionCleared(p, grid, engine))
            {
                var resolved = p with { Resolved = true };
                _active.RemoveAt(i);
                _recentlyResolved.Add(resolved);
            }
        }

        // Step 4: Check for new petition triggers (only if < MaxActive citywide)
        if (_active.Count < MaxActive)
            CheckAndIssuePetitions(grid, engine, tick);
    }

    // ── Condition checking ──────────────────────────────────────────────────

    private bool IsConditionCleared(Petition petition, CityGrid grid, SimulationEngine engine)
    {
        return petition.Category switch
        {
            "Happiness"    => !HasHappinessTrigger(grid),
            "Power"        => !HasPowerTrigger(grid),
            "Employment"   => !HasEmploymentTrigger(engine),
            "Services"     => !HasServicesTrigger(grid, engine),
            "Pollution"    => !HasPollutionTrigger(grid),
            "Overcrowding" => !HasOvercrowdingTrigger(grid),
            _              => true,
        };
    }

    // ── Petition generation ──────────────────────────────────────────────────

    private void CheckAndIssuePetitions(CityGrid grid, SimulationEngine engine, int tick)
    {
        TryIssue("Happiness",    grid, engine, tick, FindHappinessCluster,    HappinessTexts);
        TryIssue("Power",        grid, engine, tick, FindPowerCluster,        PowerTexts);
        TryIssue("Employment",   grid, engine, tick, FindEmploymentCluster,   EmploymentTexts);
        TryIssue("Services",     grid, engine, tick, FindServicesCluster,     ServicesTexts);
        TryIssue("Pollution",    grid, engine, tick, FindPollutionCluster,    PollutionTexts);
        TryIssue("Overcrowding", grid, engine, tick, FindOvercrowdingCluster, OvercrowdingTexts);
    }

    private delegate IReadOnlyList<(int x, int y)>? ClusterFinder(CityGrid grid, SimulationEngine engine);

    private void TryIssue(
        string category,
        CityGrid grid,
        SimulationEngine engine,
        int tick,
        ClusterFinder findCluster,
        string[] texts)
    {
        // Already have this category active? (Not a strict dedup, but prevents stacking same category)
        // Actually: dedup is per district name — the same DISTRICT can't have two petitions
        // But multiple districts can have the same category.

        if (_active.Count >= MaxActive) return;

        var cluster = findCluster(grid, engine);
        if (cluster == null || cluster.Count == 0) return;

        var districtName = DistrictNamer.Name(grid, cluster);

        // Max 1 active petition per district
        if (_active.Any(p => p.DistrictName == districtName)) return;

        var districtHash = ComputeDistrictHash(cluster);
        var id = $"{category}_{districtHash}";

        // Dedup by id (same category + same cluster area)
        if (_active.Any(p => p.Id == id)) return;
        if (_expired.Any(p => p.Id == id && !p.PenaltyApplied)) return; // recently expired, don't re-issue immediately

        var textIndex = tick % 3;
        var text = texts[textIndex].Replace("{d}", districtName);

        var petition = new Petition(
            Id:            id,
            DistrictName:  districtName,
            Text:          text,
            Category:      category,
            IssuedTick:    tick,
            DeadlineTick:  tick + DeadlineTicks,
            Resolved:      false,
            PenaltyApplied: false
        );

        _active.Add(petition);
        _newThisTick.Add(petition);
    }

    // ── Cluster finders ──────────────────────────────────────────────────────

    private static IReadOnlyList<(int x, int y)>? FindHappinessCluster(CityGrid grid, SimulationEngine engine)
    {
        // Find contiguous residential tiles all with happiness < 0.45
        var unhappy = grid.AllTiles()
            .Where(t => t.Zone == ZoneType.Residential && t.Happiness < 0.45)
            .Select(t => (t.X, t.Y))
            .ToHashSet();

        return FindContiguousCluster(unhappy, minSize: 3);
    }

    private static IReadOnlyList<(int x, int y)>? FindPowerCluster(CityGrid grid, SimulationEngine engine)
    {
        // Only triggers when there IS a power plant (brownout/reach failure, not pre-power era)
        var hasPowerPlant = grid.AllTiles().Any(t =>
            t.Zone is ZoneType.PowerPlant or ZoneType.CoalPlant or ZoneType.NuclearPlant);
        if (!hasPowerPlant) return null;

        var unpowered = grid.AllTiles()
            .Where(t => t.Zone is ZoneType.Residential or ZoneType.Commercial or ZoneType.Industrial
                        && !t.HasPower)
            .Select(t => (t.X, t.Y))
            .ToHashSet();

        return FindContiguousCluster(unpowered, minSize: 3);
    }

    private static IReadOnlyList<(int x, int y)>? FindEmploymentCluster(CityGrid grid, SimulationEngine engine)
    {
        if (engine.EmploymentSystem.EmploymentRatio >= 0.40) return null;
        if (engine.Population.Population <= 150) return null;

        // Use residential tiles near industrial areas, or just any R cluster
        var rTiles = grid.AllTiles()
            .Where(t => t.Zone == ZoneType.Residential)
            .Select(t => (t.X, t.Y))
            .ToHashSet();

        // Return the first cluster of ≥3 residential tiles
        return FindContiguousCluster(rTiles, minSize: 3);
    }

    private static IReadOnlyList<(int x, int y)>? FindServicesCluster(CityGrid grid, SimulationEngine engine)
    {
        // ≥4 residential tiles with no fire or police coverage
        var servicelessTiles = new HashSet<(int, int)>();

        var fireAndPoliceTiles = grid.AllTiles()
            .Where(t => t.Zone is ZoneType.FireStation or ZoneType.FireHQ
                                   or ZoneType.PoliceStation or ZoneType.PoliceHQ)
            .ToList();

        if (engine.LastServiceCoverage != null)
        {
            // Use the ratio to decide if we should even scan
            var cov = engine.LastServiceCoverage;
            var avgCov = (cov.FireCoveragePercent + cov.PoliceCoveragePercent) / 2.0f;
            if (avgCov >= 0.60f) return null; // good coverage — no petition
        }

        foreach (var tile in grid.TilesOfType(ZoneType.Residential))
        {
            // Check road-graph coverage if available
            bool hasFireCoverage   = false;
            bool hasPoliceCoverage = false;

            foreach (var svc in fireAndPoliceTiles)
            {
                var radius = svc.Zone is ZoneType.FireStation or ZoneType.FireHQ ? 8.0f : 8.0f;
                var dist   = engine.RoadGraph.GetDistanceViaRoads(grid, tile.X, tile.Y, svc.X, svc.Y);
                if (dist <= radius)
                {
                    if (svc.Zone is ZoneType.FireStation or ZoneType.FireHQ)
                        hasFireCoverage = true;
                    else
                        hasPoliceCoverage = true;
                }
                if (hasFireCoverage && hasPoliceCoverage) break;
            }

            if (!hasFireCoverage && !hasPoliceCoverage)
                servicelessTiles.Add((tile.X, tile.Y));
        }

        return FindContiguousCluster(servicelessTiles, minSize: 4);
    }

    private static IReadOnlyList<(int x, int y)>? FindPollutionCluster(CityGrid grid, SimulationEngine engine)
    {
        // ≥3 residential tiles with PollutionLevel > 0.6
        // Fallback: residential with 2+ direct industrial neighbours
        var polluted = new HashSet<(int, int)>();

        foreach (var tile in grid.TilesOfType(ZoneType.Residential))
        {
            if (tile.PollutionLevel > 0.6)
            {
                polluted.Add((tile.X, tile.Y));
            }
            else
            {
                // Fallback: count direct industrial neighbours
                var industrialNeighbors = grid.AdjacentTiles(tile.X, tile.Y)
                    .Count(t => t.Zone == ZoneType.Industrial);
                if (industrialNeighbors >= 2)
                    polluted.Add((tile.X, tile.Y));
            }
        }

        return FindContiguousCluster(polluted, minSize: 3);
    }

    private static IReadOnlyList<(int x, int y)>? FindOvercrowdingCluster(CityGrid grid, SimulationEngine engine)
    {
        // ≥3 residential buildings at ≥95% capacity
        var overcrowded = new List<(int x, int y)>();

        foreach (var building in grid.Buildings.Values)
        {
            if (building.Zone != ZoneType.Residential) continue;

            // Sum population across all building tiles
            var totalPop = building.Tiles()
                .Where(t => grid.IsInBounds(t.X, t.Y))
                .Sum(t => grid.GetPopulation(t.X, t.Y));

            // Capacity from building type definition
            var typeDef  = Buildings.BuildingCatalog.Find(building.TypeId);
            var capacity = typeDef?.MaxPopulation ?? (building.TileCount * 50);

            if (capacity > 0 && totalPop >= (int)(capacity * 0.95))
                overcrowded.Add((building.AnchorX, building.AnchorY));
        }

        if (overcrowded.Count < 3) return null;

        // Return tiles for the first 3 overcrowded buildings (for district naming)
        return overcrowded.Take(3).ToList();
    }

    // ── Trigger presence checks (for resolution) ────────────────────────────

    private static bool HasHappinessTrigger(CityGrid grid)
    {
        var unhappy = grid.AllTiles()
            .Where(t => t.Zone == ZoneType.Residential && t.Happiness < 0.45)
            .Select(t => (t.X, t.Y))
            .ToHashSet();
        return FindContiguousCluster(unhappy, minSize: 3) != null;
    }

    private static bool HasPowerTrigger(CityGrid grid)
    {
        var hasPowerPlant = grid.AllTiles().Any(t =>
            t.Zone is ZoneType.PowerPlant or ZoneType.CoalPlant or ZoneType.NuclearPlant);
        if (!hasPowerPlant) return false;

        var unpowered = grid.AllTiles()
            .Where(t => t.Zone is ZoneType.Residential or ZoneType.Commercial or ZoneType.Industrial
                        && !t.HasPower)
            .Select(t => (t.X, t.Y))
            .ToHashSet();
        return FindContiguousCluster(unpowered, minSize: 3) != null;
    }

    private static bool HasEmploymentTrigger(SimulationEngine engine) =>
        engine.EmploymentSystem.EmploymentRatio < 0.40 && engine.Population.Population > 150;

    private static bool HasServicesTrigger(CityGrid grid, SimulationEngine engine)
    {
        var fireAndPoliceTiles = grid.AllTiles()
            .Where(t => t.Zone is ZoneType.FireStation or ZoneType.FireHQ
                                   or ZoneType.PoliceStation or ZoneType.PoliceHQ)
            .ToList();

        var servicelessTiles = new HashSet<(int, int)>();
        foreach (var tile in grid.TilesOfType(ZoneType.Residential))
        {
            bool hasCoverage = fireAndPoliceTiles.Any(svc =>
            {
                var dist = engine.RoadGraph.GetDistanceViaRoads(grid, tile.X, tile.Y, svc.X, svc.Y);
                return dist <= 8.0f;
            });
            if (!hasCoverage) servicelessTiles.Add((tile.X, tile.Y));
        }
        return FindContiguousCluster(servicelessTiles, minSize: 4) != null;
    }

    private static bool HasPollutionTrigger(CityGrid grid)
    {
        var polluted = new HashSet<(int, int)>();
        foreach (var tile in grid.TilesOfType(ZoneType.Residential))
        {
            if (tile.PollutionLevel > 0.6)
                polluted.Add((tile.X, tile.Y));
            else
            {
                var industrialNeighbors = grid.AdjacentTiles(tile.X, tile.Y)
                    .Count(t => t.Zone == ZoneType.Industrial);
                if (industrialNeighbors >= 2)
                    polluted.Add((tile.X, tile.Y));
            }
        }
        return FindContiguousCluster(polluted, minSize: 3) != null;
    }

    private static bool HasOvercrowdingTrigger(CityGrid grid)
    {
        var overcrowdedCount = 0;
        foreach (var building in grid.Buildings.Values)
        {
            if (building.Zone != ZoneType.Residential) continue;
            var totalPop = building.Tiles()
                .Where(t => grid.IsInBounds(t.X, t.Y))
                .Sum(t => grid.GetPopulation(t.X, t.Y));
            var typeDef  = Buildings.BuildingCatalog.Find(building.TypeId);
            var capacity = typeDef?.MaxPopulation ?? (building.TileCount * 50);
            if (capacity > 0 && totalPop >= (int)(capacity * 0.95))
                overcrowdedCount++;
        }
        return overcrowdedCount >= 3;
    }

    // ── Cluster utilities ────────────────────────────────────────────────────

    /// <summary>
    /// BFS flood-fill through the given candidate set to find the first contiguous cluster
    /// of at least <paramref name="minSize"/> tiles (4-connected orthogonal adjacency).
    /// Returns the cluster tiles, or null if no qualifying cluster exists.
    /// </summary>
    private static IReadOnlyList<(int x, int y)>? FindContiguousCluster(
        HashSet<(int, int)> candidates, int minSize)
    {
        if (candidates.Count < minSize) return null;

        var visited = new HashSet<(int, int)>();

        foreach (var start in candidates)
        {
            if (visited.Contains(start)) continue;

            // BFS
            var cluster = new List<(int, int)>();
            var queue   = new Queue<(int, int)>();
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                cluster.Add((cx, cy));

                int[] dx = { 0, 0, -1, 1 };
                int[] dy = { -1, 1, 0, 0 };
                for (var i = 0; i < 4; i++)
                {
                    var nx = cx + dx[i];
                    var ny = cy + dy[i];
                    var nb = (nx, ny);
                    if (!visited.Contains(nb) && candidates.Contains(nb))
                    {
                        visited.Add(nb);
                        queue.Enqueue(nb);
                    }
                }
            }

            if (cluster.Count >= minSize)
                return cluster;
        }

        return null;
    }

    // ── Penalty management ───────────────────────────────────────────────────

    private void AddPenaltyDistrict(string districtName)
    {
        _penaltyTicks[districtName] = PenaltyDuration;
    }

    // ── Hash utilities ───────────────────────────────────────────────────────

    private static int ComputeDistrictHash(IReadOnlyList<(int x, int y)> tiles)
    {
        // Stable, order-independent hash from tile coords
        int hash = 0;
        foreach (var (x, y) in tiles)
            hash = unchecked(hash + x * 31 + y * 17);
        return Math.Abs(hash);
    }

    // ── Petition text variants ───────────────────────────────────────────────

    private static readonly string[] HappinessTexts =
    {
        "Residents of {d} are exhausted and unhappy. More parks and services are needed — urgently.",
        "The families of {d} feel abandoned by the city. Happiness is at a breaking point.",
        "Citizens in {d} are moving away. Fix services and reduce pollution before it's too late.",
    };

    private static readonly string[] PowerTexts =
    {
        "The lights keep going out in {d}. Our businesses can't operate without reliable power.",
        "Residents of {d} demand immediate power restoration. This outage has gone on too long.",
        "Everything in {d} is dark. We need another power plant — now.",
    };

    private static readonly string[] EmploymentTexts =
    {
        "Workers in {d} have nowhere to go. Zone more industrial areas near our neighborhoods.",
        "The young people of {d} are idle. We need factories and workshops, not empty promises.",
        "Unemployment in {d} is soaring. Build more industry or we'll look for opportunity elsewhere.",
    };

    private static readonly string[] ServicesTexts =
    {
        "Children in {d} have no school within reach. Build one before the next generation is lost.",
        "We haven't seen a police officer in {d} in weeks. Crime is rising — act now.",
        "{d} has no fire station coverage. One fire and the whole district burns.",
    };

    private static readonly string[] PollutionTexts =
    {
        "The air in {d} is barely breathable. Move the factories away from our homes.",
        "Our children in {d} are getting sick from the pollution. Enough is enough.",
        "Residents of {d} demand a green buffer zone between us and the industrial district.",
    };

    private static readonly string[] OvercrowdingTexts =
    {
        "The buildings of {d} are overflowing. Zone more residential land before people leave.",
        "{d} is bursting at the seams. We need more housing — zone new residential areas now.",
        "There's no room left in {d}. Expand the residential zone or watch us leave for other cities.",
    };
}
