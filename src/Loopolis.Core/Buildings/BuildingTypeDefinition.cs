namespace Loopolis.Core.Buildings;

using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

public record BuildingCondition(BuildingConditionType Type, int Param = 0, ZoneType? ServiceZone = null, double DoubleParam = 0.0);

public enum BuildingConditionType { RoadAccess, ForestNearby, ServiceCoverage, HillTerrain, MinLandValue }

public record BuildingTypeDefinition(
    string TypeId,
    ZoneType Zone,
    int Width,
    int Height,
    GameState MinMilestone,
    BuildingCondition[] Conditions,
    double PollutionStrength = 1.0
)
{
    public int TilesCount => Width * Height;
    public int MaxPopulation => TilesCount * 50;
}

public static class BuildingCatalog
{
    // The order matters: BuildingGrowthSystem tries larger types first
    // For each zone, ordered by area descending within milestone groups
    public static readonly BuildingTypeDefinition[] All =
    [
        // Residential
        new("res_apartment_4x4",       ZoneType.Residential, 4, 4, GameState.City, [
            new(BuildingConditionType.ServiceCoverage, ServiceZone: ZoneType.School),
            new(BuildingConditionType.ServiceCoverage, ServiceZone: ZoneType.PoliceStation),
            new(BuildingConditionType.ServiceCoverage, ServiceZone: ZoneType.FireStation),
        ]),
        new("res_villa_hillside_3x3",  ZoneType.Residential, 3, 3, GameState.Town, [
            new(BuildingConditionType.HillTerrain),
            new(BuildingConditionType.MinLandValue, DoubleParam: 0.7),
        ]),
        new("res_villa_2x3",           ZoneType.Residential, 2, 3, GameState.Town, [new(BuildingConditionType.ForestNearby, 3)]),
        new("res_villa_3x2",           ZoneType.Residential, 3, 2, GameState.Town, [new(BuildingConditionType.ForestNearby, 3)]),
        new("res_townhouse_2x2",       ZoneType.Residential, 2, 2, GameState.Active, []),
        new("res_house_1x1",           ZoneType.Residential, 1, 1, GameState.Active, []),

        // Commercial
        new("com_shopping_3x3",   ZoneType.Commercial, 3, 3, GameState.City, []),
        new("com_strip_1x3",      ZoneType.Commercial, 1, 3, GameState.Town, []),
        new("com_strip_3x1",      ZoneType.Commercial, 3, 1, GameState.Town, []),
        new("com_shop_1x1",       ZoneType.Commercial, 1, 1, GameState.Active, []),

        // Industrial
        new("ind_park_4x2",       ZoneType.Industrial, 4, 2, GameState.City,   []),
        new("ind_park_2x4",       ZoneType.Industrial, 2, 4, GameState.City,   []),
        // Terrain-conditional 2×2 upgrades — checked before warehouse so terrain wins when available.
        // ForestNearby(0) = forest tile must exist within the 2×2 footprint itself (radius 0).
        // HillTerrain    = at least one footprint tile has HeightLevel >= 2.
        new("ind_mill_2x2",       ZoneType.Industrial, 2, 2, GameState.Town,
            [new(BuildingConditionType.ForestNearby, 0)],
            PollutionStrength: 0.55),   // timber mill: cleaner than a generic warehouse (0.55 vs 1.0)
        new("ind_quarry_2x2",     ZoneType.Industrial, 2, 2, GameState.Town,
            [new(BuildingConditionType.HillTerrain)],
            PollutionStrength: 1.65),   // quarry: dirty extraction, significantly more pollution than warehouse
        new("ind_warehouse_2x2",  ZoneType.Industrial, 2, 2, GameState.Town,   []),   // default 2×2 (PollutionStrength 1.0)
        new("ind_factory_1x1",    ZoneType.Industrial, 1, 1, GameState.Active,  []),
    ];

    public static BuildingTypeDefinition? Find(string typeId) =>
        All.FirstOrDefault(t => t.TypeId == typeId);

    public static string BaseTypeIdFor(ZoneType zone) => zone switch
    {
        ZoneType.Residential => "res_house_1x1",
        ZoneType.Commercial  => "com_shop_1x1",
        ZoneType.Industrial  => "ind_factory_1x1",
        _ => throw new ArgumentException($"No base building for zone {zone}")
    };

    /// <summary>
    /// Returns the zone type for a given building type ID.
    /// Derived from the type ID prefix: res_* → Residential, com_* → Commercial, ind_* → Industrial.
    /// Used by BuildingDegradationSystem when demolishing back to bare zone tiles.
    /// </summary>
    public static ZoneType GetZoneForBuilding(string typeId)
    {
        if (typeId.StartsWith("res_")) return ZoneType.Residential;
        if (typeId.StartsWith("com_")) return ZoneType.Commercial;
        if (typeId.StartsWith("ind_")) return ZoneType.Industrial;
        var found = Find(typeId);
        if (found != null) return found.Zone;
        throw new ArgumentException($"Cannot determine zone for building type '{typeId}'");
    }
}
