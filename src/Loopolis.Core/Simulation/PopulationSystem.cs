using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

public class PopulationSystem
{
    private const int ResidentsPerZone = 50;
    private const double GrowthRate    = 0.05;
    private const double DeclineRate   = 0.10; // faster than growth — losing services hurts

    public int Population { get; private set; }

    /// <summary>
    /// Recalculates population each tick.
    ///
    /// Growth:  only from zones that are ready (powered + road access).
    /// Decline: only when current population EXCEEDS capacity (services were lost).
    ///          Zones that were never developed don't cause decline — they just sit empty.
    ///
    /// This means:
    ///   - Inactive zones: no effect on population
    ///   - Losing power or roads: population decays toward the new lower capacity
    ///   - Building more ready zones: population grows toward the higher capacity
    /// </summary>
    public void Tick(CityGrid grid)
    {
        var residentialTiles = grid.TilesOfType(ZoneType.Residential).ToList();
        var readyTiles = residentialTiles.Where(t => t.IsReadyToDevelop).ToList();
        var capacity   = readyTiles.Count * ResidentsPerZone;

        // Grow toward capacity from ready zones, weighted by each zone's demand factor and happiness
        var growth = 0;
        foreach (var tile in readyTiles)
        {
            growth += (int)(GrowthRate * ResidentsPerZone * tile.DemandFactor * tile.Happiness);
        }

        // Decline only when existing population exceeds new capacity (services lost)
        var excess  = Math.Max(0, Population - capacity);
        var decline = (int)(excess * DeclineRate);

        Population = Math.Max(0, Math.Min(capacity, Population + growth - decline));
    }

    public void SetPopulation(int population) =>
        Population = Math.Max(0, population);
}
