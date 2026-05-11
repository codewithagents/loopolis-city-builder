using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Orchestrates all simulation systems in the correct tick order.
///
/// Tick order:
///   1. PowerNetwork.Propagate      — mark tiles that have electricity
///   2. RoadNetwork.Propagate       — mark zones with road access
///   3. RoadTrafficSystem.Propagate — compute traffic load + overload flags per road/avenue tile
///   4. PollutionSystem.Propagate   — industrial zones emit pollution
///   5. DemandSystem.Propagate      — demand depends on which zones are ready (powered + road)
///   6. HappinessSystem.Propagate   — happiness uses pollution + demand + services + traffic penalty
///   7. Population.Tick             — grow/decline based on ready zones + demand + happiness + traffic
///   8. Budget.SetPopulation        — sync population to budget
///   9. Budget.CollectTaxes         — income from current population
///  10. Budget.DeductMaintenance    — costs from current grid
///  11. MilestoneSystem.Check       — check for milestone progression and bankruptcy
///
/// This class has no Godot dependencies and is safe to use from tests, Runner, and Godot.
/// </summary>
public class SimulationEngine
{
    public CityGrid Grid { get; }
    public BudgetSystem Budget { get; }
    public PopulationSystem Population { get; }
    public PowerNetwork PowerNetwork { get; }
    public RoadNetwork RoadNetwork { get; }
    public RoadTrafficSystem RoadTrafficSystem { get; }
    public DemandSystem DemandSystem { get; }
    public PollutionSystem PollutionSystem { get; }
    public HappinessSystem HappinessSystem { get; }
    public MilestoneSystem MilestoneSystem { get; }
    public EventSystem EventSystem { get; }
    public EmploymentSystem EmploymentSystem { get; }
    public BuildingGrowthSystem BuildingGrowthSystem { get; } = new();
    public int TickCount { get; private set; }

    /// <summary>Set each tick when a new event fires; cleared at the start of the next tick.</summary>
    public string? LatestEventBanner { get; private set; }

    private int _lowHappinessTicks = 0;
    private const int LowHappinessLimit = 30;
    private const double AbandonThreshold = 0.30;

    public SimulationEngine(CityGrid grid, BudgetSystem budget, PopulationSystem population,
        PowerNetwork powerNetwork, RoadNetwork roadNetwork, DemandSystem demandSystem,
        PollutionSystem? pollutionSystem = null, HappinessSystem? happinessSystem = null,
        MilestoneSystem? milestoneSystem = null, EventSystem? eventSystem = null,
        EmploymentSystem? employmentSystem = null, RoadTrafficSystem? roadTrafficSystem = null)
    {
        Grid = grid;
        Budget = budget;
        Population = population;
        PowerNetwork = powerNetwork;
        RoadNetwork = roadNetwork;
        RoadTrafficSystem = roadTrafficSystem ?? new RoadTrafficSystem();
        DemandSystem = demandSystem;
        PollutionSystem = pollutionSystem ?? new PollutionSystem();
        HappinessSystem = happinessSystem ?? new HappinessSystem();
        MilestoneSystem = milestoneSystem ?? new MilestoneSystem();
        EventSystem = eventSystem ?? new EventSystem();
        EmploymentSystem = employmentSystem ?? new EmploymentSystem();
    }

    public void Tick()
    {
        LatestEventBanner = null;
        PowerNetwork.Propagate(Grid);
        RoadNetwork.Propagate(Grid);
        RoadTrafficSystem.Propagate(Grid); // traffic load after road access is known
        PollutionSystem.Propagate(Grid);   // pollution before happiness
        DemandSystem.Propagate(Grid);      // demand before happiness
        var newEvent = EventSystem.Tick(Grid, Population.Population);
        if (newEvent != null) LatestEventBanner = newEvent.Name;
        HappinessSystem.Propagate(Grid, Budget.TaxModifier, EventSystem.HappinessPenalty, RoadTrafficSystem);  // happiness uses pollution + demand + tax modifier + event penalty + traffic

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
        var employmentMultiplier = EmploymentSystem.Propagate(Grid, Population.Population);
        Population.Tick(Grid, employmentMultiplier, RoadTrafficSystem);
        Budget.SetPopulation(Population.Population);
        Budget.CollectTaxes();
        Budget.CollectCommercialIncome(Grid);
        Budget.DeductMaintenance(Grid);
        MilestoneSystem.Check(Population.Population, Budget.Balance, Budget.NetIncomePerTick, TickCount);
        TickCount++;
    }
}
