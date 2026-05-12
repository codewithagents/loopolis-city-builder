using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

/// <summary>
/// Tests for BuildingDegradationSystem: multi-tile buildings that lose power or road
/// have a 2% chance per tick to degrade back to bare zone tiles.
/// </summary>
[TestFixture]
public class BuildingDegradationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a 2×2 residential townhouse in the grid at (ax, ay).
    /// All tiles get power and road access, and a population.
    /// Returns the building ID.
    /// </summary>
    private static string PlaceTownhouse(CityGrid grid, int ax, int ay, bool powered = true, bool roadAccess = true)
    {
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(ax + dx, ay + dy, ZoneType.Residential);
            grid.SetPower(ax + dx, ay + dy, powered);
            grid.SetRoadAccess(ax + dx, ay + dy, roadAccess);
            grid.SetPopulation(ax + dx, ay + dy, 20);
        }

        var id = Guid.NewGuid().ToString("N")[..8];
        var building = new Building(id, "res_townhouse_2x2", ZoneType.Residential, ax, ay, 2, 2);
        grid.Buildings[id] = building;
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
            grid.SetBuildingId(ax + dx, ay + dy, id);

        return id;
    }

    // ── Test 1: Powered townhouse stays intact ────────────────────────────────

    [Test]
    public void PoweredTownhouse_WithRoadAccess_StaysIntact()
    {
        // A townhouse with all tiles powered and road access meets requirements.
        // Even with 100 ticks it should never degrade.
        var grid = new CityGrid(10, 10);
        var id = PlaceTownhouse(grid, 3, 3, powered: true, roadAccess: true);

        // Seed=42 gives deterministic results: no degradation should occur in 100 ticks
        var system = new BuildingDegradationSystem(seed: 42);

        for (var i = 0; i < 100; i++)
        {
            var demolished = system.Propagate(grid);
            Assert.That(demolished, Is.Empty,
                $"Powered townhouse with road access should not degrade at tick {i + 1}");
        }

        Assert.That(grid.Buildings.ContainsKey(id), Is.True,
            "Powered townhouse should still exist after 100 ticks");
    }

    // ── Test 2: Unpowered townhouse has 2% chance to degrade ─────────────────

    [Test]
    public void UnpoweredTownhouse_FailsRequirements()
    {
        // Removing power from a townhouse means it fails requirements.
        // Verify that Propagate identifies the failure (it will roll for degradation).
        var grid = new CityGrid(10, 10);
        PlaceTownhouse(grid, 3, 3, powered: false, roadAccess: true);

        // Use seed=1 which will NOT trigger the 2% roll on the first call
        // We just confirm that it CAN degrade (not that it always does on tick 1)
        var system = new BuildingDegradationSystem(seed: 1);

        // Over 200 ticks, a 2% chance should have triggered (~98% probability of at least one)
        string? lastBuildingId = grid.Buildings.Keys.FirstOrDefault();
        bool degraded = false;

        for (var i = 0; i < 200; i++)
        {
            var demolished = system.Propagate(grid);
            if (demolished.Count > 0)
            {
                degraded = true;
                break;
            }
            // Re-place if the building was already demolished (shouldn't happen yet)
            if (!grid.Buildings.Any()) break;
        }

        Assert.That(degraded, Is.True,
            "Unpowered townhouse should degrade within 200 ticks (2% per tick, ~98% probability)");
    }

    // ── Test 3: Deterministic degradation with seeded RNG ────────────────────

    [Test]
    public void SeededRng_DeterministicDegradation()
    {
        // With a fixed seed, the first degradation event is predictable.
        // Seed=0: first double below 0.02 happens at a known tick.
        // We verify two runs with the same seed produce identical results.
        var grid1 = new CityGrid(10, 10);
        PlaceTownhouse(grid1, 3, 3, powered: false, roadAccess: true);

        var grid2 = new CityGrid(10, 10);
        PlaceTownhouse(grid2, 3, 3, powered: false, roadAccess: true);

        var system1 = new BuildingDegradationSystem(seed: 77);
        var system2 = new BuildingDegradationSystem(seed: 77);

        int firstDegradationTick1 = -1;
        int firstDegradationTick2 = -1;

        for (var i = 0; i < 500; i++)
        {
            var d1 = system1.Propagate(grid1);
            if (d1.Count > 0 && firstDegradationTick1 < 0) firstDegradationTick1 = i;

            var d2 = system2.Propagate(grid2);
            if (d2.Count > 0 && firstDegradationTick2 < 0) firstDegradationTick2 = i;

            // Stop after both have degraded
            if (firstDegradationTick1 >= 0 && firstDegradationTick2 >= 0) break;
        }

        Assert.That(firstDegradationTick1, Is.GreaterThanOrEqualTo(0),
            "First system should degrade within 500 ticks");
        Assert.That(firstDegradationTick1, Is.EqualTo(firstDegradationTick2),
            "Same seed should produce identical first-degradation tick");
    }

    // ── Test 4: Degraded building returns tiles to bare zone type ─────────────

    [Test]
    public void DegradedBuilding_ReturnsTilesToBareZone()
    {
        // After degradation: tiles still have their ZoneType (Residential),
        // but BuildingId is null and Population is 0.
        var grid = new CityGrid(10, 10);
        var id = PlaceTownhouse(grid, 3, 3, powered: false, roadAccess: true);

        // Use a seed that guarantees immediate degradation.
        // We'll find the right seed by scanning.
        var system = new BuildingDegradationSystem(seed: 0);

        // Force degradation by replacing the building after each non-triggering tick
        bool degraded = false;
        for (var i = 0; i < 500; i++)
        {
            if (!grid.Buildings.ContainsKey(id))
            {
                // Was already demolished — re-place to keep trying
                // (shouldn't happen in this test structure)
                break;
            }
            var demolished = system.Propagate(grid);
            if (demolished.Count > 0)
            {
                degraded = true;
                break;
            }
        }

        Assert.That(degraded, Is.True, "Should have degraded within 500 ticks with seed 0");
        Assert.That(grid.Buildings.ContainsKey(id), Is.False,
            "Degraded building should be removed from grid.Buildings");

        // All tiles should have ZoneType = Residential (zone preserved, just unbuilt)
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            var tile = grid.GetTile(3 + dx, 3 + dy);
            Assert.That(tile.Zone, Is.EqualTo(ZoneType.Residential),
                $"Tile ({3 + dx},{3 + dy}) should remain Residential after degradation");
            Assert.That(tile.BuildingId, Is.Null,
                $"Tile ({3 + dx},{3 + dy}) BuildingId should be cleared after degradation");
            Assert.That(tile.Population, Is.EqualTo(0),
                $"Tile ({3 + dx},{3 + dy}) population should reset to 0 after degradation");
        }
    }

    // ── Test 5: 1×1 cottage never degrades ────────────────────────────────────

    [Test]
    public void Cottage_1x1_NeverDegrades()
    {
        // 1×1 buildings are exempt from degradation (they are base buildings,
        // managed by BuildingGrowthSystem and PopulationSystem directly).
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        // No power, no road — a completely isolated cottage
        grid.SetPower(5, 5, false);
        grid.SetRoadAccess(5, 5, false);

        var id = Guid.NewGuid().ToString("N")[..8];
        var cottage = new Building(id, "res_house_1x1", ZoneType.Residential, 5, 5, 1, 1);
        grid.Buildings[id] = cottage;
        grid.SetBuildingId(5, 5, id);

        var system = new BuildingDegradationSystem(seed: 0);

        for (var i = 0; i < 200; i++)
        {
            var demolished = system.Propagate(grid);
            Assert.That(demolished, Is.Empty,
                $"1×1 cottage should never degrade (tick {i + 1})");
        }

        Assert.That(grid.Buildings.ContainsKey(id), Is.True,
            "Cottage should still exist after 200 ticks — only multi-tile buildings degrade");
    }

    // ── Test 6: Road disconnection triggers degradation chance ────────────────

    [Test]
    public void RoadDisconnected_Townhouse_CanDegrade()
    {
        // A townhouse with all tiles powered but NO road access fails requirements.
        var grid = new CityGrid(10, 10);
        PlaceTownhouse(grid, 3, 3, powered: true, roadAccess: false);

        var system = new BuildingDegradationSystem(seed: 1);

        bool degraded = false;
        for (var i = 0; i < 300; i++)
        {
            var demolished = system.Propagate(grid);
            if (demolished.Count > 0)
            {
                degraded = true;
                break;
            }
            if (!grid.Buildings.Any()) break;
        }

        Assert.That(degraded, Is.True,
            "Townhouse with no road access should degrade within 300 ticks");
    }

    // ── Test 7: DemolishResult includes building type ID ─────────────────────

    [Test]
    public void Propagate_ReturnsDemolishedTypeIds()
    {
        // The return value of Propagate() should contain the type IDs of demolished buildings.
        var grid = new CityGrid(10, 10);
        PlaceTownhouse(grid, 3, 3, powered: false, roadAccess: true);

        var system = new BuildingDegradationSystem(seed: 0);
        List<string> demolished = new();

        for (var i = 0; i < 500; i++)
        {
            demolished = system.Propagate(grid);
            if (demolished.Count > 0) break;
        }

        Assert.That(demolished, Is.Not.Empty,
            "Should return at least one demolished type ID after degradation");
        Assert.That(demolished, Contains.Item("res_townhouse_2x2"),
            "Demolished type ID should match the building's TypeId");
    }

    // ── Test 8: Single-tile exemption also applies to commercial and industrial ─

    [Test]
    public void CommercialShop_1x1_NeverDegrades()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Commercial);
        grid.SetPower(5, 5, false);
        grid.SetRoadAccess(5, 5, false);

        var id = Guid.NewGuid().ToString("N")[..8];
        var shop = new Building(id, "com_shop_1x1", ZoneType.Commercial, 5, 5, 1, 1);
        grid.Buildings[id] = shop;
        grid.SetBuildingId(5, 5, id);

        var system = new BuildingDegradationSystem(seed: 0);

        for (var i = 0; i < 200; i++)
        {
            var demolished = system.Propagate(grid);
            Assert.That(demolished, Is.Empty, $"1×1 commercial shop should never degrade (tick {i + 1})");
        }
    }

    // ── Test 9: Mixed grid — only unpowered multi-tile building degrades ──────

    [Test]
    public void MixedGrid_OnlyFailingBuilding_Degrades()
    {
        // Healthy townhouse + failing townhouse side by side.
        // Only the one that fails requirements should ever be in the demolish list.
        var grid = new CityGrid(20, 10);
        var goodId = PlaceTownhouse(grid, 3, 3, powered: true, roadAccess: true);
        PlaceTownhouse(grid, 8, 3, powered: false, roadAccess: true); // bad one

        var system = new BuildingDegradationSystem(seed: 5);

        bool badDegraded = false;
        bool goodDegraded = false;

        for (var i = 0; i < 300; i++)
        {
            var demolished = system.Propagate(grid);
            if (demolished.Contains("res_townhouse_2x2"))
            {
                // Which one was demolished?
                if (!grid.Buildings.ContainsKey(goodId))
                    goodDegraded = true;
                else
                    badDegraded = true;
            }
        }

        Assert.That(badDegraded, Is.True,
            "Unpowered townhouse should have degraded within 300 ticks");
        Assert.That(goodDegraded, Is.False,
            "Powered townhouse with road access should never degrade");
    }
}
