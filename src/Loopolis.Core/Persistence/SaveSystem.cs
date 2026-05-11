using System.Text.Json;
using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Persistence;

public static class SaveSystem
{
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,  // compact saves
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Build a SaveGame snapshot from the current engine + grid state.</summary>
    public static SaveGame Capture(SimulationEngine engine, CityGrid grid, int terrainSeed, string taxLevel, int tick)
    {
        var tiles = grid.AllTiles()
            .Where(t => t.Zone != ZoneType.Empty)
            .Select(t => new SavedTile(t.X, t.Y, t.Zone.ToString(), t.Population, t.BuildingId))
            .ToArray();

        var buildings = grid.Buildings.Values
            .Select(b => new SavedBuilding(b.Id, b.TypeId, b.Zone.ToString(), b.AnchorX, b.AnchorY, b.Width, b.Height))
            .ToArray();

        return new SaveGame(
            Version:     CurrentVersion,
            Tick:        tick,
            Balance:     engine.Budget.Balance,
            TaxLevel:    taxLevel,
            GameState:   engine.MilestoneSystem.CurrentState.ToString(),
            TerrainSeed: terrainSeed,
            Tiles:       tiles,
            Buildings:   buildings
        );
    }

    /// <summary>Serialize a save to a JSON string.</summary>
    public static string Serialize(SaveGame save) =>
        JsonSerializer.Serialize(save, Options);

    /// <summary>Deserialize a save. Returns null if parsing fails.</summary>
    public static SaveGame? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<SaveGame>(json, Options); }
        catch { return null; }
    }

    /// <summary>
    /// Restore zone placements and per-tile population into an empty grid.
    /// Call AFTER terrain generation (terrain is generated separately from the seed).
    /// Does NOT set HasPower, HasRoadAccess etc — those are recalculated on first Tick().
    /// Version 1 saves: buildings will re-initialize from BuildingGrowthSystem on first tick.
    /// Version 2+ saves: building entities are restored directly.
    /// </summary>
    public static void RestoreGrid(CityGrid grid, SaveGame save)
    {
        foreach (var t in save.Tiles)
        {
            if (!Enum.TryParse<ZoneType>(t.Zone, out var zoneType)) continue;
            if (!grid.IsInBounds(t.X, t.Y)) continue;

            // Terrain may block placement (water tiles). Use the direct tile approach to bypass
            // the water check only for non-Empty zones that the original game placed.
            // Actually: respect water blocking — if the terrain is water, skip. This handles
            // edge cases where terrain generation slight variance might put water where a tile was.
            grid.SetZone(t.X, t.Y, zoneType);

            if (t.Population > 0)
                grid.SetPopulation(t.X, t.Y, t.Population);

            if (t.BuildingId != null)
                grid.SetBuildingId(t.X, t.Y, t.BuildingId);
        }

        // Restore building entities (version 2+). Version 1 saves have null Buildings
        // — BuildingGrowthSystem will re-create them on the first tick.
        if (save.Buildings != null)
        {
            foreach (var b in save.Buildings)
            {
                if (!Enum.TryParse<ZoneType>(b.Zone, out var zone)) continue;
                var building = new Building(b.Id, b.TypeId, zone, b.AnchorX, b.AnchorY, b.Width, b.Height);
                grid.Buildings[b.Id] = building;
            }
        }
    }
}
