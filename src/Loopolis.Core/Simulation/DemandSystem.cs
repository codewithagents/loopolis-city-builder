using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Propagates demand multipliers across the city grid.
///
/// Mechanic:
///   - Residential zones within Chebyshev distance 3 of a ready Commercial zone get DemandFactor = 1.5
///     (jobs nearby → 50% faster growth; wider radius rewards compact mixed districts)
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

    /// <summary>
    /// Chebyshev radius within which a ready Commercial zone boosts a Residential zone's demand.
    /// At Chebyshev-3 a commercial tile can be up to 3 tiles away in each axis (a 7×7 square).
    /// </summary>
    private const int CommercialBoostRadius = 3;

    public void Propagate(CityGrid grid)
    {
        grid.ClearDemand();

        // Pre-compute: list of all ready Commercial tiles (avoid repeated LINQ per residential tile)
        var readyCommercial = grid.TilesOfType(ZoneType.Commercial)
            .Where(t => t.IsReadyToDevelop)
            .ToList();

        foreach (var tile in grid.AllTiles())
        {
            if (!tile.IsReadyToDevelop)
                continue;

            switch (tile.Zone)
            {
                case ZoneType.Residential:
                    // Boost if any ready Commercial tile is within Chebyshev distance 3
                    var hasNearbyCommercial = readyCommercial.Any(c =>
                        Math.Abs(c.X - tile.X) <= CommercialBoostRadius &&
                        Math.Abs(c.Y - tile.Y) <= CommercialBoostRadius);
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
