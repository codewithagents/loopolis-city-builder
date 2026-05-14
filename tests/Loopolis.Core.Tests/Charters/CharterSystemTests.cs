using Loopolis.Core.Charters;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;
using NUnit.Framework;

namespace Loopolis.Core.Tests.Charters;

[TestFixture]
public class CharterSystemTests
{
    private CharterSystem _system = null!;

    [SetUp]
    public void SetUp() => _system = new CharterSystem();

    // ── Basic state tests ─────────────────────────────────────────────────────

    [Test]
    public void InitialState_IsNone_AndNotPending()
    {
        Assert.That(_system.ActiveCharter, Is.EqualTo(CharterType.None));
        Assert.That(_system.TownCharterPending, Is.False);
    }

    [Test]
    public void SelectCharter_SetsMerchant_Correctly()
    {
        _system.SelectCharter(CharterType.Merchant);
        Assert.That(_system.ActiveCharter, Is.EqualTo(CharterType.Merchant));
    }

    [Test]
    public void SelectCharter_SetsIndustrial_Correctly()
    {
        _system.SelectCharter(CharterType.Industrial);
        Assert.That(_system.ActiveCharter, Is.EqualTo(CharterType.Industrial));
    }

    [Test]
    public void SelectCharter_SetsCivic_Correctly()
    {
        _system.SelectCharter(CharterType.Civic);
        Assert.That(_system.ActiveCharter, Is.EqualTo(CharterType.Civic));
    }

    [Test]
    public void SelectCharter_CannotBeChanged_AfterSelection()
    {
        _system.SelectCharter(CharterType.Merchant);
        _system.SelectCharter(CharterType.Industrial); // attempt to change
        Assert.That(_system.ActiveCharter, Is.EqualTo(CharterType.Merchant));
    }

    [Test]
    public void NotifyTownMilestone_SetsTownCharterPending()
    {
        _system.NotifyTownMilestone();
        Assert.That(_system.TownCharterPending, Is.True);
    }

    [Test]
    public void SelectCharter_ClearsTownCharterPending()
    {
        _system.NotifyTownMilestone();
        _system.SelectCharter(CharterType.Civic);
        Assert.That(_system.TownCharterPending, Is.False);
    }

    [Test]
    public void NotifyTownMilestone_DoesNotSetPending_WhenCharterAlreadyChosen()
    {
        _system.SelectCharter(CharterType.Merchant);
        _system.NotifyTownMilestone();
        Assert.That(_system.TownCharterPending, Is.False);
    }

    // ── Merchant charter modifier tests ──────────────────────────────────────

    [Test]
    public void CommercialGrowthMultiplier_IsMerchantBonus_WhenMerchant()
    {
        _system.SelectCharter(CharterType.Merchant);
        Assert.That(_system.CommercialGrowthMultiplier, Is.EqualTo(1.30).Within(0.001));
    }

