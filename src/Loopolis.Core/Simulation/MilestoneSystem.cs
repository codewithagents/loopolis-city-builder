namespace Loopolis.Core.Simulation;

public enum GameState { Active, Town, City, Metropolis, Loopolis, Bankrupt, Abandoned }

public record MilestoneReached(string Name, string Emoji, int PopulationRequired, int ReachedAtTick);

/// <summary>
/// Tracks game progression milestones and detects bankruptcy.
///
/// Milestones:
///   500     → Town       🥉
///   5,000   → City       🥈
///   25,000  → Metropolis 🥇
///   100,000 → Loopolis   🏆 (win condition)
///
/// Bankruptcy: negative balance AND zero population (unrecoverable)
///
/// Call Check() each tick after Budget and Population have been updated.
/// </summary>
public class MilestoneSystem
{
    private static readonly (int Population, string Name, string Emoji, GameState State)[] Milestones =
    {
        (500,     "Town",       "🥉", GameState.Town),
        (5_000,   "City",       "🥈", GameState.City),
        (25_000,  "Metropolis", "🥇", GameState.Metropolis),
        (100_000, "Loopolis",   "🏆", GameState.Loopolis),
    };

    public GameState CurrentState { get; private set; } = GameState.Active;
    public List<MilestoneReached> Reached { get; } = new();
    public MilestoneReached? LatestMilestone => Reached.Count > 0 ? Reached[^1] : null;

    public void Check(int population, double balance, double netPerTick, int tick)
    {
        // Bankruptcy: unrecoverable — either no population with negative balance,
        // or deep debt spiral past -$10,000 (inhabited cities can also go bankrupt)
        if ((balance < 0 && population == 0) || balance < -10_000)
        {
            CurrentState = GameState.Bankrupt;
            return;
        }

        // Milestone progression (only advances, never goes back)
        foreach (var (req, name, emoji, state) in Milestones)
        {
            if (population >= req && !Reached.Any(r => r.Name == name))
            {
                Reached.Add(new MilestoneReached(name, emoji, req, tick));
                CurrentState = state;
            }
        }
    }

    /// <summary>Forces the city into the Abandoned state (called by SimulationEngine when happiness is persistently low).</summary>
    public void Abandon() => CurrentState = GameState.Abandoned;

    /// <summary>
    /// Recovers the city from the Abandoned state back to Active (or the highest milestone reached).
    /// Called by SimulationEngine when happiness recovers sufficiently after an abandonment.
    /// </summary>
    public void RecoverFromAbandonment()
    {
        if (CurrentState != GameState.Abandoned) return;

        // Restore to the highest milestone reached, or Active if none
        CurrentState = Reached.Count > 0
            ? Milestones.Last(m => Reached.Any(r => r.Name == m.Name)).State
            : GameState.Active;
    }

    public bool IsOver => CurrentState == GameState.Bankrupt || CurrentState == GameState.Abandoned || CurrentState == GameState.Loopolis;
}
