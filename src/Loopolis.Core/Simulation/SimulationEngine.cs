using Loopolis.Core.Buildings;
using Loopolis.Core.Charters;
using Loopolis.Core.Graph;
using Loopolis.Core.Grid;
using Loopolis.Core.Petitions;
using Loopolis.Core.Policies;
using Loopolis.Core.Scenarios;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Orchestrates all simulation systems in the correct tick order.
///
/// Tick order:
///   1. PowerNetwork.Propagate         — mark tiles that have electricity
///   2. PowerCapacitySystem.Propagate  — compute city-level supply/demand + brownout flag
///   3. RoadNetwork.Propagate          — mark zones with road access
///   4. RoadGraph.ResetEdgeTraffic     — clear previous tick's edge traffic
///   5. WorkerFlowSystem.Route         — route workers R→I, accumulate edge traffic
///   6. RoadTrafficSystem.Propagate    — set tile.TrafficLoad from real edge-traffic data
///   7. PollutionSystem.Propagate      — industrial zones + CoalPlant emit pollution
///   8. DemandSystem.Propagate         — demand depends on which zones are ready (powered + road)
///   9. HappinessSystem.Propagate      — happiness uses pollution + demand + services + traffic + brownout penalty
///  10. LandValueSystem.Propagate      — land value from terrain, pollution, happiness, power
///  11. Population.Tick                — grow/decline based on ready zones + demand + happiness + traffic + brownout multiplier
///  12. Budget.SetPopulation           — sync population to budget
///  13. Budget.CollectTaxes            — income from current population (modified by land value for residential)
///  14. Budget.CollectCommercialIncome — commercial tile income
///  15. Budget.DeductMaintenance       — costs from current grid
///  16. PolicySystem.Tick              — deduct active policy costs from budget
///  17. MilestoneSystem.Check          — check for milestone progression and bankruptcy
///
/// This class has no Godot dependencies and is safe to use from tests, Runner, and Godot.
/// </summary>
public class SimulationEngine
{
    public CityGrid Grid { get; }
    public BudgetSystem Budget { get; }
    public PopulationSystem Population { get; }
    public PowerNetwork PowerNetwork { get; }
    public PowerCapacitySystem PowerCapacitySystem { get; }
    public RoadNetwork RoadNetwork { get; }
    public RoadTrafficSystem RoadTrafficSystem { get; }
    public DemandSystem DemandSystem { get; }
    public PollutionSystem PollutionSystem { get; }
    public HappinessSystem HappinessSystem { get; }
    public MilestoneSystem MilestoneSystem { get; }
    public EventSystem EventSystem { get; }
    public EmploymentSystem EmploymentSystem { get; }
    public BuildingGrowthSystem BuildingGrowthSystem { get; } = new();
    public BuildingDegradationSystem BuildingDegradationSystem { get; }  // seeded in constructor
    public LandValueSystem LandValueSystem { get; } = new();
    public RoadGraph RoadGraph { get; } = new();
    public WorkerFlowSystem WorkerFlowSystem { get; } = new();
    public PolicySystem PolicySystem { get; } = new();
    public CharterSystem Charters { get; } = new();
    public CityStatisticsSystem Statistics { get; } = new();
    public PetitionSystem PetitionSystem { get; } = new();
    public ServiceFatigueSystem ServiceFatigue { get; } = new();
    public int TickCount { get; private set; }

    // ── Scenario tracking ───────────────────────────────────────────────────

    /// <summary>
    /// The active scenario, if any. Set externally before the first tick.
    /// When null, the engine runs as a sandbox (no goal or medal tracking).
    /// </summary>
    public ScenarioDefinition? ActiveScenario { get; set; }

    /// <summary>True once the scenario goal population has been reached.</summary>
    public bool ScenarioComplete { get; private set; }

    /// <summary>"Gold", "Silver", "Bronze", or null (no medal / not yet complete).</summary>
    public string? MedalEarned { get; private set; }

    /// <summary>True when the tick limit has been exceeded without meeting the goal.</summary>
    public bool ScenarioFailed { get; private set; }

