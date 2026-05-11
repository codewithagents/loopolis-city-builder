using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

public class PopulationSystem
{
    private const int ResidentsPerZone = 50;
    private const double GrowthRate    = 0.05;
    private const double DeclineRate   = 0.10; // faster than growth — losing services hurts

    // Commercial constants
    private const int ActivityCapacity            = 50;
    private const double CommercialBaseGrowthRate  = 0.04;
    private const double CommercialMinGrowthRate   = 0.005;
    private const double CommercialMaxGrowthRate   = 0.06;
    private const double CommercialDeclineRate     = 0.02;
    private const double CommercialDeclineThreshold = 5.0;

    // Industrial constants
    private const double IndustrialGrowthRate  = 0.025;
    private const double IndustrialDeclineRate = 0.05;

    /// <summary>Total population: sum of all residential tile populations only.
    /// Commercial and Industrial activity is intentionally excluded so that
    /// milestones and HUD counts remain residential-only.</summary>
    public int Population { get; private set; }

    /// <summary>
    /// Grows or declines population per residential tile each tick.
    /// Also grows Commercial activity (demand-driven) and Industrial activity (steady).
    ///
    /// Growth:  only for tiles that are ready (powered + road access), toward capacity 50.
    ///          Demand boost (DemandFactor) and happiness both act as growth multipliers.
    /// Decline: only for tiles that are no longer ready but have population > 0,
    ///          population decays at DeclineRate per tick toward 0.
    ///
    /// Total Population = sum of residential tile populations only.
    /// </summary>
    public void Tick(CityGrid grid, double employmentMultiplier = 1.0, RoadTrafficSystem? trafficSystem = null,
        PowerCapacitySystem? powerCapacitySystem = null)
    {
        var totalPopulation = 0;

        // Brownout growth multiplier: throttles all zone growth when supply < demand
        var brownoutGrowthMultiplier = powerCapacitySystem?.GrowthMultiplier ?? 1.0;

        foreach (var tile in grid.TilesOfType(ZoneType.Residential))
        {
            var current = grid.GetPopulation(tile.X, tile.Y);

            // Building-based development: tile must belong to a building (assigned by BuildingGrowthSystem).
            // Road-adjacent tiles get initialized as buildings; interior tiles don't develop standalone.
            bool canDevelop = tile.BuildingId != null;

            int newPop;
            if (canDevelop && tile.HasPower)
            {
                // Grow toward capacity, modified by demand factor, happiness, employment, traffic, and brownout.
                // Guarantee at least 1 unit of growth only when employment is adequate (≥40%).
                // With severe unemployment (<40%) growth CAN stall — a real signal to build industrial.
                var trafficMultiplier = trafficSystem?.GetGrowthMultiplier(grid, tile.X, tile.Y) ?? 1.0;
                var growthMultiplier = tile.DemandFactor * tile.Happiness * employmentMultiplier * trafficMultiplier * brownoutGrowthMultiplier;
                var rawGrowth = GrowthRate * ResidentsPerZone * growthMultiplier;
                var minGrowth = employmentMultiplier >= 0.4 ? 1 : 0;
                var growth = current < ResidentsPerZone ? Math.Max(minGrowth, (int)rawGrowth) : 0;
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

        // --- Commercial activity growth ---
        foreach (var tile in grid.TilesOfType(ZoneType.Commercial))
        {
            var current = grid.GetPopulation(tile.X, tile.Y);

            // Building-based development: tile must belong to a building (assigned by BuildingGrowthSystem).
            bool canDevelop = tile.BuildingId != null;

            if (tile.HasPower && canDevelop)
            {
                // Sum population of adjacent residential tiles
                double adjacentResidential = 0;
                foreach (var n in grid.AdjacentTiles(tile.X, tile.Y))
                    if (n.Zone == ZoneType.Residential)
                        adjacentResidential += n.Population;

                // Grow faster when more residential nearby, capped at ActivityCapacity
                double commercialGrowthRate = CommercialBaseGrowthRate * (adjacentResidential / 100.0);
                commercialGrowthRate = Math.Clamp(commercialGrowthRate, CommercialMinGrowthRate, CommercialMaxGrowthRate);
                var rawGrowth = commercialGrowthRate * (ActivityCapacity - current);
                // Guarantee at least 1 unit of progress when there is room to grow
                var growth = current < ActivityCapacity ? Math.Max(1, (int)rawGrowth) : 0;
                var newPop = Math.Min(ActivityCapacity, current + growth);

                // Slow decline when no residents nearby
                if (adjacentResidential < CommercialDeclineThreshold && current > 0)
                    newPop = Math.Max(0, (int)(current - CommercialDeclineRate * current));

                if (newPop != current)
                    grid.SetPopulation(tile.X, tile.Y, newPop);
            }
            else if (current > 0)
            {
                // Lost power or road access — activity declines
                var newPop = Math.Max(0, (int)(current - CommercialDeclineRate * current));
                if (newPop != current)
                    grid.SetPopulation(tile.X, tile.Y, newPop);
            }
        }

        // --- Industrial activity growth ---
        foreach (var tile in grid.TilesOfType(ZoneType.Industrial))
        {
            var current = grid.GetPopulation(tile.X, tile.Y);

            // Building-based development: tile must belong to a building (assigned by BuildingGrowthSystem).
            bool canDevelop = tile.BuildingId != null;

            if (tile.HasPower && canDevelop)
            {
                // Fixed slow growth regardless of neighbours
                var rawGrowth = IndustrialGrowthRate * (ActivityCapacity - current);
                // Guarantee at least 1 unit of progress when there is room to grow
                var growth = current < ActivityCapacity ? Math.Max(1, (int)rawGrowth) : 0;
                var newPop = Math.Min(ActivityCapacity, current + growth);
                if (newPop != current)
                    grid.SetPopulation(tile.X, tile.Y, newPop);
            }
            else if (current > 0)
            {
                // Lost power or road — activity declines
                var newPop = Math.Max(0, (int)(current - IndustrialDeclineRate * current));
                if (newPop != current)
                    grid.SetPopulation(tile.X, tile.Y, newPop);
            }
        }
    }

    public void SetPopulation(int population) =>
        Population = Math.Max(0, population);
}
