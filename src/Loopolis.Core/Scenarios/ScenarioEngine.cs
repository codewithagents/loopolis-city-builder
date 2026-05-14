namespace Loopolis.Core.Scenarios;

/// <summary>
/// Stateless helper that evaluates scenario completion and medal awards.
/// All methods are static — no instance state required.
/// </summary>
public static class ScenarioEngine
{
    /// <summary>
    /// Checks whether a scenario goal has been met and which medal (if any) was earned.
    /// </summary>
    /// <param name="scenario">The active scenario definition.</param>
    /// <param name="population">Current city population.</param>
    /// <param name="tick">Current tick count (0-based).</param>
    /// <returns>
    /// <c>complete = true</c> when population ≥ goal target.
    /// <c>medal</c> is "Gold", "Silver", "Bronze", or null.
    /// When <c>complete = false</c> and tick limit exceeded, returns (false, null) — scenario failed.
    /// </returns>
    public static (bool complete, string? medal) CheckCompletion(
        ScenarioDefinition scenario,
        int population,
        int tick)
    {
        var goalReached = population >= scenario.Goal.TargetPopulation;

        if (!goalReached)
        {
            // Tick limit exceeded without reaching goal — scenario failed
            if (scenario.TickLimit > 0 && tick > scenario.TickLimit)
                return (false, null);

            return (false, null);
        }

        // Goal reached — determine medal based on tick thresholds
        // Gold = fastest (≤ Gold threshold), then Silver, then Bronze
        string? medal;
        if (tick <= scenario.Medals.Gold)
            medal = "Gold";
        else if (tick <= scenario.Medals.Silver)
            medal = "Silver";
        else if (tick <= scenario.Medals.Bronze)
            medal = "Bronze";
        else
            medal = null; // goal reached but outside all medal windows

        return (true, medal);
    }

    /// <summary>
    /// Returns true when the tick limit has been exceeded without completing the scenario goal.
    /// Always returns false when TickLimit is 0 (sandbox / no limit).
    /// </summary>
    public static bool IsFailure(ScenarioDefinition scenario, int population, int tick)
    {
        if (scenario.TickLimit <= 0) return false;
        return tick > scenario.TickLimit && population < scenario.Goal.TargetPopulation;
    }
}