    /// <summary>
    /// Building type IDs demolished during the last tick by BuildingDegradationSystem.
    /// Empty list if nothing degraded.
    /// </summary>
    public List<string> LastDegradedBuildings { get; private set; } = new();

    /// <summary>
    /// Building type IDs created during the last tick by BuildingGrowthSystem (Initialize + TryGrow).
    /// Includes both newly initialised 1×1 bases and upgraded multi-tile buildings.
    /// Empty list if nothing was created.
    /// </summary>
    public List<string> LastNewBuildingTypeIds { get; private set; } = new();

    /// <summary>Result of the most recent WorkerFlowSystem.Route call. Null before first tick.</summary>
    public WorkerFlowResult? LastWorkerFlow { get; private set; }

    /// <summary>
    /// Capacity-aware service coverage snapshot from the most recent tick. Null before first tick.
    /// Computed by HappinessSystem.ComputeServiceCoverage after happiness propagation.
    /// </summary>
    public ServiceCoverageResult? LastServiceCoverage { get; private set; }

    /// <summary>Set each tick when a new event fires; cleared at the start of the next tick.</summary>
    public string? LatestEventBanner { get; private set; }

    // ── Event response helpers (for Godot / Runner) ─────────────────────────────

    /// <summary>True when an event is active and the player has not yet responded.</summary>
    public bool HasPendingEvent => EventSystem.ActiveResponse != null && !EventSystem.ActiveResponse.Responded;

    /// <summary>Type string of the pending event (e.g. "FireBreak"), or null when none.</summary>
    public string? PendingEventType => EventSystem.ActiveResponse?.EventType;

    /// <summary>Intervention cost for the current pending event, or 0 when none.</summary>
    public int PendingEventCost => EventSystem.ActiveResponse?.Cost ?? 0;

    /// <summary>
    /// Processes the player's Intervene response for the current event.
    /// Deducts cost from budget and fast-resolves the event.
    /// Returns true on success, false when no event is pending or funds are insufficient.
    /// </summary>
    public bool RespondToCurrentEvent() => EventSystem.RespondToEvent(Budget);

    private int _lowHappinessTicks = 0;
    private const int LowHappinessLimit = 50;   // give player time to react before abandonment
    private const double AbandonThreshold = 0.25; // lowered from 0.30 — no-service cities shouldn't auto-abandon

    // Tracks the previous milestone state for charter notification detection
    private GameState _previousMilestoneState = GameState.Active;

    /// <summary>
    /// The seed used to initialise all random subsystems (EventSystem, BuildingDegradationSystem).
    /// Stored so callers can save and restore it for deterministic replay.
    /// </summary>
    public int Seed { get; }

    /// <param name="seed">
    /// Optional RNG seed. When null, a seed is derived from <see cref="Environment.TickCount"/>
    /// so different game instances vary while tests that pass an explicit seed are fully deterministic.
    /// </param>
    public SimulationEngine(CityGrid grid, BudgetSystem budget, PopulationSystem population,
        PowerNetwork powerNetwork, RoadNetwork roadNetwork, DemandSystem demandSystem,
        PollutionSystem? pollutionSystem = null, HappinessSystem? happinessSystem = null,
        MilestoneSystem? milestoneSystem = null, EventSystem? eventSystem = null,
        EmploymentSystem? employmentSystem = null, RoadTrafficSystem? roadTrafficSystem = null,
        PowerCapacitySystem? powerCapacitySystem = null,
        int? seed = null)
    {
        // Resolve seed: explicit value or a new one from the system clock (varies per game instance).
        Seed = seed ?? Environment.TickCount;

        Grid = grid;
        Budget = budget;
        Population = population;
        PowerNetwork = powerNetwork;
        PowerCapacitySystem = powerCapacitySystem ?? new PowerCapacitySystem();
        RoadNetwork = roadNetwork;
        RoadTrafficSystem = roadTrafficSystem ?? new RoadTrafficSystem();
        DemandSystem = demandSystem;
        PollutionSystem = pollutionSystem ?? new PollutionSystem();
        HappinessSystem = happinessSystem ?? new HappinessSystem();
        MilestoneSystem = milestoneSystem ?? new MilestoneSystem();
        // Distribute seeds to subsystems that use randomness.
        // Each subsystem gets a unique offset so they are independent.
        EventSystem = eventSystem ?? new EventSystem(new Random(Seed));
        BuildingDegradationSystem = new BuildingDegradationSystem(Seed + 1);
        EmploymentSystem = employmentSystem ?? new EmploymentSystem();
    }

