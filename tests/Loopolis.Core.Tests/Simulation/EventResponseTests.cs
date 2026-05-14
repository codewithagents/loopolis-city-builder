using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

/// <summary>
/// Tests for the event-response (intervention) system.
/// Every test that needs a guaranteed event uses a controlled RNG so no test is flaky.
/// </summary>
[TestFixture]
public class EventResponseTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build an EventSystem with a deterministic RNG that always wants to trigger an event
    /// AND always picks FireBreak (no fire station in the grid means the uncovered branch fires).
    /// Returns the system plus a grid that has a police station (no fire station →
    /// FireBreak forced in the small-city ≤ 200 path is irrelevant here; we use pop 500).
    /// </summary>
    private static (EventSystem events, CityGrid grid) MakeFireBreakSetup()
    {
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid   = new CityGrid(10, 10);
        // No fire station → FireBreak is always chosen for small-city path
        grid.SetZone(1, 2, ZoneType.PoliceStation); // police only → FireBreak forced
        return (events, grid);
    }

    /// <summary>Tick until an event fires, returning true when one fires within maxTicks.</summary>
    private static bool TickUntilEvent(EventSystem events, CityGrid grid, int population = 500, int maxTicks = 300)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var ev = events.Tick(grid, population);
            if (ev != null) return true;
        }
        return false;
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Test]
    public void FireBreak_SetsActiveResponse_WithCost800()
    {
        var (events, grid) = MakeFireBreakSetup();
        // Add an occupied residential tile so FireTileX/Y gets set
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPopulation(5, 5, 10);

        var fired = TickUntilEvent(events, grid);

        Assert.That(fired, Is.True, "FireBreak should have fired");
        Assert.That(events.ActiveEvent?.Type, Is.EqualTo(CityEventType.FireBreak));
        Assert.That(events.ActiveResponse, Is.Not.Null);
        Assert.That(events.ActiveResponse!.EventType, Is.EqualTo("FireBreak"));
        Assert.That(events.ActiveResponse.Cost, Is.EqualTo(800));
    }

    [Test]
    public void RespondToEvent_DeductsCostFromBudget()
    {
        var (events, grid) = MakeFireBreakSetup();
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPopulation(5, 5, 10);
        TickUntilEvent(events, grid);

        var budget = new BudgetSystem(5_000);
        var balanceBefore = budget.Balance;

        events.RespondToEvent(budget);

        Assert.That(budget.Balance, Is.EqualTo(balanceBefore - 800).Within(0.01));
    }

    [Test]
    public void RespondToEvent_ReturnsFalse_WhenInsufficientFunds()
    {
        var (events, grid) = MakeFireBreakSetup();
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPopulation(5, 5, 10);
        TickUntilEvent(events, grid);

        var budget = new BudgetSystem(100); // only $100 — cannot afford $800

        var result = events.RespondToEvent(budget);

        Assert.That(result, Is.False, "Should return false when balance < intervention cost");
        Assert.That(budget.Balance, Is.EqualTo(100).Within(0.01), "Balance should be unchanged");
    }

    [Test]
    public void RespondToEvent_ReturnsTrue_WhenFundsSufficient()
    {
        var (events, grid) = MakeFireBreakSetup();
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPopulation(5, 5, 10);
        TickUntilEvent(events, grid);

        var budget = new BudgetSystem(10_000);

        var result = events.RespondToEvent(budget);

        Assert.That(result, Is.True, "Should return true when balance >= cost");
    }

    [Test]
    public void RespondToEvent_SetsRespondedFlag()
    {
        var (events, grid) = MakeFireBreakSetup();
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPopulation(5, 5, 10);
        TickUntilEvent(events, grid);

        var budget = new BudgetSystem(10_000);
        events.RespondToEvent(budget);

        Assert.That(events.ActiveResponse!.Responded, Is.True,
            "Responded flag should be true after intervention");
    }

    [Test]
    public void ActiveResponse_ClearedAfterEventEndsNaturally()
    {
        var (events, grid) = MakeFireBreakSetup();
        // Add fire station so event ends in 20 ticks (covered) and no demolition
        grid.SetZone(2, 2, ZoneType.FireStation);
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPopulation(5, 5, 10);
        TickUntilEvent(events, grid);

        Assert.That(events.ActiveResponse, Is.Not.Null, "Response should be set when event fires");

        // Tick through the entire event duration (≤ 60 ticks)
        for (var i = 0; i < 70; i++)
            events.Tick(grid, 500);

        Assert.That(events.HasActiveEvent, Is.False, "Event should have ended");
        Assert.That(events.ActiveResponse, Is.Null,
            "ActiveResponse should be cleared after event ends naturally");
    }

    [Test]
    public void RespondToEvent_ReturnsFalse_WhenNoActiveEvent()
    {
        var events = new EventSystem(new AlwaysTriggerRng());

        // No event fired — ActiveResponse is null
        var budget = new BudgetSystem(10_000);
        var result = events.RespondToEvent(budget);

        Assert.That(result, Is.False, "Should return false when no event is pending");
    }

    [Test]
    public void CrimeWave_ResponseCosts600()
    {
        // Sequence RNG: 0.0 triggers, then 0.0 (< 0.5) picks CrimeWave when only police missing.
        // Grid has a fire station but no police station → CrimeWave forced.
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid   = new CityGrid(10, 10);
        grid.SetZone(1, 1, ZoneType.FireStation); // fire station → police missing → CrimeWave

        TickUntilEvent(events, grid);

        Assert.That(events.ActiveEvent?.Type, Is.EqualTo(CityEventType.CrimeWave));
        Assert.That(events.ActiveResponse, Is.Not.Null);
        Assert.That(events.ActiveResponse!.Cost, Is.EqualTo(600),
            "CrimeWave intervention should cost $600");
    }

    [Test]
    public void RespondToEvent_ResolvesEventFasterThanFullDuration()
    {
        var (events, grid) = MakeFireBreakSetup();
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPopulation(5, 5, 10);
        TickUntilEvent(events, grid);

        // The event without a fire station lasts 60 ticks; after response it should end <= 5 more ticks
        var budget = new BudgetSystem(10_000);
        events.RespondToEvent(budget);

        for (var i = 0; i < 5; i++)
            events.Tick(grid, 500);

        Assert.That(events.HasActiveEvent, Is.False,
            "After intervention the event should resolve in 5 or fewer ticks");
    }

    [Test]
    public void SimulationEngine_ExposesHasPendingEvent_WhenEventFires()
    {
        var grid   = new CityGrid(10, 10);
        // Seed 5 cottage tiles (res_house_1x1 sustains at capacity=25 without power).
        // 5 × 25 = 125 pop — above the 100-pop event threshold, stable every tick.
        for (var r = 0; r < 5; r++)
        {
            grid.SetZone(r, 0, ZoneType.Residential);
            grid.SetBuildingId(r, 0, "res_house_1x1");
            grid.SetPopulation(r, 0, 25);
        }
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetZone(5, 6, ZoneType.Road);
        grid.SetPopulation(5, 5, 10);

        var eventSystem = new EventSystem(new AlwaysTriggerRng());
        var engine = new SimulationEngine(
            grid,
            new BudgetSystem(10_000),
            new PopulationSystem(),
            new PowerNetwork(),
            new RoadNetwork(),
            new DemandSystem(),
            eventSystem: eventSystem,
            seed: 42);

        // Tick until an event fires (population seeded to 500 via setup override is not possible
        // here, so we tick until the event system's cooldown expires and RNG forces a fire)
        var hasPending = false;
        for (var i = 0; i < 500; i++)
        {
            engine.Tick();
            if (engine.HasPendingEvent) { hasPending = true; break; }
        }

        Assert.That(hasPending, Is.True,
            "SimulationEngine.HasPendingEvent should become true when an event fires");
        Assert.That(engine.PendingEventType, Is.Not.Null.And.Not.Empty);
        Assert.That(engine.PendingEventCost, Is.GreaterThan(0));
    }

    [Test]
    public void RespondToCurrentEvent_ThroughSimulationEngine_DeductsBudget()
    {
        var grid   = new CityGrid(10, 10);
        // Seed 5 cottage tiles — stable at capacity=25 without power, total 125 pop > 100 threshold
        for (var r = 0; r < 5; r++)
        {
            grid.SetZone(r, 0, ZoneType.Residential);
            grid.SetBuildingId(r, 0, "res_house_1x1");
            grid.SetPopulation(r, 0, 25);
        }
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetZone(5, 6, ZoneType.Road);
        grid.SetPopulation(5, 5, 10);

        var eventSystem = new EventSystem(new AlwaysTriggerRng());
        var budget      = new BudgetSystem(50_000);
        var engine = new SimulationEngine(
            grid,
            budget,
            new PopulationSystem(),
            new PowerNetwork(),
            new RoadNetwork(),
            new DemandSystem(),
            eventSystem: eventSystem,
            seed: 42);

        // Tick until pending event appears
        for (var i = 0; i < 500 && !engine.HasPendingEvent; i++)
            engine.Tick();

        Assert.That(engine.HasPendingEvent, Is.True, "An event must be pending for this test");

        var balanceBefore = budget.Balance;
        var cost          = engine.PendingEventCost;
        var result        = engine.RespondToCurrentEvent();

        Assert.That(result, Is.True, "RespondToCurrentEvent should succeed with sufficient funds");
        Assert.That(budget.Balance, Is.EqualTo(balanceBefore - cost).Within(0.01),
            "RespondToCurrentEvent should deduct the cost from the budget");
    }
}
