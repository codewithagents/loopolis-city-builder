using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Constants and demand calculations for the service capacity model (G4).
///
/// Each service building has a finite capacity measured in "units":
///   School        — 200 student-population units (tile.Population per residential tile)
///   PoliceStation — 300 resident-population units
///   FireStation   — 400 building-protection units (1 per developed tile with a BuildingId)
///   Hospital      — 80 bed units (tile.Population per residential tile)
///
/// Coverage is no longer purely binary: a service building covers the closest tiles first
/// (by road-graph distance), draining its capacity, until it runs out. Tiles beyond the
/// capacity limit are uncovered even if within the coverage radius.
///
/// PoliceHQ and FireHQ use the same capacity as their base type (same building, higher radius).
/// </summary>
public static class ServiceCapacityModel
{
    /// <summary>
    /// How many "units" each service building type can handle per tick.
    /// PoliceHQ / FireHQ inherit their base type's capacity.
    /// </summary>
    public static readonly Dictionary<ZoneType, int> Capacity = new()
    {
        [ZoneType.School]        = 200,  // 200 student-population units
        [ZoneType.PoliceStation] = 300,  // 300 resident-population units
        [ZoneType.FireStation]   = 400,  // 400 buildings (any developed tile with BuildingId)
        [ZoneType.Hospital]      = 80,   // 80 beds (population units)
        [ZoneType.PoliceHQ]      = 300,  // same capacity as PoliceStation
        [ZoneType.FireHQ]        = 400,  // same capacity as FireStation
    };

    /// <summary>
    /// Returns the number of capacity units a single covered tile consumes from a service building.
    ///
    /// FireStation / FireHQ: consume 1 unit per developed tile (any zone with a BuildingId).
    /// All other services: consume tile.Population (residential population count).
    ///
    /// A tile with Population=0 (not yet grown) still counts as 1 unit for fire protection
    /// if it has a building — the building itself is the asset being protected, not its residents.
    /// </summary>
    public static int GetDemandPerTile(ZoneType serviceType, Tile coveredTile)
    {
        return serviceType is ZoneType.FireStation or ZoneType.FireHQ
            ? (coveredTile.BuildingId != null ? 1 : 0)
            : coveredTile.Population;
    }

    /// <summary>Maximum number of tiles processed per service building per tick.</summary>
    public const int MaxTilesPerBuilding = 50;
}
