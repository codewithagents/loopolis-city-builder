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
}