    // ── Scenario zone constraint helpers ───────────────────────────────────────

    /// <summary>
    /// Returns true if the given zone type is allowed under the current active scenario's
    /// DisabledZones constraint.  When no scenario is active, or when DisabledZones is null,
    /// all zones are permitted.
    /// </summary>
    public bool IsZoneAllowed(ZoneType zone)
    {
        if (ActiveScenario?.DisabledZones == null) return true;
        return !ActiveScenario.DisabledZones.Contains(zone);
    }

    // ── Tile placement helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Scan the current grid and add nodes to the RoadGraph for every Road or Avenue tile.
    /// Call this once after constructing an engine with a pre-populated grid (e.g. scenario setup
    /// or loading a saved game) so that road-graph-based coverage is correct from tick 1.
    /// Safe to call multiple times — adding an existing node is a no-op.
    /// </summary>
    public void SeedRoadGraphFromGrid()
    {
        foreach (var tile in Grid.AllTiles())
        {
            switch (tile.Zone)
            {
                case ZoneType.Road:
                    RoadGraph.AddNode(tile.X, tile.Y, 1.0f);
                    if (tile.IsBorderConnection)
                        RoadGraph.SetExternalAnchor(tile.X, tile.Y);
                    break;
                case ZoneType.Avenue:
                    RoadGraph.AddNode(tile.X, tile.Y, 0.5f);
                    if (tile.IsBorderConnection)
                        RoadGraph.SetExternalAnchor(tile.X, tile.Y);
                    break;
            }
        }
    }

    /// <summary>
    /// Place a zone tile and keep the RoadGraph in sync.
    /// Road tiles are added as nodes (weight 1.0) and Avenue tiles as nodes (weight 0.5).
    /// </summary>
    public void PlaceTile(int x, int y, ZoneType zone)
    {
        Grid.SetZone(x, y, zone);

        switch (zone)
        {
            case ZoneType.Road:
                RoadGraph.AddNode(x, y, 1.0f);
                // If this tile is a border connection (placed via PlaceBorderConnection before PlaceTile),
                // tag it as an external anchor in the road graph.
                if (Grid.GetTile(x, y).IsBorderConnection)
                    RoadGraph.SetExternalAnchor(x, y);
                break;
            case ZoneType.Avenue:
                RoadGraph.AddNode(x, y, 0.5f);
                if (Grid.GetTile(x, y).IsBorderConnection)
                    RoadGraph.SetExternalAnchor(x, y);
                break;
        }
    }

    /// <summary>
    /// Erase a tile and keep the RoadGraph in sync.
    /// If the erased tile was Road or Avenue, its node is removed from the graph.
    /// </summary>
    public void EraseTile(int x, int y)
    {
        var tile = Grid.GetTile(x, y);
        if (tile.IsBorderConnection)
            return; // border connections are permanent — cannot be erased

        var oldZone = tile.Zone;
        Grid.SetZone(x, y, ZoneType.Empty);

        if (oldZone == ZoneType.Road || oldZone == ZoneType.Avenue)
            RoadGraph.RemoveNode(x, y);
    }