    [Test]
    public void CommercialGrowthMultiplier_IsOne_WhenIndustrial()
    {
        _system.SelectCharter(CharterType.Industrial);
        Assert.That(_system.CommercialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void CommercialGrowthMultiplier_IsOne_WhenCivic()
    {
        _system.SelectCharter(CharterType.Civic);
        Assert.That(_system.CommercialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void LandValueBonus_IsMerchantBonus_WhenMerchant()
    {
        _system.SelectCharter(CharterType.Merchant);
        Assert.That(_system.LandValueBonus, Is.EqualTo(0.06).Within(0.001));
    }

    [Test]
    public void LandValueBonus_IsZero_WhenNotMerchant()
    {
        _system.SelectCharter(CharterType.Industrial);
        Assert.That(_system.LandValueBonus, Is.EqualTo(0.0).Within(0.001));
    }

    // ── Industrial charter modifier tests ─────────────────────────────────────

    [Test]
    public void IndustrialGrowthMultiplier_IsIndustrialBonus_WhenIndustrial()
    {
        _system.SelectCharter(CharterType.Industrial);
        Assert.That(_system.IndustrialGrowthMultiplier, Is.EqualTo(1.35).Within(0.001));
    }

    [Test]
    public void IndustrialGrowthMultiplier_IsOne_WhenMerchant()
    {
        _system.SelectCharter(CharterType.Merchant);
        Assert.That(_system.IndustrialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void JobsPerTileBonus_IsTen_WhenIndustrial()
    {
        _system.SelectCharter(CharterType.Industrial);
        Assert.That(_system.JobsPerTileBonus, Is.EqualTo(10));
    }

    [Test]
    public void JobsPerTileBonus_IsZero_WhenNotIndustrial()
    {
        _system.SelectCharter(CharterType.Merchant);
        Assert.That(_system.JobsPerTileBonus, Is.EqualTo(0));
    }

    // ── Civic charter modifier tests ──────────────────────────────────────────

    [Test]
    public void ServiceCoverageRadiusBonus_IsThree_WhenCivic()
    {
        _system.SelectCharter(CharterType.Civic);
        Assert.That(_system.ServiceCoverageRadiusBonus, Is.EqualTo(3.0f).Within(0.001f));
    }

    [Test]
    public void ServiceCoverageRadiusBonus_IsZero_WhenNotCivic()
    {
        _system.SelectCharter(CharterType.Merchant);
        Assert.That(_system.ServiceCoverageRadiusBonus, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void ParkHappinessMultiplier_IsTwo_WhenCivic()
    {
        _system.SelectCharter(CharterType.Civic);
        Assert.That(_system.ParkHappinessMultiplier, Is.EqualTo(2.0).Within(0.001));
    }

    [Test]
    public void ParkHappinessMultiplier_IsOne_WhenNotCivic()
    {
        _system.SelectCharter(CharterType.Merchant);
        Assert.That(_system.ParkHappinessMultiplier, Is.EqualTo(1.0).Within(0.001));
    }

    // ── None charter returns neutral values ───────────────────────────────────

    [Test]
    public void NoneCharter_ReturnsAllNeutralValues()
    {
        // No charter selected — all values are neutral
        Assert.Multiple(() =>
        {
            Assert.That(_system.CommercialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
            Assert.That(_system.LandValueBonus, Is.EqualTo(0.0).Within(0.001));
            Assert.That(_system.IndustrialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));
            Assert.That(_system.JobsPerTileBonus, Is.EqualTo(0));
            Assert.That(_system.ServiceCoverageRadiusBonus, Is.EqualTo(0f).Within(0.001f));
            Assert.That(_system.ParkHappinessMultiplier, Is.EqualTo(1.0).Within(0.001));
        });
    }

    // ── CharterLibrary tests ──────────────────────────────────────────────────

    [Test]
    public void CharterLibrary_ContainsThreeTownCharters()
    {
        Assert.That(CharterLibrary.AllTownCharters.Count, Is.EqualTo(3));
    }

    [Test]
    public void CharterLibrary_ContainsMerchantIndustrialCivic()
    {
        var types = CharterLibrary.AllTownCharters.Select(c => c.Type).ToList();
        Assert.That(types, Does.Contain(CharterType.Merchant));
        Assert.That(types, Does.Contain(CharterType.Industrial));
        Assert.That(types, Does.Contain(CharterType.Civic));
    }

    [Test]
    public void CharterLibrary_Find_ReturnsMerchantDefinition()
    {
        var def = CharterLibrary.Find(CharterType.Merchant);
        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Name, Is.EqualTo("Merchant Charter"));
        Assert.That(def.Era, Is.EqualTo("Town"));
    }

    [Test]
    public void CharterLibrary_Find_ReturnsNullForNone()
    {
        var def = CharterLibrary.Find(CharterType.None);
        Assert.That(def, Is.Null);
    }

    [Test]
    public void CharterLibrary_AllCharters_HaveValidEra()
    {
        foreach (var charter in CharterLibrary.AllTownCharters)
            Assert.That(charter.Era, Is.EqualTo("Town"));
    }

    [Test]
    public void CharterLibrary_AllCharters_HaveNonEmptyStrings()
    {
        foreach (var charter in CharterLibrary.AllTownCharters)
        {
            Assert.That(charter.Name,        Is.Not.Empty);
            Assert.That(charter.Description, Is.Not.Empty);
            Assert.That(charter.Effect,      Is.Not.Empty);
        }
    }

    // ── Integration: SimulationEngine wiring ────────────────────────────────

    [Test]
    public void SimulationEngine_ExposeCharters_Property()
    {
        var grid   = new CityGrid(10, 10);
        var engine = new SimulationEngine(grid, new BudgetSystem(), new PopulationSystem(),
            new PowerNetwork(), new RoadNetwork(), new DemandSystem(), seed: 42);

        Assert.That(engine.Charters, Is.Not.Null);
        Assert.That(engine.Charters.ActiveCharter, Is.EqualTo(CharterType.None));
    }

    [Test]
    public void SimulationEngine_NotifiesTownCharter_WhenTownReached()
    {
        // Arrange: a powered city with many residential zones to reach 500 pop quickly
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();
        grid.SetZone(0, 7, ZoneType.CoalPlant);
        for (var x = 1; x <= 12; x++) grid.SetZone(x, 7, ZoneType.Road);
        for (var x = 1; x <= 12; x++)
        for (var y = 5; y <= 6; y++)
            grid.SetZone(x, y, ZoneType.Residential);
        for (var x = 1; x <= 12; x++)
        for (var y = 8; y <= 9; y++)
            grid.SetZone(x, y, ZoneType.Residential);

        var engine = new SimulationEngine(grid, new BudgetSystem(), new PopulationSystem(),
            new PowerNetwork(), new RoadNetwork(), new DemandSystem(), seed: 42);
        engine.SeedRoadGraphFromGrid();

        // Run until Town milestone or bail out at 2000 ticks
        var reachedTown = false;
        for (var t = 0; t < 2000; t++)
        {
            engine.Tick();
            if (engine.MilestoneSystem.CurrentState == GameState.Town)
            {
                reachedTown = true;
                break;
            }
        }

        Assert.That(reachedTown, Is.True, "City should reach Town milestone");
        Assert.That(engine.Charters.TownCharterPending, Is.True,
            "TownCharterPending should be true after reaching Town with no charter chosen");
    }

    [Test]
    public void SimulationEngine_TownCharterPending_ClearsAfterSelection()
    {
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();
        grid.SetZone(0, 7, ZoneType.CoalPlant);
        for (var x = 1; x <= 12; x++) grid.SetZone(x, 7, ZoneType.Road);
        for (var x = 1; x <= 12; x++)
        for (var y = 5; y <= 6; y++)
            grid.SetZone(x, y, ZoneType.Residential);
        for (var x = 1; x <= 12; x++)
        for (var y = 8; y <= 9; y++)
            grid.SetZone(x, y, ZoneType.Residential);

        var engine = new SimulationEngine(grid, new BudgetSystem(), new PopulationSystem(),
            new PowerNetwork(), new RoadNetwork(), new DemandSystem(), seed: 42);
        engine.SeedRoadGraphFromGrid();

        for (var t = 0; t < 2000; t++)
        {
            engine.Tick();
            if (engine.MilestoneSystem.CurrentState == GameState.Town) break;
        }

        // Select a charter
        engine.Charters.SelectCharter(CharterType.Merchant);

        Assert.That(engine.Charters.TownCharterPending, Is.False);
        Assert.That(engine.Charters.ActiveCharter, Is.EqualTo(CharterType.Merchant));
    }

    // ── Integration: Merchant charter boosts commercial activity ─────────────

    [Test]
    public void Integration_MerchantCharter_CommercialMultiplier_IsAppliedInEngine()
    {
        // Verify that the Merchant charter's CommercialGrowthMultiplier is correctly wired
        // through SimulationEngine.Tick() to PopulationSystem.Tick().
        //
        // Commercial growth formula: rawGrowth = baseRate × (50 - current) × multiplier
        // baseRate = clamp(0.04 × adjacentResidential/100, 0.008, 0.06)
        //
        // Design: one commercial tile with 3 adjacent residential tiles each at pop=50.
        // adjacentResidential = 150 → baseRate = clamp(0.04 × 1.5, 0.008, 0.06) = 0.06 (clamped)
        // At current=23 (50-current=27):
        //   Control:  int(0.06 × 27 × 1.00) = int(1.62) = 1
        //   Merchant: int(0.06 × 27 × 1.30) = int(2.11) = 2
        // After 1 tick: control=24, merchant=25.

        static SimulationEngine BuildEngine(bool useMerchant)
        {
            var grid = new CityGrid(12, 12);
            grid.SetFlatTerrain();
            grid.SetZone(0, 5, ZoneType.CoalPlant);
            for (var x = 1; x <= 10; x++) grid.SetZone(x, 5, ZoneType.Road);

            // 3 residential tiles adjacent to the commercial tile, each at full pop
            // (4,6), (6,6), (5,7) are all 4-way adjacent to (5,6)
            grid.SetZone(4, 6, ZoneType.Residential); grid.SetPopulation(4, 6, 50);
            grid.SetZone(6, 6, ZoneType.Residential); grid.SetPopulation(6, 6, 50);
            grid.SetZone(5, 7, ZoneType.Residential); grid.SetPopulation(5, 7, 50);

            // Commercial tile at (5,6), pre-set to activity=23 with a BuildingId for development
            grid.SetZone(5, 6, ZoneType.Commercial);
            grid.SetPopulation(5, 6, 23);
            grid.SetBuildingId(5, 6, "test-com");

            var engine = new SimulationEngine(grid, new BudgetSystem(), new PopulationSystem(),
                new PowerNetwork(), new RoadNetwork(), new DemandSystem(), seed: 42);
            engine.SeedRoadGraphFromGrid();

            if (useMerchant)
                engine.Charters.SelectCharter(CharterType.Merchant);

            return engine;
        }

        var merchantEngine = BuildEngine(useMerchant: true);
        var controlEngine  = BuildEngine(useMerchant: false);

        // Run 1 tick
        merchantEngine.Tick();
        controlEngine.Tick();

        var merchantActivity = merchantEngine.Grid.TilesOfType(ZoneType.Commercial)
            .Sum(c => merchantEngine.Grid.GetPopulation(c.X, c.Y));
        var controlActivity = controlEngine.Grid.TilesOfType(ZoneType.Commercial)
            .Sum(c => controlEngine.Grid.GetPopulation(c.X, c.Y));

        Assert.That(merchantActivity, Is.GreaterThan(controlActivity),
            $"Merchant charter ({merchantActivity}) should have higher activity than control ({controlActivity}) after 1 tick (expected control=24, merchant=25)");
    }

    // ── Integration: Industrial charter boosts employment ────────────────────

    // ── SelectCharter(None) guard ────────────────────────────────────────────

    [Test]
    public void SelectCharter_WithNone_DoesNotClearPending()
    {
        // Bug guard: calling SelectCharter(None) should be a no-op.
        // Previously it would clear TownCharterPending without setting an actual charter.
        _system.NotifyTownMilestone();
        Assert.That(_system.TownCharterPending, Is.True, "Precondition: pending should be true");

        _system.SelectCharter(CharterType.None); // should be a no-op

        Assert.That(_system.TownCharterPending, Is.True,
            "TownCharterPending must remain true after SelectCharter(None)");
        Assert.That(_system.ActiveCharter, Is.EqualTo(CharterType.None),
            "ActiveCharter must remain None — SelectCharter(None) is a no-op");
    }

    [Test]
    public void SelectCharter_WithNone_CanStillSelectValidCharterAfterward()
    {
        // After a no-op SelectCharter(None), player should still be able to pick a real charter.
        _system.NotifyTownMilestone();
        _system.SelectCharter(CharterType.None); // no-op
        _system.SelectCharter(CharterType.Civic); // should succeed

        Assert.That(_system.ActiveCharter, Is.EqualTo(CharterType.Civic));
        Assert.That(_system.TownCharterPending, Is.False);
    }

    [Test]
    public void Integration_IndustrialCharter_IncreasesEmployment_OverControl()
    {
        // Run two identical 300-tick cities — one with Industrial charter, one without.
        // Industrial city should have more available jobs.
        static int RunIndustrialCity(bool useIndustrial)
        {
            var grid = new CityGrid(12, 12);
            grid.SetFlatTerrain();
            grid.SetZone(0, 5, ZoneType.CoalPlant);
            for (var x = 1; x <= 10; x++) grid.SetZone(x, 5, ZoneType.Road);
            for (var x = 2; x <= 9; x++) grid.SetZone(x, 4, ZoneType.Residential);
            for (var x = 2; x <= 9; x++) grid.SetZone(x, 6, ZoneType.Industrial);

            var engine = new SimulationEngine(grid, new BudgetSystem(), new PopulationSystem(),
                new PowerNetwork(), new RoadNetwork(), new DemandSystem(), seed: 42);
            engine.SeedRoadGraphFromGrid();

            if (useIndustrial)
                engine.Charters.SelectCharter(CharterType.Industrial);

            for (var t = 0; t < 300; t++) engine.Tick();

            return engine.EmploymentSystem.AvailableJobs;
        }

        var industrialJobs = RunIndustrialCity(useIndustrial: true);
        var controlJobs    = RunIndustrialCity(useIndustrial: false);

        Assert.That(industrialJobs, Is.GreaterThan(controlJobs),
            $"Industrial charter ({industrialJobs} jobs) should produce more jobs than control ({controlJobs} jobs)");
    }
}
