namespace Loopolis.Core.Charters;

/// <summary>Human-readable metadata about a charter for UI display.</summary>
public record CharterDefinition(
    CharterType Type,
    string Name,           // e.g. "Merchant Charter"
    string Description,    // 1 sentence for the choice card
    string Effect,         // formatted effect string for choice card, e.g. "Commercial +30%, Land Value +6%"
    string Era             // "Town" | "City" | "Metropolis"
);
