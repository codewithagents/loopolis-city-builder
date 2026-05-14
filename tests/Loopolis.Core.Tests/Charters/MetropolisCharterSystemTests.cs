using Loopolis.Core.Charters;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;
using NUnit.Framework;

namespace Loopolis.Core.Tests.Charters;

[TestFixture]
public class MetropolisCharterSystemTests
{
    private CharterSystem _system = null!;

    [SetUp]
    public void SetUp() => _system = new CharterSystem();

    // ── Basic state tests ─────────────────────────────────────────────────────

    [Test]
    public void MetropolisCharterPending_IsFalse_AtStart()
    {
        Assert.That(_system.MetropolisCharterPending, Is.False);
        Assert.That(_system.MetropolisCharter, Is.EqualTo(CharterType.None));
    }

    [Test]
    public void NotifyMetropolisMilestone_SetsPending()
    {
        _system.NotifyMetropolisMilestone();
        Assert.That(_system.MetropolisCharterPending, Is.True);
    }

    [Test]
    public void NotifyMetropolisMilestone_DoesNotSetPending_WhenCharterAlreadyChosen()
    {
        _system.SelectMetropolisCharter(CharterType.NexusCity);
        _system.NotifyMetropolisMilestone();
        Assert.That(_system.MetropolisCharterPending, Is.False);
    }

    [Test]
    public void SelectMetropolisCharter_None_DoesNothing()
    {
        _system.NotifyMetropolisMilestone();
        _system.SelectMetropolisCharter(CharterType.None);

        Assert.That(_system.MetropolisCharterPending, Is.True,
            "MetropolisCharterPending must remain true after SelectMetropolisCharter(None) — it is a no-op");
        Assert.That(_system.MetropolisCharter, Is.EqualTo(CharterType.None));
    }

    [Test]
    public void SelectMetropolisCharter_IsIdempotent()
    {
        _system.SelectMetropolisCharter(CharterType.NexusCity);
        _system.SelectMetropolisCharter(CharterType.GreenUtopia); // attempt to change — should be ignored

        Assert.That(_system.MetropolisCharter, Is.EqualTo(CharterType.NexusCity),
            "Metropolis charter cannot be changed once chosen");
    }

    [Test]
    public void SelectMetropolisCharter_ClearsMetropolisCharterPending()
    {
        _system.NotifyMetropolisMilestone();
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);

