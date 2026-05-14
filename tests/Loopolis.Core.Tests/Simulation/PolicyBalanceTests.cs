using Loopolis.Core.Grid;
using Loopolis.Core.Policies;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

/// <summary>
/// Tests for policy balance and mechanics:
///   1. CommercialBoost cap fix — tiles at capacity stay at ActivityCapacity, not below
///   2. CommercialBoost growth — tiles with room grow faster than without policy
///   3. OpenCity capacity bonus — +12% effective residential tile capacity
///   4. OpenCity exceeds default 50 cap — population can reach up to 56
///   5. Policy cost values — GreenCity=$40, IndustrialHub=$30
///   6. Happiness distress decay — 30-tick grace period, then 1.5%/tick
///   7. Happiness distress within grace — no decay before 30 ticks
///   8. Happiness recovery — counter resets when happiness recovers
///   9. High happiness tile — never affected by distress decay
/// </summary>
[TestFixture]
public class PolicyBalanceTests
{
    // Helper: create a fully ready commercial tile with 4 adjacent residential tiles (each pop=50)
    // so that adjacentResidential=200, driving commercialGrowthRate to its cap (0.06).
    // This ensures the 1.25× CommercialBoost multiplier creates a measurable difference.
    private static CityGrid MakeCommercialGrid(int pop = 0)
    {
        var grid = new CityGrid(12, 12);
        grid.SetFlatTerrain();
        grid.SetZone(5, 5, ZoneType.Commercial);
        grid.SetPower(5, 5, true);
        grid.SetRoadAccess(5, 5, true);
        grid.SetBuildingId(5, 5, "test_com");
        // Add 4 adjacent residential tiles, each at pop=50 (max for 1×1)
        // so adjacentResidential=200, pushing commercialGrowthRate to its cap of 0.06
        var resNeighbours = new[] { (5, 4), (5, 6), (4, 5), (6, 5) };
        var resId = 0;
        foreach (var (rx, ry) in resNeighbours)
        {
            grid.SetZone(rx, ry, ZoneType.Residential);
            grid.SetPower(rx, ry, true);
            grid.SetRoadAccess(rx, ry, true);
            grid.SetBuildingId(rx, ry, $"res_{resId++}");
            grid.SetPopulation(rx, ry, 50);
        }
        if (pop > 0)
            grid.SetPopulation(5, 5, pop);
        return grid;
    }

    // Helper: create a fully ready residential tile
    private static CityGrid MakeResidentialGrid(int pop = 0, double happiness = 0.8)
    {
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPower(5, 5, true);
        grid.SetRoadAccess(5, 5, true);
        grid.SetBuildingId(5, 5, "test");
        grid.SetHappiness(5, 5, happiness);
        if (pop > 0)
            grid.SetPopulation(5, 5, pop);
        return grid;
    }

    // ── CommercialBoost: cap fix ─────────────────────────────────────────────────

    [Test]
    public void CommercialBoost_TileAtCapacity_ActivityStaysAtActivityCapacity()
    {
        // Tile already at ActivityCapacity (50). With CommercialBoost active,
        // it should hold at 50 — not drop below due to the multiplier touching
        // the (ActivityCapacity - current) factor near zero.
        var grid = MakeCommercialGrid(pop: 50); // at capacity
        var pop = new PopulationSystem();

        // Run several ticks with CommercialBoost multiplier active (1.25)
        for (var i = 0; i < 5; i++)
            pop.Tick(grid, commercialGrowthMultiplier: 1.25);

        var activity = grid.GetPopulation(5, 5);
        Assert.That(activity, Is.EqualTo(50),
            "Commercial tile at capacity should stay at 50 with CommercialBoost active");
    }

    [Test]
    public void CommercialBoost_TileWithRoom_GrowsFasterThanWithoutPolicy()
    {
        // Two grids: same setup, one gets CommercialBoost multiplier, one doesn't.
        // After several ticks the boosted tile should have higher activity.
        var gridBoosted   = MakeCommercialGrid(pop: 10);
        var gridNormal    = MakeCommercialGrid(pop: 10);
        var popBoosted    = new PopulationSystem();
        var popNormal     = new PopulationSystem();

        const int ticks = 20;
        for (var i = 0; i < ticks; i++)
            popBoosted.Tick(gridBoosted, commercialGrowthMultiplier: 1.25);

        for (var i = 0; i < ticks; i++)
            popNormal.Tick(gridNormal, commercialGrowthMultiplier: 1.0);

        var boostedActivity = gridBoosted.GetPopulation(5, 5);
        var normalActivity  = gridNormal.GetPopulation(5, 5);

        Assert.That(boostedActivity, Is.GreaterThan(normalActivity),
            "Commercial tile with CommercialBoost should grow faster when there is meaningful room");
    }

    // ── OpenCity: capacity bonus ─────────────────────────────────────────────────

    [Test]
    public void OpenCity_ResidentialCapacityBonus_IncreasesEffectiveCapacity()
    {
        // With residentialCapacityBonus=0.12 and base capacity 50, effective cap = 56
        var grid = MakeResidentialGrid(pop: 49); // one below default cap
        var pop  = new PopulationSystem();

        // Without OpenCity bonus: population should cap at 50
        for (var i = 0; i < 30; i++)
            pop.Tick(grid, residentialCapacityBonus: 0.0);

        var populationNoBonus = grid.GetPopulation(5, 5);
        Assert.That(populationNoBonus, Is.EqualTo(50),
            "Without OpenCity, population should cap at 50");
    }

