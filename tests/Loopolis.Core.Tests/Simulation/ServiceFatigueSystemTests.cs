using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;
using NUnit.Framework;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class ServiceFatigueSystemTests
{
    private ServiceFatigueSystem _system = null!;

    [SetUp]
    public void SetUp() => _system = new ServiceFatigueSystem();

    // ── Helper: minimal grid with one service tile ───────────────────────────

    private static CityGrid MakeGridWithService(ZoneType serviceZone, int x = 5, int y = 5)
    {
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();
        grid.SetZone(x, y, serviceZone);
        return grid;
    }

    // ── 1. New tile starts at full capacity ──────────────────────────────────

    [Test]
    public void NewTile_StartsAtFullCapacity()
    {
        // A tile never seen by the system returns 1.0
        Assert.That(_system.GetCapacity(5, 5), Is.EqualTo(1.0));
    }

    // ── 2. No decay before City milestone ───────────────────────────────────

    [Test]
    public void Propagate_BeforeCityMilestone_DoesNotDecay()
    {
        var grid = MakeGridWithService(ZoneType.FireStation);

        // Run 100 ticks at sub-City states
        for (var i = 0; i < 100; i++)
        {
            _system.Propagate(grid, GameState.Active);
            _system.Propagate(grid, GameState.Town);
        }

        // Capacity should remain at 1.0 — system never ran
        Assert.That(_system.GetCapacity(5, 5), Is.EqualTo(1.0));
    }

    // ── 3. Decay by 0.002 per tick after City milestone ──────────────────────

    [Test]
    public void Propagate_AfterCityMilestone_DecaysBy0002PerTick()
    {
        var grid = MakeGridWithService(ZoneType.FireStation);

        _system.Propagate(grid, GameState.City); // tick 1 — tile registered at 1.0, then decayed
        var capacityAfterOneTick = _system.GetCapacity(5, 5);

        Assert.That(capacityAfterOneTick, Is.EqualTo(1.0 - 0.002).Within(1e-9));
    }

    [Test]
    public void Propagate_MultipleTicksAtCity_DecaysCorrectly()
    {
        var grid = MakeGridWithService(ZoneType.FireStation);

        for (var i = 0; i < 50; i++)
            _system.Propagate(grid, GameState.City);

        var expected = 1.0 - 50 * 0.002; // 0.90
        Assert.That(_system.GetCapacity(5, 5), Is.EqualTo(expected).Within(1e-9));
    }

    // ── 4. Capacity never drops below 40% ────────────────────────────────────

    [Test]
    public void Capacity_NeverDropsBelowMinimum()
    {
        var grid = MakeGridWithService(ZoneType.FireStation);

        // 1000 ticks — way more than the 300 needed to reach 40%
        for (var i = 0; i < 1000; i++)
            _system.Propagate(grid, GameState.City);

        Assert.That(_system.GetCapacity(5, 5), Is.EqualTo(0.40).Within(1e-9));
    }

    // ── 5. NeedsRenovation true below 60% ────────────────────────────────────

    [Test]
    public void NeedsRenovation_FalseAbove60Percent()
    {
        var grid = MakeGridWithService(ZoneType.FireStation);

        // Decay just 50 ticks → capacity = 0.90 (above 0.60)
        for (var i = 0; i < 50; i++)
            _system.Propagate(grid, GameState.City);

        Assert.That(_system.NeedsRenovation(5, 5), Is.False);
    }

    [Test]
    public void NeedsRenovation_TrueBelow60Percent()
    {
        var grid = MakeGridWithService(ZoneType.FireStation);

        // Need (1.0 - 0.60) / 0.002 + 1 = 201 ticks to cross below 0.60
        for (var i = 0; i < 201; i++)
            _system.Propagate(grid, GameState.City);

        // capacity = 1.0 - 201*0.002 = 0.598 < 0.60
        Assert.That(_system.NeedsRenovation(5, 5), Is.True);
    }

    // ── 6. Renovate resets capacity to 1.0 ───────────────────────────────────

    [Test]
    public void Renovate_ResetsCapacityToFull()
    {
        var grid   = MakeGridWithService(ZoneType.FireStation);
        var budget = new BudgetSystem(initialBalance: 10_000);

        // Decay 201 ticks so it needs renovation
        for (var i = 0; i < 201; i++)
            _system.Propagate(grid, GameState.City);

        Assert.That(_system.NeedsRenovation(5, 5), Is.True);

        var result = _system.Renovate(5, 5, budget);

        Assert.That(result, Is.True);
        Assert.That(_system.GetCapacity(5, 5), Is.EqualTo(1.0));
        Assert.That(_system.NeedsRenovation(5, 5), Is.False);
    }

    // ── 7. Renovate deducts from budget ──────────────────────────────────────

    [Test]
    public void Renovate_DeductsFromBudget()
    {
        var grid   = MakeGridWithService(ZoneType.FireStation);
        var budget = new BudgetSystem(initialBalance: 2_000);

        _system.Propagate(grid, GameState.City);

        var balanceBefore = budget.Balance;
        _system.Renovate(5, 5, budget);

        Assert.That(budget.Balance, Is.EqualTo(balanceBefore - ServiceFatigueSystem.RenovationCost));
    }

    // ── 8. Renovate fails with insufficient funds ─────────────────────────────

    [Test]
    public void Renovate_FailsIfInsufficientFunds()
    {
        var grid   = MakeGridWithService(ZoneType.FireStation);
        var budget = new BudgetSystem(initialBalance: 100); // less than $500

        _system.Propagate(grid, GameState.City);

        var result = _system.Renovate(5, 5, budget);

        Assert.That(result, Is.False);
        Assert.That(budget.Balance, Is.EqualTo(100)); // unchanged
    }

    // ── 9. Propagate removes tile when erased ────────────────────────────────

    [Test]
    public void Propagate_RemovesTile_WhenErased()
    {
        var grid = MakeGridWithService(ZoneType.FireStation);

        _system.Propagate(grid, GameState.City); // register tile
        Assert.That(_system.GetCapacity(5, 5), Is.LessThan(1.0)); // tracked

        // Erase the tile
        grid.SetZone(5, 5, ZoneType.Empty);

        _system.Propagate(grid, GameState.City); // should remove from tracking

        // Back to default 1.0 (unknown tile)
        Assert.That(_system.GetCapacity(5, 5), Is.EqualTo(1.0));
    }

    // ── 10. Propagate tracks new tiles at full capacity ───────────────────────

    [Test]
    public void Propagate_TracksNewTilesAtFullCapacity()
    {
        var grid = MakeGridWithService(ZoneType.FireStation, x: 3, y: 3);

        // Run 50 ticks for the existing tile
        for (var i = 0; i < 50; i++)
            _system.Propagate(grid, GameState.City);

        // Place a new service tile
        grid.SetZone(7, 7, ZoneType.PoliceStation);

        _system.Propagate(grid, GameState.City); // tick 51

        // New tile registered at 1.0 then decayed once → 0.998
        Assert.That(_system.GetCapacity(7, 7), Is.EqualTo(1.0 - 0.002).Within(1e-9));

        // Old tile decayed 51 ticks → 1.0 - 51*0.002 = 0.898
        Assert.That(_system.GetCapacity(3, 3), Is.EqualTo(1.0 - 51 * 0.002).Within(1e-9));
    }

    // ── 11. DeprecatedTiles returns only fatigued tiles ───────────────────────

    [Test]
    public void DeprecatedTiles_ReturnsOnlyFatiguedTiles()
    {
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();
        grid.SetZone(2, 2, ZoneType.FireStation);
        grid.SetZone(8, 8, ZoneType.PoliceStation);

        // Decay (2,2) to below warning; keep (8,8) fresh
        for (var i = 0; i < 201; i++)
            _system.Propagate(grid, GameState.City);

        // Place fresh Police at (8,8) AFTER registering at tick 201 — it's already below 0.60
        // Both tiles decayed 201 ticks. Let's add a brand-new tile now and check only that one is fresh.
        grid.SetZone(6, 6, ZoneType.School);
        _system.Propagate(grid, GameState.City); // tick 202 — (6,6) registered at 1.0, decayed → 0.998

        var deprecated = _system.DeprecatedTiles.ToHashSet();

        // (2,2) and (8,8) both decayed 202 ticks → capacity = 1.0 - 202*0.002 = 0.596 < 0.60 → deprecated
        Assert.That(deprecated, Does.Contain((2, 2)));
        Assert.That(deprecated, Does.Contain((8, 8)));

        // (6,6) decayed only 1 tick → 0.998 > 0.60 → NOT deprecated
        Assert.That(deprecated, Does.Not.Contain((6, 6)));
    }

    // ── 12. Renovate fails for unknown tile ───────────────────────────────────

    [Test]
    public void Renovate_ReturnsFalse_ForUntrackedTile()
    {
        var budget = new BudgetSystem(initialBalance: 10_000);

        // Never propagated — (5,5) is not in _capacities
        var result = _system.Renovate(5, 5, budget);

        Assert.That(result, Is.False);
        Assert.That(budget.Balance, Is.EqualTo(10_000)); // no charge
    }

    // ── 13. Integration: 300-tick simulation at City ──────────────────────────

    [Test]
    public void Integration_300TicksAtCity_ServiceTileNeedsRenovation()
    {
        // 300 × 0.002 = 0.6 decay from 1.0 → capacity = 0.40
        // 0.40 < 0.60 (FatigueWarning) → NeedsRenovation = true
        var grid = MakeGridWithService(ZoneType.FireStation);

        for (var i = 0; i < 300; i++)
            _system.Propagate(grid, GameState.City);

        Assert.That(_system.GetCapacity(5, 5), Is.EqualTo(0.40).Within(1e-9)); // clamped at min
        Assert.That(_system.NeedsRenovation(5, 5), Is.True);
        Assert.That(_system.DeprecatedTiles.Any(), Is.True);
    }

    // ── 14. Works with all service zone types ────────────────────────────────

    [TestCase(ZoneType.FireStation)]
    [TestCase(ZoneType.PoliceStation)]
    [TestCase(ZoneType.School)]
    [TestCase(ZoneType.Hospital)]
    [TestCase(ZoneType.FireHQ)]
    [TestCase(ZoneType.PoliceHQ)]
    public void AllServiceZoneTypes_DecayCorrectly(ZoneType serviceZone)
    {
        var grid = MakeGridWithService(serviceZone);

        _system.Propagate(grid, GameState.City);

        Assert.That(_system.GetCapacity(5, 5), Is.EqualTo(1.0 - 0.002).Within(1e-9));
    }

    // ── 15. Metropolis and Loopolis also trigger decay ───────────────────────

    [TestCase(GameState.Metropolis)]
    [TestCase(GameState.Loopolis)]
    public void Propagate_AtMetropolisOrLoopolis_AlsoDecays(GameState state)
    {
        var grid = MakeGridWithService(ZoneType.FireStation);

        _system.Propagate(grid, state);

        Assert.That(_system.GetCapacity(5, 5), Is.EqualTo(1.0 - 0.002).Within(1e-9));
    }

    // ── 16. Save / restore snapshot round-trip ───────────────────────────────

    [Test]
    public void SaveRestore_SnapshotRoundTrip()
    {
        var grid = MakeGridWithService(ZoneType.FireStation);

        for (var i = 0; i < 100; i++)
            _system.Propagate(grid, GameState.City);

        var snapshot = _system.GetSnapshot();
        var expectedCapacity = _system.GetCapacity(5, 5);

        var restored = new ServiceFatigueSystem();
        restored.RestoreSnapshot(snapshot);

        Assert.That(restored.GetCapacity(5, 5), Is.EqualTo(expectedCapacity).Within(1e-9));
    }
}
