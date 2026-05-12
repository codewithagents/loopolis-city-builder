using Loopolis.Core.Buildings;
using Loopolis.Core.Graph;
using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

public class PopulationSystem
{
    private const int ResidentsPerZone = 50;
    private const double GrowthRate    = 0.05;
    private const double DeclineRate   = 0.10; // faster than growth — losing services hurts
    private const double BorderMigrationMultiplier = 1.2;  // R-tiles reachable from border connection get +20% growth
    private const float  BorderMigrationMaxDistance = 12.0f; // road-graph distance threshold

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
        PowerCapacitySystem? powerCapacitySystem = null, RoadGraph? roadGraph = null)
    {
        var totalPopulation = 0;

        // Brownout growth multiplier: throttles all zone growth when supply < demand
        var brownoutGrowthMultiplier = powerCapacitySystem?.GrowthMultiplier ?? 1.0;

        // Pre-compute Dijkstra distances from each external anchor (border connection) once per tick.
        // Only computed when the road graph has at least one external anchor, to avoid unnecessary work.
        List<Dictionary<(int x, int y), float>>? anchorDistanceMaps = null;
        if (roadGraph != null && roadGraph.ExternalAnchors.Count > 0)
        {
            anchorDistanceMaps = new List<Dictionary<(int x, int y), float>>();
            foreach (var anchor in roadGraph.ExternalAnchors)
                anchorDistanceMaps.Add(roadGraph.ShortestPathSourceMap(anchor.x, anchor.y));
        }

        foreach (var tile in grid.TilesOfType(ZoneType.Residential))
        {
            var current = grid.GetPopulation(tile.X, tile.Y);

            // Building-based development: tile must belong to a building (assigned by BuildingGrowthSystem).
            // Road-adjacent tiles get initialized as buildings; interior tiles don't develop standalone.
            bool canDevelop = tile.BuildingId != null;

            // Resolve the building type for this tile (if any)
            var buildingTypeId = tile.BuildingId != null && grid.Buildings.TryGetValue(tile.BuildingId, out var bldgRecord)
                ? bldgRecord.TypeId
                : tile.BuildingId; // fallback: tile.BuildingId itself (e.g. "test" in unit tests)

            // Effective capacity: res_house_1x1 without power caps at 25 (cottage-level).
            // All other residential buildings have standard capacity (power required to form them at all).
            var tileCapacity = canDevelop
                ? BuildingGrowthSystem.GetEffectiveCapacity(buildingTypeId ?? "res_house_1x1", tile.HasPower)
                : ResidentsPerZone;

            // res_house_1x1 can grow without power (road access is sufficient to form the building).
            // All other residential buildings require power to grow (they could never form without it).
            bool isCottage = buildingTypeId == "res_house_1x1";
            bool canGrow = canDevelop && (tile.HasPower || isCottage);

            int newPop;
            if (canGrow)
            {
                // Grow toward effective capacity, modified by demand factor, happiness, employment, traffic, and brownout.
                // Guarantee at least 1 unit of growth only when employment is adequate (≥40%).
                // With severe unemployment (<40%) growth CAN stall — a real signal to build industrial.
                var trafficMultiplier = trafficSystem?.GetGrowthMultiplier(grid, tile.X, tile.Y) ?? 1.0;

                // Border migration multiplier: +20% growth for R-tiles within road-graph distance 12
                // of any external anchor (border connection tile). Simulates migration pressure.
                var borderMultiplier = 1.0;
                if (anchorDistanceMaps != null && roadGraph != null)
                {
                    // Find the tile's nearest road neighbour entry node
                    var tileNode = FindRoadNeighbour(grid, roadGraph, tile.X, tile.Y);
                    if (tileNode.HasValue)
                    {
                        foreach (var distMap in anchorDistanceMaps)
                        {
                            if (distMap.TryGetValue(tileNode.Value, out var dist) && dist <= BorderMigrationMaxDistance)
                            {
                                borderMultiplier = BorderMigrationMultiplier;
                                break;
                            }
                        }
                    }
                }

                var growthMultiplier = tile.DemandFactor * tile.Happiness * employmentMultiplier * trafficMultiplier * brownoutGrowthMultiplier * borderMultiplier;
                var rawGrowth = GrowthRate * tileCapacity * growthMultiplier;
                var minGrowth = employmentMultiplier >= 0.4 ? 1 : 0;
                var growth = current < tileCapacity ? Math.Max(minGrowth, (int)rawGrowth) : 0;
                newPop = Math.Min(tileCapacity, current + growth);
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

    /// <summary>
    /// Returns the road-graph entry node for a given tile: the tile itself if it is a road node,
    /// otherwise the first cardinal neighbour that is a road node. Returns null if none found.
    /// </summary>
    private static (int x, int y)? FindRoadNeighbour(CityGrid grid, RoadGraph roadGraph, int x, int y)
    {
        if (roadGraph.IsRoadNode(x, y)) return (x, y);

        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { -1, 1, 0, 0 };
        for (var i = 0; i < 4; i++)
        {
            var nx = x + dx[i];
            var ny = y + dy[i];
            if (!grid.IsInBounds(nx, ny)) continue;
            if (roadGraph.IsRoadNode(nx, ny)) return (nx, ny);
        }
        return null;
    }
}