    public void Tick()
    {
        LatestEventBanner = null;
        PowerNetwork.Propagate(Grid);
        PowerCapacitySystem.Propagate(Grid);       // supply/demand after BFS power is known
        RoadNetwork.Propagate(Grid);
        RoadGraph.ResetEdgeTraffic();              // clear previous tick's worker-flow traffic
        LastWorkerFlow = WorkerFlowSystem.Route(Grid, RoadGraph);  // R→I routing, accumulates edge traffic
        RoadTrafficSystem.Propagate(Grid, RoadGraph);  // real traffic from edge data
        PollutionSystem.Propagate(Grid, PolicySystem.PollutionMultiplier);  // pollution before happiness (GreenCity reduces emission)
        DemandSystem.Propagate(Grid);      // demand before happiness
        var newEvent = EventSystem.Tick(Grid, Population.Population);
        if (newEvent != null) LatestEventBanner = newEvent.Name;

        // Fire damage: demolish the burning tile when FireBreak ends without fire station
        if (EventSystem.FireDamageOccurred && EventSystem.FireTileX >= 0)
        {
            EraseTile(EventSystem.FireTileX, EventSystem.FireTileY);
        }
        HappinessSystem.Propagate(Grid, Budget.TaxModifier, EventSystem.HappinessPenalty, RoadTrafficSystem, PowerCapacitySystem, Population.Population, RoadGraph, PolicySystem.HappinessBonusFromPolicy,
            Charters.EffectiveServiceCoverageRadiusBonus,
            Charters.EffectiveParkHappinessMultiplier,
            pollutionMultiplier: Charters.EffectivePollutionMultiplier,
            parkRadiusBonus: Charters.EffectiveParkRadiusBonus);  // happiness uses pollution + demand + tax modifier + event penalty + traffic + brownout + commute + policy bonus + charter bonuses
        LastServiceCoverage = HappinessSystem.ComputeServiceCoverage(Grid, RoadGraph, ServiceFatigue);  // capacity-aware service coverage snapshot (fatigue-adjusted)
        LandValueSystem.Propagate(Grid, Charters.EffectiveLandValueBonus);  // land value after happiness is computed

        // Track low-happiness ticks for abandonment loss condition
        var avgHappiness = HappinessSystem.AverageHappiness(Grid);
        if (avgHappiness < AbandonThreshold)
            _lowHappinessTicks++;
        else
            _lowHappinessTicks = 0;

        if (_lowHappinessTicks >= LowHappinessLimit)
            MilestoneSystem.Abandon();

        // Recovery: if abandoned but happiness has recovered well above the threshold, clear it
        if (MilestoneSystem.CurrentState == GameState.Abandoned && avgHappiness >= AbandonThreshold + 0.15)
        {
            _lowHappinessTicks = 0;
            MilestoneSystem.RecoverFromAbandonment();
        }

        // Snapshot building IDs before growth so we can detect newly-created buildings this tick.
        var buildingIdsBefore = new HashSet<string>(Grid.Buildings.Keys);

        BuildingGrowthSystem.Initialize(Grid);
        BuildingGrowthSystem.TryGrow(Grid, MilestoneSystem.CurrentState);
        LastDegradedBuildings = BuildingDegradationSystem.Propagate(Grid);

        // Collect type IDs of buildings that appeared this tick (new 1×1 bases + upgrades).
        LastNewBuildingTypeIds = new List<string>();
        foreach (var kvp in Grid.Buildings)
        {
            if (!buildingIdsBefore.Contains(kvp.Key))
                LastNewBuildingTypeIds.Add(kvp.Value.TypeId);
        }
        // Combine policy + charter job bonus (additive stacking)
        var totalJobsBonus = PolicySystem.JobsPerIndustrialTileBonus + Charters.EffectiveJobsPerTileBonus;
        var employmentMultiplier = EmploymentSystem.Propagate(Grid, Population.Population, totalJobsBonus);

        // Combine policy + charter growth multipliers (multiplicative stacking)
        var industrialGrowthMult = PolicySystem.IndustrialGrowthMultiplier * Charters.EffectiveIndustrialGrowthMultiplier;
        var commercialGrowthMult = PolicySystem.CommercialGrowthMultiplier * Charters.EffectiveCommercialGrowthMultiplier;

        Population.Tick(Grid, employmentMultiplier, RoadTrafficSystem, PowerCapacitySystem, RoadGraph,
            industrialGrowthMult, commercialGrowthMult, PolicySystem.ResidentialCapacityBonus + Charters.EffectiveResidentialCapacityBonus);
        Budget.SetPopulation(Population.Population);
        Budget.CollectTaxes(Grid, PolicySystem.TaxRateModifier + Charters.EffectiveTaxRateModifier);  // land-value-weighted residential tax (OpenCity reduces by 12%)
        Budget.CollectCommercialIncome(Grid);
        Budget.DeductMaintenance(Grid);
        PolicySystem.Tick(Budget);  // deduct active policy costs after maintenance
        MilestoneSystem.Check(Population.Population, Budget.Balance, Budget.NetIncomePerTick, TickCount);

        NotifyCharterMilestonesIfNeeded();
        _previousMilestoneState = MilestoneSystem.CurrentState;

        // Service fatigue: decay service building capacity post-City milestone
        ServiceFatigue.Propagate(Grid, MilestoneSystem.CurrentState);

        // Scenario goal / medal check (only when a scenario is active and goal not yet reached)
        if (ActiveScenario != null && !ScenarioComplete)
        {
            var (complete, medal) = ScenarioEngine.CheckCompletion(
                ActiveScenario, Population.Population, TickCount);
            if (complete)
            {
                ScenarioComplete = true;
                MedalEarned      = medal;
            }
            else if (ScenarioEngine.IsFailure(ActiveScenario, Population.Population, TickCount))
            {
                ScenarioFailed = true;
            }
        }

        // Record city statistics snapshot for trend analysis and peak tracking
        var allTiles    = Grid.AllTiles().ToList();
        var zonedTiles  = allTiles.Where(t => t.Zone is ZoneType.Residential or ZoneType.Commercial or ZoneType.Industrial).ToList();
        var poweredCount   = zonedTiles.Count(t => t.HasPower);
        var unpoweredCount = zonedTiles.Count - poweredCount;
        var avgHappinessSnap = (float)HappinessSystem.AverageHappiness(Grid);
        var avgPollutionSnap = (float)PollutionSystem.AveragePollution(Grid);
        Statistics.Record(new CitySnapshot(
            Tick:             TickCount,
            Population:       Population.Population,
            Balance:          Budget.Balance,
            AverageHappiness: avgHappinessSnap,
            PoweredTiles:     poweredCount,
            UnpoweredTiles:   unpoweredCount,
            EmployedResidents: EmploymentSystem.RequiredJobs,
            TotalJobs:        EmploymentSystem.AvailableJobs,
            AveragePollution: avgPollutionSnap
        ));

        // Petition system: citizens file complaints when simulation thresholds are breached.
        // Must run AFTER all other systems (population, happiness, employment, services) have updated.
        PetitionSystem.Tick(Grid, this);

        TickCount++;
    }

