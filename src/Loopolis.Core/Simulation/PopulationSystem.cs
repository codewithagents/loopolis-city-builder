using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

public class PopulationSystem
{
    private const int ResidentsPerZone = 50;
    private const double GrowthRate    = 0.05;
    private const double DeclineRate   = 0.10; // faster than growth — losing services hurts

    /// <summary>Total population: sum of all residential tile populations.</summary>
    public int Population { get; private set; }

    /// <summary>
    /// Grows or declines population per residential tile each tick.
    ///
    /// Growth:  only for tiles that are ready (powered + road access), toward capacity 50.
    ///          Demand boost (DemandFactor) and happiness both act as growth multipliers.
    /// Decline: only for tiles that are no longer ready but have population > 0,
    ///          population decays at DeclineRate per tick toward 0.
    ///
    /// Total Population = sum of all tile populations.
    /// </summary>
    public void Tick(CityGrid grid)
    {
        var totalPopulation = 0;

        foreach (var tile in grid.TilesOfType(ZoneType.Residential))
        {
            var current = grid.GetPopulation(tile.X, tile.Y);

            // Wave-based development: direct road adjacency always works.
            // Interior tiles unlock when a same-zone neighbour has sufficient population
            // (dense buildings whose footprint "reaches" into the block interior).
            bool canDevelop = tile.HasRoadAccess;

            if (!canDevelop && tile.HasPower)
            {
                foreach (var neighbour in grid.AdjacentTiles(tile.X, tile.Y))
                {
                    if (neighbour.Zone == tile.Zone && neighbour.Population >= 25)
                    {
                        canDevelop = true;
                        break;
                    }
                }
            }

            int newPop;
            if (canDevelop && tile.HasPower)
            {
                // Grow toward capacity, modified by demand factor and happiness
                var growthMultiplier = tile.DemandFactor * tile.Happiness;
                var growth = (int)(GrowthRate * ResidentsPerZone * growthMultiplier);
                newPop = Math.Min(ResidentsPerZone, current + growth);
            }
            else if (current > 0)
            {
                // Decline: services lost, population decays toward 0
                var decline = Math.Max(1, (int)(current * DeclineRate));
                newPop = Math.Max(0, current - decline);
            }
            else
            {
                newPop = 0;
            }

            if (newPop != current)
                grid.SetPopulation(tile.X, tile.Y, newPop);

            totalPopulation += newPop;
        }

        Population = totalPopulation;
    }

    public void SetPopulation(int population) =>
        Population = Math.Max(0, population);
}
