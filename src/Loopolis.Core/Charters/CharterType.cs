namespace Loopolis.Core.Charters;

public enum CharterType
{
    None,

    // ── Town era charters ───────────────────────────────────────────────────────

    /// Merchant Charter — commercial districts grow faster; land value is elevated.
    /// Effect: CommercialGrowthMultiplier ×1.30, LandValueBonus +0.06
    Merchant,

    /// Industrial Charter — industrial output surges; workers are more productive.
    /// Effect: IndustrialGrowthMultiplier ×1.35, JobsPerIndustrialTileBonus +10
    Industrial,

    /// Civic Charter — services reach further; parks are more valuable.
    /// Effect: ServiceCoverageRadiusBonus +3.0 (road-graph distance units), ParkHappinessMultiplier ×2.0
    Civic,

    // ── City era charters ────────────────────────────────────────────────────────

    /// Innovation Hub — density and technology; more people per building, higher tax yield.
    /// Effect: ResidentialCapacityBonus +20%, TaxRateModifier +8%
    InnovationHub,

    /// Green Canopy — environmental; pollution impact halved, parks reach further.
    /// Effect: PollutionMultiplier ×0.5, ParkRadiusBonus +2
    GreenCanopy,

    /// Trade Corridors — economic; commercial sector grows faster, land value elevated.
    /// Effect: CommercialGrowthMultiplier ×1.25, LandValueBonus +0.08
    TradeCorridors,
}
