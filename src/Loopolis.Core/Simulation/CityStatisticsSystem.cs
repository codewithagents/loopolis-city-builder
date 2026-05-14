using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// A single per-tick snapshot of the city's key metrics.
/// </summary>
public record CitySnapshot(
    int Tick,
    int Population,
    double Balance,
    float AverageHappiness,
    int PoweredTiles,
    int UnpoweredTiles,
    int EmployedResidents,
    int TotalJobs,
    float AveragePollution
);

/// <summary>
/// Tracks a rolling history of per-tick city snapshots for trend analysis and peak tracking.
///
/// History is capped at MaxHistory (200) snapshots — oldest entries are evicted first.
/// Trend methods compare the most recent 20-tick window against the previous 20-tick window.
/// A difference greater than ±2% is classified as rising (↑) or falling (↓); otherwise stable (→).
///
/// Call Record() once per tick (at the end of SimulationEngine.Tick()) with a freshly-built
/// CitySnapshot. All other methods are read-only.
/// </summary>
public class CityStatisticsSystem
{
    public const int MaxHistory       = 200;
    public const int TrendWindowSize  = 20;   // ticks in each half of the trend comparison
    public const int GrowthWindowSize = 50;   // ticks over which PopulationGrowthRate is computed
    public const double TrendThreshold = 0.02; // 2% change marks ↑/↓

    private readonly Queue<CitySnapshot> _history = new();

    // ── Public read properties ──────────────────────────────────────────────

    public IReadOnlyCollection<CitySnapshot> History => _history;
    public CitySnapshot? Latest => _history.Count > 0 ? _history.Last() : null;

    /// <summary>Highest population ever recorded.</summary>
    public int PeakPopulation { get; private set; }

    /// <summary>Highest balance ever recorded.</summary>
    public double PeakBalance { get; private set; }

    /// <summary>
    /// Population growth rate over the last GrowthWindowSize ticks, expressed as a fraction per tick.
    /// Example: 0.02 means the city gained 2% of its start-of-window population each tick on average.
    /// Zero when history is shorter than two ticks.
    /// </summary>
    public float PopulationGrowthRate { get; private set; }

    // ── Record ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a snapshot to the rolling history, evicting the oldest entry when the buffer is full.
    /// Also updates peak values and PopulationGrowthRate.
    /// </summary>
    public void Record(CitySnapshot snapshot)
    {
        _history.Enqueue(snapshot);
        if (_history.Count > MaxHistory)
            _history.Dequeue();

        // Update peaks
        if (snapshot.Population > PeakPopulation)
            PeakPopulation = snapshot.Population;
        if (snapshot.Balance > PeakBalance)
            PeakBalance = snapshot.Balance;

        // Recompute growth rate
        PopulationGrowthRate = ComputeGrowthRate();
    }

    // ── Trend helpers ───────────────────────────────────────────────────────

    /// <summary>Returns "↑", "↓", or "→" based on population change over the last two 20-tick windows.</summary>
    public string PopulationTrend() => ComputeTrend(s => (double)s.Population);

    /// <summary>Returns "↑", "↓", or "→" based on happiness change over the last two 20-tick windows.</summary>
    public string HappinessTrend() => ComputeTrend(s => (double)s.AverageHappiness);

    /// <summary>Returns "↑", "↓", or "→" based on balance change over the last two 20-tick windows.</summary>
    public string BalanceTrend() => ComputeTrend(s => s.Balance);

    // ── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Generic trend computation.
    /// Requires at least 2 × TrendWindowSize history entries for a meaningful comparison;
    /// returns "→" when there is insufficient history.
    /// </summary>
    private string ComputeTrend(Func<CitySnapshot, double> selector)
    {
        var list = _history.ToArray();
        var needed = TrendWindowSize * 2;
        if (list.Length < needed)
            return "→";

        // Newest TrendWindowSize entries
        var recent   = list[^TrendWindowSize..].Select(selector).Average();
        // Previous TrendWindowSize entries
        var previous = list[^needed..^TrendWindowSize].Select(selector).Average();

        if (previous == 0)
            return recent > 0 ? "↑" : "→";

        var change = (recent - previous) / Math.Abs(previous);

        if (change > TrendThreshold)  return "↑";
        if (change < -TrendThreshold) return "↓";
        return "→";
    }

    /// <summary>
    /// Computes population growth rate over the last GrowthWindowSize ticks.
    /// Returns (popNow - popThen) / (popThen × windowSize) clamped to a reasonable range.
    /// Zero when history is too short or start population was 0.
    /// </summary>
    private float ComputeGrowthRate()
    {
        var list = _history.ToArray();
        if (list.Length < 2)
            return 0f;

        var windowSize = Math.Min(GrowthWindowSize, list.Length);
        var oldest = list[^windowSize];
        var newest = list[^1];

        if (oldest.Population == 0)
            return newest.Population > 0 ? 1f : 0f; // treat as infinite growth, cap to 1.0

        var rate = (float)(newest.Population - oldest.Population)
                   / (oldest.Population * (float)(windowSize - 1));
        return rate;
    }
}
