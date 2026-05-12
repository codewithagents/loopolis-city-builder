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
