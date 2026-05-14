namespace Loopolis.Core.Policies;

/// <summary>
/// All available city policy types.
///
///   GreenCity       — reduces pollution, improves happiness. Cost: $80/tick.
///   IndustrialHub   — boosts industrial growth and jobs. Cost: $50/tick.
///   CommercialBoost — accelerates commercial activity growth. Cost: $60/tick.
///   OpenCity        — increases immigration rate, reduces effective tax rate. Cost: $30/tick.
/// </summary>
public enum PolicyType
{
    GreenCity,
    IndustrialHub,
    CommercialBoost,
    OpenCity,
}
