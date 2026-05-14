using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Integration;

/// <summary>
/// End-to-end smoke tests that simulate a real player building a city.
/// These are correctness tests — not balance tests.
/// They guard against: infinite loops, immediate bankruptcy, population never growing,
/// buildings never spawning.
///
/// Each test builds a minimal but functional city layout directly in Core (no Runner dependency)
/// and verifies that the simulation reaches expected milestones without crashing.
/// </summary>
[TestFixture]
public class PlaythroughTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SimulationEngine BuildEngine(CityGrid grid, double initialBalance = 20_000)
    {
        return new SimulationEngine(
            grid,
            new BudgetSystem(initialBalance),
            new PopulationSystem(),
            new PowerNetwork(),
            new RoadNetwork(),
            new DemandSystem()
        );
    }

    /// <summary>
    /// Try to place a zone tile, silently skipping water tiles and already-occupied tiles.
    /// Roads also validate cliff constraints — skip on failure.
    /// </summary>
    private static void TryPlace(SimulationEngine engine, CityGrid grid, int x, int y, ZoneType zone)
    {
        if (!grid.IsInBounds(x, y)) return;
        var tile = grid.GetTile(x, y);
        if (tile.HeightLevel <= 0) return;       // water — skip
        if (tile.Zone != ZoneType.Empty) return; // occupied — skip

        if (zone is ZoneType.Road or ZoneType.Avenue)
        {
            var (ok, _) = grid.CanPlaceRoad(x, y);
            if (!ok) return;
            engine.PlaceTile(x, y, zone);
        }
        else
        {
            grid.SetZone(x, y, zone);
        }
    }

    // ── Test 1 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Simulates the tutorial scenario: flat 32×32 map, generous starting budget.
    /// Player builds a road spine, residential (12 tiles), commercial, industrial,
    /// and a power plant adjacent to the road, then ticks 600 times.
    ///
    /// 12 residential tiles × 50 capacity = 600 max pop → should exceed Town milestone (500).
    /// Asserts: pop > 500, no bankruptcy, at least one building spawned.
    /// </summary>
    [Test, CancelAfter(10_000)]
    public void TutorialScenario_BasicCity_ReachesTownMilestone()
    {
        var grid = new CityGrid(32, 32);
        grid.SetFlatTerrain();
        var engine = BuildEngine(grid, initialBalance: 20_000);

        // Power plant at (9,15) — directly adjacent to road spine start at (10,15)
        grid.SetZone(9, 15, ZoneType.CoalPlant);

        // Main road spine: y=15, x=10..22
        for (var x = 10; x <= 22; x++)
            TryPlace(engine, grid, x, 15, ZoneType.Road);

        // Three N-S spurs connecting main road to south road (x=10, x=16, x=22).
        // Three spurs keep the maximum road-graph commute distance ≤ 10.0 for all
        // residential tiles at x=10..21, avoiding the −0.20 commute happiness penalty
        // that would otherwise push some tiles below the 0.30 distress threshold.
        foreach (var sx in new[] { 10, 16, 22 })
            for (var y = 16; y <= 18; y++)
                TryPlace(engine, grid, sx, y, ZoneType.Road);

        // South industrial road: y=19, x=10..22 (parallel to main spine)
        for (var x = 10; x <= 22; x++)
            TryPlace(engine, grid, x, 19, ZoneType.Road);

        engine.SeedRoadGraphFromGrid();

        // 12 residential tiles above main road: y=14, x=10..21
        for (var x = 10; x <= 21; x++)
            TryPlace(engine, grid, x, 14, ZoneType.Residential);

        // Commercial in the block between main and south roads: y=16..18, x=11..15
        // (not on the spur columns x=10,16,22 — those are roads).
        // Commercial is road-adjacent (to y=15 and y=19) for buildings to spawn.
        for (var x = 11; x <= 15; x++)
            TryPlace(engine, grid, x, 16, ZoneType.Commercial);

        // Industrial district south of the south road: y=20, x=10..22 (13 tiles).
        // Distance from industrial(10,20) to nearest residential(10,14) = 6 > pollution
        // radius (3) → no pollution distress on residential.
        // 13 fully-active tiles × 20 jobs = 260 jobs; at pop=500 (RequiredJobs=400),
        // ratio = 260/400 = 0.65 ≥ 0.4 → minGrowth guarantee holds → city reaches 500.
        for (var x = 10; x <= 22; x++)
            TryPlace(engine, grid, x, 20, ZoneType.Industrial);

        for (var i = 0; i < 600; i++)
            engine.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(engine.Population.Population, Is.GreaterThan(500),
                $"Population should exceed Town milestone (500) after 600 ticks. " +
                $"Actual: {engine.Population.Population}");

            Assert.That(engine.MilestoneSystem.CurrentState, Is.Not.EqualTo(GameState.Bankrupt),
                $"City should not go bankrupt. Balance: {engine.Budget.Balance:N0}");

            var hasBuilding = grid.AllTiles().Any(t => t.BuildingId != null);
            Assert.That(hasBuilding, Is.True,
                "At least one building should have spawned after 600 ticks");
        });
    }

    // ── Test 2 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Replicates the "powered_start" scenario setup: pre-built infrastructure with
    /// fire/police coverage.  Does not place anything extra — just ticks 400 times.
    /// Verifies that the pre-built layout doesn't break (pop > 200, no bankruptcy).
    /// </summary>
    [Test, CancelAfter(10_000)]
    public void PoweredStart_ExistingInfrastructure_GrowsToCity()
    {
        var grid = new CityGrid(32, 32);
        grid.SetFlatTerrain();

        // Mirror the powered_start layout from ScenarioSetup exactly
        grid.SetZone(5, 12, ZoneType.CoalPlant);
        for (var x = 6; x <= 16; x++) grid.SetZone(x, 12, ZoneType.Road);

        // Residential north of road
        for (var x = 9; x <= 14; x++) grid.SetZone(x, 11, ZoneType.Residential);

        // Commercial south of road
        grid.SetZone(9,  13, ZoneType.Commercial);
        grid.SetZone(10, 13, ZoneType.Commercial);
        grid.SetZone(11, 13, ZoneType.Commercial);

        // Industrial south of road
        grid.SetZone(6,  13, ZoneType.Industrial);
        grid.SetZone(7,  13, ZoneType.Industrial);

        // Services
        grid.SetZone(8,  13, ZoneType.FireStation);
        grid.SetZone(13, 13, ZoneType.PoliceStation);
        grid.SetZone(15, 11, ZoneType.School);

        var engine = BuildEngine(grid, initialBalance: 10_000);
        engine.SeedRoadGraphFromGrid();

        for (var i = 0; i < 400; i++)
            engine.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(engine.Population.Population, Is.GreaterThan(200),
                $"powered_start should grow past 200 pop after 400 ticks. " +
                $"Actual: {engine.Population.Population}");

            Assert.That(engine.MilestoneSystem.CurrentState, Is.Not.EqualTo(GameState.Bankrupt),
                $"powered_start should not go bankrupt. Balance: {engine.Budget.Balance:N0}");
        });
    }

    // ── Test 3 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Challenge terrain survivability: sparse buildable area simulated by a water border.
    /// We build a minimal road + residential cluster on forced-flat terrain at the center.
    /// After 200 ticks the city must not have gone bankrupt —
    /// it doesn't need to thrive, just survive.
    /// Uses a 32×32 grid with a water moat to simulate island conditions.
    /// </summary>
    [Test, CancelAfter(10_000)]
    public void IslandChain_ChallengeTerrain_DoesNotInstaBankrupt()
    {
        var grid = new CityGrid(32, 32);
        grid.SetFlatTerrain();

        // Water moat in the outer ring to simulate island isolation
        for (var x = 0; x < 32; x++)
        for (var y = 0; y < 32; y++)
        {
            if (x < 10 || x > 22 || y < 10 || y > 22)
                grid.SetHeightLevel(x, y, 0); // water
        }

        var engine = BuildEngine(grid, initialBalance: 6_500);

        // Power plant adjacent to road
        grid.SetZone(14, 16, ZoneType.CoalPlant);

        // 5-tile road on the main island
        for (var x = 15; x <= 19; x++)
            TryPlace(engine, grid, x, 16, ZoneType.Road);

        engine.SeedRoadGraphFromGrid();

        // 4 residential tiles along the road (above it)
        for (var x = 15; x <= 18; x++)
            TryPlace(engine, grid, x, 15, ZoneType.Residential);

        for (var i = 0; i < 200; i++)
            engine.Tick();

        Assert.That(engine.MilestoneSystem.CurrentState, Is.Not.EqualTo(GameState.Bankrupt),
            $"Island city should survive 200 ticks without going bankrupt. " +
            $"Balance: {engine.Budget.Balance:N0}, Pop: {engine.Population.Population}");
    }

    // ── Test 4 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Green City scenario: no industrial zones allowed.
    /// Builds residential + commercial only with power infrastructure on a 32×32 map.
    /// After 700 ticks expects pop > 300 and no bankruptcy — proving that a
    /// commercial-only economy is viable.
    /// Uses a 32×32 map (not 64×64) to stay well within the 10-second timeout.
    /// </summary>
    [Test, CancelAfter(10_000)]
    public void GreenCity_NoIndustrial_CanReachTargetPopulation()
    {
        var grid = new CityGrid(32, 32);
        grid.SetFlatTerrain();
        var engine = BuildEngine(grid, initialBalance: 20_000);

        // Power plant adjacent to road at (7,15)
        grid.SetZone(7, 15, ZoneType.CoalPlant);

        // Road spine: E-W at y=15, x=8..25
        for (var x = 8; x <= 25; x++)
            TryPlace(engine, grid, x, 15, ZoneType.Road);

        // Short N spur for more residential adjacency
        for (var y = 12; y <= 14; y++)
            TryPlace(engine, grid, 16, y, ZoneType.Road);

        engine.SeedRoadGraphFromGrid();

        // Residential above road: y=14, x=8..25
        for (var x = 8; x <= 25; x++)
            TryPlace(engine, grid, x, 14, ZoneType.Residential);

        // Residential on N spur sides
        for (var y = 12; y <= 14; y++)
        {
            TryPlace(engine, grid, 15, y, ZoneType.Residential);
            TryPlace(engine, grid, 17, y, ZoneType.Residential);
        }

        // Commercial below road (NO industrial): y=16, x=8..22
        for (var x = 8; x <= 22; x++)
            TryPlace(engine, grid, x, 16, ZoneType.Commercial);

        for (var i = 0; i < 700; i++)
            engine.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(engine.Population.Population, Is.GreaterThan(300),
                $"Green city (R+C only) should reach 300 pop after 700 ticks. " +
                $"Actual: {engine.Population.Population}");

            Assert.That(engine.MilestoneSystem.CurrentState, Is.Not.EqualTo(GameState.Bankrupt),
                $"Green city should not go bankrupt. Balance: {engine.Budget.Balance:N0}");

            // Verify no industrial tiles crept in (scenario constraint respected)
            var indCount = grid.TilesOfType(ZoneType.Industrial).Count();
            Assert.That(indCount, Is.Zero,
                "Green City: no industrial zones should be present");
        });
    }

    // ── Test 5 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Happiness decay smoke test: a city with heavy industrial pollution next to residential
    /// forces happiness below the 0.30 distress threshold.
    ///
    /// Formula: base 0.6 − 0.4 × pollutionLevel = at full pollution: 0.6 − 0.4 = 0.2
    /// The distress decay threshold is 0.30 — so high-pollution residential WILL decay.
    ///
    /// Phase 1 (100 ticks): city grows with road + residential + industrial (polluted).
    /// Phase 2 (200 ticks): no changes — happiness stays low due to pollution,
    ///   distress decay kicks in after 30-tick grace period and shrinks population.
    /// Assert: final pop &lt; pop at tick 100.
    /// </summary>
    [Test, CancelAfter(10_000)]
    public void HappinessDecay_NeglectedCity_ShrinksMeaningfully()
    {
        var grid = new CityGrid(32, 32);
        grid.SetFlatTerrain();
        var engine = BuildEngine(grid, initialBalance: 20_000);

        // Power plant adjacent to road
        grid.SetZone(5, 15, ZoneType.CoalPlant);

        // Short road spine
        for (var x = 6; x <= 12; x++)
            TryPlace(engine, grid, x, 15, ZoneType.Road);

        engine.SeedRoadGraphFromGrid();

        // 6 residential tiles adjacent to road — no services
        for (var x = 6; x <= 11; x++)
            TryPlace(engine, grid, x, 14, ZoneType.Residential);

        // Dense industrial immediately adjacent to residential (y=13, one tile above residential)
        // This causes heavy pollution on the residential tiles and drives happiness < 0.30
        for (var x = 6; x <= 11; x++)
            TryPlace(engine, grid, x, 13, ZoneType.Industrial);

        // Also place industrial on the other side of the road (y=16) for maximum pollution spread
        for (var x = 6; x <= 11; x++)
            TryPlace(engine, grid, x, 16, ZoneType.Industrial);

        // Growth phase: 100 ticks — industrial provides jobs so city grows initially
        for (var i = 0; i < 100; i++)
            engine.Tick();

        var populationAt100 = engine.Population.Population;

        // Decline phase: 200 more ticks with no changes
        // Pollution keeps happiness well below 0.30 → distress decay fires continuously
        for (var i = 0; i < 200; i++)
            engine.Tick();

        var finalPopulation = engine.Population.Population;

        // The city needs to have grown initially to make this test meaningful
        Assert.That(populationAt100, Is.GreaterThan(0),
            "City should have grown to non-zero population by tick 100");

        // After prolonged pollution-induced distress, population should have declined
        Assert.That(finalPopulation, Is.LessThan(populationAt100),
            $"Polluted city should shrink after happiness distress decay. " +
            $"Pop at tick 100: {populationAt100}, final pop: {finalPopulation}");
    }
}
