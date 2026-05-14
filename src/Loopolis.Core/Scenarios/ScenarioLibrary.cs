namespace Loopolis.Core.Scenarios;

/// <summary>
/// Central catalog of all built-in scenarios.
/// Add new entries here; the UI and runner discover them via <see cref="All"/>.
/// </summary>
public static class ScenarioLibrary
{
    public static readonly IReadOnlyList<ScenarioDefinition> All = new[]
    {
        new ScenarioDefinition(
            Id:              "fresh_start",
            Name:            "Fresh Start",
            Description:     "A clean slate. Build the foundations of a thriving town.",
            MapWidth:        32,
            MapHeight:       32,
            StartingBalance: 5_000,
            TickLimit:       400,
            Goal:            new ScenarioGoal(TargetPopulation: 500),
            Medals:          new ScenarioMedals(Bronze: 400, Silver: 300, Gold: 220)
        ),

        new ScenarioDefinition(
            Id:              "river_valley",
            Name:            "River Valley",
            Description:     "A lush valley with forests and rivers. Use the terrain wisely.",
            MapWidth:        64,
            MapHeight:       64,
            StartingBalance: 4_000,
            TickLimit:       600,
            Goal:            new ScenarioGoal(TargetPopulation: 2_000),
            Medals:          new ScenarioMedals(Bronze: 600, Silver: 450, Gold: 300)
        ),

        new ScenarioDefinition(
            Id:              "hill_town",
            Name:            "The Hill Town",
            Description:     "Build into the hills. Quarries will fund your growth.",
            MapWidth:        64,
            MapHeight:       64,
            StartingBalance: 6_000,
            TickLimit:       800,
            Goal:            new ScenarioGoal(TargetPopulation: 3_000),
            Medals:          new ScenarioMedals(Bronze: 800, Silver: 600, Gold: 400)
        ),

        new ScenarioDefinition(
            Id:              "city_challenge",
            Name:            "City Challenge",
            Description:     "Limited funds. Can you reach city status before going broke?",
            MapWidth:        64,
            MapHeight:       64,
            StartingBalance: 3_000,
            TickLimit:       1_000,
            Goal:            new ScenarioGoal(TargetPopulation: 5_000),
            Medals:          new ScenarioMedals(Bronze: 1_000, Silver: 800, Gold: 600)
        ),

        new ScenarioDefinition(
            Id:              "metro_run",
            Name:            "Metropolis Run",
            Description:     "The big league. Build a true metropolis.",
            MapWidth:        128,
            MapHeight:       128,
            StartingBalance: 8_000,
            TickLimit:       1_500,
            Goal:            new ScenarioGoal(TargetPopulation: 10_000),
            Medals:          new ScenarioMedals(Bronze: 1_500, Silver: 1_200, Gold: 900)
        ),
    };

    /// <summary>Returns the scenario with the given ID, or null if not found.</summary>
    public static ScenarioDefinition? Find(string id) =>
        All.FirstOrDefault(s => s.Id == id);
}
