namespace Loopolis.Core.Simulation;

public enum GameState { Active, Town, City, Metropolis, Loopolis, Bankrupt }

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
        // Bankruptcy: negative balance AND no population (no recovery path)
        if (balance < 0 && population == 0)
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

    public bool IsOver => CurrentState == GameState.Bankrupt || CurrentState == GameState.Loopolis;
}
