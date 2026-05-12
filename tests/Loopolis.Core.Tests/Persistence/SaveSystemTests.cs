using Loopolis.Core.Grid;
using Loopolis.Core.Persistence;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Persistence;

[TestFixture]
public class SaveSystemTests
{
    private static SimulationEngine MakeEngine(CityGrid grid)
    {
        var budget   = new BudgetSystem(initialBalance: 5_000);
        var pop      = new PopulationSystem();
        var power    = new PowerNetwork();
        var roads    = new RoadNetwork();
        var demand   = new DemandSystem();
        return new SimulationEngine(grid, budget, pop, power, roads, demand);
    }

    [Test]
    public void Capture_SerializesAllNonEmptyTiles()
    {
        var grid = new CityGrid(32, 32);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(6, 5, ZoneType.Residential);
        grid.SetZone(7, 5, ZoneType.Commercial);
        var engine = MakeEngine(grid);

        var save = SaveSystem.Capture(engine, grid, terrainSeed: 42, taxLevel: "normal", tick: 10);

        Assert.That(save.Tiles, Has.Length.EqualTo(3));
    }

    [Test]
    public void Serialize_Deserialize_RoundTrip()
    {
        var grid = new CityGrid(32, 32);
        grid.SetZone(10, 10, ZoneType.Road);
        var engine = MakeEngine(grid);

        var save   = SaveSystem.Capture(engine, grid, terrainSeed: 999, taxLevel: "high", tick: 77);
        var json   = SaveSystem.Serialize(save);
        var loaded = SaveSystem.Deserialize(json);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Balance,     Is.EqualTo(save.Balance).Within(0.001));
        Assert.That(loaded.Tick,         Is.EqualTo(77));
        Assert.That(loaded.TerrainSeed,  Is.EqualTo(999));
        Assert.That(loaded.TaxLevel,     Is.EqualTo("high"));
        Assert.That(loaded.Version,      Is.EqualTo(SaveSystem.CurrentVersion));
    }

    [Test]
    public void RestoreGrid_RecreatesZones()
    {
        var original = new CityGrid(32, 32);
        original.SetZone(3, 7, ZoneType.Road);
        original.SetZone(4, 7, ZoneType.Residential);
        original.SetZone(5, 7, ZoneType.PowerPlant);
        var engine = MakeEngine(original);

        var save = SaveSystem.Capture(engine, original, terrainSeed: 1, taxLevel: "normal", tick: 0);
        var json = SaveSystem.Serialize(save);
        var loaded = SaveSystem.Deserialize(json)!;

        var restored = new CityGrid(32, 32);
        SaveSystem.RestoreGrid(restored, loaded);

        Assert.That(restored.GetTile(3, 7).Zone, Is.EqualTo(ZoneType.Road));
        Assert.That(restored.GetTile(4, 7).Zone, Is.EqualTo(ZoneType.Residential));
        Assert.That(restored.GetTile(5, 7).Zone, Is.EqualTo(ZoneType.PowerPlant));
    }

    [Test]
    public void RestoreGrid_RecreatesPopulation()
    {
        var original = new CityGrid(32, 32);
        original.SetZone(8, 8, ZoneType.Residential);
        original.SetPopulation(8, 8, 42);
        var engine = MakeEngine(original);

        var save   = SaveSystem.Capture(engine, original, terrainSeed: 2, taxLevel: "low", tick: 50);
        var json   = SaveSystem.Serialize(save);
        var loaded = SaveSystem.Deserialize(json)!;

        var restored = new CityGrid(32, 32);
        SaveSystem.RestoreGrid(restored, loaded);

        Assert.That(restored.GetTile(8, 8).Population, Is.EqualTo(42));
    }

    [Test]
    public void Deserialize_InvalidJson_ReturnsNull()
    {
        var result = SaveSystem.Deserialize("this is not json at all }{]");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Capture_PopulationIncluded()
    {
        var grid = new CityGrid(32, 32);
        grid.SetZone(15, 15, ZoneType.Residential);
        grid.SetPopulation(15, 15, 25);
        var engine = MakeEngine(grid);

        var save = SaveSystem.Capture(engine, grid, terrainSeed: 7, taxLevel: "normal", tick: 5);

        var tile = save.Tiles.Single(t => t.X == 15 && t.Y == 15);
        Assert.That(tile.Population, Is.EqualTo(25));
    }

    [Test]
    public void Capture_IncludesHeightMapAndForestMap()
    {
        var grid = new CityGrid(32, 32);
        grid.SetHeightLevel(5, 5, 3);
        grid.SetForest(10, 10, true);
        var engine = MakeEngine(grid);

        var save = SaveSystem.Capture(engine, grid, terrainSeed: 42, taxLevel: "normal", tick: 0);

        Assert.That(save.HeightMap, Is.Not.Null, "v3 save should include HeightMap.");
        Assert.That(save.ForestMap, Is.Not.Null, "v3 save should include ForestMap.");
        Assert.That(save.HeightMap!.Length, Is.EqualTo(32 * 32));
        Assert.That(save.HeightMap[5 + 5 * 32], Is.EqualTo(3), "HeightMap should capture SetHeightLevel(5,5,3).");
        Assert.That(save.ForestMap![10 + 10 * 32], Is.True, "ForestMap should capture SetForest(10,10,true).");
    }

    [Test]
    public void RestoreGrid_HeightMapAbsent_DefaultsToFlat()
    {
        // Simulate a v1/v2 save with no HeightMap
        var v2Save = new SaveGame(
            Version:    2,
            Tick:       0,
            Balance:    5_000,
            TaxLevel:   "normal",
            GameState:  "Active",
            TerrainSeed: 0,
            Tiles:      Array.Empty<SavedTile>(),
            Buildings:  null,
            HeightMap:  null, // absent — v2 save
            ForestMap:  null
        );

        var grid = new CityGrid(32, 32);
        // Set some non-default heights to verify they get reset
        grid.SetHeightLevel(5, 5, 4);
        grid.SetForest(3, 3, true);

        SaveSystem.RestoreGrid(grid, v2Save);

        // All tiles should now be height=1 (flat), no forest
        Assert.That(grid.GetHeightLevel(5, 5), Is.EqualTo(1),
            "v2 save without HeightMap should default to height=1 everywhere.");
        Assert.That(grid.HasForestAt(3, 3), Is.False,
            "v2 save without ForestMap should clear all forests.");
    }

    [Test]
    public void RestoreGrid_HeightMapPresent_RestoredCorrectly()
    {
        var grid = new CityGrid(32, 32);
        grid.SetHeightLevel(8, 8, 5);
        grid.SetForest(12, 7, true);
        var engine = MakeEngine(grid);

        var save    = SaveSystem.Capture(engine, grid, terrainSeed: 1, taxLevel: "normal", tick: 0);
        var json    = SaveSystem.Serialize(save);
        var loaded  = SaveSystem.Deserialize(json)!;

        var restored = new CityGrid(32, 32);
        SaveSystem.RestoreGrid(restored, loaded);

        Assert.That(restored.GetHeightLevel(8, 8), Is.EqualTo(5),
            "RestoreGrid should restore non-default height levels.");
        Assert.That(restored.HasForestAt(12, 7), Is.True,
            "RestoreGrid should restore forest flags.");
    }

    [Test]
    public void Serialize_Deserialize_HeightMap_RoundTrip()
    {
        var grid = new CityGrid(32, 32);
        grid.SetHeightLevel(0, 0, 0); // water corner
        grid.SetHeightLevel(31, 31, 7); // peak corner
        var engine = MakeEngine(grid);

        var save   = SaveSystem.Capture(engine, grid, terrainSeed: 5, taxLevel: "normal", tick: 3);
        var json   = SaveSystem.Serialize(save);
        var loaded = SaveSystem.Deserialize(json)!;

        Assert.That(loaded.Version, Is.EqualTo(SaveSystem.CurrentVersion));
        Assert.That(loaded.HeightMap, Is.Not.Null);
        Assert.That(loaded.HeightMap![0 + 0 * 32], Is.EqualTo(0), "Water tile at (0,0) should round-trip.");
        Assert.That(loaded.HeightMap![31 + 31 * 32], Is.EqualTo(7), "Peak tile at (31,31) should round-trip.");
    }
}
