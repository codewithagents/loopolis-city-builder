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
}
