using Loopolis.Core.Charters;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;
using NUnit.Framework;

namespace Loopolis.Core.Tests.Charters;

[TestFixture]
public class CityCharterSystemTests
{
    private CharterSystem _system = null!;

    [SetUp]
    public void SetUp() => _system = new CharterSystem();

    // ── Basic state tests ─────────────────────────────────────────────────────

    [Test]
    public void CityCharterPending_IsFalse_AtStart()
    {
        Assert.That(_system.CityCharterPending, Is.False);
        Assert.That(_system.CityCharter, Is.EqualTo(CharterType.None));
    }

    [Test]
    public void NotifyCityMilestone_SetsPending()
    {
        _system.NotifyCityMilestone();
        Assert.That(_system.CityCharterPending, Is.True);
    }

    [Test]
    public void NotifyCityMilestone_DoesNotSetPending_WhenCityCharterAlreadyChosen()
    {
        _system.SelectCityCharter(CharterType.InnovationHub);
        _system.NotifyCityMilestone();
        Assert.That(_system.CityCharterPending, Is.False);
    }

    [Test]
    public void SelectCityCharter_None_DoesNothing()
    {
        _system.NotifyCityMilestone();
        _system.SelectCityCharter(CharterType.None);

        Assert.That(_system.CityCharterPending, Is.True,
            "CityCharterPending must remain true after SelectCityCharter(None) — it is a no-op");
        Assert.That(_system.CityCharter, Is.EqualTo(CharterType.None));
    }

    [Test]
    public void SelectCityCharter_IsIdempotent()
    {
        _system.SelectCityCharter(CharterType.InnovationHub);
        _system.SelectCityCharter(CharterType.GreenCanopy); // attempt to change — should be ignored

        Assert.That(_system.CityCharter, Is.EqualTo(CharterType.InnovationHub),
            "City charter cannot be changed once chosen");
    }

    [Test]
    public void SelectCityCharter_ClearsCityCharterPending()
    {
        _system.NotifyCityMilestone();
        _system.SelectCityCharter(CharterType.GreenCanopy);

        Assert.That(_system.CityCharterPending, Is.False);
        Assert.That(_system.CityCharter, Is.EqualTo(CharterType.GreenCanopy));
    }

    // ── InnovationHub modifier tests ──────────────────────────────────────────

    [Test]
    public void InnovationHub_ResidentialCapacityBonus_Is0_20()
    {
        _system.SelectCityCharter(CharterType.InnovationHub);
        Assert.That(_system.CityResidentialCapacityBonus, Is.EqualTo(0.20).Within(0.001));
    }

    [Test]
    public void InnovationHub_TaxRateModifier_Is0_08()
    {
        _system.SelectCityCharter(CharterType.InnovationHub);
        Assert.That(_system.CityTaxRateModifier, Is.EqualTo(0.08).Within(0.001));
    }

