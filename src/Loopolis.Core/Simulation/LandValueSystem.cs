using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Computes a land value score (0.0–1.0) for every non-water tile each tick.
///
/// Factors and bonuses:
///   +0.30  if tile terrain is Hill
///   +0.15  if any Water tile is within Chebyshev distance 3
///   +0.08  per Forest tile within Chebyshev distance 2, capped at +0.20
///   +0.10  if tile.PollutionLevel &lt; 0.1
///   +0.10  if tile.Happiness &gt; 0.7
///   +0.05  if tile.HasPower
///
/// Water tiles always get LandValue = 0.
/// Final value is clamped to [0.0, 1.0].
/// </summary>
public class LandValueSystem
{
    public const double HillBonus          = 0.30;
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
            var tile = grid.GetTile(x, y);

            // Water tiles have no land value
            if (tile.Terrain == TerrainType.Water)
                continue;

            var value = 0.0;

            // Hill bonus
            if (tile.Terrain == TerrainType.Hill)
                value += HillBonus;

            // Water-adjacent bonus — any Water tile within Chebyshev distance 3
            if (HasTerrainWithin(grid, x, y, TerrainType.Water, 3))
                value += WaterAdjacentBonus;

            // Forest bonus — count Forest tiles within Chebyshev distance 2, cap at +0.20
            var forestCount = CountTerrainWithin(grid, x, y, TerrainType.Forest, 2);
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
            .Where(t => t.Terrain != TerrainType.Water && t.Zone != ZoneType.Empty)
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
            .Where(t => t.Terrain != TerrainType.Water)
            .ToList();
        if (tiles.Count == 0) return 0.0;
        return tiles.Max(t => t.LandValue);
    }

    private static bool HasTerrainWithin(CityGrid grid, int cx, int cy, TerrainType terrain, int radius)
    {
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            if (dx == 0 && dy == 0) continue; // exclude self
            var tx = cx + dx;
            var ty = cy + dy;
            if (!grid.IsInBounds(tx, ty)) continue;
            if (grid.GetTerrain(tx, ty) == terrain) return true;
        }
        return false;
    }

    private static int CountTerrainWithin(CityGrid grid, int cx, int cy, TerrainType terrain, int radius)
    {
        var count = 0;
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            if (dx == 0 && dy == 0) continue; // exclude self
            var tx = cx + dx;
            var ty = cy + dy;
            if (!grid.IsInBounds(tx, ty)) continue;
            if (grid.GetTerrain(tx, ty) == terrain) count++;
        }
        return count;
    }
}