    // ── City Advisor ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the single most important advisory hint for the player based on
    /// the current simulation state. Computed fresh on each access — call at most
    /// once per tick (e.g. in PushStandaloneHudUpdate or StateWriter.WriteState).
    /// </summary>
    public AdvisoryMessage CurrentAdvice => CityAdvisor.Advise(BuildAdvisoryState());

    private SimulationState BuildAdvisoryState()
    {
        var allZoned = Grid.AllTiles()
            .Where(t => t.Zone is ZoneType.Residential or ZoneType.Commercial or ZoneType.Industrial)
            .ToList();
        var poweredCount = allZoned.Count(t => t.HasPower);

        // Service coverage ratio: average of the four service types from the last snapshot.
        // Falls back to 0 when no snapshot exists yet (before tick 1).
        float serviceCoverageRatio = 0f;
        if (LastServiceCoverage != null)
        {
            var cov = LastServiceCoverage;
            serviceCoverageRatio = (cov.SchoolCoveragePercent + cov.PoliceCoveragePercent
                                    + cov.FireCoveragePercent + cov.HospitalCoveragePercent) / 4f;
        }

        // Next milestone: find first threshold above current population
        (int threshold, string name) = MilestoneSystem.CurrentState switch
        {
            GameState.Active     => (500,      "Town"),
            GameState.Town       => (5_000,    "City"),
            GameState.City       => (25_000,   "Metropolis"),
            GameState.Metropolis => (100_000,  "Loopolis"),
            _                   => (0,         ""),
        };

        return new SimulationState(
            Population:            Population.Population,
            Tick:                  TickCount,
            Balance:               Budget.Balance,
            IncomePerTick:         Budget.NetIncomePerTick,
            CostPerTick:           Budget.LastMaintenanceCost,
            AverageHappiness:      (float)HappinessSystem.AverageHappiness(Grid),
            DistressedTileCount:   Population.DistressedTileCount,
            EmploymentRatio:       (float)EmploymentSystem.EmploymentRatio,
            PowerSupply:           PowerCapacitySystem.TotalSupplyMW,
            PowerDemand:           PowerCapacitySystem.TotalDemandMW,
            PoweredTiles:          poweredCount,
            TotalActiveTiles:      allZoned.Count,
            ServiceCoverageRatio:  serviceCoverageRatio,
            PopulationGrowthRate:  Statistics.PopulationGrowthRate,
            NextMilestoneThreshold: threshold,
            NextMilestoneName:     name
        );
    }

