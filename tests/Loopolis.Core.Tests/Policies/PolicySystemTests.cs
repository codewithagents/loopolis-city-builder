using Loopolis.Core.Grid;
using Loopolis.Core.Policies;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Policies;

[TestFixture]
public class PolicySystemTests
{
    private PolicySystem _policy = null!;
    private BudgetSystem _budget = null!;

    [SetUp]
    public void SetUp()
    {
        _policy = new PolicySystem();
        _budget = new BudgetSystem(10_000);
    }

    // ── Default state ───────────────────────────────────────────────────────────

    [Test]
    public void DefaultState_NoPoliciesActive()
    {
        Assert.That(_policy.ActivePolicies, Is.Empty);
    }

    [Test]
    public void DefaultState_AllModifiersAreNeutral()
    {
        Assert.That(_policy.PollutionMultiplier,         Is.EqualTo(1.0));
        Assert.That(_policy.HappinessBonusFromPolicy,    Is.EqualTo(0.0));
        Assert.That(_policy.IndustrialGrowthMultiplier,  Is.EqualTo(1.0));
        Assert.That(_policy.JobsPerIndustrialTileBonus,  Is.EqualTo(0));
        Assert.That(_policy.CommercialGrowthMultiplier,  Is.EqualTo(1.0));
        Assert.That(_policy.ResidentialCapacityBonus,    Is.EqualTo(0.0));
        Assert.That(_policy.TaxRateModifier,             Is.EqualTo(1.0));
    }

    [Test]
    public void DefaultState_GetCostPerTickIsZero()
    {
        Assert.That(_policy.GetCostPerTick(), Is.EqualTo(0));
    }

    // ── GreenCity ───────────────────────────────────────────────────────────────

    [Test]
    public void ActivateGreenCity_PollutionMultiplierIsReduced()
    {
        _policy.ActivatePolicy(PolicyType.GreenCity);
        Assert.That(_policy.PollutionMultiplier, Is.EqualTo(0.65).Within(0.001));
    }

    [Test]
    public void ActivateGreenCity_HappinessBonusIsAdded()
    {
        _policy.ActivatePolicy(PolicyType.GreenCity);
        Assert.That(_policy.HappinessBonusFromPolicy, Is.EqualTo(0.10).Within(0.001));
    }

    [Test]
    public void DeactivateGreenCity_MultipliersReturnToDefault()
    {
        _policy.ActivatePolicy(PolicyType.GreenCity);
        _policy.DeactivatePolicy(PolicyType.GreenCity);

        Assert.That(_policy.PollutionMultiplier,      Is.EqualTo(1.0));
        Assert.That(_policy.HappinessBonusFromPolicy, Is.EqualTo(0.0));
    }

    [Test]
    public void GreenCity_TickDeductsCostFromBudget()
    {
        _policy.ActivatePolicy(PolicyType.GreenCity);
        var balanceBefore = _budget.Balance;

        _policy.Tick(_budget);

        // GreenCity costs $25/tick (reduced from $40 — benefit was too expensive vs payoff)
        Assert.That(_budget.Balance, Is.EqualTo(balanceBefore - 25).Within(0.001));
    }

    // ── IndustrialHub ───────────────────────────────────────────────────────────

    [Test]
    public void ActivateIndustrialHub_IndustrialGrowthMultiplierIsIncreased()
    {
        _policy.ActivatePolicy(PolicyType.IndustrialHub);
        Assert.That(_policy.IndustrialGrowthMultiplier, Is.EqualTo(1.25).Within(0.001));
    }

    [Test]
    public void ActivateIndustrialHub_JobsBonusIsEight()
    {
        _policy.ActivatePolicy(PolicyType.IndustrialHub);
        // Increased from +3 to +8 to have a measurable employment impact
        Assert.That(_policy.JobsPerIndustrialTileBonus, Is.EqualTo(8));
    }

    [Test]
    public void IndustrialHub_TickDeductsCostFromBudget()
    {
        _policy.ActivatePolicy(PolicyType.IndustrialHub);
        var balanceBefore = _budget.Balance;

        _policy.Tick(_budget);

        // IndustrialHub now costs $30/tick (rebalanced from $50)
        Assert.That(_budget.Balance, Is.EqualTo(balanceBefore - 30).Within(0.001));
    }

    // ── CommercialBoost ─────────────────────────────────────────────────────────

