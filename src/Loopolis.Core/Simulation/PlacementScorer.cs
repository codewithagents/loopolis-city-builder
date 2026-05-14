using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Snapshot of a placement's predicted value. Shown to the player as a floating label
/// immediately after they zone a tile, Islanders-style.
/// </summary>
public record PlacementScore(
    ZoneType Zone,
    int X,
    int Y,
    int   EstimatedPotentialResidents,  // R zones
    int   EstimatedPotentialJobs,       // I zones
    double EstimatedIncomePerTick,      // C zones
    float PollutionExposure,            // 0–1, from nearby industrial
    string PrimaryLabel,                // main line, e.g. "~42 potential residents"
    string SecondaryLabel               // sub-line, e.g. "+$18/tick est." or "high pollution"
);

/// <summary>
/// Scores a proposed zone placement for player feedback. Pure scan of grid state —
/// no road-graph required, fast enough to run on every tile hover or on placement commit.
///
/// Called from the Godot layer (WorldInput.HandlePlaceTile) after a successful SetZone.
/// Returns null for tiles that cannot or should not show feedback (water, road, occupied).
/// </summary>
public static class PlacementScorer
{
    private const int   BaseResCapacity         = 50;
    private const double BaseCommercialIncome   = 8.0;
    private const int   BaseIndustrialJobs      = 20;
    private const int   NeighborRadius          = 3;   // Chebyshev distance for demand/pollution scans
    private const float PollutionWarningThreshold = 0.3f;

    /// <summary>
    /// Scores the placement of <paramref name="zone"/> at (<paramref name="x"/>,<paramref name="y"/>)
    /// on <paramref name="grid"/>. Returns null when no meaningful score can be produced:
    ///   • Water tile (unbuildable)
    ///   • Tile already occupied (non-Empty zone)
    ///   • Road / Avenue / infrastructure zone (no meaningful resident/jobs/income label)
    /// </summary>
    public static PlacementScore? Score(CityGrid grid, int x, int y, ZoneType zone)
    {
        if (!grid.IsInBounds(x, y)) return null;

        var tile = grid.GetTile(x, y);

        // Water is unbuildable
        if (tile.HeightLevel <= 0) return null;

        // Occupied tile — nothing to score
        if (tile.Zone != ZoneType.Empty) return null;

        // Infrastructure zones: no meaningful score label
        if (zone is ZoneType.Road or ZoneType.Avenue
                 or ZoneType.PowerLine or ZoneType.PowerPlant or ZoneType.CoalPlant or ZoneType.NuclearPlant
                 or ZoneType.FireStation or ZoneType.PoliceStation or ZoneType.School
                 or ZoneType.FireHQ or ZoneType.PoliceHQ or ZoneType.Hospital
                 or ZoneType.Empty)
            return null;

        return zone switch
        {
            ZoneType.Residential => ScoreResidential(grid, x, y),
            ZoneType.Commercial  => ScoreCommercial(grid, x, y),
            ZoneType.Industrial  => ScoreIndustrial(grid, x, y),
            ZoneType.Park        => ScorePark(grid, x, y),
            _                    => null,
        };
    }

    // ── Residential ─────────────────────────────────────────────────────────────

    private static PlacementScore ScoreResidential(CityGrid grid, int x, int y)
    {
        // Road adjacency factor: road-adjacent tiles have full growth potential
        var roadAdjacent = IsRoadAdjacent(grid, x, y);
        var roadFactor = roadAdjacent ? 1.0 : 0.5;

        // Demand bonus: each nearby ready commercial tile within Chebyshev-3 adds +20% (cap 2.0×)
        var nearbyCommercial = CountChebyshevNeighbors(grid, x, y, ZoneType.Commercial, NeighborRadius);
        var demandMultiplier = Math.Min(2.0, 1.0 + nearbyCommercial * 0.2);

        var estimatedResidents = (int)Math.Round(BaseResCapacity * roadFactor * demandMultiplier);

        // Pollution: count industrial tiles within Chebyshev-3
        var nearbyIndustrial = CountChebyshevNeighbors(grid, x, y, ZoneType.Industrial, NeighborRadius);
        var pollutionExposure = Math.Clamp(nearbyIndustrial / 5.0f, 0f, 1f);

        // Estimated income: rough tax approximation (population × 12% base tax)
        var estimatedIncome = estimatedResidents * 0.12;

        var primary = $"~{estimatedResidents} potential residents";

        string secondary;
        if (pollutionExposure > PollutionWarningThreshold)
            secondary = "high pollution exposure";
        else if (!roadAdjacent)
            secondary = "needs road for growth";
        else
            secondary = $"+${estimatedIncome:F0}/tick est.";

        return new PlacementScore(
            Zone: ZoneType.Residential,
            X: x, Y: y,
            EstimatedPotentialResidents: estimatedResidents,
            EstimatedPotentialJobs: 0,
            EstimatedIncomePerTick: estimatedIncome,
            PollutionExposure: pollutionExposure,
            PrimaryLabel: primary,
            SecondaryLabel: secondary);
    }

