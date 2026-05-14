using Loopolis.Core.Scenarios;
using System.IO;

namespace Loopolis.Core.Tests.Scenarios;

[TestFixture]
public class ScenarioEngineTests
{
    // ── Helper: a scenario with known thresholds ──────────────────────────────

    private static ScenarioDefinition MakeScenario(
        int targetPop  = 500,
        int tickLimit  = 400,
        int goldTick   = 180,
        int silverTick = 280,
        int bronzeTick = 400) =>
        new ScenarioDefinition(
            Id:              "test_scenario",
            Name:            "Test",
            Description:     "Test scenario",
            MapWidth:        32,
            MapHeight:       32,
            StartingBalance: 5_000,
            TickLimit:       tickLimit,
            Goal:            new ScenarioGoal(TargetPopulation: targetPop),
            Medals:          new ScenarioMedals(Bronze: bronzeTick, Silver: silverTick, Gold: goldTick)
        );

    // ── Completion + medal tests ───────────────────────────────────────────────

    [Test]
    public void ScenarioEngine_AwardsGold_WhenGoalReachedBeforeGoldThreshold()
    {
        var scenario = MakeScenario(targetPop: 500, goldTick: 180);

        var (complete, medal) = ScenarioEngine.CheckCompletion(scenario, population: 500, tick: 150);

        Assert.That(complete, Is.True);
        Assert.That(medal,    Is.EqualTo("Gold"));
    }

    [Test]
    public void ScenarioEngine_AwardsSilver_WhenGoalReachedBeforeSilverButAfterGold()
    {
        var scenario = MakeScenario(targetPop: 500, goldTick: 180, silverTick: 280);

        var (complete, medal) = ScenarioEngine.CheckCompletion(scenario, population: 500, tick: 200);

        Assert.That(complete, Is.True);
        Assert.That(medal,    Is.EqualTo("Silver"));
    }

    [Test]
    public void ScenarioEngine_AwardsBronze_WhenGoalReachedBeforeBronzeButAfterSilver()
    {
        var scenario = MakeScenario(targetPop: 500, goldTick: 180, silverTick: 280, bronzeTick: 400);

        var (complete, medal) = ScenarioEngine.CheckCompletion(scenario, population: 500, tick: 350);

        Assert.That(complete, Is.True);
        Assert.That(medal,    Is.EqualTo("Bronze"));
    }

    [Test]
    public void ScenarioEngine_NoMedal_WhenGoalReachedAfterAllThresholds()
    {
        // Goal reached but after the Bronze threshold — no medal earned
        var scenario = MakeScenario(targetPop: 500, bronzeTick: 400, tickLimit: 0); // no tick limit for this case

        var (complete, medal) = ScenarioEngine.CheckCompletion(scenario, population: 500, tick: 450);

        Assert.That(complete, Is.True);
        Assert.That(medal,    Is.Null);
    }

    [Test]
    public void ScenarioEngine_NoMedal_WhenTickLimitExceededWithoutGoal()
    {
        var scenario = MakeScenario(targetPop: 500, tickLimit: 400);

        // Tick 401 > tickLimit of 400, population 400 < 500
        var (complete, medal) = ScenarioEngine.CheckCompletion(scenario, population: 400, tick: 401);

        Assert.That(complete, Is.False);
        Assert.That(medal,    Is.Null);
    }

    [Test]
    public void ScenarioEngine_NotComplete_WhenPopulationBelowTarget()
    {
        var scenario = MakeScenario(targetPop: 500, tickLimit: 400);

        var (complete, medal) = ScenarioEngine.CheckCompletion(scenario, population: 499, tick: 100);

        Assert.That(complete, Is.False);
        Assert.That(medal,    Is.Null);
    }

    // ── Gold at exact threshold ───────────────────────────────────────────────

    [Test]
    public void ScenarioEngine_AwardsGold_WhenTickExactlyEqualsGoldThreshold()
    {
        var scenario = MakeScenario(targetPop: 500, goldTick: 180);

        var (complete, medal) = ScenarioEngine.CheckCompletion(scenario, population: 600, tick: 180);

        Assert.That(complete, Is.True);
        Assert.That(medal,    Is.EqualTo("Gold"));
    }

    // ── IsFailure helper ─────────────────────────────────────────────────────

    [Test]
    public void ScenarioEngine_IsFailure_True_WhenTickLimitExceededAndGoalNotMet()
    {
        var scenario = MakeScenario(targetPop: 500, tickLimit: 400);

        Assert.That(ScenarioEngine.IsFailure(scenario, population: 100, tick: 401), Is.True);
    }

    [Test]
    public void ScenarioEngine_IsFailure_False_WhenTickLimitZero()
    {
        var scenario = MakeScenario(targetPop: 500, tickLimit: 0);

        // tickLimit=0 → sandbox mode, never fails
        Assert.That(ScenarioEngine.IsFailure(scenario, population: 100, tick: 10_000), Is.False);
    }

    // ── ScenarioLibrary tests ─────────────────────────────────────────────────

    [Test]
    public void ScenarioLibrary_HasTenScenarios()
    {
        Assert.That(ScenarioLibrary.All.Count, Is.EqualTo(10));
    }

