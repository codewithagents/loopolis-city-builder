using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class CityStatisticsTests
{
    private CityStatisticsSystem _stats = null!;

    [SetUp]
    public void SetUp() => _stats = new CityStatisticsSystem();

    // ── Snapshot helpers ────────────────────────────────────────────────────

    private static CitySnapshot Snap(int tick, int pop, double balance = 1000,
        float happiness = 0.6f, int powered = 10, int unpowered = 0,
        int employed = 0, int jobs = 0, float pollution = 0f)
        => new(tick, pop, balance, happiness, powered, unpowered, employed, jobs, pollution);

    // ── History rolling window ──────────────────────────────────────────────

    [Test]
    public void Record_SingleSnapshot_HistoryHasOneEntry()
    {
        _stats.Record(Snap(1, 100));

        Assert.That(_stats.History.Count, Is.EqualTo(1));
    }

    [Test]
    public void Record_MaxHistoryPlusOne_OldestIsEvicted()
    {
        for (var i = 1; i <= CityStatisticsSystem.MaxHistory + 1; i++)
            _stats.Record(Snap(i, i * 10));

        Assert.That(_stats.History.Count, Is.EqualTo(CityStatisticsSystem.MaxHistory));
    }

    [Test]
    public void Record_MaxHistoryExactly_NoEviction()
    {
        for (var i = 1; i <= CityStatisticsSystem.MaxHistory; i++)
            _stats.Record(Snap(i, i));

        Assert.That(_stats.History.Count, Is.EqualTo(CityStatisticsSystem.MaxHistory));
    }

    [Test]
    public void Record_MaxHistoryPlusOne_OldestTickIsGone()
    {
        for (var i = 1; i <= CityStatisticsSystem.MaxHistory + 1; i++)
            _stats.Record(Snap(i, i));

        // Oldest tick (1) should have been evicted; first remaining entry is tick 2
        Assert.That(_stats.History.First().Tick, Is.EqualTo(2));
    }

    [Test]
    public void Latest_EmptyHistory_ReturnsNull()
    {
        Assert.That(_stats.Latest, Is.Null);
    }

    [Test]
    public void Latest_AfterRecords_ReturnsNewestSnapshot()
    {
        _stats.Record(Snap(1, 100));
        _stats.Record(Snap(2, 200));
        _stats.Record(Snap(3, 300));

        Assert.That(_stats.Latest!.Tick, Is.EqualTo(3));
        Assert.That(_stats.Latest.Population, Is.EqualTo(300));
    }

    // ── Peak tracking ───────────────────────────────────────────────────────

    [Test]
    public void PeakPopulation_TracksHighest()
    {
        _stats.Record(Snap(1, 100));
        _stats.Record(Snap(2, 500));
        _stats.Record(Snap(3, 200)); // decline — peak must NOT decrease

        Assert.That(_stats.PeakPopulation, Is.EqualTo(500));
    }

    [Test]
    public void PeakBalance_TracksHighest()
    {
        _stats.Record(Snap(1, 100, balance: 5000));
        _stats.Record(Snap(2, 100, balance: 12000));
        _stats.Record(Snap(3, 100, balance: 8000)); // balance fell — peak must not decrease

        Assert.That(_stats.PeakBalance, Is.EqualTo(12000));
    }

    [Test]
    public void PeakPopulation_StartsAtZero()
    {
        Assert.That(_stats.PeakPopulation, Is.EqualTo(0));
    }

    // ── Trend: population ───────────────────────────────────────────────────

    [Test]
    public void PopulationTrend_FewerThan40Entries_ReturnsStable()
    {
        // Need 2 × TrendWindowSize = 40 entries for a real comparison
        for (var i = 0; i < 39; i++)
            _stats.Record(Snap(i, 1000));

        Assert.That(_stats.PopulationTrend(), Is.EqualTo("→"));
    }

    [Test]
    public void PopulationTrend_Rising_ReturnsUp()
    {
        // Fill first 20-tick window with pop=100, then 20-tick window with pop=200 (+100% > 2%)
        for (var i = 0; i < 20; i++)
            _stats.Record(Snap(i, 100));
        for (var i = 20; i < 40; i++)
            _stats.Record(Snap(i, 200));

        Assert.That(_stats.PopulationTrend(), Is.EqualTo("↑"));
    }

    [Test]
    public void PopulationTrend_Declining_ReturnsDown()
    {
        // First 20 ticks at 1000, next 20 ticks at 500 (−50%)
        for (var i = 0; i < 20; i++)
            _stats.Record(Snap(i, 1000));
        for (var i = 20; i < 40; i++)
            _stats.Record(Snap(i, 500));

        Assert.That(_stats.PopulationTrend(), Is.EqualTo("↓"));
    }

    [Test]
    public void PopulationTrend_Stable_ReturnsFlat()
    {
        // Constant population — change = 0, within ±2% threshold
        for (var i = 0; i < 40; i++)
            _stats.Record(Snap(i, 1000));

        Assert.That(_stats.PopulationTrend(), Is.EqualTo("→"));
    }

    [Test]
    public void PopulationTrend_SmallChange_WithinThreshold_ReturnsFlat()
    {
        // 1% change — below the 2% threshold, should still be "→"
        for (var i = 0; i < 20; i++)
            _stats.Record(Snap(i, 1000));
        for (var i = 20; i < 40; i++)
            _stats.Record(Snap(i, 1010)); // +1% — under threshold

        Assert.That(_stats.PopulationTrend(), Is.EqualTo("→"));
    }

    // ── Trend: happiness ────────────────────────────────────────────────────

    [Test]
    public void HappinessTrend_Rising_ReturnsUp()
    {
        for (var i = 0; i < 20; i++)
            _stats.Record(Snap(i, 100, happiness: 0.5f));
        for (var i = 20; i < 40; i++)
            _stats.Record(Snap(i, 100, happiness: 0.9f)); // +80%

        Assert.That(_stats.HappinessTrend(), Is.EqualTo("↑"));
    }

    [Test]
    public void HappinessTrend_Stable_ReturnsFlat()
    {
        for (var i = 0; i < 40; i++)
            _stats.Record(Snap(i, 100, happiness: 0.7f));

        Assert.That(_stats.HappinessTrend(), Is.EqualTo("→"));
    }

    // ── Trend: balance ──────────────────────────────────────────────────────

    [Test]
    public void BalanceTrend_Rising_ReturnsUp()
    {
        for (var i = 0; i < 20; i++)
            _stats.Record(Snap(i, 100, balance: 5000));
        for (var i = 20; i < 40; i++)
            _stats.Record(Snap(i, 100, balance: 10000)); // +100%

        Assert.That(_stats.BalanceTrend(), Is.EqualTo("↑"));
    }

    [Test]
    public void BalanceTrend_Declining_ReturnsDown()
    {
        for (var i = 0; i < 20; i++)
            _stats.Record(Snap(i, 100, balance: 10000));
        for (var i = 20; i < 40; i++)
            _stats.Record(Snap(i, 100, balance: 4000)); // −60%

        Assert.That(_stats.BalanceTrend(), Is.EqualTo("↓"));
    }

    // ── PopulationGrowthRate ────────────────────────────────────────────────

    [Test]
    public void PopulationGrowthRate_EmptyHistory_IsZero()
    {
        Assert.That(_stats.PopulationGrowthRate, Is.EqualTo(0f));
    }

    [Test]
    public void PopulationGrowthRate_SingleEntry_IsZero()
    {
        _stats.Record(Snap(1, 100));

        Assert.That(_stats.PopulationGrowthRate, Is.EqualTo(0f));
    }

    [Test]
    public void PopulationGrowthRate_50TicksConstant_IsZero()
    {
        for (var i = 0; i < 50; i++)
            _stats.Record(Snap(i, 1000));

        Assert.That(_stats.PopulationGrowthRate, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void PopulationGrowthRate_GrowingPopulation_IsPositive()
    {
        // Start at 1000, grow linearly to 2000 over 50 ticks (exactly 1%/tick increase per tick)
        for (var i = 0; i < 50; i++)
            _stats.Record(Snap(i, 1000 + i * 20)); // 1000 → 1980

        Assert.That(_stats.PopulationGrowthRate, Is.GreaterThan(0f));
    }

    [Test]
    public void PopulationGrowthRate_DecliningPopulation_IsNegative()
    {
        for (var i = 0; i < 50; i++)
            _stats.Record(Snap(i, 2000 - i * 30)); // 2000 → 620

        Assert.That(_stats.PopulationGrowthRate, Is.LessThan(0f));
    }

    [Test]
    public void PopulationGrowthRate_FewerThan50Ticks_UsesAvailableWindow()
    {
        // Only 10 entries — should still compute without throwing
        _stats.Record(Snap(0, 100));
        _stats.Record(Snap(1, 110));
        _stats.Record(Snap(2, 120));

        Assert.That(() => _stats.PopulationGrowthRate, Throws.Nothing);
        Assert.That(_stats.PopulationGrowthRate, Is.GreaterThan(0f));
    }
}
