namespace Loopolis.Core.Petitions;

/// <summary>
/// A citizen petition filed when simulation thresholds are breached.
///
/// Petitions represent the city "talking back" to the player — grounded in real simulation state.
/// Citizens identify themselves by their auto-named local district (DistrictNamer).
///
/// Lifecycle:
///   IssuedTick → Resolved before DeadlineTick: no penalty.
///   Unresolved at DeadlineTick: PenaltyApplied = true, -0.05 happiness penalty for the district
///   for 100 ticks.
/// </summary>
public record Petition(
    string Id,              // "{category}_{districtHash}" — used for dedup
    string DistrictName,    // e.g. "Pine Valley"
    string Text,            // full petition text shown to player
    string Category,        // "Happiness" | "Power" | "Employment" | "Services" | "Pollution" | "Overcrowding"
    int IssuedTick,
    int DeadlineTick,       // IssuedTick + 75 ticks
    bool Resolved,
    bool PenaltyApplied     // true once the -0.05 happiness penalty has been applied
);
