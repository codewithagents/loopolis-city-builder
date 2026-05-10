using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

public class PopulationSystem
{
    private const int ResidentsPerZone = 50;
    private const double GrowthRate = 0.05;
    private const double DeclineRate = 0.03;

    public int Population { get; private set; }

    /// <summary>
    /// Recalculates population based on current grid state.
    /// Growth happens when residential zones have power and road access.
    /// Decline happens when zones lack power.
    /// </summary>
    public void Tick(CityGrid grid)
    {
        var residentialTiles = grid.TilesOfType(ZoneType.Residential).ToList();
        var poweredResidential = residentialTiles.Count(t => t.HasPower);
        var unpoweredResidential = residentialTiles.Count - poweredResidential;

        var capacity = poweredResidential * ResidentsPerZone;
        var growth = (int)(poweredResidential * GrowthRate * ResidentsPerZone);
        var decline = (int)(unpoweredResidential * DeclineRate * ResidentsPerZone);

        Population = Math.Max(0, Math.Min(capacity, Population + growth - decline));
    }

    public void SetPopulation(int population) =>
        Population = Math.Max(0, population);
}
