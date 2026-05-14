namespace Loopolis.Core.Charters;

public class CharterSystem
{
    // ── Town era charter ─────────────────────────────────────────────────────

    public CharterType ActiveCharter { get; private set; } = CharterType.None;

    /// True when a Town milestone was just reached and no charter has been chosen yet.
    public bool TownCharterPending { get; private set; }

    /// Called by SimulationEngine when Town milestone is first reached.
    public void NotifyTownMilestone()
    {
        if (ActiveCharter == CharterType.None)
            TownCharterPending = true;
    }

    /// Called by the player (via IPC command) to select a Town era charter.
    public void SelectCharter(CharterType type)
    {
        if (type == CharterType.None) return;          // cannot select None — it's the absence of a charter
        if (ActiveCharter != CharterType.None) return; // already chosen — charter is permanent
        ActiveCharter = type;
        TownCharterPending = false;
    }

    // ── City era charter ─────────────────────────────────────────────────────

    public CharterType CityCharter { get; private set; } = CharterType.None;

    /// True when a City milestone was just reached and no city charter has been chosen yet.
    public bool CityCharterPending { get; private set; }

    /// Called by SimulationEngine when City milestone is first reached.
    public void NotifyCityMilestone()
    {
        if (CityCharter == CharterType.None)
            CityCharterPending = true;
    }

    /// Called by the player (via IPC command) to select a City era charter.
    public void SelectCityCharter(CharterType type)
    {
        if (type == CharterType.None) return;        // cannot select None
        if (CityCharter != CharterType.None) return; // already chosen — charter is permanent
        CityCharter = type;
        CityCharterPending = false;
    }

    // ── Town charter modifier accessors ──────────────────────────────────────

    public double CommercialGrowthMultiplier  => ActiveCharter == CharterType.Merchant   ? 1.30 : 1.0;
    public double LandValueBonus              => ActiveCharter == CharterType.Merchant   ? 0.06 : 0.0;
    public double IndustrialGrowthMultiplier  => ActiveCharter == CharterType.Industrial ? 1.35 : 1.0;
    public int    JobsPerTileBonus            => ActiveCharter == CharterType.Industrial ? 10 : 0;
    public float  ServiceCoverageRadiusBonus  => ActiveCharter == CharterType.Civic      ? 3.0f : 0f;
    public double ParkHappinessMultiplier     => ActiveCharter == CharterType.Civic      ? 2.0 : 1.0;

    // ── City charter modifier accessors ──────────────────────────────────────

    public double CityResidentialCapacityBonus   => CityCharter == CharterType.InnovationHub  ? 0.20 : 0.0;
    public double CityTaxRateModifier            => CityCharter == CharterType.InnovationHub  ? 0.08 : 0.0;
    public float  CityPollutionMultiplier        => CityCharter == CharterType.GreenCanopy    ? 0.5f : 1.0f;
    public int    CityParkRadiusBonus            => CityCharter == CharterType.GreenCanopy    ? 2    : 0;
    public double CityCommercialGrowthMultiplier => CityCharter == CharterType.TradeCorridors ? 1.25 : 1.0;
    public double CityLandValueBonus             => CityCharter == CharterType.TradeCorridors ? 0.08 : 0.0;
}
