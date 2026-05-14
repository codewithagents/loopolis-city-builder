using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loopolis.Core.Scenarios;

/// <summary>
/// A single entry in the local leaderboard for one scenario.
/// </summary>
public record LeaderboardEntry(
    [property: JsonPropertyName("medal")]      string Medal,
    [property: JsonPropertyName("tick")]       int    Tick,
    [property: JsonPropertyName("population")] int    Population
);

/// <summary>
/// Reads and writes the player's personal-best per scenario.
/// File format: a JSON object keyed by scenario ID.
///
/// "Better" = higher medal rank first; same medal → lower tick wins.
/// Medal rank: Gold (3) > Silver (2) > Bronze (1) > null/none (0).
/// </summary>
public static class LeaderboardSystem
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented          = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Loads the leaderboard from <paramref name="filePath"/>.
    /// Returns an empty dictionary if the file does not exist or cannot be parsed.
    /// </summary>
    public static Dictionary<string, LeaderboardEntry> Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return new Dictionary<string, LeaderboardEntry>();
            var json = File.ReadAllText(filePath);
            var result = JsonSerializer.Deserialize<Dictionary<string, LeaderboardEntry>>(json, JsonOpts);
            return result ?? new Dictionary<string, LeaderboardEntry>();
        }
        catch
        {
            return new Dictionary<string, LeaderboardEntry>();
        }
    }

    /// <summary>
    /// Saves a result for <paramref name="scenarioId"/> to <paramref name="filePath"/>.
    /// Only updates the entry if the new result is strictly better than the existing one.
    /// "Better" = higher medal rank, or same medal with lower tick count.
    /// </summary>
    public static void Save(string scenarioId, string medal, int tick, int population, string filePath)
    {
        var entries = Load(filePath);

        var newEntry = new LeaderboardEntry(medal, tick, population);

        if (entries.TryGetValue(scenarioId, out var existing))
        {
            // Only overwrite when new result is strictly better
            if (!IsBetter(newEntry, existing))
                return;
        }

        entries[scenarioId] = newEntry;

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(entries, JsonOpts);
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // File write failed — silently ignore (leaderboard is non-critical)
        }
    }

    /// <summary>
    /// Returns true when <paramref name="candidate"/> is strictly better than <paramref name="existing"/>.
    /// Medal rank: Gold > Silver > Bronze.
    /// If same medal rank, lower tick count wins.
    /// </summary>
    public static bool IsBetter(LeaderboardEntry candidate, LeaderboardEntry existing)
    {
        var candidateRank = MedalRank(candidate.Medal);
        var existingRank  = MedalRank(existing.Medal);

        if (candidateRank != existingRank)
            return candidateRank > existingRank;

        // Same medal — lower tick is better
        return candidate.Tick < existing.Tick;
    }

    private static int MedalRank(string? medal) => medal switch
    {
        "Gold"   => 3,
        "Silver" => 2,
        "Bronze" => 1,
        _        => 0,
    };
}
