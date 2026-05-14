using Loopolis.Core.Grid;
using Loopolis.Core.Scenarios;
using Loopolis.Core.UI.ViewModels;

namespace Loopolis.Core.UI.Tests.ViewModels;

[TestFixture]
public class ScenarioCardViewModelTests
{
    // ---- Helpers ----

    private static ScenarioDefinition MakeScenario(
        string id = "test",
        string name = "Test Scenario",
        string description = "A simple test scenario description.",
        int targetPop = 500,
        IReadOnlyList<ZoneType>? disabledZones = null) =>
        new ScenarioDefinition(
            Id: id,
            Name: name,
            Description: description,
            MapWidth: 32,
            MapHeight: 32,
            StartingBalance: 5_000,
            TickLimit: 400,
            Goal: new ScenarioGoal(TargetPopulation: targetPop),
            Medals: new ScenarioMedals(Bronze: 400, Silver: 300, Gold: 200),
            DisabledZones: disabledZones
        );

    // ---- CardMinimumHeightEstimate regression: must always be > 0 ----

    [Test]
    public void CardMinimumHeightEstimate_AlwaysPositive_NoMedalNoRestrictions()
    {
        var vm = new ScenarioCardViewModel(MakeScenario());
        Assert.That(vm.CardMinimumHeightEstimate, Is.GreaterThan(0));
    }