    [Test]
    public void InnovationHub_OtherModifiers_AreNeutral()
    {
        _system.SelectCityCharter(CharterType.InnovationHub);
        Assert.Multiple(() =>
        {
            Assert.That(_system.CityPollutionMultiplier,        Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(_system.CityParkRadiusBonus,            Is.EqualTo(0));
            Assert.That(_system.CityCommercialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
            Assert.That(_system.CityLandValueBonus,             Is.EqualTo(0.0).Within(0.001));
        });
    }

    // ── GreenCanopy modifier tests ────────────────────────────────────────────

    [Test]
    public void GreenCanopy_PollutionMultiplier_Is0_5()
    {
        _system.SelectCityCharter(CharterType.GreenCanopy);
        Assert.That(_system.CityPollutionMultiplier, Is.EqualTo(0.5f).Within(0.001f));
    }

    [Test]
    public void GreenCanopy_ParkRadiusBonus_Is2()
    {
        _system.SelectCityCharter(CharterType.GreenCanopy);
        Assert.That(_system.CityParkRadiusBonus, Is.EqualTo(2));
    }

    [Test]
    public void GreenCanopy_OtherModifiers_AreNeutral()
    {
        _system.SelectCityCharter(CharterType.GreenCanopy);
        Assert.Multiple(() =>
        {
            Assert.That(_system.CityResidentialCapacityBonus,   Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.CityTaxRateModifier,            Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.CityCommercialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
            Assert.That(_system.CityLandValueBonus,             Is.EqualTo(0.0).Within(0.001));
        });
    }

    // ── TradeCorridors modifier tests ─────────────────────────────────────────

    [Test]
    public void TradeCorridors_CommercialMultiplier_Is1_25()
    {
        _system.SelectCityCharter(CharterType.TradeCorridors);
        Assert.That(_system.CityCommercialGrowthMultiplier, Is.EqualTo(1.25).Within(0.001));
    }

    [Test]
    public void TradeCorridors_LandValueBonus_Is0_08()
    {
        _system.SelectCityCharter(CharterType.TradeCorridors);
        Assert.That(_system.CityLandValueBonus, Is.EqualTo(0.08).Within(0.001));
    }

    [Test]
    public void TradeCorridors_OtherModifiers_AreNeutral()
    {
        _system.SelectCityCharter(CharterType.TradeCorridors);
        Assert.Multiple(() =>
        {
            Assert.That(_system.CityResidentialCapacityBonus, Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.CityTaxRateModifier,          Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.CityPollutionMultiplier,      Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(_system.CityParkRadiusBonus,          Is.EqualTo(0));
        });
    }

    // ── None city charter — neutral values ────────────────────────────────────

    [Test]
    public void NoCityCharter_AllModifiers_AreNeutral()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_system.CityResidentialCapacityBonus,   Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.CityTaxRateModifier,            Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.CityPollutionMultiplier,        Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(_system.CityParkRadiusBonus,            Is.EqualTo(0));
            Assert.That(_system.CityCommercialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
            Assert.That(_system.CityLandValueBonus,             Is.EqualTo(0.0).Within(0.001));
        });
    }

    // ── Independence from Town charter ───────────────────────────────────────

    [Test]
    public void CityCharter_DoesNotInterfere_WithTownCharter()
    {
        // Both charters are independent; selecting one should not affect the other
        _system.SelectCharter(CharterType.Merchant);
        _system.SelectCityCharter(CharterType.TradeCorridors);

        Assert.Multiple(() =>
        {
            Assert.That(_system.ActiveCharter, Is.EqualTo(CharterType.Merchant),
                "Town charter should remain Merchant");
            Assert.That(_system.CityCharter, Is.EqualTo(CharterType.TradeCorridors),
                "City charter should be TradeCorridors");
            // Town charter modifiers intact
            Assert.That(_system.CommercialGrowthMultiplier, Is.EqualTo(1.30).Within(0.001));
            Assert.That(_system.LandValueBonus,             Is.EqualTo(0.06).Within(0.001));
            // City charter modifiers intact
            Assert.That(_system.CityCommercialGrowthMultiplier, Is.EqualTo(1.25).Within(0.001));
            Assert.That(_system.CityLandValueBonus,             Is.EqualTo(0.08).Within(0.001));
        });
    }

    [Test]
    public void TownCharter_DoesNotInterfere_WithCityCharter()
    {
        // Selecting a Town charter after a City charter should not affect the City charter
        _system.SelectCityCharter(CharterType.GreenCanopy);
        _system.SelectCharter(CharterType.Civic);

        Assert.Multiple(() =>
        {
            Assert.That(_system.CityCharter, Is.EqualTo(CharterType.GreenCanopy));
            Assert.That(_system.ActiveCharter, Is.EqualTo(CharterType.Civic));
        });
    }

    // ── CharterLibrary tests ──────────────────────────────────────────────────

    [Test]
    public void CharterLibrary_ContainsThreeCityCharters()
    {
        Assert.That(CharterLibrary.AllCityCharters.Count, Is.EqualTo(3));
    }

    [Test]
    public void CharterLibrary_CityCharters_HaveCityEra()
    {
        foreach (var charter in CharterLibrary.AllCityCharters)
            Assert.That(charter.Era, Is.EqualTo("City"));
    }

    [Test]
    public void CharterLibrary_CityCharters_HaveNonEmptyStrings()
    {
        foreach (var charter in CharterLibrary.AllCityCharters)
        {
            Assert.That(charter.Name,        Is.Not.Empty);
            Assert.That(charter.Description, Is.Not.Empty);
            Assert.That(charter.Effect,      Is.Not.Empty);
        }
    }

    [Test]
    public void CharterLibrary_Find_ReturnsCity_Charter()
    {
        var def = CharterLibrary.Find(CharterType.InnovationHub);

        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Name, Is.EqualTo("Innovation Hub"));
        Assert.That(def.Era,   Is.EqualTo("City"));
    }

    [Test]
    public void CharterLibrary_Find_ReturnsGreenCanopy()
    {
        var def = CharterLibrary.Find(CharterType.GreenCanopy);
        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Name, Is.EqualTo("Green Canopy"));
    }

