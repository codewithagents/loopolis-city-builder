using System.Text.Json;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Persistence;

public static class SaveSystem
{
    public const int CurrentVersion = 1;

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
            .Select(t => new SavedTile(t.X, t.Y, t.Zone.ToString(), t.Population))
            .ToArray();

        return new SaveGame(
            Version:     CurrentVersion,
            Tick:        tick,
            Balance:     engine.Budget.Balance,
            TaxLevel:    taxLevel,
            GameState:   engine.MilestoneSystem.CurrentState.ToString(),
            TerrainSeed: terrainSeed,
            Tiles:       tiles
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
        }
    }
}