        Assert.That(_system.MetropolisCharterPending, Is.False);
        Assert.That(_system.MetropolisCharter, Is.EqualTo(CharterType.EmpireOfSteel));
    }

    // ── NexusCity modifier tests ──────────────────────────────────────────────

    [Test]
    public void NexusCity_ServiceRadiusBonus_Is5()
    {
        _system.SelectMetropolisCharter(CharterType.NexusCity);
        Assert.That(_system.MetropolisServiceRadiusBonus, Is.EqualTo(5.0f).Within(0.001f));
    }

    [Test]
    public void NexusCity_ResidentialCapacityBonus_Is0_30()
    {
        _system.SelectMetropolisCharter(CharterType.NexusCity);
        Assert.That(_system.MetropolisResidentialCapacityBonus, Is.EqualTo(0.30).Within(0.001));
    }

    [Test]
    public void NexusCity_TaxRateModifier_Is0_08()
    {
        _system.SelectMetropolisCharter(CharterType.NexusCity);
        Assert.That(_system.MetropolisTaxRateModifier, Is.EqualTo(0.08).Within(0.001));
    }

    [Test]
    public void NexusCity_OtherModifiers_AreNeutral()
    {
        _system.SelectMetropolisCharter(CharterType.NexusCity);
        Assert.Multiple(() =>
        {
            Assert.That(_system.MetropolisPollutionMultiplier,        Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(_system.MetropolisParkHappinessMultiplier,    Is.EqualTo(1.0).Within(0.001));
            Assert.That(_system.MetropolisParkRadiusBonus,            Is.EqualTo(0));
            Assert.That(_system.MetropolisIndustrialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
            Assert.That(_system.MetropolisJobsPerTileBonus,           Is.EqualTo(0));
            Assert.That(_system.MetropolisCommercialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
        });
    }

    // ── GreenUtopia modifier tests ────────────────────────────────────────────

    [Test]
    public void GreenUtopia_PollutionMultiplier_Is0_25()
    {
        _system.SelectMetropolisCharter(CharterType.GreenUtopia);
        Assert.That(_system.MetropolisPollutionMultiplier, Is.EqualTo(0.25f).Within(0.001f));
    }

    [Test]
    public void GreenUtopia_ParkHappinessMultiplier_Is3_0()
    {
        _system.SelectMetropolisCharter(CharterType.GreenUtopia);
        Assert.That(_system.MetropolisParkHappinessMultiplier, Is.EqualTo(3.0).Within(0.001));
    }

    [Test]
    public void GreenUtopia_ParkRadiusBonus_Is3()
    {
        _system.SelectMetropolisCharter(CharterType.GreenUtopia);
        Assert.That(_system.MetropolisParkRadiusBonus, Is.EqualTo(3));
    }

    [Test]
    public void GreenUtopia_OtherModifiers_AreNeutral()
    {
        _system.SelectMetropolisCharter(CharterType.GreenUtopia);
        Assert.Multiple(() =>
        {
            Assert.That(_system.MetropolisServiceRadiusBonus,         Is.EqualTo(0f).Within(0.001f));
            Assert.That(_system.MetropolisResidentialCapacityBonus,   Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.MetropolisTaxRateModifier,            Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.MetropolisIndustrialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
            Assert.That(_system.MetropolisJobsPerTileBonus,           Is.EqualTo(0));
            Assert.That(_system.MetropolisCommercialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
        });
    }

    // ── EmpireOfSteel modifier tests ──────────────────────────────────────────

    [Test]
    public void EmpireOfSteel_IndustrialGrowthMultiplier_Is1_6()
    {
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);
        Assert.That(_system.MetropolisIndustrialGrowthMultiplier, Is.EqualTo(1.6).Within(0.001));
    }

    [Test]
    public void EmpireOfSteel_JobsPerTileBonus_Is25()
    {
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);
        Assert.That(_system.MetropolisJobsPerTileBonus, Is.EqualTo(25));
    }

    [Test]
    public void EmpireOfSteel_CommercialGrowthMultiplier_Is1_3()
    {
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);
        Assert.That(_system.MetropolisCommercialGrowthMultiplier, Is.EqualTo(1.3).Within(0.001));
    }

    [Test]
    public void EmpireOfSteel_OtherModifiers_AreNeutral()
    {
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);
        Assert.Multiple(() =>
        {
            Assert.That(_system.MetropolisServiceRadiusBonus,       Is.EqualTo(0f).Within(0.001f));
            Assert.That(_system.MetropolisResidentialCapacityBonus, Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.MetropolisTaxRateModifier,          Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.MetropolisPollutionMultiplier,      Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(_system.MetropolisParkHappinessMultiplier,  Is.EqualTo(1.0).Within(0.001));
            Assert.That(_system.MetropolisParkRadiusBonus,          Is.EqualTo(0));
        });
    }

    // ── No Metropolis charter — neutral values ────────────────────────────────

    [Test]
    public void NoMetropolisCharter_AllModifiers_AreNeutral()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_system.MetropolisServiceRadiusBonus,         Is.EqualTo(0f).Within(0.001f));
            Assert.That(_system.MetropolisResidentialCapacityBonus,   Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.MetropolisTaxRateModifier,            Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.MetropolisPollutionMultiplier,        Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(_system.MetropolisParkHappinessMultiplier,    Is.EqualTo(1.0).Within(0.001));
            Assert.That(_system.MetropolisParkRadiusBonus,            Is.EqualTo(0));
            Assert.That(_system.MetropolisIndustrialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
            Assert.That(_system.MetropolisJobsPerTileBonus,           Is.EqualTo(0));
            Assert.That(_system.MetropolisCommercialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
        });
    }

    // ── Independence from Town and City charters ──────────────────────────────

    [Test]
    public void MetropolisCharter_DoesNotInterfere_WithTownOrCityCharter()
    {
        _system.SelectCharter(CharterType.Industrial);
        _system.SelectCityCharter(CharterType.TradeCorridors);
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);

        Assert.Multiple(() =>
        {
            Assert.That(_system.ActiveCharter,     Is.EqualTo(CharterType.Industrial));
            Assert.That(_system.CityCharter,       Is.EqualTo(CharterType.TradeCorridors));
            Assert.That(_system.MetropolisCharter, Is.EqualTo(CharterType.EmpireOfSteel));
            // Town modifiers intact
            Assert.That(_system.IndustrialGrowthMultiplier, Is.EqualTo(1.35).Within(0.001));
            // City modifiers intact
            Assert.That(_system.CityCommercialGrowthMultiplier, Is.EqualTo(1.25).Within(0.001));
            // Metropolis modifiers intact
            Assert.That(_system.MetropolisIndustrialGrowthMultiplier, Is.EqualTo(1.6).Within(0.001));
        });
    }

    [Test]
    public void TownAndCityCharter_DoNotInterfere_WithMetropolisCharter()
    {
        _system.SelectMetropolisCharter(CharterType.GreenUtopia);
        _system.SelectCharter(CharterType.Civic);
        _system.SelectCityCharter(CharterType.GreenCanopy);

        Assert.Multiple(() =>
        {
            Assert.That(_system.MetropolisCharter, Is.EqualTo(CharterType.GreenUtopia));
            Assert.That(_system.ActiveCharter,     Is.EqualTo(CharterType.Civic));
            Assert.That(_system.CityCharter,       Is.EqualTo(CharterType.GreenCanopy));
        });
    }

    // ── CharterLibrary tests ──────────────────────────────────────────────────

    [Test]
    public void CharterLibrary_ContainsThreeMetropolisCharters()
    {
        Assert.That(CharterLibrary.AllMetropolisCharters.Count, Is.EqualTo(3));
    }

    [Test]
    public void CharterLibrary_MetropolisCharters_HaveMetropolisEra()
    {
        foreach (var charter in CharterLibrary.AllMetropolisCharters)
            Assert.That(charter.Era, Is.EqualTo("Metropolis"));
    }

    [Test]
    public void CharterLibrary_MetropolisCharters_HaveNonEmptyStrings()
    {
        foreach (var charter in CharterLibrary.AllMetropolisCharters)
        {
            Assert.That(charter.Name,        Is.Not.Empty);
            Assert.That(charter.Description, Is.Not.Empty);
            Assert.That(charter.Effect,      Is.Not.Empty);
        }
    }

    [Test]
    public void CharterLibrary_Find_ReturnsNexusCity()
    {
        var def = CharterLibrary.Find(CharterType.NexusCity);

        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Name, Is.EqualTo("Nexus City"));
        Assert.That(def.Era,   Is.EqualTo("Metropolis"));
    }

    [Test]
    public void CharterLibrary_Find_ReturnsGreenUtopia()
    {
        var def = CharterLibrary.Find(CharterType.GreenUtopia);

        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Name, Is.EqualTo("Green Utopia"));
        Assert.That(def.Era,   Is.EqualTo("Metropolis"));
    }

    [Test]
    public void CharterLibrary_Find_ReturnsEmpireOfSteel()
    {
        var def = CharterLibrary.Find(CharterType.EmpireOfSteel);

        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Name, Is.EqualTo("Empire of Steel"));
        Assert.That(def.Era,   Is.EqualTo("Metropolis"));
    }

    // ── EmpireOfSteel land value bonus ───────────────────────────────────────

    [Test]
    public void EmpireOfSteel_LandValueBonus_Is0_10()
    {
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);
        Assert.That(_system.MetropolisLandValueBonus, Is.EqualTo(0.10).Within(0.001));
    }

    [Test]
    public void NexusCity_LandValueBonus_IsZero()
    {
        var cs = new CharterSystem();
        cs.SelectMetropolisCharter(CharterType.NexusCity);
        Assert.That(cs.MetropolisLandValueBonus, Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void GreenUtopia_LandValueBonus_IsZero()
    {
        var cs = new CharterSystem();
        cs.SelectMetropolisCharter(CharterType.GreenUtopia);
        Assert.That(cs.MetropolisLandValueBonus, Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void NoMetropolisCharter_LandValueBonus_IsZero()
    {
        Assert.That(_system.MetropolisLandValueBonus, Is.EqualTo(0.0).Within(0.001));
    }

    // ── SimulationEngine integration ─────────────────────────────────────────

    [Test]
    public void SimulationEngine_ExposesMetropolisCharter_Properties()
    {
        var grid   = new CityGrid(10, 10);
        var engine = new SimulationEngine(grid, new BudgetSystem(), new PopulationSystem(),
            new PowerNetwork(), new RoadNetwork(), new DemandSystem(), seed: 42);

        Assert.That(engine.Charters.MetropolisCharter, Is.EqualTo(CharterType.None));
        Assert.That(engine.Charters.MetropolisCharterPending, Is.False);
    }

    [Test]
    public void SimulationEngine_NotifiesMetropolisCharter_WhenMetropolisMilestoneReached()
    {
        // Build a very large city capable of reaching 25,000 population
        var grid = new CityGrid(30, 30);
        grid.SetFlatTerrain();
        // Power plant row
        grid.SetZone(0, 14, ZoneType.CoalPlant);
        grid.SetZone(0, 15, ZoneType.CoalPlant);
        // Road spine
        for (var x = 1; x <= 28; x++) grid.SetZone(x, 14, ZoneType.Road);
        // Dense residential above and below
        for (var x = 1; x <= 28; x++)
        for (var y = 5; y <= 13; y++)
            grid.SetZone(x, y, ZoneType.Residential);
        for (var x = 1; x <= 28; x++)
        for (var y = 15; y <= 23; y++)
            grid.SetZone(x, y, ZoneType.Residential);

        var budget = new BudgetSystem(initialBalance: 500_000); // prevent bankruptcy

        var engine = new SimulationEngine(grid, budget, new PopulationSystem(),
            new PowerNetwork(), new RoadNetwork(), new DemandSystem(), seed: 42);
        engine.SeedRoadGraphFromGrid();

        // Pre-select Town and City charters so the engine doesn't wait on them
        engine.Charters.SelectCharter(CharterType.Merchant);
        engine.Charters.SelectCityCharter(CharterType.InnovationHub);

        // Run until Metropolis milestone or bail out
        var reachedMetropolis = false;
        for (var t = 0; t < 15000; t++)
        {
            engine.Tick();
            if (engine.MilestoneSystem.CurrentState is GameState.Metropolis or GameState.Loopolis)
            {
                reachedMetropolis = true;
                break;
            }
        }

        Assert.That(reachedMetropolis, Is.True, "City should reach Metropolis milestone within 15000 ticks");
        Assert.That(engine.Charters.MetropolisCharterPending, Is.True,
            "MetropolisCharterPending should be true after reaching Metropolis with no metropolis charter chosen");
    }
}