    [Test]
    public void ScenarioLibrary_AllScenarios_HaveValidMedalThresholds()
    {
        foreach (var scenario in ScenarioLibrary.All)
        {
            var m = scenario.Medals;
            Assert.That(m.Gold,   Is.LessThan(m.Silver),
                $"{scenario.Id}: Gold({m.Gold}) must be < Silver({m.Silver})");
            Assert.That(m.Silver, Is.LessThan(m.Bronze),
                $"{scenario.Id}: Silver({m.Silver}) must be < Bronze({m.Bronze})");
            // When TickLimit > 0, Bronze must be ≤ TickLimit
            if (scenario.TickLimit > 0)
                Assert.That(m.Bronze, Is.LessThanOrEqualTo(scenario.TickLimit),
                    $"{scenario.Id}: Bronze({m.Bronze}) must be ≤ TickLimit({scenario.TickLimit})");
        }
    }

    [Test]
    public void ScenarioLibrary_Find_ReturnsScenariosById()
    {
        var scenario = ScenarioLibrary.Find("fresh_start");
        Assert.That(scenario,    Is.Not.Null);
        Assert.That(scenario!.Name, Is.EqualTo("Fresh Start"));
    }

    [Test]
    public void ScenarioLibrary_Find_ReturnsNull_ForUnknownId()
    {
        Assert.That(ScenarioLibrary.Find("nonexistent"), Is.Null);
    }

    [Test]
    public void ScenarioLibrary_AllScenarios_HaveNonEmptyDescriptions()
    {
        foreach (var s in ScenarioLibrary.All)
            Assert.That(s.Description, Is.Not.Empty, $"{s.Id} has empty description");
    }
}

// ── LeaderboardSystem Tests ────────────────────────────────────────────────────

[TestFixture]
public class LeaderboardSystemTests
{
    private string _tempFile = null!;

    [SetUp]
    public void SetUp()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"loopolis_lb_{Guid.NewGuid():N}.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    // ── Save / Load ────────────────────────────────────────────────────────────

    [Test]
    public void LeaderboardSystem_SavesNewEntry()
    {
        LeaderboardSystem.Save("fresh_start", "Gold", 198, 512, _tempFile);

        var entries = LeaderboardSystem.Load(_tempFile);

        Assert.That(entries.ContainsKey("fresh_start"), Is.True);
        var entry = entries["fresh_start"];
        Assert.That(entry.Medal,      Is.EqualTo("Gold"));
        Assert.That(entry.Tick,       Is.EqualTo(198));
        Assert.That(entry.Population, Is.EqualTo(512));
    }

    [Test]
    public void LeaderboardSystem_UpdatesEntry_WhenHigherMedal()
    {
        // Start with a Bronze entry
        LeaderboardSystem.Save("fresh_start", "Bronze", 400, 500, _tempFile);

        // Save a Silver result — should overwrite
        LeaderboardSystem.Save("fresh_start", "Silver", 300, 510, _tempFile);

        var entries = LeaderboardSystem.Load(_tempFile);
        Assert.That(entries["fresh_start"].Medal, Is.EqualTo("Silver"));
        Assert.That(entries["fresh_start"].Tick,  Is.EqualTo(300));
    }

    [Test]
    public void LeaderboardSystem_DoesNotDowngrade_WhenSameMedalButWorseTick()
    {
        // Save a Silver at tick 250
        LeaderboardSystem.Save("fresh_start", "Silver", 250, 510, _tempFile);

        // Try to save a Silver at tick 350 (worse)
        LeaderboardSystem.Save("fresh_start", "Silver", 350, 520, _tempFile);

        var entries = LeaderboardSystem.Load(_tempFile);
        Assert.That(entries["fresh_start"].Tick, Is.EqualTo(250),
            "Should keep the lower tick (better result)");
    }

    [Test]
    public void LeaderboardSystem_GoldBeatsSilver()
    {
        // Silver at 200
        LeaderboardSystem.Save("river_valley", "Silver", 200, 2000, _tempFile);

        // Gold at 350 (higher tick, but Gold > Silver)
        LeaderboardSystem.Save("river_valley", "Gold", 350, 2001, _tempFile);

        var entries = LeaderboardSystem.Load(_tempFile);
        Assert.That(entries["river_valley"].Medal, Is.EqualTo("Gold"),
            "Gold medal should beat Silver even with a higher tick count");
    }

    [Test]
    public void LeaderboardSystem_Load_ReturnsEmptyDict_WhenFileDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"no_such_file_{Guid.NewGuid():N}.json");
        var entries = LeaderboardSystem.Load(missing);
        Assert.That(entries, Is.Empty);
    }

    [Test]
    public void LeaderboardSystem_MultipleScenarios_StoredIndependently()
    {
        LeaderboardSystem.Save("fresh_start",  "Gold",   200, 500,  _tempFile);
        LeaderboardSystem.Save("river_valley", "Bronze", 590, 2000, _tempFile);

        var entries = LeaderboardSystem.Load(_tempFile);
        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(entries["fresh_start"].Medal,  Is.EqualTo("Gold"));
        Assert.That(entries["river_valley"].Medal, Is.EqualTo("Bronze"));
    }
}