    [Test]
    public void CharterLibrary_Find_ReturnsTradeCorridors()
    {
        var def = CharterLibrary.Find(CharterType.TradeCorridors);
        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Name, Is.EqualTo("Trade Corridors"));
    }

    // ── SimulationEngine integration ─────────────────────────────────────────

    [Test]
    public void SimulationEngine_ExposesCityCharter_Properties()
    {
        var grid   = new CityGrid(10, 10);
        var engine = new SimulationEngine(grid, new BudgetSystem(), new PopulationSystem(),
            new PowerNetwork(), new RoadNetwork(), new DemandSystem(), seed: 42);

        Assert.That(engine.Charters.CityCharter, Is.EqualTo(CharterType.None));
        Assert.That(engine.Charters.CityCharterPending, Is.False);
    }

    [Test]
    public void SimulationEngine_NotifiesCityCharter_WhenCityMilestoneReached()
    {
        // Build a large city that can reach 5,000 population
        var grid = new CityGrid(20, 20);
        grid.SetFlatTerrain();
        grid.SetZone(0, 9, ZoneType.CoalPlant);
        for (var x = 1; x <= 18; x++) grid.SetZone(x, 9, ZoneType.Road);
        // Dense residential above and below the road
        for (var x = 1; x <= 18; x++)
        for (var y = 5; y <= 8; y++)
            grid.SetZone(x, y, ZoneType.Residential);
        for (var x = 1; x <= 18; x++)
        for (var y = 10; y <= 13; y++)
            grid.SetZone(x, y, ZoneType.Residential);

        var budget = new BudgetSystem(initialBalance: 100_000); // prevent bankruptcy during long run

        var engine = new SimulationEngine(grid, budget, new PopulationSystem(),
            new PowerNetwork(), new RoadNetwork(), new DemandSystem(), seed: 42);
        engine.SeedRoadGraphFromGrid();

        // First select a town charter so the engine doesn't wait on that
        engine.Charters.SelectCharter(CharterType.Merchant);

        // Run until City milestone or bail out
        var reachedCity = false;
        for (var t = 0; t < 5000; t++)
        {
            engine.Tick();
            if (engine.MilestoneSystem.CurrentState is GameState.City
                or GameState.Metropolis or GameState.Loopolis)
            {
                reachedCity = true;
                break;
            }
        }

        Assert.That(reachedCity, Is.True, "City should reach City milestone within 5000 ticks");
        Assert.That(engine.Charters.CityCharterPending, Is.True,
            "CityCharterPending should be true after reaching City with no city charter chosen");
    }
}