    // ── Commercial ──────────────────────────────────────────────────────────────

    private static PlacementScore ScoreCommercial(CityGrid grid, int x, int y)
    {
        // Nearby residential adds customer demand: +20% per tile, cap 2.0×
        var nearbyResidential = CountChebyshevNeighbors(grid, x, y, ZoneType.Residential, NeighborRadius);
        var demandMultiplier = Math.Min(2.0, 1.0 + nearbyResidential * 0.2);
        var estimatedIncome = BaseCommercialIncome * demandMultiplier;

        var primary = $"+${estimatedIncome:F0}/tick est.";
        var secondary = nearbyResidential > 0
            ? $"Near {nearbyResidential} residential tile{(nearbyResidential == 1 ? "" : "s")}"
            : "Low demand area";

        return new PlacementScore(
            Zone: ZoneType.Commercial,
            X: x, Y: y,
            EstimatedPotentialResidents: 0,
            EstimatedPotentialJobs: 0,
            EstimatedIncomePerTick: estimatedIncome,
            PollutionExposure: 0f,
            PrimaryLabel: primary,
            SecondaryLabel: secondary);
    }

    // ── Industrial ──────────────────────────────────────────────────────────────

    private static PlacementScore ScoreIndustrial(CityGrid grid, int x, int y)
    {
        var tile = grid.GetTile(x, y);
        var isForest   = tile.HasForest && tile.HeightLevel >= 1;
        var isElevated = tile.HeightLevel >= 2;

        var primary  = $"+{BaseIndustrialJobs} potential jobs";
        var secondary = isForest   ? "Timber Mill potential"
                      : isElevated ? "Quarry potential"
                      : "";

        return new PlacementScore(
            Zone: ZoneType.Industrial,
            X: x, Y: y,
            EstimatedPotentialResidents: 0,
            EstimatedPotentialJobs: BaseIndustrialJobs,
            EstimatedIncomePerTick: 0.0,
            PollutionExposure: 0f,
            PrimaryLabel: primary,
            SecondaryLabel: secondary);
    }

    // ── Park ────────────────────────────────────────────────────────────────────

    private static PlacementScore ScorePark(CityGrid grid, int x, int y)
    {
        var nearbyResidential = CountChebyshevNeighbors(grid, x, y, ZoneType.Residential, NeighborRadius);

        var primary   = nearbyResidential > 0
            ? $"+happiness for {nearbyResidential} tile{(nearbyResidential == 1 ? "" : "s")}"
            : "+happiness (no nearby residents yet)";
        var secondary = "$3/tick maintenance";

        return new PlacementScore(
            Zone: ZoneType.Park,
            X: x, Y: y,
            EstimatedPotentialResidents: 0,
            EstimatedPotentialJobs: 0,
            EstimatedIncomePerTick: 0.0,
            PollutionExposure: 0f,
            PrimaryLabel: primary,
            SecondaryLabel: secondary);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if any orthogonal or diagonal neighbour at distance 1
    /// is a Road or Avenue tile.
    /// </summary>
    private static bool IsRoadAdjacent(CityGrid grid, int x, int y)
    {
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            var nx = x + dx; var ny = y + dy;
            if (!grid.IsInBounds(nx, ny)) continue;
            var z = grid.GetTile(nx, ny).Zone;
            if (z is ZoneType.Road or ZoneType.Avenue) return true;
        }
        return false;
    }

    /// <summary>
    /// Counts tiles of <paramref name="targetZone"/> within Chebyshev distance
    /// <paramref name="radius"/> of (x, y), excluding the origin tile itself.
    /// </summary>
    private static int CountChebyshevNeighbors(CityGrid grid, int x, int y, ZoneType targetZone, int radius)
    {
        var count = 0;
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            var nx = x + dx; var ny = y + dy;
            if (!grid.IsInBounds(nx, ny)) continue;
            if (grid.GetTile(nx, ny).Zone == targetZone) count++;
        }
        return count;
    }
}
