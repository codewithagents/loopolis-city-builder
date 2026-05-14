using System.Globalization;

namespace Loopolis.Core.UI.ViewModels;

/// <summary>
/// Happiness display level — maps to a UI color without depending on Godot.
/// </summary>
public enum HappinessLevel
{
    Good,     // >= 0.7
    Warning,  // >= 0.4 and < 0.7
    Critical  // < 0.4
}

/// <summary>
/// Pure C# ViewModel for the TopBar HUD. Formats all display strings from raw simulation
/// values. Godot nodes set these as inputs and read computed properties for display.
/// </summary>
public class TopBarViewModel
{
    // --- Inputs ---

    public double Balance { get; set; }
    public int Population { get; set; }
    public string MilestoneName { get; set; } = "Village";
    public float Happiness { get; set; }
    public int PowerSupply { get; set; }
    public int PowerDemand { get; set; }
    public int PolicyCostPerTick { get; set; }
    public int ResidentialCount { get; set; }
    public int CommercialCount { get; set; }
    public int IndustrialCount { get; set; }

    // --- Computed display properties ---

    /// <summary>
    /// Dollar-formatted balance string. Negative values use leading minus, e.g. "-$1,200".
    /// </summary>
    public string BalanceText =>
        Balance >= 0
            ? $"${Balance.ToString("N0", CultureInfo.InvariantCulture)}"
            : $"-${Math.Abs(Balance).ToString("N0", CultureInfo.InvariantCulture)}";

    /// <summary>True when the balance is negative — use to tint the label red.</summary>
    public bool BalanceNegative => Balance < 0;

    /// <summary>
    /// Compact population string: raw integer below 1,000, one-decimal "k" above.
    /// Examples: 0 → "0", 999 → "999", 1000 → "1.0k", 1204 → "1.2k", 25000 → "25.0k"
    /// </summary>
    public string PopulationText =>
        Population >= 1000
            ? $"{(Population / 1000.0).ToString("F1", CultureInfo.InvariantCulture)}k"
            : Population.ToString(CultureInfo.InvariantCulture);

    /// <summary>Combined population + milestone label, e.g. "1.2k (Town)".</summary>
    public string MilestoneLabel => $"{PopulationText} ({MilestoneName})";

    /// <summary>Power MW display, e.g. "⚡ 120/80 MW".</summary>
    public string PowerText => $"⚡ {PowerSupply}/{PowerDemand} MW";

    /// <summary>True when power demand exceeds supply (brownout condition).</summary>
    public bool PowerShortage => PowerDemand > PowerSupply;

    /// <summary>Happiness formatted to two decimal places, e.g. "😊 0.72".</summary>
    public string HappinessText => $"😊 {Happiness.ToString("F2", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Happiness level for color coding: Good (green), Warning (yellow), Critical (red).
    /// </summary>
    public HappinessLevel HappinessLevel =>
        Happiness >= 0.7f ? HappinessLevel.Good
        : Happiness >= 0.4f ? HappinessLevel.Warning
        : HappinessLevel.Critical;

    /// <summary>True when a policy is active with a non-zero cost this tick.</summary>
    public bool ShowPolicyCost => PolicyCostPerTick > 0;

    /// <summary>Policy cost label, e.g. "📋 -$15/tk".</summary>
    public string PolicyCostText => $"📋 -${PolicyCostPerTick}/tk";

    /// <summary>Zone count summary, e.g. "🏘 10  🏪 5  🏭 3".</summary>
    public string ZoneSummary => $"🏘 {ResidentialCount}  🏪 {CommercialCount}  🏭 {IndustrialCount}";
}
