using Loopolis.Core.Grid;
using Loopolis.Core.Scenarios;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Scenarios;

/// <summary>
/// Tests for the DisabledZones constraint on ScenarioDefinition and
/// the IsZoneAllowed query method on SimulationEngine.
/// </summary>
[TestFixture]
public class ScenarioConstraintsTests
{
    // ── ScenarioDefinition.DisabledZones field ────────────────────────────────

    [Test]
    public void ScenarioDefinition_WithDisabledZones_ReturnsCorrectList()
    {
        var scenario = new ScenarioDefinition(
            Id:              "test",
            Name:            "Test",
            Description:     "Test scenario",
            MapWidth:        32,
            MapHeight:       32,
            StartingBalance: 4_000,
            TickLimit:       0,
            Goal:            new ScenarioGoal(TargetPopulation: 100),
            Medals:          new ScenarioMedals(Bronze: 0, Silver: 0, Gold: 0),
            DisabledZones:   new[] { ZoneType.Industrial }
        );

        Assert.That(scenario.DisabledZones, Is.Not.Null);
        Assert.That(scenario.DisabledZones!.Count, Is.EqualTo(1));
        Assert.That(scenario.DisabledZones, Contains.Item(ZoneType.Industrial));
    }

    // ── SimulationEngine.IsZoneAllowed ────────────────────────────────────────

    private static SimulationEngine MakeEngine(ScenarioDefinition? scenario = null)
    {
        var grid   = new CityGrid(10, 10);
        var budget = new BudgetSystem();
        var pop    = new PopulationSystem();
        var power  = new PowerNetwork();
        var roads  = new RoadNetwork();
        var demand = new DemandSystem();
        var engine = new SimulationEngine(grid, budget, pop, power, roads, demand);
        engine.ActiveScenario = scenario;
        return engine;
    }

    [Test]
    public void IsZoneAllowed_ReturnsFalse_ForDisabledZone()
    {
        var scenario = new ScenarioDefinition(
            Id: "test", Name: "T", Description: "T",
            MapWidth: 32, MapHeight: 32, StartingBalance: 4_000, TickLimit: 0,
            Goal: new ScenarioGoal(TargetPopulation: 100),
            Medals: new ScenarioMedals(Bronze: 0, Silver: 0, Gold: 0),
            DisabledZones: new[] { ZoneType.Industrial }
        );
        var engine = MakeEngine(scenario);

        Assert.That(engine.IsZoneAllowed(ZoneType.Industrial), Is.False);
    }

    [Test]
    public void IsZoneAllowed_ReturnsTrue_ForAllowedZone_WhenOtherDisabled()
    {
        var scenario = new ScenarioDefinition(
            Id: "test", Name: "T", Description: "T",
            MapWidth: 32, MapHeight: 32, StartingBalance: 4_000, TickLimit: 0,
            Goal: new ScenarioGoal(TargetPopulation: 100),
            Medals: new ScenarioMedals(Bronze: 0, Silver: 0, Gold: 0),
            DisabledZones: new[] { ZoneType.Industrial }
        );
        var engine = MakeEngine(scenario);

        Assert.That(engine.IsZoneAllowed(ZoneType.Residential), Is.True);
        Assert.That(engine.IsZoneAllowed(ZoneType.Commercial),  Is.True);
        Assert.That(engine.IsZoneAllowed(ZoneType.Road),        Is.True);
    }

    [Test]
    public void IsZoneAllowed_ReturnsTrue_ForAllZones_WhenNoScenarioActive()
    {
        var engine = MakeEngine(scenario: null);

        Assert.That(engine.IsZoneAllowed(ZoneType.Residential), Is.True);
        Assert.That(engine.IsZoneAllowed(ZoneType.Commercial),  Is.True);
        Assert.That(engine.IsZoneAllowed(ZoneType.Industrial),  Is.True);
        Assert.That(engine.IsZoneAllowed(ZoneType.Road),        Is.True);
        Assert.That(engine.IsZoneAllowed(ZoneType.Park),        Is.True);
    }

    // ── New scenario definitions ──────────────────────────────────────────────

    [Test]
    public void GreenCity_HasDisabledZones_Industrial()
    {
        var scenario = ScenarioLibrary.Find("green_city");
        Assert.That(scenario, Is.Not.Null, "green_city scenario must exist");
        Assert.That(scenario!.DisabledZones, Is.Not.Null, "green_city must have DisabledZones");
        Assert.That(scenario.DisabledZones, Contains.Item(ZoneType.Industrial));
    }

    [Test]
    public void ServiceFirst_HasDisabledZones_Commercial()
    {
        var scenario = ScenarioLibrary.Find("service_first");
        Assert.That(scenario, Is.Not.Null, "service_first scenario must exist");
        Assert.That(scenario!.DisabledZones, Is.Not.Null, "service_first must have DisabledZones");
        Assert.That(scenario.DisabledZones, Contains.Item(ZoneType.Commercial));
    }

    [Test]
    public void ScenarioLibrary_HasSixteenScenarios()
    {
        Assert.That(ScenarioLibrary.All.Count, Is.EqualTo(16));
    }
}
