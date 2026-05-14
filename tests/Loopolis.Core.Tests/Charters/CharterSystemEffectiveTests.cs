using Loopolis.Core.Charters;
using NUnit.Framework;

namespace Loopolis.Core.Tests.Charters;

/// <summary>
/// Tests for the Effective* combined-era accessor properties on CharterSystem.
/// Each Effective* property stacks Town + City + Metropolis modifiers so that
/// SimulationEngine only needs to call one property per modifier type.
/// </summary>
[TestFixture]
public class CharterSystemEffectiveTests
{
    private CharterSystem _system = null!;

    [SetUp]
    public void SetUp() => _system = new CharterSystem();

    // ── No charters active — neutral values ──────────────────────────────────

    [Test]
    public void NoCharters_EffectiveCommercialGrowthMultiplier_IsOne()
        => Assert.That(_system.EffectiveCommercialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));

    [Test]
    public void NoCharters_EffectiveIndustrialGrowthMultiplier_IsOne()
        => Assert.That(_system.EffectiveIndustrialGrowthMultiplier, Is.EqualTo(1.0).Within(0.001));

    [Test]
    public void NoCharters_EffectiveLandValueBonus_IsZero()
        => Assert.That(_system.EffectiveLandValueBonus, Is.EqualTo(0.0).Within(0.001));

    [Test]
    public void NoCharters_EffectiveResidentialCapacityBonus_IsZero()
        => Assert.That(_system.EffectiveResidentialCapacityBonus, Is.EqualTo(0.0).Within(0.001));

    [Test]
    public void NoCharters_EffectiveTaxRateModifier_IsZero()
        => Assert.That(_system.EffectiveTaxRateModifier, Is.EqualTo(0.0).Within(0.001));

    [Test]
    public void NoCharters_EffectivePollutionMultiplier_IsOne()
        => Assert.That(_system.EffectivePollutionMultiplier, Is.EqualTo(1.0f).Within(0.001f));

    [Test]
    public void NoCharters_EffectiveServiceCoverageRadiusBonus_IsZero()
        => Assert.That(_system.EffectiveServiceCoverageRadiusBonus, Is.EqualTo(0f).Within(0.001f));

    [Test]
    public void NoCharters_EffectiveParkRadiusBonus_IsZero()
        => Assert.That(_system.EffectiveParkRadiusBonus, Is.EqualTo(0));

    [Test]
    public void NoCharters_EffectiveJobsPerTileBonus_IsZero()
        => Assert.That(_system.EffectiveJobsPerTileBonus, Is.EqualTo(0));

    [Test]
    public void NoCharters_EffectiveParkHappinessMultiplier_IsOne()
        => Assert.That(_system.EffectiveParkHappinessMultiplier, Is.EqualTo(1.0).Within(0.001));

    // ── Town-only charter ─────────────────────────────────────────────────────

    [Test]
    public void TownMerchant_EffectiveCommercialGrowthMultiplier_Is1_30()
    {
        _system.SelectCharter(CharterType.Merchant);
        Assert.That(_system.EffectiveCommercialGrowthMultiplier, Is.EqualTo(1.30).Within(0.001));
    }

    [Test]
    public void TownMerchant_EffectiveLandValueBonus_Is0_06()
    {
        _system.SelectCharter(CharterType.Merchant);
        Assert.That(_system.EffectiveLandValueBonus, Is.EqualTo(0.06).Within(0.001));
    }

    [Test]
    public void TownIndustrial_EffectiveIndustrialGrowthMultiplier_Is1_35()
    {
        _system.SelectCharter(CharterType.Industrial);
        Assert.That(_system.EffectiveIndustrialGrowthMultiplier, Is.EqualTo(1.35).Within(0.001));
    }

    [Test]
    public void TownIndustrial_EffectiveJobsPerTileBonus_Is10()
    {
        _system.SelectCharter(CharterType.Industrial);
        Assert.That(_system.EffectiveJobsPerTileBonus, Is.EqualTo(10));
    }

    [Test]
    public void TownCivic_EffectiveServiceCoverageRadiusBonus_Is3()
    {
        _system.SelectCharter(CharterType.Civic);
        Assert.That(_system.EffectiveServiceCoverageRadiusBonus, Is.EqualTo(3.0f).Within(0.001f));
    }

    [Test]
    public void TownCivic_EffectiveParkHappinessMultiplier_Is2_0()
    {
        _system.SelectCharter(CharterType.Civic);
        Assert.That(_system.EffectiveParkHappinessMultiplier, Is.EqualTo(2.0).Within(0.001));
    }

    // ── City-only charter ─────────────────────────────────────────────────────

    [Test]
    public void CityInnovationHub_EffectiveResidentialCapacityBonus_Is0_20()
    {
        _system.SelectCityCharter(CharterType.InnovationHub);
        Assert.That(_system.EffectiveResidentialCapacityBonus, Is.EqualTo(0.20).Within(0.001));
    }

    [Test]
    public void CityInnovationHub_EffectiveTaxRateModifier_Is0_08()
    {
        _system.SelectCityCharter(CharterType.InnovationHub);
        Assert.That(_system.EffectiveTaxRateModifier, Is.EqualTo(0.08).Within(0.001));
    }

    [Test]
    public void CityGreenCanopy_EffectivePollutionMultiplier_Is0_5()
    {
        _system.SelectCityCharter(CharterType.GreenCanopy);
        Assert.That(_system.EffectivePollutionMultiplier, Is.EqualTo(0.5f).Within(0.001f));
    }

    [Test]
    public void CityGreenCanopy_EffectiveParkRadiusBonus_Is2()
    {
        _system.SelectCityCharter(CharterType.GreenCanopy);
        Assert.That(_system.EffectiveParkRadiusBonus, Is.EqualTo(2));
    }

    [Test]
    public void CityTradeCorridors_EffectiveCommercialGrowthMultiplier_Is1_25()
    {
        _system.SelectCityCharter(CharterType.TradeCorridors);
        Assert.That(_system.EffectiveCommercialGrowthMultiplier, Is.EqualTo(1.25).Within(0.001));
    }

    [Test]
    public void CityTradeCorridors_EffectiveLandValueBonus_Is0_08()
    {
        _system.SelectCityCharter(CharterType.TradeCorridors);
        Assert.That(_system.EffectiveLandValueBonus, Is.EqualTo(0.08).Within(0.001));
    }

    // ── Metropolis-only charter ───────────────────────────────────────────────

    [Test]
    public void MetropolisNexusCity_EffectiveServiceCoverageRadiusBonus_Is5()
    {
        _system.SelectMetropolisCharter(CharterType.NexusCity);
        Assert.That(_system.EffectiveServiceCoverageRadiusBonus, Is.EqualTo(5.0f).Within(0.001f));
    }

    [Test]
    public void MetropolisNexusCity_EffectiveResidentialCapacityBonus_Is0_30()
    {
        _system.SelectMetropolisCharter(CharterType.NexusCity);
        Assert.That(_system.EffectiveResidentialCapacityBonus, Is.EqualTo(0.30).Within(0.001));
    }

    [Test]
    public void MetropolisNexusCity_EffectiveTaxRateModifier_Is0_08()
    {
        _system.SelectMetropolisCharter(CharterType.NexusCity);
        Assert.That(_system.EffectiveTaxRateModifier, Is.EqualTo(0.08).Within(0.001));
    }

    [Test]
    public void MetropolisEmpireOfSteel_EffectiveLandValueBonus_Is0_10()
    {
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);
        Assert.That(_system.EffectiveLandValueBonus, Is.EqualTo(0.10).Within(0.001));
    }

    [Test]
    public void MetropolisEmpireOfSteel_EffectiveIndustrialGrowthMultiplier_Is1_6()
    {
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);
        Assert.That(_system.EffectiveIndustrialGrowthMultiplier, Is.EqualTo(1.6).Within(0.001));
    }

    [Test]
    public void MetropolisEmpireOfSteel_EffectiveJobsPerTileBonus_Is25()
    {
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);
        Assert.That(_system.EffectiveJobsPerTileBonus, Is.EqualTo(25));
    }

    // ── Cross-era stacking ────────────────────────────────────────────────────

    [Test]
    public void MerchantPlusTradeCorridors_EffectiveCommercialGrowthMultiplier_Is1_625()
    {
        // Merchant ×1.30 × TradeCorridors ×1.25 = 1.625
        _system.SelectCharter(CharterType.Merchant);
        _system.SelectCityCharter(CharterType.TradeCorridors);
        Assert.That(_system.EffectiveCommercialGrowthMultiplier, Is.EqualTo(1.625).Within(0.001));
    }

    [Test]
    public void MerchantPlusTradePlusEmpireOfSteel_EffectiveCommercialGrowthMultiplier_Is2_1125()
    {
        // Merchant ×1.30 × TradeCorridors ×1.25 × EmpireOfSteel ×1.30 ≈ 2.1125
        _system.SelectCharter(CharterType.Merchant);
        _system.SelectCityCharter(CharterType.TradeCorridors);
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);
        Assert.That(_system.EffectiveCommercialGrowthMultiplier, Is.EqualTo(2.1125).Within(0.001));
    }

    [Test]
    public void GreenCanopyPlusGreenUtopia_EffectivePollutionMultiplier_Is0_125()
    {
        // GreenCanopy ×0.5 × GreenUtopia ×0.25 = 0.125
        _system.SelectCityCharter(CharterType.GreenCanopy);
        _system.SelectMetropolisCharter(CharterType.GreenUtopia);
        Assert.That(_system.EffectivePollutionMultiplier, Is.EqualTo(0.125f).Within(0.001f));
    }

    [Test]
    public void InnovationHubPlusNexusCity_EffectiveResidentialCapacityBonus_Is0_50()
    {
        // InnovationHub +0.20 + NexusCity +0.30 = 0.50
        _system.SelectCityCharter(CharterType.InnovationHub);
        _system.SelectMetropolisCharter(CharterType.NexusCity);
        Assert.That(_system.EffectiveResidentialCapacityBonus, Is.EqualTo(0.50).Within(0.001));
    }

    [Test]
    public void InnovationHubPlusNexusCity_EffectiveTaxRateModifier_Is0_16()
    {
        // InnovationHub +0.08 + NexusCity +0.08 = 0.16
        _system.SelectCityCharter(CharterType.InnovationHub);
        _system.SelectMetropolisCharter(CharterType.NexusCity);
        Assert.That(_system.EffectiveTaxRateModifier, Is.EqualTo(0.16).Within(0.001));
    }

    [Test]
    public void MerchantPlusTradeCorridors_EffectiveLandValueBonus_Is0_14()
    {
        // Merchant +0.06 + TradeCorridors +0.08 = 0.14
        _system.SelectCharter(CharterType.Merchant);
        _system.SelectCityCharter(CharterType.TradeCorridors);
        Assert.That(_system.EffectiveLandValueBonus, Is.EqualTo(0.14).Within(0.001));
    }

    [Test]
    public void AllThreeLandValueCharters_EffectiveLandValueBonus_Is0_24()
    {
        // Merchant +0.06 + TradeCorridors +0.08 + EmpireOfSteel +0.10 = 0.24
        _system.SelectCharter(CharterType.Merchant);
        _system.SelectCityCharter(CharterType.TradeCorridors);
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);
        Assert.That(_system.EffectiveLandValueBonus, Is.EqualTo(0.24).Within(0.001));
    }

    [Test]
    public void CivicPlusGreenUtopia_EffectiveParkHappinessMultiplier_Is6_0()
    {
        // Civic ×2.0 × GreenUtopia ×3.0 = 6.0
        _system.SelectCharter(CharterType.Civic);
        _system.SelectMetropolisCharter(CharterType.GreenUtopia);
        Assert.That(_system.EffectiveParkHappinessMultiplier, Is.EqualTo(6.0).Within(0.001));
    }

    [Test]
    public void CivicPlusNexusCity_EffectiveServiceCoverageRadiusBonus_Is8()
    {
        // Civic +3 + NexusCity +5 = 8
        _system.SelectCharter(CharterType.Civic);
        _system.SelectMetropolisCharter(CharterType.NexusCity);
        Assert.That(_system.EffectiveServiceCoverageRadiusBonus, Is.EqualTo(8.0f).Within(0.001f));
    }

    [Test]
    public void GreenCanopyPlusGreenUtopia_EffectiveParkRadiusBonus_Is5()
    {
        // GreenCanopy +2 + GreenUtopia +3 = 5
        _system.SelectCityCharter(CharterType.GreenCanopy);
        _system.SelectMetropolisCharter(CharterType.GreenUtopia);
        Assert.That(_system.EffectiveParkRadiusBonus, Is.EqualTo(5));
    }

    [Test]
    public void IndustrialPlusEmpireOfSteel_EffectiveJobsPerTileBonus_Is35()
    {
        // Industrial +10 + EmpireOfSteel +25 = 35
        _system.SelectCharter(CharterType.Industrial);
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);
        Assert.That(_system.EffectiveJobsPerTileBonus, Is.EqualTo(35));
    }

    [Test]
    public void IndustrialPlusEmpireOfSteel_EffectiveIndustrialGrowthMultiplier_Is2_16()
    {
        // Industrial ×1.35 × EmpireOfSteel ×1.60 = 2.16
        _system.SelectCharter(CharterType.Industrial);
        _system.SelectMetropolisCharter(CharterType.EmpireOfSteel);
        Assert.That(_system.EffectiveIndustrialGrowthMultiplier, Is.EqualTo(2.16).Within(0.001));
    }
}
