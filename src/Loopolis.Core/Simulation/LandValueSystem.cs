using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Computes a land value score (0.0–1.0) for every non-water tile each tick.
///
/// Factors and bonuses:
///   +0.35  if tile is a Plateau (elevated + flat neighbours)
///   +0.20  if tile HeightLevel ≥ 2 but not a plateau (generic elevated bonus)
///   +0.15  if any Water tile is within Chebyshev distance 3
///   +0.08  per Forest tile within Chebyshev distance 2, capped at +0.20
///   +0.10  if tile.PollutionLevel &lt; 0.1
///   +0.10  if tile.Happiness &gt; 0.7
///   +0.05  if tile.HasPower
///
/// Water tiles (HeightLevel ≤ 0) always get LandValue = 0.
/// Final value is clamped to [0.0, 1.0].
///
/// NOTE: HillBonus kept as an alias for ElevatedBonus for backward compatibility with tests.
/// </summary>
public class LandValueSystem
{
    // Legacy constant — points to ElevatedBonus for backward compat
    [Obsolete("Use PlateauBonus or ElevatedBonus. HillBonus is kept only for backward compatibility.")]
    public const double HillBonus          = ElevatedBonus;

    public const double PlateauBonus       = 0.35;
    public const double ElevatedBonus      = 0.20;
    public const double WaterAdjacentBonus = 0.15;
    public const double ForestBonusPerTile = 0.08;
    public const double ForestBonusMax     = 0.20;
    public const double LowPollutionBonus  = 0.10;
    public const double HighHappinessBonus = 0.10;
    public const double PowerBonus         = 0.05;

    public void Propagate(CityGrid grid)
    {
        grid.ClearLandValue();

        for (var x = 0; x < grid.Width; x++)
        for (var y = 0; y < grid.Height; y++)
        {
            // Water tiles have no land value
            if (grid.GetHeightLevel(x, y) <= 0)
                continue;

            var tile = grid.GetTile(x, y);
            var value = 0.0;

            // Plateau / elevated bonus
            if (grid.IsPlateau(x, y))
                value += PlateauBonus;
            else if (grid.GetHeightLevel(x, y) >= 2)
                value += ElevatedBonus;

            // Water-adjacent bonus — any Water tile within Chebyshev distance 3
            if (HasWaterWithin(grid, x, y, 3))
                value += WaterAdjacentBonus;

            // Forest bonus — count Forest tiles within Chebyshev distance 2, cap at +0.20
            var forestCount = CountForestWithin(grid, x, y, 2);
            value += Math.Min(forestCount * ForestBonusPerTile, ForestBonusMax);

            // Low pollution bonus
            if (tile.PollutionLevel < 0.1)
                value += LowPollutionBonus;

            // High happiness bonus
            if (tile.Happiness > 0.7)
                value += HighHappinessBonus;

            // Power bonus
            if (tile.HasPower)
                value += PowerBonus;

            grid.SetLandValue(x, y, Math.Clamp(value, 0.0, 1.0));
        }
    }

    /// <summary>
    /// Returns the average LandValue across all non-water, non-empty tiles.
    /// Returns 0.0 if no such tiles exist.
    /// </summary>
    public double AverageLandValue(CityGrid grid)
    {
        var tiles = grid.AllTiles()
            .Where(t => grid.GetHeightLevel(t.X, t.Y) > 0 && t.Zone != ZoneType.Empty)
            .ToList();
        if (tiles.Count == 0) return 0.0;
        return tiles.Average(t => t.LandValue);
    }

    /// <summary>
    /// Returns the maximum LandValue across all non-water tiles.
    /// Returns 0.0 if no such tiles exist.
    /// </summary>
    public double MaxLandValue(CityGrid grid)
    {
        var tiles = grid.AllTiles()
            .Where(t => grid.GetHeightLevel(t.X, t.Y) > 0)
            .ToList();
        if (tiles.Count == 0) return 0.0;
        return tiles.Max(t => t.LandValue);
    }

    private static bool HasWaterWithin(CityGrid grid, int cx, int cy, int radius)
    {
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            var tx = cx + dx; var ty = cy + dy;
            if (!grid.IsInBounds(tx, ty)) continue;
            if (grid.GetHeightLevel(tx, ty) <= 0) return true;
        }
        return false;
    }

    private static int CountForestWithin(CityGrid grid, int cx, int cy, int radius)
    {
        var count = 0;
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            var tx = cx + dx; var ty = cy + dy;
            if (!grid.IsInBounds(tx, ty)) continue;
            if (grid.HasForestAt(tx, ty)) count++;
        }
        return count;
    }
}
