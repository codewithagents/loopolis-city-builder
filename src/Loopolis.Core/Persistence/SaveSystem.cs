using System.Text.Json;
using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Persistence;

public static class SaveSystem
{
    public const int CurrentVersion = 3;

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

        // Flatten height and forest maps (row-major: index = x + y * width)
        var heightMap  = new int[grid.Width * grid.Height];
        var forestMap  = new bool[grid.Width * grid.Height];
        for (var y = 0; y < grid.Height; y++)
        for (var x = 0; x < grid.Width; x++)
        {
            heightMap[x + y * grid.Width] = grid.GetHeightLevel(x, y);
            forestMap[x + y * grid.Width] = grid.HasForestAt(x, y);
        }

        return new SaveGame(
            Version:    CurrentVersion,
            Tick:       tick,
            Balance:    engine.Budget.Balance,
            TaxLevel:   taxLevel,
            GameState:  engine.MilestoneSystem.CurrentState.ToString(),
            TerrainSeed: terrainSeed,
            Tiles:      tiles,
            Buildings:  buildings,
            HeightMap:  heightMap,
            ForestMap:  forestMap,
            GridWidth:  grid.Width,
            GridHeight: grid.Height
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
    /// Version 1–2 saves: HeightMap absent → default all tiles to height=1 (flat terrain).
    /// Version 3+ saves: HeightMap and ForestMap are applied if present.
    /// </summary>
    public static void RestoreGrid(CityGrid grid, SaveGame save)
    {
        // Restore height map (version 3+). Version 1–2 saves have null HeightMap → flat default.
        if (save.HeightMap != null && save.HeightMap.Length == save.GridWidth * save.GridHeight)
        {
            for (var y = 0; y < save.GridHeight && y < grid.Height; y++)
            for (var x = 0; x < save.GridWidth && x < grid.Width; x++)
            {
                var h = save.HeightMap[x + y * save.GridWidth];
                grid.SetHeightLevel(x, y, h);
            }
            grid.ComputeAverageHeight();
        }
        else
        {
            // Version 1–2 backward compat: default to flat terrain
            grid.SetFlatTerrain();
        }

        // Restore forest map (version 3+)
        if (save.ForestMap != null && save.ForestMap.Length == save.GridWidth * save.GridHeight)
        {
            for (var y = 0; y < save.GridHeight && y < grid.Height; y++)
            for (var x = 0; x < save.GridWidth && x < grid.Width; x++)
            {
                grid.SetForest(x, y, save.ForestMap[x + y * save.GridWidth]);
            }
        }

        foreach (var t in save.Tiles)
        {
            if (!Enum.TryParse<ZoneType>(t.Zone, out var zoneType)) continue;
            if (!grid.IsInBounds(t.X, t.Y)) continue;

            // Respect water blocking — if the terrain is water, skip.
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
