using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Orchestrates all simulation systems in the correct tick order.
///
/// Tick order:
///   1. PowerNetwork.Propagate    — mark tiles that have electricity
///   2. RoadNetwork.Propagate     — mark zones with road access
///   3. DemandSystem.Propagate    — demand depends on which zones are ready (powered + road)
///   4. Population.Tick           — grow/decline based on ready zones + demand factors
///   5. Budget.SetPopulation      — sync population to budget
///   6. Budget.CollectTaxes       — income from current population
///   7. Budget.DeductMaintenance  — costs from current grid
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
    public int TickCount { get; private set; }

    public SimulationEngine(CityGrid grid, BudgetSystem budget, PopulationSystem population,
        PowerNetwork powerNetwork, RoadNetwork roadNetwork, DemandSystem demandSystem)
    {
        Grid = grid;
        Budget = budget;
        Population = population;
        PowerNetwork = powerNetwork;
        RoadNetwork = roadNetwork;
        DemandSystem = demandSystem;
    }

    public void Tick()
    {
        PowerNetwork.Propagate(Grid);
        RoadNetwork.Propagate(Grid);
        DemandSystem.Propagate(Grid);   // demand depends on powered + road-access zones
        Population.Tick(Grid);
        Budget.SetPopulation(Population.Population);
        Budget.CollectTaxes();
        Budget.DeductMaintenance(Grid);
        TickCount++;
    }
}