    // ── Charter milestone notifications ────────────────────────────────────

    private void NotifyCharterMilestonesIfNeeded()
    {
        // When the city just reached Town for the first time, prompt charter selection
        if (_previousMilestoneState != GameState.Town
            && MilestoneSystem.CurrentState == GameState.Town
            && !MilestoneSystem.IsOver)
        {
            Charters.NotifyTownMilestone();
        }

        // When the city just reached City milestone for the first time, prompt city charter selection
        if (_previousMilestoneState is GameState.Active or GameState.Town
            && MilestoneSystem.CurrentState is GameState.City or GameState.Metropolis or GameState.Loopolis
            && !MilestoneSystem.IsOver)
        {
            Charters.NotifyCityMilestone();
        }

        // When the city just reached Metropolis milestone for the first time, prompt metropolis charter selection
        if (_previousMilestoneState == GameState.City
            && MilestoneSystem.CurrentState is GameState.Metropolis or GameState.Loopolis
            && !MilestoneSystem.IsOver)
        {
            Charters.NotifyMetropolisMilestone();
        }
    }

    // ── Service renovation ──────────────────────────────────────────────────

    /// <summary>
    /// Renovate a service tile at (x, y): resets its fatigue to 100% and deducts $500 from budget.
    /// Returns true on success, false if tile is not a tracked service tile or funds are insufficient.
    /// </summary>
    public bool RenovateService(int x, int y) =>
        ServiceFatigue.Renovate(x, y, Budget);

    // ── Manual upgrade ──────────────────────────────────────────────────────

    /// <summary>
    /// Result of a manual upgrade attempt.
    /// </summary>
    public record ManualUpgradeResult(bool Success, string? Reason, string? NewBuildingTypeId, int? Cost);

    /// <summary>
    /// Let the player spend money to force a building tier-up before it reaches 80% capacity.
    /// Looks up the building at (x, y), validates funds and conditions, then delegates to
    /// <see cref="ManualUpgradeSystem.TryUpgrade"/>.
    /// </summary>
    public ManualUpgradeResult ManualUpgrade(int x, int y)
    {
        var tile = Grid.GetTile(x, y);
        if (tile?.BuildingId == null) return new(false, "No building here", null, null);
        if (!Grid.Buildings.TryGetValue(tile.BuildingId, out var rec))
            return new(false, "Building not found", null, null);

        var cost = ManualUpgradeSystem.GetUpgradeCost(rec.TypeId);
        var (success, reason, newTypeId) = ManualUpgradeSystem.TryUpgrade(
            Grid, x, y, Budget, MilestoneSystem);
        return new(success, reason, newTypeId, success ? cost : null);
    }
}
