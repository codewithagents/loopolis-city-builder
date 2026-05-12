using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Degrades buildings that no longer meet their build requirements.
///
/// Each tick, for every multi-tile building (size > 1×1):
///   - Check if the building's requirements are still met (power + road access).
///   - If requirements are NOT met: 2% chance per tick to demolish back to bare zone tiles.
///
/// This makes city decline visible and gradual — cut power to a district and
/// townhouses slowly crumble back to cottages over ~50 ticks.
///
/// Single-tile buildings (res_house_1x1, com_shop_1x1, ind_factory_1x1) are exempt:
/// they are managed by BuildingGrowthSystem and PopulationSystem directly.
///
/// Wire order: run AFTER BuildingGrowthSystem.TryGrow — buildings grow first, then check decay.
/// </summary>
public class BuildingDegradationSystem
{
    private const double DegradationChance = 0.02; // 2% per tick

    private readonly Random _rng;

    public BuildingDegradationSystem(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Check each multi-tile building. If it no longer meets its requirements,
    /// apply a 2% chance per tick to demolish it back to bare zone tiles.
    /// Returns the list of building type IDs that were demolished this tick (for event logging / HUD).
    /// </summary>
    public List<string> Propagate(CityGrid grid)
    {
        var demolished = new List<string>();

        // Snapshot building list to avoid modifying collection while iterating
        var buildings = grid.Buildings.Values.ToList();

        foreach (var building in buildings)
        {
            // Only multi-tile buildings degrade (1×1 bases are managed by other systems)
            if (building.TileCount <= 1) continue;

            // Skip if this building was already removed in this tick (absorbed or demolished)
            if (!grid.Buildings.ContainsKey(building.Id)) continue;

            if (!MeetsRequirements(grid, building))
            {
                if (_rng.NextDouble() < DegradationChance)
                {
                    DemolishBuilding(grid, building);
                    demolished.Add(building.TypeId);
                }
            }
        }

        return demolished;
    }

    /// <summary>
    /// Checks whether a building still meets its operational requirements:
    ///   - Power: all tiles in the building must have HasPower (res_house_1x1 exempt, but those are 1×1 and skipped).
    ///   - Road access: at least one tile must have HasRoadAccess.
    /// </summary>
    private static bool MeetsRequirements(CityGrid grid, Building building)
    {
        bool hasRoadAccess = false;

        foreach (var (tx, ty) in building.Tiles())
        {
            if (!grid.IsInBounds(tx, ty)) return false;

            var tile = grid.GetTile(tx, ty);

            // All tiles must have power (multi-tile buildings always require power)
            if (!tile.HasPower) return false;

            if (tile.HasRoadAccess) hasRoadAccess = true;
        }

        return hasRoadAccess;
    }

    /// <summary>
    /// Demolishes a building: removes it from the grid's building registry,
    /// clears BuildingId from all tiles, resets population to 0,
    /// and leaves the zone type intact (tiles remain R/C/I, just unbuilt).
    /// </summary>
    private static void DemolishBuilding(CityGrid grid, Building building)
    {
        grid.Buildings.Remove(building.Id);

        foreach (var (tx, ty) in building.Tiles())
        {
            if (!grid.IsInBounds(tx, ty)) continue;
            grid.SetBuildingId(tx, ty, null);
            grid.SetPopulation(tx, ty, 0);
        }
    }
}
