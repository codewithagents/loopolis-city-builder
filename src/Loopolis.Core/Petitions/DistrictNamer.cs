using Loopolis.Core.Grid;

namespace Loopolis.Core.Petitions;

/// <summary>
/// Pure static class. Given a CityGrid and a set of representative tiles, returns a
/// deterministic district name.
///
/// Naming priority:
///   1. Forest majority → ["Pine Valley", "Oak Ridge", "Maple Grove", "Cedar Creek"]
///   2. Elevated majority (HeightLevel >= 2) → ["Ridge Heights", "Summit Hills", "High Peaks", "Hilltop"]
///   3. Any water-adjacent tile → ["Shore District", "Riverside", "Harbor View", "Bayfront"]
///   4. Default → compass direction based on centroid vs grid center
///      (centroid within 20% of center → "Midtown" or "Downtown" alternating by hash)
///
/// Determinism: uses (centroidX * 31 + centroidY) % names.Length to pick an index.
/// Same tile cluster always produces the same name.
/// </summary>
public static class DistrictNamer
{
    private static readonly string[] ForestNames  = { "Pine Valley", "Oak Ridge", "Maple Grove", "Cedar Creek" };
    private static readonly string[] HeightNames  = { "Ridge Heights", "Summit Hills", "High Peaks", "Hilltop" };
    private static readonly string[] ShoreNames   = { "Shore District", "Riverside", "Harbor View", "Bayfront" };
    private static readonly string[] CompassNames = { "North End", "South End", "East Side", "West Side", "Midtown", "Downtown" };

    /// <summary>
    /// Returns a deterministic district name for the given representative tile cluster.
    /// </summary>
    /// <param name="grid">The city grid — used for terrain lookups and adjacency checks.</param>
    /// <param name="tiles">Representative zone tiles for the district (x, y) pairs.</param>
    public static string Name(CityGrid grid, IReadOnlyList<(int x, int y)> tiles)
    {
        if (tiles.Count == 0)
            return "Downtown";

        // Compute centroid
        double sumX = 0, sumY = 0;
        foreach (var (x, y) in tiles) { sumX += x; sumY += y; }
        var centroidX = sumX / tiles.Count;
        var centroidY = sumY / tiles.Count;

        // Deterministic hash seed from centroid
        var hashSeed = (int)(centroidX * 31 + centroidY);

        // Count terrain features
        int forestCount   = 0;
        int elevatedCount = 0;
        bool hasWaterAdjacent = false;

        foreach (var (x, y) in tiles)
        {
            if (grid.HasForestAt(x, y))
                forestCount++;

            if (grid.GetHeightLevel(x, y) >= 2)
                elevatedCount++;

            // Check if any adjacent tile is water (HeightLevel == 0)
            if (!hasWaterAdjacent)
            {
                foreach (var adj in grid.AdjacentTiles(x, y))
                {
                    if (adj.HeightLevel == 0)
                    {
                        hasWaterAdjacent = true;
                        break;
                    }
                }
            }
        }

        int majority = tiles.Count / 2 + 1; // strict majority

        // Priority 1: Forest majority
        if (forestCount >= majority)
            return ForestNames[Math.Abs(hashSeed) % ForestNames.Length];

        // Priority 2: Elevated majority
        if (elevatedCount >= majority)
            return HeightNames[Math.Abs(hashSeed) % HeightNames.Length];

        // Priority 3: Water-adjacent
        if (hasWaterAdjacent)
            return ShoreNames[Math.Abs(hashSeed) % ShoreNames.Length];

        // Priority 4: Compass direction from grid center
        return CompassName(grid, centroidX, centroidY, hashSeed);
    }

    private static string CompassName(CityGrid grid, double centroidX, double centroidY, int hashSeed)
    {
        var centerX = grid.Width  / 2.0;
        var centerY = grid.Height / 2.0;

        var dx = centroidX - centerX;
        var dy = centroidY - centerY;

        // Within 20% of grid dimensions from center → Midtown or Downtown
        var thresholdX = grid.Width  * 0.20;
        var thresholdY = grid.Height * 0.20;

        if (Math.Abs(dx) <= thresholdX && Math.Abs(dy) <= thresholdY)
        {
            // Alternate between Midtown and Downtown based on hash
            return (Math.Abs(hashSeed) % 2 == 0) ? "Midtown" : "Downtown";
        }

        // Determine dominant direction
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx > 0 ? "East Side" : "West Side";
        else
            return dy < 0 ? "North End" : "South End";
    }
}
