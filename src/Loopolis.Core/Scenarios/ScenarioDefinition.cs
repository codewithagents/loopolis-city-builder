using Loopolis.Core.Grid;

namespace Loopolis.Core.Scenarios;

/// <summary>
/// Population target a player must reach to complete a scenario.
/// </summary>
public record ScenarioGoal(
    int TargetPopulation
);

/// <summary>
/// Tick-count thresholds for medal awards.
/// Gold is earned by finishing fastest (lowest tick count).
/// All three values must satisfy: Gold &lt; Silver &lt; Bronze.
/// </summary>
public record ScenarioMedals(
    int Bronze,
    int Silver,
    int Gold
);

/// <summary>
/// Immutable definition of a single scenario.
/// TickLimit = 0 means no tick limit (sandbox / pure goal mode).
/// TerrainSeed = null means use the default scenario setup logic (no procedural terrain).
/// DisabledZones = null means all zones are available; a non-null list restricts placement.
/// </summary>
public record ScenarioDefinition(
    string Id,
    string Name,
    string Description,
    int MapWidth,
    int MapHeight,
    int StartingBalance,
    int TickLimit,
    ScenarioGoal Goal,
    ScenarioMedals Medals,
    string? TerrainSeed = null,
    IReadOnlyList<ZoneType>? DisabledZones = null
);
