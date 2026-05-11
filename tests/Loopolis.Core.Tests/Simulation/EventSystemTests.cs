using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class EventSystemTests
{
    /// <summary>
    /// Helper: build a minimal grid with a residential zone (no service buildings).
    /// </summary>
    private static CityGrid MakeBasicGrid() => new CityGrid(10, 10);

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
        // Initial cooldown is 100 ticks; run exactly 99 and expect no event
        var events = new EventSystem(new AlwaysTriggerRng());
        var grid = MakeBasicGrid();

        CityEvent? fired = null;
        for (var i = 0; i < 99; i++)
            fired = events.Tick(grid, population: 500) ?? fired;

        Assert.That(fired, Is.Null,
            "No event should fire during the initial 100-tick cooldown period");
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
