using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class MilestoneSystemTests
{
    private MilestoneSystem _milestones = null!;

    [SetUp]
    public void SetUp() => _milestones = new MilestoneSystem();

    [Test]
    public void NoPop_StaysActive()
    {
        _milestones.Check(population: 0, balance: 5_000, netPerTick: 10, tick: 0);

        Assert.That(_milestones.CurrentState, Is.EqualTo(GameState.Active));
        Assert.That(_milestones.Reached, Is.Empty);
    }

    [Test]
    public void Reaches500_EarnsTownMilestone()
    {
        _milestones.Check(population: 500, balance: 5_000, netPerTick: 10, tick: 42);

        Assert.That(_milestones.CurrentState, Is.EqualTo(GameState.Town));
        Assert.That(_milestones.Reached, Has.Count.EqualTo(1));
        Assert.That(_milestones.Reached[0].Name, Is.EqualTo("Town"));
        Assert.That(_milestones.Reached[0].Emoji, Is.EqualTo("🥉"));
    }

    [Test]
    public void Reaches5000_EarnsCity()
    {
        _milestones.Check(population: 5_000, balance: 10_000, netPerTick: 50, tick: 100);

        Assert.That(_milestones.CurrentState, Is.EqualTo(GameState.City));
        Assert.That(_milestones.Reached.Any(r => r.Name == "City"), Is.True);
    }

    [Test]
    public void MilestoneTracksTickNumber()
    {
        _milestones.Check(population: 500, balance: 5_000, netPerTick: 10, tick: 77);

        Assert.That(_milestones.Reached[0].ReachedAtTick, Is.EqualTo(77));
    }

    [Test]
    public void BankruptWhenBalanceNegativeAndNoPopulation()
    {
        _milestones.Check(population: 0, balance: -100, netPerTick: -5, tick: 200);

        Assert.That(_milestones.CurrentState, Is.EqualTo(GameState.Bankrupt));
        Assert.That(_milestones.IsOver, Is.True);
    }

    [Test]
    public void NoBankruptWithNegativeBalanceIfPopulationExists()
    {
        // City can recover — negative balance but people still live there
        _milestones.Check(population: 1_000, balance: -500, netPerTick: 5, tick: 100);

        Assert.That(_milestones.CurrentState, Is.Not.EqualTo(GameState.Bankrupt),
            "Should not go bankrupt if there is still population (city can recover)");
    }

    [Test]
    public void MilestonesAccumulate_AllTracked()
    {
        // Single Check call with high enough population accumulates all milestones
        _milestones.Check(population: 100_000, balance: 50_000, netPerTick: 100, tick: 500);

        Assert.That(_milestones.Reached, Has.Count.EqualTo(4),
            "Should reach Town, City, Metropolis, and Loopolis");
        Assert.That(_milestones.Reached.Select(r => r.Name),
            Is.EquivalentTo(new[] { "Town", "City", "Metropolis", "Loopolis" }));
    }

    [Test]
    public void MilestoneNotDuplicated_OnSubsequentCalls()
    {
        _milestones.Check(population: 500, balance: 5_000, netPerTick: 10, tick: 10);
        _milestones.Check(population: 600, balance: 5_000, netPerTick: 10, tick: 20);
        _milestones.Check(population: 700, balance: 5_000, netPerTick: 10, tick: 30);

        // Town milestone should only be recorded once
        Assert.That(_milestones.Reached.Count(r => r.Name == "Town"), Is.EqualTo(1));
    }

    [Test]
    public void LoopolisIsOver()
    {
        _milestones.Check(population: 100_000, balance: 50_000, netPerTick: 100, tick: 1000);

        Assert.That(_milestones.CurrentState, Is.EqualTo(GameState.Loopolis));
        Assert.That(_milestones.IsOver, Is.True);
    }

    [Test]
    public void LatestMilestone_ReturnsLastReached()
    {
        _milestones.Check(population: 100_000, balance: 50_000, netPerTick: 100, tick: 500);

        Assert.That(_milestones.LatestMilestone, Is.Not.Null);
        Assert.That(_milestones.LatestMilestone!.Name, Is.EqualTo("Loopolis"));
    }

    [Test]
    public void LatestMilestone_NullWhenNoneReached()
    {
        Assert.That(_milestones.LatestMilestone, Is.Null);
    }
}
