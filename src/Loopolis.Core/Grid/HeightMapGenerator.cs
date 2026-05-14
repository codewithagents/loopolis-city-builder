namespace Loopolis.Core.Grid;

/// <summary>
/// Generates procedural height maps using the diamond-square algorithm.
///
/// Height values range from 0–10:
///   0      → Water (impassable, ~20% of tiles)
///   1      → Flat (buildable, ~55% of tiles)
///   2–4    → Elevated hills (buildable, ~20% of tiles)
///   5–10   → High peaks (buildable, ~5% of tiles)
///
/// Grid sizes: works on any W×H grid by generating on a canvas of size
/// SmallestPowerOf2Plus1(max(W,H)) and trimming to the requested dimensions.
/// Examples: 32×32 → 33×33 canvas, 128×128 → 129×129 canvas.
/// </summary>
public static class HeightMapGenerator
{
    /// <summary>
    /// Returns the smallest value 2^k+1 that is >= n+1.
    /// Examples: n=32 → 33 (2^5+1), n=64 → 65 (2^6+1), n=128 → 129 (2^7+1), n=100 → 129 (2^7+1).
    /// </summary>
    public static int SmallestPowerOf2Plus1(int n)
    {
        // We need 2^k + 1 >= n + 1, i.e. 2^k >= n
        var power = 1;
        while (power < n)
            power *= 2;
        return power + 1;
    }

