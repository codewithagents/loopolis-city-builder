using Loopolis.Core.Grid;
using Loopolis.Core.Scenarios;

namespace Loopolis.Core.UI.ViewModels;

/// <summary>
/// Pure C# ViewModel for a single scenario card in the MainMenu scenario picker.
/// Encapsulates all display/formatting logic so Godot nodes can remain dumb renderers.
/// </summary>
public class ScenarioCardViewModel
{
    public string Id { get; }
    public string Title { get; }

    /// <summary>
    /// Shortened description suitable for a compact card (max 80 chars, truncated with "…").
    /// </summary>
    public string Description { get; }

    /// <summary>Difficulty label derived from goal population: Easy / Medium / Hard / Expert.</summary>
    public string DifficultyLabel { get; }

    /// <summary>Medal emoji for the player's personal best, or empty string if not played.</summary>
    public string MedalBadge { get; }

    /// <summary>True if the scenario disables at least one zone type.</summary>
    public bool HasDisabledZones { get; }

    /// <summary>
    /// Human-readable label for disabled zones, e.g. "⛔ No Industrial", or empty string.
    /// When multiple zones are disabled they are joined: "⛔ No Industrial, Commercial".
    /// </summary>
    public string DisabledZoneBadge { get; }

    /// <summary>
    /// Estimated minimum card height in pixels. This value is always positive (>0) and is
    /// used by layout code to size cards — the regression target for the height=0 bug.
    /// </summary>
    public int CardMinimumHeightEstimate { get; }

    private const int BaseCardHeight = 120;
    private const int MedalRowHeight = 24;
    private const int DisabledZoneRowHeight = 22;

    public ScenarioCardViewModel(ScenarioDefinition scenario, string? bestMedal = null)
    {
        Id = scenario.Id;
        Title = scenario.Name;
        Description = Shorten(scenario.Description, 80);
        DifficultyLabel = DeriveDifficulty(scenario.Goal.TargetPopulation);
        MedalBadge = MedalToBadge(bestMedal);

        HasDisabledZones = scenario.DisabledZones is { Count: > 0 };
        DisabledZoneBadge = BuildDisabledZoneBadge(scenario.DisabledZones);

        // Height is always at least BaseCardHeight — never zero.
        // Extra rows are added only when the content is present.
        int height = BaseCardHeight;
        if (!string.IsNullOrEmpty(MedalBadge)) height += MedalRowHeight;
        if (HasDisabledZones) height += DisabledZoneRowHeight;
        CardMinimumHeightEstimate = height;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string Shorten(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..(maxLength - 1)] + "…";
    }

    private static string DeriveDifficulty(int targetPopulation) => targetPopulation switch
    {
        <= 100 => "Tutorial",
        <= 1_000 => "Easy",
        <= 5_000 => "Medium",
        <= 15_000 => "Hard",
        _ => "Expert"
    };

    private static string MedalToBadge(string? medal) => medal?.ToLowerInvariant() switch
    {
        "gold" => "🥇",
        "silver" => "🥈",
        "bronze" => "🥉",
        _ => ""
    };

    private static string BuildDisabledZoneBadge(IReadOnlyList<ZoneType>? disabled)
    {
        if (disabled == null || disabled.Count == 0) return "";
        var names = disabled.Select(z => z.ToString());
        return "⛔ No " + string.Join(", ", names);
    }
}
