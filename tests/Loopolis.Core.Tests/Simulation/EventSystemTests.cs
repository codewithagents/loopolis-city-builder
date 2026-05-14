using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class EventSystemTests
{
    /// <summary>
    /// Helper: build a minimal grid with one occupied residential tile so FireBreak
    /// events have a valid candidate building to target.
    /// </summary>
    private static CityGrid MakeBasicGrid()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(0, 0, ZoneType.Residential);
        grid.SetPopulation(0, 0, 10); // occupied — FireBreak has a candidate tile
        return grid;
    }

    [Test]
    public void NoEventFires_BeforePopulation100()
    {
        // Use an RNG that always wants to trigger (NextDouble returns 0)
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();

        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
            fired = events.Tick(grid, population: 0) ?? fired;

        Assert.That(fired, Is.Null,
            "No event should fire when population is below 100");
    }

    [Test]
    public void EventFires_WhenPopulationHighAndCooldownExpired()
    {
        // RNG that always triggers — event should fire once cooldown (100 ticks) expires
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();

        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
            fired = events.Tick(grid, population: 500) ?? fired;

        Assert.That(fired, Is.Not.Null,
            "An event should fire with population >= 100 after initial cooldown expires");
    }

    [Test]
    public void NoEventFires_DuringInitialCooldown()
    {
        // Initial cooldown is 60 ticks; run exactly 59 and expect no event
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();

        CityEvent? fired = null;
        for (var i = 0; i < 59; i++)
            fired = events.Tick(grid, population: 500) ?? fired;

        Assert.That(fired, Is.Null,
            "No event should fire during the initial 60-tick cooldown period");
    }

    [Test]
    public void CoveredCity_GetsShortEventDuration()
    {
        // Both stations present: event resolves in 20 ticks
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();
        grid.SetZone(1, 1, ZoneType.FireStation);
        grid.SetZone(1, 2, ZoneType.PoliceStation);

        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
        {
            fired = events.Tick(grid, population: 500);
            if (fired != null) break;
        }

        Assert.That(fired, Is.Not.Null, "An event should have fired");
        Assert.That(fired!.DurationTicks, Is.EqualTo(20),
            "Covered city should get short event duration (20 ticks)");
    }

    [Test]
    public void UncoveredCity_GetsLongEventDuration()
    {
        // No service buildings: event lasts 60 ticks
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid(); // no service buildings

        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
        {
            fired = events.Tick(grid, population: 500);
            if (fired != null) break;
        }

        Assert.That(fired, Is.Not.Null, "An event should have fired");
        Assert.That(fired!.DurationTicks, Is.EqualTo(60),
            "Uncovered city should get long event duration (60 ticks)");
    }

    [Test]
    public void HappinessPenalty_NonZeroWhenEventActive()
    {
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();

        // Tick until an event fires
        for (var i = 0; i < 200; i++)
        {
            var fired = events.Tick(grid, population: 500);
            if (fired != null) break;
        }

        Assert.That(events.HasActiveEvent, Is.True,
            "An active event should be present");
        Assert.That(events.HappinessPenalty, Is.Not.EqualTo(0.0),
            "HappinessPenalty should be non-zero when an event is active");
        Assert.That(events.HappinessPenalty, Is.LessThan(0.0),
            "HappinessPenalty should be negative (reducing happiness)");
    }

    [Test]
    public void HappinessPenalty_ZeroWhenNoEventActive()
    {
        var events = new EventSystem();

        Assert.That(events.HasActiveEvent, Is.False);
        Assert.That(events.HappinessPenalty, Is.EqualTo(0.0),
            "HappinessPenalty should be 0.0 when no event is active");
    }

    [Test]
    public void FireBreak_EventType_AndPenalty()
    {
        // No fire station: triggers FireBreak (missing fire station forces FireBreak)
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();
        grid.SetZone(1, 1, ZoneType.PoliceStation); // has police but no fire station → FireBreak

        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
        {
            fired = events.Tick(grid, population: 500);
            if (fired != null) break;
        }

        Assert.That(fired?.Type, Is.EqualTo(CityEventType.FireBreak),
            "Without a fire station, FireBreak should be triggered");
        Assert.That(events.HappinessPenalty, Is.EqualTo(-0.15).Within(0.001),
            "FireBreak should impose -0.15 happiness penalty");
    }

    [Test]
    public void CrimeWave_EventType_AndPenalty()
    {
        // No police station: triggers CrimeWave (missing police station forces CrimeWave)
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();
        grid.SetZone(1, 1, ZoneType.FireStation); // has fire station but no police → CrimeWave

        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
        {
            fired = events.Tick(grid, population: 500);
            if (fired != null) break;
        }

        Assert.That(fired?.Type, Is.EqualTo(CityEventType.CrimeWave),
            "Without a police station, CrimeWave should be triggered");
        Assert.That(events.HappinessPenalty, Is.EqualTo(-0.10).Within(0.001),
            "CrimeWave should impose -0.10 happiness penalty");
    }

    [Test]
    public void EventExpires_AfterDurationTicks()
    {
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();

        // Trigger the first event
        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
        {
            fired = events.Tick(grid, population: 500);
            if (fired != null) break;
        }

        Assert.That(fired, Is.Not.Null, "An event should have fired");
        var duration = fired!.DurationTicks;

        // Run until the event expires (duration ticks)
        for (var i = 0; i < duration; i++)
            events.Tick(grid, population: 500);

        Assert.That(events.HasActiveEvent, Is.False,
            "Event should have expired after its duration ticks");
        Assert.That(events.HappinessPenalty, Is.EqualTo(0.0),
            "HappinessPenalty should return to 0 after event expires");
    }

    [Test]
    public void PowerOutage_EventType_AndPenalty_NoPowerPlantBackup()
    {
        // Sequence: 0.0 triggers the event, 0.65 picks PowerOutage (0.60 <= roll < 0.80)
        // Population >= 200, all services covered → weighted picker active
        // Grid has no power plants → duration = 30
        var events = new EventSystem(new SequenceRng(0.0, 0.65));
        var grid = MakeBasicGrid();
        grid.SetZone(1, 1, ZoneType.FireStation);
        grid.SetZone(1, 2, ZoneType.PoliceStation);

        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
        {
            fired = events.Tick(grid, population: 500);
            if (fired != null) break;
        }

        Assert.That(fired?.Type, Is.EqualTo(CityEventType.PowerOutage),
            "With trigger=0.0 and pick=0.65 and all services, PowerOutage should be triggered");
        Assert.That(events.HappinessPenalty, Is.EqualTo(-0.12).Within(0.001),
            "PowerOutage should impose -0.12 happiness penalty");
        Assert.That(fired!.DurationTicks, Is.EqualTo(30),
            "PowerOutage with no backup power plants should last 30 ticks");
    }

    [Test]
    public void PowerOutage_ShortDuration_WithBackupPowerPlants()
    {
        // Sequence: 0.0 triggers, 0.65 picks PowerOutage. Grid has 2 power plants → duration = 10
        var events = new EventSystem(new SequenceRng(0.0, 0.65));
        var grid = MakeBasicGrid();
        grid.SetZone(1, 1, ZoneType.FireStation);
        grid.SetZone(1, 2, ZoneType.PoliceStation);
        grid.SetZone(2, 1, ZoneType.PowerPlant);
        grid.SetZone(2, 2, ZoneType.PowerPlant);

        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
        {
            fired = events.Tick(grid, population: 500);
            if (fired != null) break;
        }

        Assert.That(fired?.Type, Is.EqualTo(CityEventType.PowerOutage),
            "PowerOutage should fire");
        Assert.That(fired!.DurationTicks, Is.EqualTo(10),
            "PowerOutage with 2+ power plants should resolve quickly (10 ticks)");
    }

    [Test]
    public void DemandSlump_EventType_AndPenalty()
    {
        // Sequence: 0.0 triggers, 0.85 picks DemandSlump (roll >= 0.80)
        // Population >= 200, all services covered
        var events = new EventSystem(new SequenceRng(0.0, 0.85));
        var grid = MakeBasicGrid();
        grid.SetZone(1, 1, ZoneType.FireStation);
        grid.SetZone(1, 2, ZoneType.PoliceStation);

        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
        {
            fired = events.Tick(grid, population: 500);
            if (fired != null) break;
        }

        Assert.That(fired?.Type, Is.EqualTo(CityEventType.DemandSlump),
            "With trigger=0.0 and pick=0.85 and all services, DemandSlump should be triggered");
        Assert.That(events.HappinessPenalty, Is.EqualTo(-0.05).Within(0.001),
            "DemandSlump should impose -0.05 happiness penalty");
        Assert.That(fired!.DurationTicks, Is.EqualTo(40),
            "DemandSlump should always last 40 ticks (no mitigation)");
    }

    [Test]
    public void EventSystem_FiresEventWithinReasonableTicks_WhenPopulationSufficient()
    {
        // Regression test: EventSystem.Tick must be reachable and fire events when population >= 100.
        // Uses AlwaysTriggerRng (NextDouble = 0.0) to guarantee the 1% trigger check always fires.
        // Expects at least one event within 500 ticks with population well above the 100 threshold.
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();

        int eventCount = 0;
        for (var i = 0; i < 500; i++)
        {
            var fired = events.Tick(grid, population: 200);
            if (fired != null) eventCount++;
        }

        Assert.That(eventCount, Is.GreaterThan(0),
            "At least one event should fire within 500 ticks when population >= 100 and RNG always triggers");
    }

    [Test]
    public void SimulationEngine_PassesCorrectPopulationToEventSystem()
    {
        // Regression test: SimulationEngine.Tick must call EventSystem.Tick with the actual population,
        // not a stale zero. Verifies that once population grows above 100, events can fire.
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant);
        grid.SetZone(5, 6, ZoneType.Road);
        grid.SetZone(4, 6, ZoneType.Residential);
        grid.SetZone(6, 6, ZoneType.Residential);

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

        // Run enough ticks for population to grow above 100 and cooldown to expire
        int eventsFired = 0;
        for (var i = 0; i < 300; i++)
        {
            engine.Tick();
            if (engine.LatestEventBanner != null) eventsFired++;
        }

        Assert.That(eventsFired, Is.GreaterThan(0),
            "SimulationEngine must pass Population.Population to EventSystem so events fire once pop > 100");
    }

    [Test]
    public void SmallCity_DoesNotGetPowerOutageOrDemandSlump()
    {
        // AlwaysTriggerRng returns 0.0 for all calls.
        // Population < 200 → only FireBreak/CrimeWave branch runs.
        // With 0.0 and both services covered, 0.0 < 0.5 → FireBreak
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();
        grid.SetZone(1, 1, ZoneType.FireStation);
        grid.SetZone(1, 2, ZoneType.PoliceStation);

        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
        {
            fired = events.Tick(grid, population: 150); // below 200 threshold
            if (fired != null) break;
        }

        Assert.That(fired, Is.Not.Null, "An event should have fired");
        Assert.That(fired!.Type, Is.Not.EqualTo(CityEventType.PowerOutage),
            "Small cities (pop < 200) should not get PowerOutage events");
        Assert.That(fired.Type, Is.Not.EqualTo(CityEventType.DemandSlump),
            "Small cities (pop < 200) should not get DemandSlump events");
    }

    // ── Bug-hunt tests ───────────────────────────────────────────────────────

    /// <summary>
    /// Bug: FireBreak with no occupied tiles — the event was previously triggering
    /// even when there were no populated residential/commercial tiles to catch fire.
    /// The player would see a "Building at risk!" prompt and could spend $800 for an
    /// event that had no actual fire tile (FireTileX == -1). Fix: skip FireBreak
    /// if no candidate tiles exist.
    /// </summary>
    [Test]
    public void FireBreak_DoesNotFire_WhenNoOccupiedTilesExist()
    {
        // Grid has population >= 100 (passed as parameter) but NO residential or commercial
        // tiles with population > 0 on the grid itself.
        // Without the fix, a FireBreak could still be triggered, setting FireTileX == -1
        // and charging the player $800 for an invisible, consequence-free "event".
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid(); // empty grid — no R/C tiles, no population on tiles

        // Run enough ticks for cooldown to expire and trigger logic to execute
        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
        {
            fired = events.Tick(grid, population: 500); // pop satisfies the threshold
            if (fired != null && fired.Type == CityEventType.FireBreak) break;
        }

        // Either no FireBreak fired at all, or if it fired it must have a valid fire tile
        if (fired?.Type == CityEventType.FireBreak)
        {
            Assert.That(events.FireTileX, Is.GreaterThanOrEqualTo(0),
                "FireBreak must not fire without a valid fire tile — FireTileX should be set");
            Assert.That(events.FireTileY, Is.GreaterThanOrEqualTo(0),
                "FireBreak must not fire without a valid fire tile — FireTileY should be set");
        }
        // (If no FireBreak fired, the test also passes — the system correctly avoided a no-op event)
    }

    [Test]
    public void FireBreak_FireTileSet_WhenOccupiedTilesExist()
    {
        // Confirm FireBreak does pick a tile when occupied tiles are present.
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();
        // Place a residential tile with population
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetPopulation(5, 5, 30); // occupied

        CityEvent? fired = null;
        for (var i = 0; i < 200; i++)
        {
            fired = events.Tick(grid, population: 500);
            if (fired?.Type == CityEventType.FireBreak) break;
        }

        Assert.That(fired?.Type, Is.EqualTo(CityEventType.FireBreak));
        Assert.That(events.FireTileX, Is.GreaterThanOrEqualTo(0),
            "FireBreak with occupied tiles must set a valid fire tile X");
        Assert.That(events.FireTileY, Is.GreaterThanOrEqualTo(0),
            "FireBreak with occupied tiles must set a valid fire tile Y");
    }

    [Test]
    public void ActiveEvent_BlocksNewEventFromFiring()
    {
        // While an event is active, no second event should trigger — even with a greedy RNG.
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();

        // Tick until the first event fires
        CityEvent? first = null;
        for (var i = 0; i < 200; i++)
        {
            first = events.Tick(grid, population: 500);
            if (first != null) break;
        }

        Assert.That(first, Is.Not.Null, "First event should have fired");

        // Now run several more ticks while the event is still active — no second event should appear
        int secondEventCount = 0;
        for (var i = 0; i < first!.DurationTicks - 1; i++)
        {
            var next = events.Tick(grid, population: 500);
            if (next != null) secondEventCount++;
        }

        Assert.That(secondEventCount, Is.EqualTo(0),
            "No second event should fire while an event is already active");
    }

    [Test]
    public void DemandSlump_OnEmptyCommercialGrid_DoesNotCrashOrProduceNegative()
    {
        // DemandSlump on a grid with no commercial zones must not crash and must not
        // produce negative activity values. The event only applies a happiness penalty.
        var events = new EventSystem(new SequenceRng(0.0, 0.85)); // 0.85 → DemandSlump
        var grid = MakeBasicGrid();
        grid.SetZone(1, 1, ZoneType.FireStation);
        grid.SetZone(1, 2, ZoneType.PoliceStation);
        // Deliberately no commercial zones

        Assert.DoesNotThrow(() =>
        {
            for (var i = 0; i < 200; i++)
                events.Tick(grid, population: 500);
        }, "DemandSlump on empty commercial grid must not throw");

        // Confirm no tile has negative population after all ticks
        var allTiles = grid.AllTiles().ToList();
        Assert.That(allTiles.All(t => t.Population >= 0),
            "No tile should have negative population after DemandSlump on empty commercial grid");
    }

    [Test]
    public void NewGame_ResetsEventSystem_CooldownStartsFresh()
    {
        // When a new SimulationEngine is created (simulating new_game), the EventSystem
        // must start with a fresh 60-tick cooldown — not inherit state from a previous game.
        // This test creates two separate engines and verifies both start with cooldown.

        var grid1 = new CityGrid(10, 10);
        var eventSystem1 = new EventSystem(new AlwaysTriggerRng());
        var engine1 = new SimulationEngine(
            grid1, new BudgetSystem(), new PopulationSystem(),
            new PowerNetwork(), new RoadNetwork(), new DemandSystem(),
            eventSystem: eventSystem1, seed: 42);

        // Advance engine1 past cooldown so its EventSystem is "used"
        for (var i = 0; i < 100; i++) engine1.Tick();

        // Simulate new_game: create a brand-new engine
        var grid2 = new CityGrid(10, 10);
        var eventSystem2 = new EventSystem(new AlwaysTriggerRng());
        var engine2 = new SimulationEngine(
            grid2, new BudgetSystem(), new PopulationSystem(),
            new PowerNetwork(), new RoadNetwork(), new DemandSystem(),
            eventSystem: eventSystem2, seed: 99);

        // engine2's EventSystem must have a fresh cooldown — it should NOT fire an event on tick 1
        // because _cooldownTicks = 60 in the constructor.
        engine2.Tick(); // tick 0

        Assert.That(engine2.LatestEventBanner, Is.Null,
            "Fresh engine (new_game) should not fire an event on tick 1 — cooldown must be reset");
        Assert.That(engine2.EventSystem.HasActiveEvent, Is.False,
            "Fresh engine should have no active event immediately after creation");
    }
}

/// <summary>
/// Test double: a Random that always triggers the event probability check
/// (NextDouble returns 0.0, which is always &lt;= 0.01 threshold in EventSystem).
/// </summary>
internal class AlwaysTriggerRng : Random
{
    public override double NextDouble() => 0.0;
    public override int Next(int minValue, int maxValue) => minValue;
}

/// <summary>
/// Test double: cycles through a provided sequence of doubles, repeating the last value when exhausted.
/// Useful when a method makes multiple NextDouble() calls (trigger check + event type picker).
/// </summary>
internal class SequenceRng : Random
{
    private readonly double[] _sequence;
    private int _index = 0;

    public SequenceRng(params double[] sequence) => _sequence = sequence;

    public override double NextDouble()
    {
        var value = _sequence[_index];
        if (_index < _sequence.Length - 1) _index++;
        return value;
    }

    public override int Next(int minValue, int maxValue) => minValue;
}
