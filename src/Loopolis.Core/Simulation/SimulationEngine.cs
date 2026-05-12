using Loopolis.Core.Buildings;
using Loopolis.Core.Graph;
using Loopolis.Core.Grid;

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
///  16. MilestoneSystem.Check          — check for milestone progression and bankruptcy
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
    public BuildingDegradationSystem BuildingDegradationSystem { get; } = new();
    public LandValueSystem LandValueSystem { get; } = new();
    public RoadGraph RoadGraph { get; } = new();
    public WorkerFlowSystem WorkerFlowSystem { get; } = new();
    public int TickCount { get; private set; }

    /// <summary>
    /// Building type IDs demolished during the last tick by BuildingDegradationSystem.
    /// Empty list if nothing degraded.
    /// </summary>
    public List<string> LastDegradedBuildings { get; private set; } = new();

    /// <summary>Result of the most recent WorkerFlowSystem.Route call. Null before first tick.</summary>
    public WorkerFlowResult? LastWorkerFlow { get; private set; }

    /// <summary>
    /// Capacity-aware service coverage snapshot from the most recent tick. Null before first tick.
    /// Computed by HappinessSystem.ComputeServiceCoverage after happiness propagation.
    /// </summary>
    public ServiceCoverageResult? LastServiceCoverage { get; private set; }

    /// <summary>Set each tick when a new event fires; cleared at the start of the next tick.</summary>
    public string? LatestEventBanner { get; private set; }

    private int _lowHappinessTicks = 0;
    private const int LowHappinessLimit = 50;   // give player time to react before abandonment
    private const double AbandonThreshold = 0.25; // lowered from 0.30 — no-service cities shouldn't auto-abandon

    public SimulationEngine(CityGrid grid, BudgetSystem budget, PopulationSystem population,
        PowerNetwork powerNetwork, RoadNetwork roadNetwork, DemandSystem demandSystem,
        PollutionSystem? pollutionSystem = null, HappinessSystem? happinessSystem = null,
        MilestoneSystem? milestoneSystem = null, EventSystem? eventSystem = null,
        EmploymentSystem? employmentSystem = null, RoadTrafficSystem? roadTrafficSystem = null,
        PowerCapacitySystem? powerCapacitySystem = null)
    {
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
        EventSystem = eventSystem ?? new EventSystem();
        EmploymentSystem = employmentSystem ?? new EmploymentSystem();
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
        PollutionSystem.Propagate(Grid);           // pollution before happiness
        DemandSystem.Propagate(Grid);      // demand before happiness
        var newEvent = EventSystem.Tick(Grid, Population.Population);
        if (newEvent != null) LatestEventBanner = newEvent.Name;
        HappinessSystem.Propagate(Grid, Budget.TaxModifier, EventSystem.HappinessPenalty, RoadTrafficSystem, PowerCapacitySystem, Population.Population, RoadGraph);  // happiness uses pollution + demand + tax modifier + event penalty + traffic + brownout + commute (road-graph distance)
        LastServiceCoverage = HappinessSystem.ComputeServiceCoverage(Grid, RoadGraph);  // capacity-aware service coverage snapshot
        LandValueSystem.Propagate(Grid);   // land value after happiness is computed

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

        BuildingGrowthSystem.Initialize(Grid);
        BuildingGrowthSystem.TryGrow(Grid, MilestoneSystem.CurrentState);
        LastDegradedBuildings = BuildingDegradationSystem.Propagate(Grid);
        var employmentMultiplier = EmploymentSystem.Propagate(Grid, Population.Population);
        Population.Tick(Grid, employmentMultiplier, RoadTrafficSystem, PowerCapacitySystem, RoadGraph);
        Budget.SetPopulation(Population.Population);
        Budget.CollectTaxes(Grid);  // land-value-weighted residential tax
        Budget.CollectCommercialIncome(Grid);
        Budget.DeductMaintenance(Grid);
        MilestoneSystem.Check(Population.Population, Budget.Balance, Budget.NetIncomePerTick, TickCount);
        TickCount++;
    }
}
