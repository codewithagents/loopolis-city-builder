namespace Loopolis.Core.Buildings;

using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

/// <summary>
/// Allows the player to spend money to force a building tier-up before the 80% auto-upgrade threshold.
///
/// The manual upgrade bypasses the capacity check (80% full) but still enforces:
///   - Milestone unlock (e.g. can't upgrade to highrise without Metropolis milestone)
///   - Service coverage conditions (e.g. apartment → highrise requires all 4 services)
///   - Power requirements (multi-tile buildings need all footprint tiles powered)
///   - Terrain conditions (mill needs forest, quarry needs elevated terrain)
///
/// Cost is charged up-front; if no valid footprint exists the cost is NOT charged.
/// </summary>
public static class ManualUpgradeSystem
{
    /// <summary>
    /// Cost to manually upgrade from a given source building type to the next tier.
    /// Buildings not listed here have no upgrade path and return null from GetUpgradeCost.
    /// </summary>
    private static readonly Dictionary<string, int> UpgradeCosts = new()
    {
        ["res_house_1x1"]      = 600,
        ["res_townhouse_2x2"]  = 1_200,
        ["res_apartment_4x4"]  = 3_500,   // → highrise, most expensive
        ["com_shop_1x1"]       = 500,
        ["com_strip_1x3"]      = 1_000,
        ["com_strip_3x1"]      = 1_000,
        ["com_shopping_3x3"]   = 2_200,
        ["ind_factory_1x1"]    = 700,
        ["ind_warehouse_2x2"]  = 2_000,
        ["ind_mill_2x2"]       = 1_500,
        ["ind_quarry_2x2"]     = 1_800,
    };

    /// <summary>
    /// Returns the cost to manually upgrade this building type, or null if no upgrade is possible.
    /// A null return means the building is already at the maximum tier for its zone.
    /// </summary>
    public static int? GetUpgradeCost(string buildingTypeId) =>
        UpgradeCosts.TryGetValue(buildingTypeId, out var cost) ? cost : null;

    /// <summary>
    /// Attempt to manually upgrade the building at the given tile coordinates.
    ///
    /// Pre-conditions checked:
    ///   - There is a tile at (x, y) with a building.
    ///   - That building has a next tier (is in UpgradeCosts).
    ///   - The player can afford the cost.
    ///
    /// Growth conditions checked (same as auto-upgrade, except 80% capacity is SKIPPED):
    ///   - Milestone unlock.
    ///   - All footprint tiles powered (for upgrades beyond 1×1).
    ///   - Service coverage conditions.
    ///   - Terrain conditions (forest/hill).
    ///
    /// On success: cost is charged to budget, building is replaced with the next tier,
    /// and the new building type ID is returned.
    ///
    /// On failure: budget is unchanged and a human-readable reason string is returned.
    /// </summary>
    public static (bool Success, string? Reason, string? NewBuildingTypeId) TryUpgrade(
        CityGrid grid,
        int x, int y,
        BudgetSystem budget,
        MilestoneSystem milestones)
    {
        // ── Locate the building ──────────────────────────────────────────────

        var tile = grid.GetTile(x, y);
        if (tile == null)
            return (false, "No tile at this location", null);
        if (tile.BuildingId == null)
            return (false, "No building here", null);
        if (!grid.Buildings.TryGetValue(tile.BuildingId, out var building))
            return (false, "Building not found", null);

        var sourceTypeId = building.TypeId;

        // ── Funds and upgrade-path check ─────────────────────────────────────

        var cost = GetUpgradeCost(sourceTypeId);
        if (cost == null)
            return (false, "This building cannot be upgraded further", null);
        if (!budget.CanAfford(cost.Value))
            return (false, $"Not enough funds (need ${cost.Value:N0})", null);

        // ── Find a valid upgrade candidate and anchor ─────────────────────────
        //
        // Mirrors BuildingGrowthSystem.TryUpgrade logic, minus the 80% capacity check.

        var upgradeCandidates = BuildingCatalog.All
            .Where(t => t.Zone == building.Zone && t.TilesCount > building.TileCount)
            .OrderByDescending(t => t.TilesCount);

        foreach (var candidate in upgradeCandidates)
        {
            // Milestone gate
            if (!MilestoneReached(milestones.CurrentState, candidate.MinMilestone))
                continue;

            if (TryFindValidAnchor(grid, building, candidate, out var anchor))
            {
                // Charge and grow
                budget.Charge(cost.Value);
                GrowBuilding(grid, building, candidate, anchor);
                return (true, null, candidate.TypeId);
            }
        }

        return (false, "No valid footprint found for an upgrade (check power, terrain, or service coverage)", null);
    }

    // ── Internal helpers — mirrors BuildingGrowthSystem private methods ───────

    private static bool TryFindValidAnchor(CityGrid grid, Building current, BuildingTypeDefinition target,
        out (int X, int Y) validAnchor)
    {
        for (var ax = current.AnchorX - (target.Width - 1); ax <= current.AnchorX + current.Width - 1; ax++)
        {
            for (var ay = current.AnchorY - (target.Height - 1); ay <= current.AnchorY + current.Height - 1; ay++)
            {
                if (IsValidAnchor(grid, current, target, ax, ay))
                {
                    validAnchor = (ax, ay);
                    return true;
                }
            }
        }
        validAnchor = default;
        return false;
    }

