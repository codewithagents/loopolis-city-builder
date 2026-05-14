using Loopolis.Core.UI.ViewModels;

namespace Loopolis.Core.UI.Tests.ViewModels;

[TestFixture]
public class TopBarViewModelTests
{
    private TopBarViewModel _vm = null!;

    [SetUp]
    public void SetUp() => _vm = new TopBarViewModel();

    // ---- Balance ----

    [Test]
    public void BalanceText_PositiveBalance_FormatsWithDollarSign()
    {
        _vm.Balance = 1200;
        Assert.That(_vm.BalanceText, Is.EqualTo("$1,200"));
    }

    [Test]
    public void BalanceText_ZeroBalance_FormatsAsZero()
    {
        _vm.Balance = 0;
        Assert.That(_vm.BalanceText, Is.EqualTo("$0"));
    }

    [Test]
    public void BalanceText_NegativeBalance_FormatsWithMinus()
    {
        _vm.Balance = -500;
        Assert.That(_vm.BalanceText, Is.EqualTo("-$500"));
    }

    [Test]
    public void BalanceText_NegativeBalance_NoDoubleNegative()
    {
        _vm.Balance = -1234;
        Assert.That(_vm.BalanceText, Does.StartWith("-$").And.Not.Contains("--"));
    }

    [Test]
    public void BalanceNegative_True_WhenNegative()
    {
        _vm.Balance = -1;
        Assert.That(_vm.BalanceNegative, Is.True);
    }

    [Test]
    public void BalanceNegative_False_WhenPositive()
    {
        _vm.Balance = 1;
        Assert.That(_vm.BalanceNegative, Is.False);
    }

    [Test]
    public void BalanceNegative_False_WhenZero()
    {
        _vm.Balance = 0;
        Assert.That(_vm.BalanceNegative, Is.False);
    }

    // ---- Population ----

    [Test]
    public void PopulationText_Under1000_ShowsRaw()
    {
        _vm.Population = 450;
        Assert.That(_vm.PopulationText, Is.EqualTo("450"));
    }

    [Test]
    public void PopulationText_Zero_ShowsZero()
    {
        _vm.Population = 0;
        Assert.That(_vm.PopulationText, Is.EqualTo("0"));
    }

    [Test]
    public void PopulationText_Exactly1000_ShowsDecimalK()
    {
        _vm.Population = 1000;
        Assert.That(_vm.PopulationText, Is.EqualTo("1.0k"));
    }

    [Test]
    public void PopulationText_Over1000_ShowsOneDecimalK()
    {
        _vm.Population = 1204;
        Assert.That(_vm.PopulationText, Is.EqualTo("1.2k"));
    }

    [Test]
    public void PopulationText_LargeValue_ShowsScaled()
    {
        _vm.Population = 25000;
        Assert.That(_vm.PopulationText, Is.EqualTo("25.0k"));
    }

    [Test]
    public void MilestoneLabel_CombinesPopAndMilestone()
    {
        _vm.Population = 500;
        _vm.MilestoneName = "Town";
        Assert.That(_vm.MilestoneLabel, Is.EqualTo("500 (Town)"));
    }

    [Test]
    public void MilestoneLabel_UsesKFormattingForLargePopulation()
    {
        _vm.Population = 5200;
        _vm.MilestoneName = "City";
        Assert.That(_vm.MilestoneLabel, Is.EqualTo("5.2k (City)"));
    }

    // ---- Power ----

    [Test]
    public void PowerShortage_WhenDemandExceedsSupply()
    {
        _vm.PowerSupply = 100;
        _vm.PowerDemand = 120;
        Assert.That(_vm.PowerShortage, Is.True);
    }

    [Test]
    public void PowerShortage_False_WhenSupplyAdequate()
    {
        _vm.PowerSupply = 200;
        _vm.PowerDemand = 120;
        Assert.That(_vm.PowerShortage, Is.False);
    }

    [Test]
    public void PowerShortage_False_WhenSupplyEqualsDemannd()
    {
        _vm.PowerSupply = 100;
        _vm.PowerDemand = 100;
        Assert.That(_vm.PowerShortage, Is.False);
    }

    [Test]
    public void PowerText_FormatsSupplyAndDemand()
    {
        _vm.PowerSupply = 120;
        _vm.PowerDemand = 80;
        Assert.That(_vm.PowerText, Is.EqualTo("⚡ 120/80 MW"));
    }

    // ---- Policy cost ----

    [Test]
    public void ShowPolicyCost_False_WhenZeroCost()
    {
        _vm.PolicyCostPerTick = 0;
        Assert.That(_vm.ShowPolicyCost, Is.False);
    }

    [Test]
    public void ShowPolicyCost_True_WhenCostPositive()
    {
        _vm.PolicyCostPerTick = 15;
        Assert.That(_vm.ShowPolicyCost, Is.True);
    }

    [Test]
    public void PolicyCostText_FormatsCorrectly()
    {
        _vm.PolicyCostPerTick = 15;
        Assert.That(_vm.PolicyCostText, Is.EqualTo("📋 -$15/tk"));
    }

    // ---- Happiness ----

    [Test]
    public void HappinessLevel_Good_Above0_7()
    {
        _vm.Happiness = 0.75f;
        Assert.That(_vm.HappinessLevel, Is.EqualTo(HappinessLevel.Good));
    }

    [Test]
    public void HappinessLevel_Good_AtExactly0_7()
    {
        _vm.Happiness = 0.7f;
        Assert.That(_vm.HappinessLevel, Is.EqualTo(HappinessLevel.Good));
    }

    [Test]
    public void HappinessLevel_Warning_Between0_4_And0_7()
    {
        _vm.Happiness = 0.55f;
        Assert.That(_vm.HappinessLevel, Is.EqualTo(HappinessLevel.Warning));
    }

    [Test]
    public void HappinessLevel_Warning_AtExactly0_4()
    {
        _vm.Happiness = 0.4f;
        Assert.That(_vm.HappinessLevel, Is.EqualTo(HappinessLevel.Warning));
    }

    [Test]
    public void HappinessLevel_Critical_Below0_4()
    {
        _vm.Happiness = 0.2f;
        Assert.That(_vm.HappinessLevel, Is.EqualTo(HappinessLevel.Critical));
    }

    [Test]
    public void HappinessText_FormatsToTwoDecimalPlaces()
    {
        _vm.Happiness = 0.72f;
        Assert.That(_vm.HappinessText, Is.EqualTo("😊 0.72"));
    }

    // ---- Zone summary ----

    [Test]
    public void ZoneSummary_FormatsAllThreeCounts()
    {
        _vm.ResidentialCount = 10;
        _vm.CommercialCount = 5;
        _vm.IndustrialCount = 3;
        Assert.That(_vm.ZoneSummary, Is.EqualTo("🏘 10  🏪 5  🏭 3"));
    }

    [Test]
    public void ZoneSummary_ZeroCounts_StillFormats()
    {
        _vm.ResidentialCount = 0;
        _vm.CommercialCount = 0;
        _vm.IndustrialCount = 0;
        Assert.That(_vm.ZoneSummary, Is.EqualTo("🏘 0  🏪 0  🏭 0"));
    }
}
