using Loopolis.Core.Simulation;
using NUnit.Framework;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class CityAdvisorTests
{
    // ── Helper builders ─────────────────────────────────────────────────────

    /// <summary>Returns a "healthy city" state — no rules should fire except Good.</summary>
    private static SimulationState HealthyState(
        int population           = 200,
        double balance           = 5_000,
        double incomePerTick     = 50.0,
        double costPerTick       = 20.0,
        float happiness          = 0.80f,
        int distressedTiles      = 0,
        float employmentRatio    = 0.80f,
        int powerSupply          = 1_000,
        int powerDemand          = 500,
        int poweredTiles         = 50,
        int totalActiveTiles     = 60,
        float serviceCoverage    = 0.70f,
        float growthRate         = 0.05f,
        int nextThreshold        = 500,
        string nextMilestone     = "Town") =>
        new SimulationState(
            Population:             population,
            Tick:                   100,
            Balance:                balance,
            IncomePerTick:          incomePerTick,
            CostPerTick:            costPerTick,
            AverageHappiness:       happiness,
            DistressedTileCount:    distressedTiles,
            EmploymentRatio:        employmentRatio,
            PowerSupply:            powerSupply,
            PowerDemand:            powerDemand,
            PoweredTiles:           poweredTiles,
            TotalActiveTiles:       totalActiveTiles,
            ServiceCoverageRatio:   serviceCoverage,
            PopulationGrowthRate:   growthRate,
            NextMilestoneThreshold: nextThreshold,
            NextMilestoneName:      nextMilestone);

    // ── Rule 1: Budget Critical ──────────────────────────────────────────────

    [Test]
    public void BudgetCritical_FiresWhenBalance_LessThan20xCostPerTick()
    {
        // costPerTick=10, balance must be < 200 to trigger
        var state = HealthyState(balance: 150, costPerTick: 10);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Critical));
        Assert.That(advice.Category, Is.EqualTo("Budget"));
    }

    [Test]
    public void BudgetCritical_DoesNotFire_WhenBalance_MeetThreshold()
    {
        // balance = exactly 20×costPerTick should not trigger (condition is strictly less than)
        var state = HealthyState(balance: 200, costPerTick: 10);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.Not.EqualTo(AdvisoryPriority.Critical).Or.Not.Property("Category").EqualTo("Budget"));
    }

    [Test]
    public void BudgetCritical_TakesPriorityOverBrownout()
    {
        // Both conditions apply — budget critical should win (higher priority rule fires first)
        var state = HealthyState(
            balance: 50,         // < 20×costPerTick=10 → Budget Critical
            costPerTick: 10,
            powerSupply: 500,
            powerDemand: 600);   // also brownout
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Category, Is.EqualTo("Budget"));
    }

    [Test]
    public void BudgetCritical_MessageContains_BalanceAndDeficit()
    {
        var state = HealthyState(balance: 100, incomePerTick: 5, costPerTick: 10);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Text, Does.Contain("100"));
        Assert.That(advice.Text, Does.Contain("-5"));
    }

    // ── Rule 2: Power Brownout ───────────────────────────────────────────────

    [Test]
    public void PowerBrownout_FiresWhenDemand_ExceedsSupply()
    {
        var state = HealthyState(powerSupply: 500, powerDemand: 600);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Critical));
        Assert.That(advice.Category, Is.EqualTo("Power"));
    }

    [Test]
    public void PowerBrownout_DoesNotFire_WhenSupplyIsZero()
    {
        // No power plant at all — not an actionable brownout (early game)
        var state = HealthyState(powerSupply: 0, powerDemand: 50);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Category, Is.Not.EqualTo("Power").Or.Not.Property("Priority").EqualTo(AdvisoryPriority.Critical));
    }

    [Test]
    public void PowerBrownout_MessageContainsMwValues()
    {
        var state = HealthyState(powerSupply: 500, powerDemand: 700);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Text, Does.Contain("700"));
        Assert.That(advice.Text, Does.Contain("500"));
    }

    // ── Rule 3: Distress Decay ───────────────────────────────────────────────

    [Test]
    public void DistressDecay_FiresWhenDistressedTileCount_AtLeastThree()
    {
        var state = HealthyState(distressedTiles: 3);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Critical));
        Assert.That(advice.Category, Is.EqualTo("Happiness"));
    }

    [Test]
    public void DistressDecay_DoesNotFire_WithTwoDistressedTiles()
    {
        var state = HealthyState(distressedTiles: 2);
        var advice = CityAdvisor.Advise(state);
        // Must not be the distress decay rule
        Assert.That(
            advice.Category == "Happiness" && advice.Priority == AdvisoryPriority.Critical,
            Is.False);
    }

    [Test]
    public void DistressDecay_MessageContainsTileCount()
    {
        var state = HealthyState(distressedTiles: 5);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Text, Does.Contain("5"));
    }

    // ── Rule 4: High Unemployment ────────────────────────────────────────────

    [Test]
    public void HighUnemployment_FiresWhenRatioBelow45Percent_AndPopOver100()
    {
        var state = HealthyState(population: 200, employmentRatio: 0.40f);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Warning));
        Assert.That(advice.Category, Is.EqualTo("Employment"));
    }

    [Test]
    public void HighUnemployment_DoesNotFire_WithSmallPop()
    {
        var state = HealthyState(population: 50, employmentRatio: 0.20f);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Category, Is.Not.EqualTo("Employment"));
    }

    // ── Rule 5: Power Capacity Near Limit ────────────────────────────────────

    [Test]
    public void PowerNearLimit_FiresWhenDemandExceeds85Percent()
    {
        // demand = 900, supply = 1000 → 90% → triggers
        var state = HealthyState(powerSupply: 1000, powerDemand: 900);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Warning));
        Assert.That(advice.Category, Is.EqualTo("Power"));
    }

    [Test]
    public void PowerNearLimit_DoesNotFire_WhenDemandBelow85Percent()
    {
        // demand = 800, supply = 1000 → 80% → no trigger
        var state = HealthyState(powerSupply: 1000, powerDemand: 800);
        var advice = CityAdvisor.Advise(state);
        // Either not Power category or not Warning
        Assert.That(
            advice.Category == "Power" && advice.Priority == AdvisoryPriority.Warning,
            Is.False);
    }

    // ── Rule 6: Growth Stalled — Variants ────────────────────────────────────

    [Test]
    public void NoGrowth_UnpoweredVariant_FiresWhenUnpoweredRatioHigh()
    {
        // 10 powered, 30 total → unpowered ratio = 66% > 30%
        var state = HealthyState(
            population: 100,
            growthRate: 0.0f,
            poweredTiles: 10,
            totalActiveTiles: 30);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Warning));
        Assert.That(advice.Category, Is.EqualTo("Growth"));
        Assert.That(advice.Text, Does.Contain("unpowered").Or.Contain("power"));
    }

    [Test]
    public void NoGrowth_HappinessVariant_FiresWhenHappinessLow()
    {
        // Powered fine, but happiness < 0.45
        var state = HealthyState(
            population: 100,
            growthRate: 0.0f,
            poweredTiles: 55,
            totalActiveTiles: 60,
            happiness: 0.35f);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Warning));
        Assert.That(advice.Category, Is.EqualTo("Growth"));
        Assert.That(advice.Text, Does.Contain("happiness").Or.Contain("Happiness"));
    }

    [Test]
    public void NoGrowth_GenericVariant_FiresWhenCauseIsUnknown()
    {
        // Both power and happiness fine — generic "zone more" message
        var state = HealthyState(
            population: 100,
            growthRate: 0.0f,
            poweredTiles: 58,
            totalActiveTiles: 60,
            happiness: 0.75f);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Warning));
        Assert.That(advice.Category, Is.EqualTo("Growth"));
        Assert.That(advice.Text, Does.Contain("residential").Or.Contain("Residential").Or.Contain("zone"));
    }

    // ── Rule 7: Low Service Coverage ────────────────────────────────────────

    [Test]
    public void LowServiceCoverage_FiresWhenBelow50Percent_AndPopOver200()
    {
        var state = HealthyState(population: 300, serviceCoverage: 0.40f);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Warning));
        Assert.That(advice.Category, Is.EqualTo("Services"));
    }

    [Test]
    public void LowServiceCoverage_DoesNotFire_WithSmallPop()
    {
        var state = HealthyState(population: 150, serviceCoverage: 0.20f);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Category, Is.Not.EqualTo("Services"));
    }

    // ── Rule 8: Idle Budget ──────────────────────────────────────────────────

    [Test]
    public void IdleCity_Tip_FiresWhenBalanceLargeAndGrowthStalled()
    {
        // income=50, balance=3000 > 50×50=2500, growthRate=0.005 < 0.01
        var state = HealthyState(
            population: 100,
            balance: 3_000,
            incomePerTick: 50,
            growthRate: 0.005f);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Tip));
        Assert.That(advice.Category, Is.EqualTo("Budget"));
    }

    // ── Rule 9: Near Milestone ────────────────────────────────────────────────

    [Test]
    public void NearMilestone_Tip_FiresAt85PercentOfThreshold()
    {
        // nextThreshold=500, population=450 (90% of 500 > 85%)
        var state = HealthyState(
            population: 450,
            nextThreshold: 500,
            nextMilestone: "Town",
            growthRate: 0.05f); // healthy growth so rule 6 doesn't fire
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Tip));
        Assert.That(advice.Category, Is.EqualTo("Growth"));
        Assert.That(advice.Text, Does.Contain("Town"));
    }

    [Test]
    public void NearMilestone_DoesNotFire_BelowThreshold()
    {
        // population = 400, 80% of 500 < 85%
        var state = HealthyState(
            population: 400,
            nextThreshold: 500,
            nextMilestone: "Town",
            growthRate: 0.05f);
        var advice = CityAdvisor.Advise(state);
        Assert.That(
            advice.Category == "Growth" && advice.Priority == AdvisoryPriority.Tip,
            Is.False);
    }

    // ── Rule 10: Good State ───────────────────────────────────────────────────

    [Test]
    public void GoodState_FiresWhenNothingIsWrong()
    {
        var state = HealthyState();
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Good));
        Assert.That(advice.Category, Is.EqualTo("Idle"));
    }

    [Test]
    public void GoodState_MessageContains_PopulationAndHappiness()
    {
        var state = HealthyState(population: 200, happiness: 0.80f);
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Text, Does.Contain("200"));
        Assert.That(advice.Text, Does.Contain("0.80"));
    }

    // ── Priority ordering ─────────────────────────────────────────────────────

    [Test]
    public void PriorityOrder_BrownoutOverNoGrowth()
    {
        // No growth and brownout both apply — brownout is higher priority
        var state = HealthyState(
            powerSupply: 500,
            powerDemand: 700,    // brownout
            population: 100,
            growthRate: 0.0f);   // no growth too
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Category, Is.EqualTo("Power"));
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Critical));
    }

    // ── Edge cases and boundary conditions ────────────────────────────────────

    [Test]
    public void ZeroCostPerTick_DoesNotTriggerBudgetCritical()
    {
        // When costPerTick = 0, the budget critical rule must not fire (avoids 0×20=0 false alarm).
        var state = new SimulationState(
            Population:             100,
            Tick:                   50,
            Balance:                0,  // zero balance but also zero costs — city is dormant
            IncomePerTick:          0,
            CostPerTick:            0,  // no costs at all — rule should not fire
            AverageHappiness:       1.0f,
            DistressedTileCount:    0,
            EmploymentRatio:        1.0f,
            PowerSupply:            0,
            PowerDemand:            0,
            PoweredTiles:           0,
            TotalActiveTiles:       0,
            ServiceCoverageRatio:   0f,
            PopulationGrowthRate:   0f,
            NextMilestoneThreshold: 500,
            NextMilestoneName:      "Town");

        var advice = CityAdvisor.Advise(state);
        Assert.That(
            advice.Category == "Budget" && advice.Priority == AdvisoryPriority.Critical,
            Is.False,
            "Budget critical should not fire when costPerTick is 0");
    }

    [Test]
    public void ZeroIncomePerTick_DoesNotTriggerIdleTip()
    {
        // When incomePerTick = 0, the idle city tip (balance > incomePerTick × 50) must not fire.
        // incomePerTick = 0 means the condition would be balance > 0, which is nearly always true —
        // guard is: incomePerTick > 0
        var state = HealthyState(
            population: 200,
            balance: 10_000,
            incomePerTick: 0,         // no income
            growthRate: 0.005f);       // stalled growth

        var advice = CityAdvisor.Advise(state);
        Assert.That(
            advice.Category == "Budget" && advice.Priority == AdvisoryPriority.Tip,
            Is.False,
            "Idle city tip should not fire when incomePerTick is 0");
    }

    [Test]
    public void ZeroTotalActiveTiles_DoesNotCrashUnpoweredRule()
    {
        // totalActiveTiles = 0 could cause division by zero in unpowered-ratio check.
        var state = HealthyState(
            population: 100,
            growthRate: 0.0f,         // triggers Rule 6
            poweredTiles: 0,
            totalActiveTiles: 0);     // zero tiles — guard must protect against division

        Assert.DoesNotThrow(() => CityAdvisor.Advise(state),
            "Advise should not throw when totalActiveTiles = 0");
    }

    [Test]
    public void RuleOrdering_DistressOverHighUnemployment()
    {
        // Distress (Rule 3) should fire before high unemployment (Rule 4)
        var state = HealthyState(
            distressedTiles: 5,       // Rule 3: distress decay active
            population: 200,
            employmentRatio: 0.30f);  // Rule 4: high unemployment — should be suppressed

        var advice = CityAdvisor.Advise(state);
        Assert.That(advice.Category, Is.EqualTo("Happiness"),
            "Distress (Rule 3) should take priority over High Unemployment (Rule 4)");
        Assert.That(advice.Priority, Is.EqualTo(AdvisoryPriority.Critical));
    }

    [Test]
    public void ZeroPopulation_ReturnsGoodOrTip_WithoutCrash()
    {
        var state = new SimulationState(
            Population:             0,
            Tick:                   0,
            Balance:                4_000,
            IncomePerTick:          0,
            CostPerTick:            0,
            AverageHappiness:       1.0f,
            DistressedTileCount:    0,
            EmploymentRatio:        1.0f,
            PowerSupply:            0,
            PowerDemand:            0,
            PoweredTiles:           0,
            TotalActiveTiles:       0,
            ServiceCoverageRatio:   0f,
            PopulationGrowthRate:   0f,
            NextMilestoneThreshold: 500,
            NextMilestoneName:      "Town");

        // Should not throw and should return something sensible
        Assert.DoesNotThrow(() => CityAdvisor.Advise(state));
        var advice = CityAdvisor.Advise(state);
        Assert.That(advice, Is.Not.Null);
        Assert.That(advice.Text, Is.Not.Empty);
    }
}
