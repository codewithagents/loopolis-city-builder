using System.Globalization;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Priority levels for advisory messages. Higher values = more urgent.
/// The Advise() method returns the highest-priority matching rule.
/// </summary>
public enum AdvisoryPriority { Good, Tip, Warning, Critical }

/// <summary>
/// A single advisory message produced by CityAdvisor.Advise().
/// </summary>
public record AdvisoryMessage(
    string Text,
    AdvisoryPriority Priority,
    string Category
);

/// <summary>
/// Plain snapshot of the simulation state needed by CityAdvisor.
/// Populated from SimulationEngine fields; no Godot dependencies.
/// </summary>
public record SimulationState(
    int Population,
    int Tick,
    double Balance,
    double IncomePerTick,
    double CostPerTick,
    float AverageHappiness,
    int DistressedTileCount,
    float EmploymentRatio,
    int PowerSupply,
    int PowerDemand,
    int PoweredTiles,
    int TotalActiveTiles,
    float ServiceCoverageRatio,
    float PopulationGrowthRate,
    int NextMilestoneThreshold,
    string NextMilestoneName
);

/// <summary>
/// Pure static analyzer that reads a SimulationState snapshot and returns
/// the single most important advisory hint for the player.
///
/// Priority order (first matching rule fires):
///   1. Critical — Bankruptcy imminent (balance &lt; costPerTick × 20)
///   2. Critical — Power brownout (demand > supply, supply > 0)
///   3. Critical — Distress decay active (≥3 tiles in persistent unhappy decay)
///   4. Warning  — High unemployment (ratio &lt; 0.45, pop > 100)
///   5. Warning  — Power capacity near limit (demand > supply × 0.85)
///   6. Warning  — No growth for 50+ ticks (growthRate &lt; 0.001, pop > 50)
///   7. Warning  — Low service coverage (ratio &lt; 0.5, pop > 200)
///   8. Tip      — Budget surplus, city idle (balance > income × 50, growthRate &lt; 0.01)
///   9. Tip      — Near next milestone (population > threshold × 0.85)
///  10. Good     — Everything is fine
/// </summary>
public static class CityAdvisor
{
    public static AdvisoryMessage Advise(SimulationState state)
    {
        // ── Rule 1: Bankruptcy imminent ──────────────────────────────────────
        // Only fires when the city has debt or a clear runway of &lt;20 ticks.
        if (state.CostPerTick > 0 && state.Balance < state.CostPerTick * 20)
        {
            var deficit = state.IncomePerTick - state.CostPerTick;
            var deficitStr = deficit >= 0
                ? string.Create(CultureInfo.InvariantCulture, $"+{deficit:F1}")
                : string.Create(CultureInfo.InvariantCulture, $"{deficit:F1}");
            return new AdvisoryMessage(
                string.Create(CultureInfo.InvariantCulture,
                    $"Budget critical — ${state.Balance:F0} left at {deficitStr}/tick. Reduce costs or build more zones."),
                AdvisoryPriority.Critical,
                "Budget");
        }

        // ── Rule 2: Power brownout (supply > 0 but demand exceeds supply) ───
        if (state.PowerSupply > 0 && state.PowerDemand > state.PowerSupply)
        {
            return new AdvisoryMessage(
                string.Create(CultureInfo.InvariantCulture,
                    $"Brownout! Power demand {state.PowerDemand}MW exceeds supply {state.PowerSupply}MW — place another plant."),
                AdvisoryPriority.Critical,
                "Power");
        }

        // ── Rule 3: Distress decay active ────────────────────────────────────
        if (state.DistressedTileCount >= 3)
        {
            return new AdvisoryMessage(
                string.Create(CultureInfo.InvariantCulture,
                    $"City in distress — {state.DistressedTileCount} tiles losing residents. Add services or parks near unhappy zones."),
                AdvisoryPriority.Critical,
                "Happiness");
        }

        // ── Rule 4: High unemployment ────────────────────────────────────────
        if (state.Population > 100 && state.EmploymentRatio < 0.45f)
        {
            return new AdvisoryMessage(
                string.Create(CultureInfo.InvariantCulture,
                    $"Unemployment high ({state.EmploymentRatio:P0}) — zone more industrial or add services."),
                AdvisoryPriority.Warning,
                "Employment");
        }

        // ── Rule 5: Power capacity near limit ───────────────────────────────
        if (state.PowerSupply > 0 && state.PowerDemand > state.PowerSupply * 0.85)
        {
            var pct = (int)(state.PowerDemand / (double)state.PowerSupply * 100);
            return new AdvisoryMessage(
                string.Create(CultureInfo.InvariantCulture, $"Power at {pct}% capacity — build another plant before brownout."),
                AdvisoryPriority.Warning,
                "Power");
        }

        // ── Rule 6: No growth for 50+ ticks ─────────────────────────────────
        if (state.Population > 50 && state.PopulationGrowthRate < 0.001f)
        {
            // Diagnose the likely cause
            float unpoweredRatio = state.TotalActiveTiles > 0
                ? 1f - ((float)state.PoweredTiles / state.TotalActiveTiles)
                : 0f;

            if (unpoweredRatio > 0.3f)
            {
                var unpoweredPct = (int)(unpoweredRatio * 100);
                return new AdvisoryMessage(
                    string.Create(CultureInfo.InvariantCulture, $"Growth stalled — {unpoweredPct}% of tiles unpowered. Extend the power grid."),
                    AdvisoryPriority.Warning,
                    "Growth");
            }

            if (state.AverageHappiness < 0.45f)
            {
                return new AdvisoryMessage(
                    string.Create(CultureInfo.InvariantCulture, $"Growth stalled — happiness low ({state.AverageHappiness:F2}). Add a park or fire station."),
                    AdvisoryPriority.Warning,
                    "Growth");
            }

            return new AdvisoryMessage(
                "Growth stalled — zone more residential along roads to expand capacity.",
                AdvisoryPriority.Warning,
                "Growth");
        }

        // ── Rule 7: Low service coverage ─────────────────────────────────────
        if (state.Population > 200 && state.ServiceCoverageRatio < 0.5f)
        {
            var pct = (int)(state.ServiceCoverageRatio * 100);
            return new AdvisoryMessage(
                string.Create(CultureInfo.InvariantCulture, $"Only {pct}% of city has service coverage — build fire/police stations."),
                AdvisoryPriority.Warning,
                "Services");
        }

        // ── Rule 8: Budget surplus, city idle ────────────────────────────────
        if (state.IncomePerTick > 0
            && state.Balance > state.IncomePerTick * 50
            && state.PopulationGrowthRate < 0.01f
            && state.Population > 0)
        {
            return new AdvisoryMessage(
                string.Create(CultureInfo.InvariantCulture, $"${state.Balance:F0} saved with nowhere to go — try a manual upgrade (G) or add a park."),
                AdvisoryPriority.Tip,
                "Budget");
        }

        // ── Rule 9: Near next milestone ──────────────────────────────────────
        if (state.NextMilestoneThreshold > 0
            && state.Population > state.NextMilestoneThreshold * 0.85
            && state.Population < state.NextMilestoneThreshold)
        {
            var gap = state.NextMilestoneThreshold - state.Population;
            return new AdvisoryMessage(
                string.Create(CultureInfo.InvariantCulture, $"Almost {state.NextMilestoneName}! {gap} more residents needed — zone more residential."),
                AdvisoryPriority.Tip,
                "Growth");
        }

        // ── Rule 10: Everything looks good ───────────────────────────────────
        var incomeSign = state.IncomePerTick >= 0 ? "+" : "";
        return new AdvisoryMessage(
            string.Create(CultureInfo.InvariantCulture,
                $"City thriving. Population {state.Population} · Happiness {state.AverageHappiness:F2} · {incomeSign}${state.IncomePerTick:F1}/tick"),
            AdvisoryPriority.Good,
            "Idle");
    }
}
