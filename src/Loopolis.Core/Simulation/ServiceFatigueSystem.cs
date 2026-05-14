using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Post-City service fatigue: service buildings slowly degrade after the City milestone.
/// Each service tile has an EffectiveCapacityMultiplier (1.0 → 0.40 min, decays 0.002/tick).
///
/// Propagate() is called each tick when GameState is City or higher.
/// Renovation: calling Renovate(x, y) resets that tile to 1.0 and costs $500.
/// </summary>
public class ServiceFatigueSystem
{
    private const double DecayPerTick   = 0.002; // 0.2% per tick
    private const double MinCapacity    = 0.40;  // floor: 40%
    private const double FatigueWarning = 0.60;  // below this → flag as needing repair
    public  const int    RenovationCost = 500;

    // tile key (x, y) → effective capacity multiplier
    private readonly Dictionary<(int x, int y), double> _capacities = new();

    /// <summary>Effective capacity multiplier for a service tile. Returns 1.0 for new/unknown tiles.</summary>
    public double GetCapacity(int x, int y) =>
        _capacities.TryGetValue((x, y), out var v) ? v : 1.0;

    /// <summary>True when a tile is at &lt; FatigueWarning capacity and needs renovation.</summary>
    public bool NeedsRenovation(int x, int y) =>
        GetCapacity(x, y) < FatigueWarning;

    /// <summary>All tiles currently flagged for renovation (capacity &lt; 60%).</summary>
    public IEnumerable<(int x, int y)> DeprecatedTiles =>
        _capacities.Where(kvp => kvp.Value < FatigueWarning).Select(kvp => kvp.Key);

    /// <summary>True when any service tile is being tracked (City milestone reached at least once).</summary>
    public bool IsActive => _capacities.Count > 0 || _fatigueEverActive;
    private bool _fatigueEverActive;

    /// <summary>
    /// Decay all known service tiles by DecayPerTick.
    /// Add any NEW service tiles not yet tracked at full capacity.
    /// Only runs when gameState is City or higher.
    /// </summary>
    public void Propagate(CityGrid grid, GameState gameState)
    {
        if (gameState is not (GameState.City or GameState.Metropolis or GameState.Loopolis))
            return;

        _fatigueEverActive = true;

        var serviceTiles = grid.AllTiles()
            .Where(t => t.Zone is ZoneType.FireStation or ZoneType.PoliceStation
                                 or ZoneType.School or ZoneType.Hospital
                                 or ZoneType.FireHQ or ZoneType.PoliceHQ)
            .ToList();

        // Add new service tiles at full capacity
        foreach (var tile in serviceTiles)
        {
            var key = (tile.X, tile.Y);
            if (!_capacities.ContainsKey(key))
                _capacities[key] = 1.0;
        }

        // Decay all tracked service tiles
        foreach (var key in _capacities.Keys.ToList())
            _capacities[key] = Math.Max(MinCapacity, _capacities[key] - DecayPerTick);

        // Remove tiles that are no longer service tiles (erased/changed)
        var serviceKeys = serviceTiles.Select(t => (t.X, t.Y)).ToHashSet();
        foreach (var key in _capacities.Keys.ToList())
            if (!serviceKeys.Contains(key))
                _capacities.Remove(key);
    }

    /// <summary>
    /// Renovate a service tile: reset its capacity to 1.0 and deduct $500 from budget.
    /// Returns false if the tile is not a tracked service tile or insufficient funds.
    /// </summary>
    public bool Renovate(int x, int y, BudgetSystem budget)
    {
        var key = (x, y);
        if (!_capacities.ContainsKey(key)) return false;
        if (budget.Balance < RenovationCost) return false;
        _capacities[key] = 1.0;
        budget.Charge(RenovationCost);
        return true;
    }

    // ── Save / restore ────────────────────────────────────────────────────────

    /// <summary>Returns a snapshot for serialization.</summary>
    public IReadOnlyDictionary<(int x, int y), double> GetSnapshot() => _capacities;

    /// <summary>Restores state from a snapshot (e.g. after save/load).</summary>
    public void RestoreSnapshot(IReadOnlyDictionary<(int x, int y), double> snapshot)
    {
        _capacities.Clear();
        foreach (var (k, v) in snapshot)
            _capacities[k] = v;
        if (_capacities.Count > 0)
            _fatigueEverActive = true;
    }
}
