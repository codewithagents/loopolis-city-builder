using Loopolis.Core.Simulation;

namespace Loopolis.Core.Policies;

/// <summary>
/// Manages active city policies and exposes modifier accessors queried by other systems.
///
/// Active policies:
///   GreenCity       — PollutionMultiplier=0.65, HappinessBonus=+0.10, Cost=$40/tick
///   IndustrialHub   — IndustrialGrowthMultiplier=1.25, JobsPerIndustrialTileBonus=+8, Cost=$30/tick
///   CommercialBoost — CommercialGrowthMultiplier=1.25, Cost=$30/tick
///   OpenCity        — ResidentialCapacityBonus=+12%, TaxRateModifier=0.88, Cost=$15/tick
///
/// Call Tick(budget) each tick (after BudgetSystem has charged maintenance) to deduct policy costs.
/// Systems query the modifier accessors instead of checking ActivePolicies directly.
/// </summary>
public class PolicySystem
{
    // ── Cost constants ──────────────────────────────────────────────────────────
    private const int GreenCityCost       = 40;
    private const int IndustrialHubCost   = 30;
    private const int CommercialBoostCost = 30;
    private const int OpenCityCost        = 15;

    // ── Active policies ─────────────────────────────────────────────────────────
    /// <summary>Set of currently active policies. Read-only from outside.</summary>
    public IReadOnlySet<PolicyType> ActivePolicies => _activePolicies;
    private readonly HashSet<PolicyType> _activePolicies = new();

    // ── Modifier accessors ──────────────────────────────────────────────────────

    /// <summary>
    /// Multiplier applied to each industrial tile's pollution emission.
    /// 1.0 normally; 0.65 when GreenCity is active (35% reduction).
    /// </summary>
    public double PollutionMultiplier =>
        _activePolicies.Contains(PolicyType.GreenCity) ? 0.65 : 1.0;

    /// <summary>
    /// Flat happiness bonus added to every ready residential tile.
    /// 0.0 normally; +0.10 when GreenCity is active.
    /// </summary>
    public double HappinessBonusFromPolicy =>
        _activePolicies.Contains(PolicyType.GreenCity) ? 0.10 : 0.0;

    /// <summary>
    /// Multiplier applied to industrial rawGrowth in PopulationSystem.
    /// 1.0 normally; 1.25 when IndustrialHub is active.
    /// </summary>
    public double IndustrialGrowthMultiplier =>
        _activePolicies.Contains(PolicyType.IndustrialHub) ? 1.25 : 1.0;

    /// <summary>
    /// Bonus jobs added per industrial tile when computing available jobs.
    /// 0 normally; +8 when IndustrialHub is active.
    /// </summary>
    public int JobsPerIndustrialTileBonus =>
        _activePolicies.Contains(PolicyType.IndustrialHub) ? 8 : 0;

    /// <summary>
    /// Multiplier applied to commercial rawGrowth in PopulationSystem.
    /// 1.0 normally; 1.25 when CommercialBoost is active.
    /// </summary>
    public double CommercialGrowthMultiplier =>
        _activePolicies.Contains(PolicyType.CommercialBoost) ? 1.25 : 1.0;

    /// <summary>
    /// Fractional bonus added to the effective capacity of residential tiles.
    /// 0.0 normally; +0.12 when OpenCity is active (+12% residential capacity = denser housing).
    /// This replaces the old ImmigrationMultiplier approach which was inert at capacity.
    /// </summary>
    public double ResidentialCapacityBonus =>
        _activePolicies.Contains(PolicyType.OpenCity) ? 0.12 : 0.0;

    /// <summary>
    /// Deprecated: kept for backward compatibility. Use ResidentialCapacityBonus instead.
    /// ImmigrationMultiplier was inert once tiles reached capacity; ResidentialCapacityBonus
    /// increases the cap itself so growth can continue to a higher ceiling.
    /// Returns 1.0 always (OpenCity no longer uses a growth-rate multiplier).
    /// </summary>
    [Obsolete("Use ResidentialCapacityBonus instead — ImmigrationMultiplier is inert at capacity.")]
    public double ImmigrationMultiplier => 1.0;

    /// <summary>
    /// Multiplier applied to the effective tax rate in BudgetSystem.
    /// 1.0 normally; 0.88 when OpenCity is active (12% lower tax revenue).
    /// </summary>
    public double TaxRateModifier =>
        _activePolicies.Contains(PolicyType.OpenCity) ? 0.88 : 1.0;

    // ── Policy management ───────────────────────────────────────────────────────

    /// <summary>Activate a policy. No-op if already active.</summary>
    public void ActivatePolicy(PolicyType type) =>
        _activePolicies.Add(type);

    /// <summary>Deactivate a policy. No-op if not active.</summary>
    public void DeactivatePolicy(PolicyType type) =>
        _activePolicies.Remove(type);

    /// <summary>Returns true if the given policy is currently active.</summary>
    public bool IsActive(PolicyType type) =>
        _activePolicies.Contains(type);

    /// <summary>Returns the total budget cost per tick for all active policies.</summary>
    public int GetCostPerTick()
    {
        var total = 0;
        foreach (var policy in _activePolicies)
        {
            total += policy switch
            {
                PolicyType.GreenCity       => GreenCityCost,
                PolicyType.IndustrialHub   => IndustrialHubCost,
                PolicyType.CommercialBoost => CommercialBoostCost,
                PolicyType.OpenCity        => OpenCityCost,
                _                          => 0,
            };
        }
        return total;
    }

    // ── Tick ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deducts active policy costs from the budget.
    /// Call once per tick after BudgetSystem.DeductMaintenance().
    /// </summary>
    public void Tick(BudgetSystem budget)
    {
        var cost = GetCostPerTick();
        if (cost > 0)
            budget.ApplyPolicyCost(cost);
    }
}
