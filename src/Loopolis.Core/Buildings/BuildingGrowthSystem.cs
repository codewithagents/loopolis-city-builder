namespace Loopolis.Core.Buildings;

using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

public class BuildingGrowthSystem
{
    private static readonly ZoneType[] GrowableZones =
        [ZoneType.Residential, ZoneType.Commercial, ZoneType.Industrial];

    /// <summary>
    /// Initialize road-adjacent unbuilt zone tiles as 1x1 base buildings.
    /// Call at start of each tick BEFORE PopulationSystem.
    ///
    /// res_house_1x1 (residential base): road access alone is sufficient — no power required.
    /// All other 1x1 bases (com_shop_1x1, ind_factory_1x1): require road access only too,
    /// since commercial/industrial need power to actually generate activity (handled by PopulationSystem).
    /// Power is required for all upgrades (2×2+) — enforced in TryUpgrade.
    /// </summary>
    public void Initialize(CityGrid grid)
    {
        for (var x = 0; x < grid.Width; x++)
        {
            for (var y = 0; y < grid.Height; y++)
            {
                var tile = grid.GetTile(x, y);
                if (tile.BuildingId != null) continue; // already a building
                if (!GrowableZones.Contains(tile.Zone)) continue; // not a zone tile
                if (!tile.HasRoadAccess) continue; // not road-adjacent

                // res_house_1x1 only needs road access (power unlocks full capacity, not existence).
                // com_shop_1x1 and ind_factory_1x1 also only need road access here;
                // power is needed for activity to grow (enforced in PopulationSystem).
                CreateBuilding(grid, x, y, BuildingCatalog.BaseTypeIdFor(tile.Zone));
            }
        }
    }

    /// <summary>
    /// Try to grow buildings that are near capacity.
    /// Call after Initialize() each tick.
    /// </summary>
    public void TryGrow(CityGrid grid, GameState currentMilestone)
    {
        // Sort buildings by area descending (larger buildings get priority in absorption)
        var candidates = grid.Buildings.Values
            .OrderByDescending(b => b.TileCount)
            .ToList();

        foreach (var building in candidates)
        {
            // Skip if building was absorbed in this tick
            if (!grid.Buildings.ContainsKey(building.Id)) continue;

            var typeDef = BuildingCatalog.Find(building.TypeId)!;
            var currentPop = building.Tiles().Sum(t => grid.GetTile(t.X, t.Y).Population);
            var capacityFraction = typeDef.MaxPopulation > 0 ? (double)currentPop / typeDef.MaxPopulation : 0;

            if (capacityFraction < 0.80) continue; // not full enough to grow

            TryUpgrade(grid, building, currentMilestone);
        }
    }

    private void TryUpgrade(CityGrid grid, Building building, GameState currentMilestone)
    {
        var upgradeCandidates = BuildingCatalog.All
            .Where(t => t.Zone == building.Zone && t.TilesCount > building.TileCount)
            .OrderByDescending(t => t.TilesCount);

        foreach (var candidate in upgradeCandidates)
        {
            if (!MilestoneReached(currentMilestone, candidate.MinMilestone)) continue;

            // Try all anchor positions where building's tiles would be contained
            if (TryFindValidAnchor(grid, building, candidate, currentMilestone, out var anchor))
            {
                GrowBuilding(grid, building, candidate, anchor);
                return;
            }
        }
    }

    private bool TryFindValidAnchor(CityGrid grid, Building current, BuildingTypeDefinition target,
        GameState milestone, out (int X, int Y) validAnchor)
    {
        // Try all anchor positions where the target footprint would contain at least one tile of current building
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

    private bool IsValidAnchor(CityGrid grid, Building current, BuildingTypeDefinition target, int ax, int ay)
    {
        // All tiles in the target footprint must be in bounds
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

                // Tile must not belong to a LARGER building
                if (tile.BuildingId != null && tile.BuildingId != current.Id)
                {
                    var otherBuilding = grid.Buildings.GetValueOrDefault(tile.BuildingId);
                    if (otherBuilding != null && otherBuilding.TileCount >= target.TilesCount)
                        return false; // Can't absorb a building same size or larger
                }

                // Power required for all upgrades beyond 1×1.
                // res_house_1x1 can exist without power, but growing to 2×2+ requires all tiles powered.
                if (target.TilesCount > 1 && !tile.HasPower)
                    return false;

                if (tile.HasRoadAccess) hasRoadAccess = true;
                if (current.ContainsTile(tx, ty)) containsCurrentBuilding = true;
            }
        }

        if (!hasRoadAccess) return false;
        if (!containsCurrentBuilding) return false;

        // Check special conditions
        return CheckConditions(grid, target, ax, ay);
    }

    private bool CheckConditions(CityGrid grid, BuildingTypeDefinition target, int ax, int ay)
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

    private static bool HasHillTerrain(CityGrid grid, int ax, int ay, int w, int h)
    {
        // At least one tile in the footprint must be elevated (HeightLevel >= 2)
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
        // At least one tile in the footprint must meet the minimum land value threshold
        for (var dx = 0; dx < w; dx++)
        for (var dy = 0; dy < h; dy++)
        {
            var tx = ax + dx; var ty = ay + dy;
            if (!grid.IsInBounds(tx, ty)) continue;
            if (grid.GetTile(tx, ty).LandValue >= minValue) return true;
        }
        return false;
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
        // Check any tile in footprint is covered by the service
        for (var dx = 0; dx < w; dx++)
        for (var dy = 0; dy < h; dy++)
        {
            // Service coverage is indicated by the HappinessSystem's per-tile coverage flag
            // We check if any nearby service building exists within 10 tiles
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

    private void GrowBuilding(CityGrid grid, Building current, BuildingTypeDefinition target, (int X, int Y) anchor)
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

        // Register new building and update tile IDs
        grid.Buildings[newId] = newBuilding;
        foreach (var (tx, ty) in newBuilding.Tiles())
            grid.SetBuildingId(tx, ty, newId);
    }

    private void CreateBuilding(CityGrid grid, int x, int y, string typeId)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var typeDef = BuildingCatalog.Find(typeId)!;
        var building = new Building(id, typeId, typeDef.Zone, x, y, 1, 1);
        grid.Buildings[id] = building;
        grid.SetBuildingId(x, y, id);
    }

    private static bool MilestoneReached(GameState current, GameState required) =>
        required == GameState.Active || (int)current >= (int)required;

    /// <summary>
    /// Returns the effective population capacity for a tile, taking into account
    /// the power-as-density-unlock rule for res_house_1x1.
    ///
    /// res_house_1x1 unpowered → capacity 25 (cottage-level).
    /// res_house_1x1 powered   → capacity 50 (normal).
    /// All other buildings     → standard formula: tileCount × 50.
    /// </summary>
    public static int GetEffectiveCapacity(string buildingTypeId, bool hasPower)
    {
        if (buildingTypeId == "res_house_1x1")
            return hasPower ? 50 : 25;

        // All other buildings use the standard formula from the catalog
        var type = BuildingCatalog.Find(buildingTypeId);
        return type != null ? type.TilesCount * 50 : 50;
    }
}