    /// <summary>
    /// Generate a W×H height map using the diamond-square algorithm.
    /// Returns int[W, H] with values in [0, 10].
    /// Same seed always produces the same map.
    /// </summary>
    public static int[,] Generate(int width, int height, int seed, float roughness = 0.5f)
    {
        var canvasSize = SmallestPowerOf2Plus1(Math.Max(width, height));
        var rng = new Random(seed);
        var canvas = new float[canvasSize, canvasSize];

        // Initialise the four corners with random values in [0, 1]
        canvas[0, 0]                              = (float)rng.NextDouble();
        canvas[canvasSize - 1, 0]                = (float)rng.NextDouble();
        canvas[0, canvasSize - 1]                = (float)rng.NextDouble();
        canvas[canvasSize - 1, canvasSize - 1]   = (float)rng.NextDouble();

        // Diamond-square steps
        var stepSize = canvasSize - 1; // starts at power-of-2
        var scale = 1.0f;
        while (stepSize > 1)
        {
            var half = stepSize / 2;

            // Diamond step: for each square, set the centre from its four corners
            for (var y = 0; y < canvasSize - 1; y += stepSize)
            for (var x = 0; x < canvasSize - 1; x += stepSize)
            {
                var avg = (canvas[x,           y] +
                           canvas[x + stepSize, y] +
                           canvas[x,           y + stepSize] +
                           canvas[x + stepSize, y + stepSize]) / 4.0f;
                canvas[x + half, y + half] = avg + ((float)rng.NextDouble() * 2 - 1) * scale;
            }

            // Square step: for each diamond midpoint, set it from up to 4 neighbours
            for (var y = 0; y < canvasSize; y += half)
            for (var x = (y + half) % stepSize; x < canvasSize; x += stepSize)
            {
                var sum = 0.0f;
                var count = 0;
                if (x - half >= 0)            { sum += canvas[x - half, y]; count++; }
                if (x + half < canvasSize)    { sum += canvas[x + half, y]; count++; }
                if (y - half >= 0)            { sum += canvas[x, y - half]; count++; }
                if (y + half < canvasSize)    { sum += canvas[x, y + half]; count++; }
                canvas[x, y] = sum / count + ((float)rng.NextDouble() * 2 - 1) * scale;
            }

            stepSize /= 2;
            scale    *= roughness;
        }

        // Normalise the canvas to [0, 1]
        var minVal = float.MaxValue;
        var maxVal = float.MinValue;
        for (var x = 0; x < canvasSize; x++)
        for (var y = 0; y < canvasSize; y++)
        {
            if (canvas[x, y] < minVal) minVal = canvas[x, y];
            if (canvas[x, y] > maxVal) maxVal = canvas[x, y];
        }
        var range = maxVal - minVal;
        if (range < 0.0001f) range = 0.0001f;

        for (var x = 0; x < canvasSize; x++)
        for (var y = 0; y < canvasSize; y++)
            canvas[x, y] = (canvas[x, y] - minVal) / range;

        // Map to int height levels using distribution thresholds:
        //   0–0.20  → 0 (Water,   ~20%)
        //   0.20–0.75 → 1 (Flat,  ~55%)
        //   0.75–0.95 → 2–4 (Elevated, ~20%)
        //   0.95–1.0  → 5–10 (Peaks,  ~5%)
        var result = new int[width, height];
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            var v = canvas[x, y]; // trimmed to [width, height] from the larger canvas
            result[x, y] = NormalizedToHeight(v);
        }
        return result;
    }

    /// <summary>
    /// Generate a W×H height map using a named terrain seed.
    /// Named seeds produce geographically distinct terrain shapes.
    /// Returns int[W, H] with values in [0, 10].
    /// </summary>
    public static int[,] GenerateNamed(string terrainSeed, int width, int height)
    {
        return terrainSeed switch
        {
            "island_chain"  => GenerateIslandChain(width, height),
            "narrow_valley" => GenerateNarrowValley(width, height),
            "river_delta"   => GenerateRiverDelta(width, height),
            _               => Generate(width, height, terrainSeed.GetHashCode())
        };
    }

    /// <summary>
    /// Island Chain: ~40% water coverage. Elevated island clusters surrounded by water,
    /// connected by narrow land bridges. Uses fixed seed for determinism.
    /// </summary>
    public static int[,] GenerateIslandChain(int width, int height)
    {
        var rng    = new Random(0xC0FFEE); // fixed seed for determinism
        var result = new int[width, height];

        // Start with all water
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
            result[x, y] = 0;

        // Scatter 10 island seeds and grow each outward with decreasing probability
        const int islandCount = 10;
        for (var i = 0; i < islandCount; i++)
        {
            // Island seeds placed in a grid-like pattern with some jitter to avoid clustering
            var cx = (int)(width  * (0.1 + 0.8 * ((i % 4) / 3.0))) + rng.Next(-4, 5);
            var cy = (int)(height * (0.1 + 0.8 * ((i / 4) / 2.0))) + rng.Next(-4, 5);
            cx = Math.Clamp(cx, 2, width  - 3);
            cy = Math.Clamp(cy, 2, height - 3);

            // Island size: radius 3–7 tiles
            var radius = 3 + rng.Next(5);

            // Place island using flood-fill with decreasing probability from center
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var px = cx + dx;
                var py = cy + dy;
                if (px < 0 || px >= width || py < 0 || py >= height) continue;

                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > radius) continue;

                // Probability decreases with distance from center
                var prob = 1.0 - (dist / radius) * 0.7;
                if (rng.NextDouble() < prob)
                {
                    // Center tiles are elevated, edges are flat
                    result[px, py] = dist < radius * 0.4 ? 2 : 1;
                }
            }
        }

        // Add a few narrow land bridges connecting islands
        // Simple horizontal and vertical bridges
        for (var bridgeIdx = 0; bridgeIdx < 4; bridgeIdx++)
        {
            var bx1 = 5 + rng.Next(width  - 10);
            var by1 = 5 + rng.Next(height - 10);
            var bx2 = 5 + rng.Next(width  - 10);
            var by2 = by1 + rng.Next(-3, 4); // mostly horizontal bridges

            var minX = Math.Min(bx1, bx2);
            var maxX = Math.Max(bx1, bx2);
            for (var bx = minX; bx <= maxX; bx++)
            {
                // Bridge width: 1 tile wide
                if (by1 >= 0 && by1 < height) result[bx, by1] = 1;
                if (by2 >= 0 && by2 < height) result[bx, by2] = 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Narrow Valley: 128×128 map with tall mountain walls on east and west edges.
    /// Only the central corridor (roughly x=30–94 on a 128-wide map) is flat.
    /// Uses fixed seed for determinism.
    /// </summary>
    public static int[,] GenerateNarrowValley(int width, int height)
    {
        var rng    = new Random(0xBADC0DE); // fixed seed for determinism
        var result = new int[width, height];

        // Determine valley corridor: center 50% of width, ±some jitter
        var valleyLeft  = (int)(width * 0.23);  // ~30 for 128-wide
        var valleyRight = (int)(width * 0.73);  // ~93 for 128-wide

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            if (x >= valleyLeft && x <= valleyRight)
            {
                // Valley floor — mostly flat (height 1) with some gentle variation
                result[x, y] = rng.NextDouble() < 0.06 ? 2 : 1;
            }
            else
            {
                // Mountain wall — elevated (height 2+), steeper near edges
                var distFromValley = x < valleyLeft
                    ? valleyLeft  - x
                    : x - valleyRight;

                // Mountain height rises with distance from valley
                if      (distFromValley <= 2)  result[x, y] = rng.NextDouble() < 0.5 ? 2 : 1;
                else if (distFromValley <= 5)  result[x, y] = 2 + rng.Next(2);
                else if (distFromValley <= 10) result[x, y] = 3 + rng.Next(3);
                else                           result[x, y] = 5 + rng.Next(4);
            }
        }

        return result;
    }

    /// <summary>
    /// River Delta: 64×64 mostly flat map with 3 diagonal water channels (width 3 tiles)
    /// running from top-left toward bottom-right direction.
    /// Uses fixed seed for determinism.
    /// </summary>
    public static int[,] GenerateRiverDelta(int width, int height)
    {
        var rng    = new Random(0xDE1A01); // fixed seed for determinism
        var result = new int[width, height];

        // Start with flat land
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
            result[x, y] = 1;

        // Draw 3 diagonal water channels from different start points along the top/left edges.
        // Channels run diagonally toward bottom-right (slope ~1).
        // Channel offsets spread them evenly across the map.
        var channelOffsets = new[] { width / 5, width / 2, width * 4 / 5 };

        foreach (var offset in channelOffsets)
        {
            // Channel starts at top edge and runs diagonally SE
            // Use Bresenham-style line from (offset, 0) toward (offset + height, height)
            for (var step = 0; step < width + height; step++)
            {
                // Parametric: cx moves right at 0.5 rate as cy moves down
                var cy = step * height / (width + height);
                var cx = offset + step * width / (width + height);

                // Draw channel with width of 3 tiles
                for (var w = -1; w <= 1; w++)
                {
                    var px = cx + w;
                    var py = cy;
                    if (px >= 0 && px < width && py >= 0 && py < height)
                        result[px, py] = 0; // water

                    // Also widen slightly diagonally
                    var px2 = cx;
                    var py2 = cy + w;
                    if (px2 >= 0 && px2 < width && py2 >= 0 && py2 < height)
                        result[px2, py2] = 0; // water
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Generate a W×H forest map. Marks roughly 12% of non-water tiles as forest.
    /// Forest placement is seeded independently (forestSeed = seed + 1 by default).
    /// </summary>
    public static bool[,] GenerateForest(int width, int height, int seed)
    {
        // Use seed+1 for forest so it differs from the height map
        var rng    = new Random(seed + 1);
        var result = new bool[width, height];

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
            result[x, y] = rng.NextDouble() < 0.12;

        return result;
    }

    // ── Threshold mapping ─────────────────────────────────────────────────────

    private static int NormalizedToHeight(float v)
    {
        if (v < 0.20f) return 0;     // Water
        if (v < 0.75f) return 1;     // Flat
        if (v < 0.83f) return 2;
        if (v < 0.90f) return 3;
        if (v < 0.95f) return 4;
        if (v < 0.97f) return 5;
        if (v < 0.98f) return 6;
        if (v < 0.99f) return 7;
        if (v < 0.995f) return 8;
        if (v < 0.999f) return 9;
        return 10;
    }
}
