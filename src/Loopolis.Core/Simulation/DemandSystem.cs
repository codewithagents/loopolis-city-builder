using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Propagates demand multipliers across the city grid.
///
/// Mechanic:
///   - Residential zones adjacent to ready Commercial zones get DemandFactor = 1.5
///     (jobs nearby → 50% faster growth)
///   - Commercial zones adjacent to ready Industrial zones get DemandFactor = 1.5
///     (industrial supply chain → commercial demand, tracked for future use)
///   - All other zones keep DemandFactor = 1.0 (baseline)
///
/// Only "ready" zones (powered + road access) count as demand sources.
/// Call Propagate() each tick, after PowerNetwork and RoadNetwork have propagated.
/// </summary>
public class DemandSystem
{
    private const double BoostFactor = 1.5;

    public void Propagate(CityGrid grid)
    {
        grid.ClearDemand();

        foreach (var tile in grid.AllTiles())
        {
            if (!tile.IsReadyToDevelop)
                continue;

            switch (tile.Zone)
            {
                case ZoneType.Residential:
                    var hasNearbyCommercial = grid.AdjacentTiles(tile.X, tile.Y)
                        .Any(n => n.Zone == ZoneType.Commercial && n.IsReadyToDevelop);
                    if (hasNearbyCommercial)
                        grid.SetDemand(tile.X, tile.Y, BoostFactor);
                    break;

                case ZoneType.Commercial:
                    var hasNearbyIndustrial = grid.AdjacentTiles(tile.X, tile.Y)
                        .Any(n => n.Zone == ZoneType.Industrial && n.IsReadyToDevelop);
                    if (hasNearbyIndustrial)
                        grid.SetDemand(tile.X, tile.Y, BoostFactor);
                    break;
            }
        }
    }
}
