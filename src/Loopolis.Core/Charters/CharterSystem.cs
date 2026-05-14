namespace Loopolis.Core.Charters;

public class CharterSystem
{
    public CharterType ActiveCharter { get; private set; } = CharterType.None;

    /// True when a Town milestone was just reached and no charter has been chosen yet.
    public bool TownCharterPending { get; private set; }

    /// Called by SimulationEngine when Town milestone is first reached.
    public void NotifyTownMilestone()
    {
        if (ActiveCharter == CharterType.None)
            TownCharterPending = true;
    }

    /// Called by the player (via IPC command) to select a charter.
    public void SelectCharter(CharterType type)
    {
        if (type == CharterType.None) return;          // cannot select None — it's the absence of a charter
        if (ActiveCharter != CharterType.None) return; // already chosen — charter is permanent
        ActiveCharter = type;
        TownCharterPending = false;
    }

    // ── Modifier accessors (queried by other systems) ─────────────────────────

    public double CommercialGrowthMultiplier  => ActiveCharter == CharterType.Merchant   ? 1.30 : 1.0;
    public double LandValueBonus              => ActiveCharter == CharterType.Merchant   ? 0.06 : 0.0;
    public double IndustrialGrowthMultiplier  => ActiveCharter == CharterType.Industrial ? 1.35 : 1.0;
    public int    JobsPerTileBonus            => ActiveCharter == CharterType.Industrial ? 10 : 0;
    public float  ServiceCoverageRadiusBonus  => ActiveCharter == CharterType.Civic      ? 3.0f : 0f;
    public double ParkHappinessMultiplier     => ActiveCharter == CharterType.Civic      ? 2.0 : 1.0;
}
