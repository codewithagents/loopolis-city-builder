using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Orchestrates all simulation systems in the correct tick order.
///
/// Tick order:
///   1. PowerNetwork.Propagate    — mark tiles that have electricity
///   2. RoadNetwork.Propagate     — mark zones with road access
///   3. PollutionSystem.Propagate — industrial zones emit pollution
///   4. DemandSystem.Propagate    — demand depends on which zones are ready (powered + road)
///   5. HappinessSystem.Propagate — happiness uses pollution + demand + services
///   6. Population.Tick           — grow/decline based on ready zones + demand + happiness
///   7. Budget.SetPopulation      — sync population to budget
///   8. Budget.CollectTaxes       — income from current population
///   9. Budget.DeductMaintenance  — costs from current grid
///  10. MilestoneSystem.Check     — check for milestone progression and bankruptcy
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
    public DemandSystem DemandSystem { get; }
    public PollutionSystem PollutionSystem { get; }
    public HappinessSystem HappinessSystem { get; }
    public MilestoneSystem MilestoneSystem { get; }
    public int TickCount { get; private set; }

    public SimulationEngine(CityGrid grid, BudgetSystem budget, PopulationSystem population,
        PowerNetwork powerNetwork, RoadNetwork roadNetwork, DemandSystem demandSystem,
        PollutionSystem? pollutionSystem = null, HappinessSystem? happinessSystem = null,
        MilestoneSystem? milestoneSystem = null)
    {
        Grid = grid;
        Budget = budget;
        Population = population;
        PowerNetwork = powerNetwork;
        RoadNetwork = roadNetwork;
        DemandSystem = demandSystem;
        PollutionSystem = pollutionSystem ?? new PollutionSystem();
        HappinessSystem = happinessSystem ?? new HappinessSystem();
        MilestoneSystem = milestoneSystem ?? new MilestoneSystem();
    }

    public void Tick()
    {
        PowerNetwork.Propagate(Grid);
        RoadNetwork.Propagate(Grid);
        PollutionSystem.Propagate(Grid);  // pollution before happiness
        DemandSystem.Propagate(Grid);     // demand before happiness
        HappinessSystem.Propagate(Grid);  // happiness uses pollution + demand
        Population.Tick(Grid);
        Budget.SetPopulation(Population.Population);
        Budget.CollectTaxes();
        Budget.CollectCommercialIncome(Grid);
        Budget.DeductMaintenance(Grid);
        MilestoneSystem.Check(Population.Population, Budget.Balance, Budget.NetIncomePerTick, TickCount);
        TickCount++;
    }
}
