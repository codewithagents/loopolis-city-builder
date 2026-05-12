using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Computes city-level power supply vs. demand and determines whether a brownout is occurring.
///
/// Supply = sum of MW output for all CoalPlant (500 MW) and NuclearPlant (3,000 MW) tiles on the grid.
///          Legacy PowerPlant tiles count as CoalPlant (500 MW).
///
/// Demand = sum per zoned tile:
///   Residential:                         2 MW/tile
///   Commercial:                          3 MW/tile
///   Industrial:                          5 MW/tile
///   FireStation/PoliceStation/School:   10 MW/tile
///   FireHQ/PoliceHQ/Hospital:           20 MW/tile
///   CoalPlant/NuclearPlant/PowerPlant:   0 (generates, doesn't consume)
///   Road/Avenue/PowerLine/Empty:         0
///
/// capacityRatio = totalSupplyMW / max(totalDemandMW, 1)
/// isBrownout    = capacityRatio &lt; 1.0
///
/// Brownout effects (consumed by HappinessSystem and PopulationSystem):
///   happinessPenalty    = −0.10 × (1.0 − capacityRatio)   (applies to BFS-powered tiles only)
///   growthMultiplier    = clamp(capacityRatio, 0.3, 1.0)   (throttles growth rate for all zones)
///
/// Call Propagate() each tick after PowerNetwork.Propagate().
/// </summary>
public class PowerCapacitySystem
{
    // MW output per plant tile
    public const int CoalPlantOutputMW    =   500;
    public const int NuclearPlantOutputMW = 3_000;

    // MW demand per tile type
    private static readonly IReadOnlyDictionary<ZoneType, int> DemandMW =
        new Dictionary<ZoneType, int>
        {
            { ZoneType.Residential,   2 },
            { ZoneType.Commercial,    3 },
            { ZoneType.Industrial,    5 },
            { ZoneType.FireStation,  10 },
            { ZoneType.PoliceStation,10 },
            { ZoneType.School,       10 },
            { ZoneType.FireHQ,       20 },
            { ZoneType.PoliceHQ,     20 },
            { ZoneType.Hospital,     20 },
        };

    private const double BrownoutHappinessFactor = 0.10; // coefficient for -0.10*(1-ratio)
    private const double MinGrowthMultiplier     = 0.30;

    public int    TotalSupplyMW    { get; private set; }
    public int    TotalDemandMW    { get; private set; }
    public double CapacityRatio    { get; private set; } = 1.0;
    public bool   IsBrownout       { get; private set; }

    /// <summary>
    /// True only when there IS at least one power plant (supply > 0) but demand exceeds supply.
    /// False when there is no power plant at all (supply == 0), which is the normal early-game
    /// state and should NOT be treated as an actionable brownout.
    /// Use this in Tier-1 skip-pause checks instead of IsBrownout.
    /// </summary>
    public bool IsActiveBrownout => TotalSupplyMW > 0 && IsBrownout;

    /// <summary>
    /// Happiness penalty per powered tile during a brownout: −0.10 × (1.0 − capacityRatio).
    /// Zero when not in brownout.
    /// </summary>
    public double BrownoutHappinessPenalty { get; private set; }

    /// <summary>
    /// Growth rate multiplier applied to all zone growth during a brownout.
    /// Clamped to [0.30, 1.0]. Is 1.0 when not in brownout.
    /// </summary>
    public double GrowthMultiplier { get; private set; } = 1.0;

    public void Propagate(CityGrid grid)
    {
        var supply = 0;
        var demand = 0;

        foreach (var tile in grid.AllTiles())
        {
            // Supply from power plants
            if (tile.Zone == ZoneType.CoalPlant || tile.Zone == ZoneType.PowerPlant)
                supply += CoalPlantOutputMW;
            else if (tile.Zone == ZoneType.NuclearPlant)
                supply += NuclearPlantOutputMW;

            // Demand from consuming tiles
            if (DemandMW.TryGetValue(tile.Zone, out var tileDemand))
                demand += tileDemand;
        }

        TotalSupplyMW = supply;
        TotalDemandMW = demand;

        var ratio = supply / (double)Math.Max(demand, 1);
        CapacityRatio = ratio;
        IsBrownout    = ratio < 1.0;

        BrownoutHappinessPenalty = IsBrownout
            ? -BrownoutHappinessFactor * (1.0 - ratio)
            : 0.0;

        GrowthMultiplier = IsBrownout
            ? Math.Max(MinGrowthMultiplier, ratio)
            : 1.0;
    }
}
