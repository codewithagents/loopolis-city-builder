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
            Id:              "tutorial",
            Name:            "Tutorial — Your First City",
            Description:     "Learn the basics. Place roads, zones, and power to start your first city.",
            MapWidth:        32,
            MapHeight:       32,
            StartingBalance: 6_000,
            TickLimit:       0,
            Goal:            new ScenarioGoal(TargetPopulation: 100),
            Medals:          new ScenarioMedals(Bronze: 0, Silver: 0, Gold: 0)
        ),

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

        new ScenarioDefinition(
            Id:              "coastal_town",
            Name:            "Coastal Town",
            Description:     "A city hemmed in by water. Build around the coast and across the islands.",
            MapWidth:        64,
            MapHeight:       64,
            StartingBalance: 5_500,
            TickLimit:       700,
            Goal:            new ScenarioGoal(TargetPopulation: 2_500),
            Medals:          new ScenarioMedals(Bronze: 700, Silver: 550, Gold: 380)
        ),

        new ScenarioDefinition(
            Id:              "polluted_legacy",
            Name:            "The Polluted Legacy",
            Description:     "A struggling industrial base. Clean it up and grow a city people want to live in.",
            MapWidth:        64,
            MapHeight:       64,
            StartingBalance: 7_000,
            TickLimit:       900,
            Goal:            new ScenarioGoal(TargetPopulation: 4_000),
            Medals:          new ScenarioMedals(Bronze: 900, Silver: 700, Gold: 500)
        ),

        new ScenarioDefinition(
            Id:              "forest_reserve",
            Name:            "Forest Reserve",
            Description:     "Dense forest everywhere. Use timber mills wisely — your forests are worth protecting.",
            MapWidth:        64,
            MapHeight:       64,
            StartingBalance: 4_500,
            TickLimit:       600,
            Goal:            new ScenarioGoal(TargetPopulation: 1_500),
            Medals:          new ScenarioMedals(Bronze: 600, Silver: 460, Gold: 310)
        ),

        new ScenarioDefinition(
            Id:              "boom_town",
            Name:            "Boom Town",
            Description:     "Big budget, big ambitions. How fast can you build a real city?",
            MapWidth:        64,
            MapHeight:       64,
            StartingBalance: 15_000,
            TickLimit:       600,
            Goal:            new ScenarioGoal(TargetPopulation: 5_000),
            Medals:          new ScenarioMedals(Bronze: 600, Silver: 480, Gold: 340)
        ),

        new ScenarioDefinition(
            Id:              "founders_challenge",
            Name:            "Founder's Challenge",
            Description:     "Tiny map, almost no money. Prove you can do more with less.",
            MapWidth:        32,
            MapHeight:       32,
            StartingBalance: 3_500,
            TickLimit:       500,
            Goal:            new ScenarioGoal(TargetPopulation: 1_000),
            Medals:          new ScenarioMedals(Bronze: 500, Silver: 400, Gold: 280)
        ),
    };

    /// <summary>Returns the scenario with the given ID, or null if not found.</summary>
    public static ScenarioDefinition? Find(string id) =>
        All.FirstOrDefault(s => s.Id == id);
}