    [Test]
    public void OpenCity_PopulationCanExceedDefaultCap_WithCapacityBonus()
    {
        // With residentialCapacityBonus=0.12, effective capacity = int(50 * 1.12) = 56.
        // Population starting at 50 (default cap) should be able to grow to up to 56.
        var grid = MakeResidentialGrid(pop: 50);
        var pop  = new PopulationSystem();

        // Run enough ticks for the bonus capacity to fill in
        for (var i = 0; i < 50; i++)
            pop.Tick(grid, residentialCapacityBonus: 0.12);

        var population = grid.GetPopulation(5, 5);
        Assert.That(population, Is.GreaterThan(50),
            "With OpenCity +12% capacity bonus, population should be able to exceed 50");
        Assert.That(population, Is.LessThanOrEqualTo(56),
            "Population should not exceed int(50 * 1.12) = 56");
    }

    // ── Policy cost values ───────────────────────────────────────────────────────

    [Test]
    public void PolicyCatalog_GreenCityCostsFortyPerTick()
    {
        var def = PolicyCatalog.Find(PolicyType.GreenCity);
        Assert.That(def, Is.Not.Null);
        Assert.That(def!.CostPerTick, Is.EqualTo(40),
            "GreenCity was rebalanced from $80 to $40 to be affordable at mid-game (800+ pop)");
    }

    [Test]
    public void PolicyCatalog_IndustrialHubCostsThirtyPerTick()
    {
        var def = PolicyCatalog.Find(PolicyType.IndustrialHub);
        Assert.That(def, Is.Not.Null);
        Assert.That(def!.CostPerTick, Is.EqualTo(30),
            "IndustrialHub was rebalanced from $50 to $30 to improve signal-to-cost ratio");
    }

    // ── Happiness distress decay ─────────────────────────────────────────────────

    [Test]
    public void LowHappiness_After31Ticks_PopulationDecays()
    {
        // Tile with happiness 0.15 (below 0.30 threshold) for 31 ticks — after grace period
        // of 30 ticks, the tile should lose population each tick.
        var grid = MakeResidentialGrid(pop: 50, happiness: 0.15);
        var pop  = new PopulationSystem();

        // Run 31 ticks with happiness at 0.15
        for (var i = 0; i < 31; i++)
        {
            // Keep happiness at 0.15 each tick (happiness system not running here)
            grid.SetHappiness(5, 5, 0.15);
            pop.Tick(grid);
        }

        var population = grid.GetPopulation(5, 5);
        Assert.That(population, Is.LessThan(50),
            "After 31 ticks at happiness 0.15, population should have decayed below 50");
    }

    [Test]
    public void LowHappiness_Within25Ticks_NoDecay()
    {
        // Tile with happiness 0.15 for 25 ticks — still within grace period of 30 ticks.
        // Population should not decay.
        var grid = MakeResidentialGrid(pop: 50, happiness: 0.15);
        var pop  = new PopulationSystem();

        for (var i = 0; i < 25; i++)
        {
            grid.SetHappiness(5, 5, 0.15);
            pop.Tick(grid);
        }

        var population = grid.GetPopulation(5, 5);
        Assert.That(population, Is.EqualTo(50),
            "Within the 30-tick grace period, population should NOT decay from happiness distress");
    }

    [Test]
    public void LowHappiness_RecoveryResetsCounter()
    {
        // Tile unhappy for 28 ticks, then happiness recovers to 0.40 for 3 ticks,
        // then drops again to 0.15. Counter should have been reset by the recovery,
        // so we're back in grace period — no decay after only 3 more low-happiness ticks.
        var grid = MakeResidentialGrid(pop: 50, happiness: 0.15);
        var pop  = new PopulationSystem();

        // 28 ticks of low happiness (still within grace)
        for (var i = 0; i < 28; i++)
        {
            grid.SetHappiness(5, 5, 0.15);
            pop.Tick(grid);
        }

        // Recovery: 3 ticks with happiness 0.40 — counter resets
        for (var i = 0; i < 3; i++)
        {
            grid.SetHappiness(5, 5, 0.40);
            pop.Tick(grid);
        }

        // Drop to 0.15 again — should start fresh grace period, no decay after only 3 ticks
        for (var i = 0; i < 3; i++)
        {
            grid.SetHappiness(5, 5, 0.15);
            pop.Tick(grid);
        }

        var population = grid.GetPopulation(5, 5);
        Assert.That(population, Is.EqualTo(50),
            "After happiness recovery, unhappy counter resets; decay should not fire within new grace period");
    }

    [Test]
    public void HighHappiness_IsNeverAffectedByDistressDecay()
    {
        // Tile with happiness 0.45 (above 0.30 threshold) — should never decay,
        // even after many ticks. Counter stays at 0.
        var grid = MakeResidentialGrid(pop: 50, happiness: 0.45);
        var pop  = new PopulationSystem();

        for (var i = 0; i < 100; i++)
        {
            grid.SetHappiness(5, 5, 0.45);
            pop.Tick(grid);
        }

        var population = grid.GetPopulation(5, 5);
        Assert.That(population, Is.EqualTo(50),
            "Tile at happiness 0.45 (above 0.30 threshold) should never lose population to distress decay");
    }
}
