using Loopolis.Core.Simulation;

namespace Loopolis.Core.Policies;

/// <summary>
/// Immutable data definition for a single city policy.
/// </summary>
/// <param name="Type">The enum identifier for this policy.</param>
/// <param name="Name">Display name shown in the UI.</param>
/// <param name="Description">Short description of the policy's effects.</param>
/// <param name="CostPerTick">Budget deducted every tick while active.</param>
/// <param name="UnlockAt">Minimum milestone state required to activate this policy.</param>
public record PolicyDefinition(
    PolicyType Type,
    string Name,
    string Description,
    int CostPerTick,
    GameState UnlockAt
);

/// <summary>
/// Static catalog of all policy definitions.
/// </summary>
public static class PolicyCatalog
{
    public static readonly PolicyDefinition[] All =
    {
        new(
            PolicyType.GreenCity,
            "Green City",
            "Invest in clean infrastructure. Reduces industrial pollution by 35% and boosts happiness by +0.10 city-wide. Costs $80/tick.",
            CostPerTick: 80,
            UnlockAt: GameState.Active
        ),
        new(
            PolicyType.IndustrialHub,
            "Industrial Hub",
            "Attract manufacturing investment. Increases industrial growth rate by 25% and adds +3 jobs per industrial tile. Costs $50/tick.",
            CostPerTick: 50,
            UnlockAt: GameState.Town
        ),
        new(
            PolicyType.CommercialBoost,
            "Commercial Boost",
            "Offer business incentives. Accelerates commercial activity growth by 25%. Costs $60/tick.",
            CostPerTick: 60,
            UnlockAt: GameState.Town
        ),
        new(
            PolicyType.OpenCity,
            "Open City",
            "Lower barriers to entry. Increases immigration rate by 40% but reduces effective tax revenue by 12%. Costs $30/tick.",
            CostPerTick: 30,
            UnlockAt: GameState.Active
        ),
    };

    /// <summary>Returns the definition for the given policy type, or null if not found.</summary>
    public static PolicyDefinition? Find(PolicyType type) =>
        Array.Find(All, p => p.Type == type);
}