    [Test]
    public void CardMinimumHeightEstimate_AlwaysPositive_WithGoldMedal()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(), bestMedal: "gold");
        Assert.That(vm.CardMinimumHeightEstimate, Is.GreaterThan(0));
    }

    [Test]
    public void CardMinimumHeightEstimate_AlwaysPositive_WithDisabledZones()
    {
        var vm = new ScenarioCardViewModel(
            MakeScenario(disabledZones: new[] { ZoneType.Industrial }));
        Assert.That(vm.CardMinimumHeightEstimate, Is.GreaterThan(0));
    }

    [Test]
    public void CardMinimumHeightEstimate_LargerWithMedal_ThanWithout()
    {
        var withoutMedal = new ScenarioCardViewModel(MakeScenario());
        var withMedal = new ScenarioCardViewModel(MakeScenario(), bestMedal: "bronze");
        Assert.That(withMedal.CardMinimumHeightEstimate, Is.GreaterThan(withoutMedal.CardMinimumHeightEstimate));
    }

    [Test]
    public void CardMinimumHeightEstimate_LargerWithDisabledZones_ThanWithout()
    {
        var plain = new ScenarioCardViewModel(MakeScenario());
        var restricted = new ScenarioCardViewModel(
            MakeScenario(disabledZones: new[] { ZoneType.Commercial }));
        Assert.That(restricted.CardMinimumHeightEstimate, Is.GreaterThan(plain.CardMinimumHeightEstimate));
    }

    // ---- DisabledZoneBadge ----

    [Test]
    public void DisabledZoneBadge_Empty_WhenNoRestrictions()
    {
        var vm = new ScenarioCardViewModel(MakeScenario());
        Assert.That(vm.DisabledZoneBadge, Is.EqualTo(""));
    }

    [Test]
    public void DisabledZoneBadge_Empty_WhenNullDisabledZones()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(disabledZones: null));
        Assert.That(vm.DisabledZoneBadge, Is.EqualTo(""));
    }

    [Test]
    public void DisabledZoneBadge_ShowsLabel_WhenIndustrialDisabled()
    {
        var vm = new ScenarioCardViewModel(
            MakeScenario(disabledZones: new[] { ZoneType.Industrial }));
        Assert.That(vm.DisabledZoneBadge, Is.EqualTo("⛔ No Industrial"));
    }

    [Test]
    public void DisabledZoneBadge_ShowsLabel_WhenCommercialDisabled()
    {
        var vm = new ScenarioCardViewModel(
            MakeScenario(disabledZones: new[] { ZoneType.Commercial }));
        Assert.That(vm.DisabledZoneBadge, Is.EqualTo("⛔ No Commercial"));
    }

    [Test]
    public void DisabledZoneBadge_JoinsMultipleZones()
    {
        var vm = new ScenarioCardViewModel(
            MakeScenario(disabledZones: new[] { ZoneType.Industrial, ZoneType.Commercial }));
        Assert.That(vm.DisabledZoneBadge, Is.EqualTo("⛔ No Industrial, Commercial"));
    }

    [Test]
    public void HasDisabledZones_False_WhenNoRestrictions()
    {
        var vm = new ScenarioCardViewModel(MakeScenario());
        Assert.That(vm.HasDisabledZones, Is.False);
    }

    [Test]
    public void HasDisabledZones_True_WhenIndustrialDisabled()
    {
        var vm = new ScenarioCardViewModel(
            MakeScenario(disabledZones: new[] { ZoneType.Industrial }));
        Assert.That(vm.HasDisabledZones, Is.True);
    }

    // ---- MedalBadge ----

    [Test]
    public void MedalBadge_Empty_WhenNotPlayed()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(), bestMedal: null);
        Assert.That(vm.MedalBadge, Is.EqualTo(""));
    }

    [Test]
    public void MedalBadge_Gold_WhenBestMedalIsGold()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(), bestMedal: "gold");
        Assert.That(vm.MedalBadge, Is.EqualTo("🥇"));
    }

    [Test]
    public void MedalBadge_Silver_WhenBestMedalIsSilver()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(), bestMedal: "silver");
        Assert.That(vm.MedalBadge, Is.EqualTo("🥈"));
    }

    [Test]
    public void MedalBadge_Bronze_WhenBestMedalIsBronze()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(), bestMedal: "bronze");
        Assert.That(vm.MedalBadge, Is.EqualTo("🥉"));
    }

    [Test]
    public void MedalBadge_CaseInsensitive_Gold()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(), bestMedal: "Gold");
        Assert.That(vm.MedalBadge, Is.EqualTo("🥇"));
    }

    // ---- Title + Description ----

    [Test]
    public void Title_MatchesScenarioName()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(name: "Fresh Start"));
        Assert.That(vm.Title, Is.EqualTo("Fresh Start"));
    }

    [Test]
    public void Description_ShortDescription_NotTruncated()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(description: "Short description."));
        Assert.That(vm.Description, Is.EqualTo("Short description."));
    }

    [Test]
    public void Description_LongDescription_TruncatedWithEllipsis()
    {
        var longDesc = new string('A', 100);
        var vm = new ScenarioCardViewModel(MakeScenario(description: longDesc));
        Assert.That(vm.Description.Length, Is.EqualTo(80));
        Assert.That(vm.Description, Does.EndWith("…"));
    }

    // ---- Difficulty ----

    [Test]
    public void DifficultyLabel_Tutorial_ForTargetPop100OrBelow()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(targetPop: 100));
        Assert.That(vm.DifficultyLabel, Is.EqualTo("Tutorial"));
    }

    [Test]
    public void DifficultyLabel_Easy_ForTargetPop1000()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(targetPop: 1000));
        Assert.That(vm.DifficultyLabel, Is.EqualTo("Easy"));
    }

    [Test]
    public void DifficultyLabel_Medium_ForTargetPop5000()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(targetPop: 5000));
        Assert.That(vm.DifficultyLabel, Is.EqualTo("Medium"));
    }

    [Test]
    public void DifficultyLabel_Hard_ForTargetPop10000()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(targetPop: 10_000));
        Assert.That(vm.DifficultyLabel, Is.EqualTo("Hard"));
    }

    [Test]
    public void DifficultyLabel_Expert_ForTargetPopAbove15000()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(targetPop: 50_000));
        Assert.That(vm.DifficultyLabel, Is.EqualTo("Expert"));
    }

    // ---- ID ----

    [Test]
    public void Id_MatchesScenarioId()
    {
        var vm = new ScenarioCardViewModel(MakeScenario(id: "fresh_start"));
        Assert.That(vm.Id, Is.EqualTo("fresh_start"));
    }
}