    private static bool IsValidAnchor(CityGrid grid, Building current, BuildingTypeDefinition target, int ax, int ay)
    {
        if (ax < 0 || ay < 0 || ax + target.Width > grid.Width || ay + target.Height > grid.Height)
            return false;

        bool hasRoadAccess = false;
        bool containsCurrentBuilding = false;

        for (var dx = 0; dx < target.Width; dx++)
        {
            for (var dy = 0; dy < target.Height; dy++)
            {
                var tx = ax + dx;
                var ty = ay + dy;
                var tile = grid.GetTile(tx, ty);

                // All tiles must be same zone or already part of current building
                if (tile.Zone != current.Zone) return false;

                // Tile must not belong to a LARGER building (can absorb smaller ones)
                if (tile.BuildingId != null && tile.BuildingId != current.Id)
                {
                    var otherBuilding = grid.Buildings.GetValueOrDefault(tile.BuildingId);
                    if (otherBuilding != null && otherBuilding.TileCount >= target.TilesCount)
                        return false;
                }

                // Power required for all upgrades beyond 1×1
                if (target.TilesCount > 1 && !tile.HasPower)
                    return false;

                if (tile.HasRoadAccess) hasRoadAccess = true;
                if (current.ContainsTile(tx, ty)) containsCurrentBuilding = true;
            }
        }

        if (!hasRoadAccess) return false;
        if (!containsCurrentBuilding) return false;

        return CheckConditions(grid, target, ax, ay);
    }

    private static bool CheckConditions(CityGrid grid, BuildingTypeDefinition target, int ax, int ay)
    {
        foreach (var condition in target.Conditions)
        {
            switch (condition.Type)
            {
                case BuildingConditionType.ForestNearby:
                    if (!HasForestNearby(grid, ax, ay, target.Width, target.Height, condition.Param))
                        return false;
                    break;
                case BuildingConditionType.ServiceCoverage:
                    if (!HasServiceCoverage(grid, ax, ay, target.Width, target.Height, condition.ServiceZone!.Value))
                        return false;
                    break;
                case BuildingConditionType.HillTerrain:
                    if (!HasHillTerrain(grid, ax, ay, target.Width, target.Height))
                        return false;
                    break;
                case BuildingConditionType.MinLandValue:
                    if (!HasMinLandValue(grid, ax, ay, target.Width, target.Height, condition.DoubleParam))
                        return false;
                    break;
            }
        }
        return true;
    }

    private static bool HasForestNearby(CityGrid grid, int ax, int ay, int w, int h, int radius)
    {
        for (var dx = -radius; dx < w + radius; dx++)
        for (var dy = -radius; dy < h + radius; dy++)
        {
            var tx = ax + dx; var ty = ay + dy;
            if (!grid.IsInBounds(tx, ty)) continue;
            if (grid.HasForestAt(tx, ty)) return true;
        }
        return false;
    }

    private static bool HasServiceCoverage(CityGrid grid, int ax, int ay, int w, int h, ZoneType serviceZone)
    {
        for (var dx = 0; dx < w; dx++)
        for (var dy = 0; dy < h; dy++)
        {
            if (HasNearbyService(grid, ax + dx, ay + dy, serviceZone, 10)) return true;
        }
        return false;
    }

    private static bool HasNearbyService(CityGrid grid, int cx, int cy, ZoneType serviceZone, int range)
    {
        for (var dx = -range; dx <= range; dx++)
        for (var dy = -range; dy <= range; dy++)
        {
            var tx = cx + dx; var ty = cy + dy;
            if (!grid.IsInBounds(tx, ty)) continue;
            if (Math.Abs(dx) + Math.Abs(dy) > range) continue;
            if (grid.GetTile(tx, ty).Zone == serviceZone) return true;
        }
        return false;
    }

    private static bool HasHillTerrain(CityGrid grid, int ax, int ay, int w, int h)
    {
        for (var dx = 0; dx < w; dx++)
        for (var dy = 0; dy < h; dy++)
        {
            var tx = ax + dx; var ty = ay + dy;
            if (!grid.IsInBounds(tx, ty)) continue;
            if (grid.GetHeightLevel(tx, ty) >= 2) return true;
        }
        return false;
    }

    private static bool HasMinLandValue(CityGrid grid, int ax, int ay, int w, int h, double minValue)
    {
        for (var dx = 0; dx < w; dx++)
        for (var dy = 0; dy < h; dy++)
        {
            var tx = ax + dx; var ty = ay + dy;
            if (!grid.IsInBounds(tx, ty)) continue;
            if (grid.GetTile(tx, ty).LandValue >= minValue) return true;
        }
        return false;
    }

    private static void GrowBuilding(CityGrid grid, Building current, BuildingTypeDefinition target, (int X, int Y) anchor)
    {
        var newId = Guid.NewGuid().ToString("N")[..8];
        var newBuilding = new Building(newId, target.TypeId, current.Zone,
            anchor.X, anchor.Y, target.Width, target.Height);

        // Absorb all smaller buildings in the new footprint
        var absorbed = new HashSet<string>();
        for (var dx = 0; dx < target.Width; dx++)
        {
            for (var dy = 0; dy < target.Height; dy++)
            {
                var tile = grid.GetTile(anchor.X + dx, anchor.Y + dy);
                if (tile.BuildingId != null && tile.BuildingId != current.Id)
                    absorbed.Add(tile.BuildingId);
            }
        }
        foreach (var absorbedId in absorbed)
            grid.Buildings.Remove(absorbedId);
        grid.Buildings.Remove(current.Id);

        // Register new building and update tile references
        grid.Buildings[newId] = newBuilding;
        foreach (var (tx, ty) in newBuilding.Tiles())
            grid.SetBuildingId(tx, ty, newId);
    }

    private static bool MilestoneReached(GameState current, GameState required) =>
        required == GameState.Active || (int)current >= (int)required;
}