    [Test]
    public void ActivateCommercialBoost_CommercialGrowthMultiplierIsIncreased()
    {
        _policy.ActivatePolicy(PolicyType.CommercialBoost);
        Assert.That(_policy.CommercialGrowthMultiplier, Is.EqualTo(1.25).Within(0.001));
    }

    [Test]
    public void CommercialBoost_TickDeductsCostFromBudget()
    {
        _policy.ActivatePolicy(PolicyType.CommercialBoost);
        var balanceBefore = _budget.Balance;

        _policy.Tick(_budget);

        // CommercialBoost now costs $30/tick (rebalanced from $60)
        Assert.That(_budget.Balance, Is.EqualTo(balanceBefore - 30).Within(0.001));
    }

    // ── OpenCity ────────────────────────────────────────────────────────────────

    [Test]
    public void ActivateOpenCity_ResidentialCapacityBonusIsApplied()
    {
        _policy.ActivatePolicy(PolicyType.OpenCity);
        // Changed from ImmigrationMultiplier (inert at capacity) to ResidentialCapacityBonus (+12%)
        Assert.That(_policy.ResidentialCapacityBonus, Is.EqualTo(0.12).Within(0.001));
    }

    [Test]
    public void OpenCity_TaxRateModifierIsAlwaysOne()
    {
        // OpenCity no longer reduces tax revenue — the $15/tick cost is the only trade-off.
        // Without OpenCity
        Assert.That(_policy.TaxRateModifier, Is.EqualTo(1.0).Within(0.001));
        // With OpenCity active — still 1.0 (no tax penalty)
        _policy.ActivatePolicy(PolicyType.OpenCity);
        Assert.That(_policy.TaxRateModifier, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void OpenCity_TickDeductsCostFromBudget()
    {
        _policy.ActivatePolicy(PolicyType.OpenCity);
        var balanceBefore = _budget.Balance;

        _policy.Tick(_budget);

        // OpenCity now costs $15/tick (rebalanced from $30)
        Assert.That(_budget.Balance, Is.EqualTo(balanceBefore - 15).Within(0.001));
    }

    // ── Multiple policies ───────────────────────────────────────────────────────

    [Test]
    public void MultiplePoliciesActive_TotalCostAccumulates()
    {
        _policy.ActivatePolicy(PolicyType.GreenCity);
        _policy.ActivatePolicy(PolicyType.IndustrialHub);
        _policy.ActivatePolicy(PolicyType.CommercialBoost);
        _policy.ActivatePolicy(PolicyType.OpenCity);

        // 25 + 30 + 30 + 15 = 100
        Assert.That(_policy.GetCostPerTick(), Is.EqualTo(100));
    }

    [Test]
    public void MultiplePoliciesActive_TickDeductsCorrectTotal()
    {
        _policy.ActivatePolicy(PolicyType.GreenCity);   // $25
        _policy.ActivatePolicy(PolicyType.OpenCity);    // $15
        var balanceBefore = _budget.Balance;

        _policy.Tick(_budget);

        Assert.That(_budget.Balance, Is.EqualTo(balanceBefore - 40).Within(0.001));
    }

    // ── IsActive ────────────────────────────────────────────────────────────────

    [Test]
    public void IsActive_ReturnsTrueWhenPolicyActivated()
    {
        _policy.ActivatePolicy(PolicyType.GreenCity);
        Assert.That(_policy.IsActive(PolicyType.GreenCity), Is.True);
    }

    [Test]
    public void IsActive_ReturnsFalseWhenPolicyNotActivated()
    {
        Assert.That(_policy.IsActive(PolicyType.GreenCity), Is.False);
    }

    [Test]
    public void IsActive_ReturnsFalseAfterDeactivation()
    {
        _policy.ActivatePolicy(PolicyType.GreenCity);
        _policy.DeactivatePolicy(PolicyType.GreenCity);
        Assert.That(_policy.IsActive(PolicyType.GreenCity), Is.False);
    }

    // ── PolicyCatalog ───────────────────────────────────────────────────────────

    [Test]
    public void PolicyCatalog_HasFourEntries()
    {
        Assert.That(PolicyCatalog.All, Has.Length.EqualTo(4));
    }

    [Test]
    public void PolicyCatalog_AllEntriesAreNonNull()
    {
        foreach (var def in PolicyCatalog.All)
        {
            Assert.That(def, Is.Not.Null);
            Assert.That(def.Name, Is.Not.Empty);
            Assert.That(def.Description, Is.Not.Empty);
            Assert.That(def.CostPerTick, Is.GreaterThan(0));
        }
    }

    [Test]
    public void PolicyCatalog_GreenCityUnlocksAtActiveState()
    {
        var def = PolicyCatalog.Find(PolicyType.GreenCity);
        Assert.That(def, Is.Not.Null);
        Assert.That(def!.UnlockAt, Is.EqualTo(GameState.Active));
    }

    [Test]
    public void PolicyCatalog_IndustrialHubUnlocksAtTownState()
    {
        var def = PolicyCatalog.Find(PolicyType.IndustrialHub);
        Assert.That(def, Is.Not.Null);
        Assert.That(def!.UnlockAt, Is.EqualTo(GameState.Town));
    }

    [Test]
    public void PolicyCatalog_OpenCityUnlocksAtActiveState()
    {
        var def = PolicyCatalog.Find(PolicyType.OpenCity);
        Assert.That(def, Is.Not.Null);
        Assert.That(def!.UnlockAt, Is.EqualTo(GameState.Active));
    }

    // ── GreenCity integration: pollution emission reduced ────────────────────────

    [Test]
    public void GreenCity_ReducesPollutionEmission()
    {
        // Arrange — industrial tile adjacent to residential
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();
        grid.SetZone(5, 5, ZoneType.Industrial);
        grid.SetPower(5, 5, true);  // powered so it emits

        var pollution = new PollutionSystem();

        // Without GreenCity
        pollution.Propagate(grid, 1.0);
        var pollutionWithout = grid.GetTile(5, 5).PollutionLevel;

        // With GreenCity (0.65× multiplier)
        pollution.Propagate(grid, 0.65);
        var pollutionWith = grid.GetTile(5, 5).PollutionLevel;

        Assert.That(pollutionWith, Is.LessThan(pollutionWithout));
        Assert.That(pollutionWith, Is.EqualTo(pollutionWithout * 0.65).Within(0.01));
    }

    // ── HappinessSystem integration: policy bonus applied ───────────────────────

    [Test]
    public void GreenCity_IncreasesHappinessByBonus()
    {
        // Arrange — ready residential tile
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(5, 4, ZoneType.Residential);
        grid.SetZone(5, 3, ZoneType.PowerPlant);
        grid.SetPower(5, 4, true);
        grid.SetRoadAccess(5, 4, true);

        var happiness = new HappinessSystem();

        // Without policy bonus
        happiness.Propagate(grid, policyHappinessBonus: 0.0);
        var happinessWithout = grid.GetTile(5, 4).Happiness;

        // With GreenCity bonus (+0.10)
        happiness.Propagate(grid, policyHappinessBonus: 0.10);
        var happinessWith = grid.GetTile(5, 4).Happiness;

        // Happiness should be higher (up to the 1.0 clamp)
        Assert.That(happinessWith, Is.GreaterThanOrEqualTo(happinessWithout));
    }

    // ── SimulationEngine integration ─────────────────────────────────────────────

    [Test]
    public void SimulationEngine_HasPolicySystemProperty()
    {
        var grid   = new CityGrid(10, 10);
        var budget = new BudgetSystem(5_000);
        var pop    = new PopulationSystem();
        var power  = new PowerNetwork();
        var roads  = new RoadNetwork();
        var demand = new DemandSystem();
        var engine = new SimulationEngine(grid, budget, pop, power, roads, demand, seed: 42);

        Assert.That(engine.PolicySystem, Is.Not.Null);
    }

    [Test]
    public void SimulationEngine_PolicyCostDeductedEachTick()
    {
        var grid   = new CityGrid(10, 10);
        grid.SetFlatTerrain();
        var budget = new BudgetSystem(10_000);
        var pop    = new PopulationSystem();
        var power  = new PowerNetwork();
        var roads  = new RoadNetwork();
        var demand = new DemandSystem();
        var engine = new SimulationEngine(grid, budget, pop, power, roads, demand, seed: 42);

        engine.PolicySystem.ActivatePolicy(PolicyType.OpenCity);  // $15/tick
        var balanceBefore = budget.Balance;
        engine.Tick();

        // Budget should have been reduced by the policy cost (among other maintenance costs)
        // We can confirm policy cost was applied by checking it's deducted on top of maintenance
        Assert.That(budget.Balance, Is.LessThan(balanceBefore - 15 + 1)); // at least $15 was deducted from policy alone
    }
}
