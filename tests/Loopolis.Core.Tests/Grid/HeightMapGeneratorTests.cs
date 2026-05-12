using Loopolis.Core.Grid;

namespace Loopolis.Core.Tests.Grid;

[TestFixture]
public class HeightMapGeneratorTests
{
    // ── Output dimensions ────────────────────────────────────────────────────

    [Test]
    public void Generate_ReturnsCorrectDimensions()
    {
        var map = HeightMapGenerator.Generate(32, 32, seed: 42);
        Assert.That(map.GetLength(0), Is.EqualTo(32));
        Assert.That(map.GetLength(1), Is.EqualTo(32));
    }

    [Test]
    public void Generate_NonSquare_ReturnsCorrectDimensions()
    {
        var map = HeightMapGenerator.Generate(20, 15, seed: 7);
        Assert.That(map.GetLength(0), Is.EqualTo(20));
        Assert.That(map.GetLength(1), Is.EqualTo(15));
    }

    // ── Value range ──────────────────────────────────────────────────────────

    [Test]
    public void Generate_AllValues_InRange_0_To_10()
    {
        var map = HeightMapGenerator.Generate(32, 32, seed: 1234);
        for (var x = 0; x < 32; x++)
        for (var y = 0; y < 32; y++)
            Assert.That(map[x, y], Is.InRange(0, 10),
                $"Height at ({x},{y}) = {map[x,y]} is outside [0, 10].");
    }

    // ── Determinism ──────────────────────────────────────────────────────────

    [Test]
    public void Generate_SameSeed_ProducesIdenticalMaps()
    {
        var map1 = HeightMapGenerator.Generate(32, 32, seed: 99);
        var map2 = HeightMapGenerator.Generate(32, 32, seed: 99);

        for (var x = 0; x < 32; x++)
        for (var y = 0; y < 32; y++)
            Assert.That(map1[x, y], Is.EqualTo(map2[x, y]),
                $"Same seed should produce identical maps at ({x},{y}).");
    }

    [Test]
    public void Generate_DifferentSeeds_ProduceDifferentMaps()
    {
        var map1 = HeightMapGenerator.Generate(32, 32, seed: 1);
        var map2 = HeightMapGenerator.Generate(32, 32, seed: 2);

        // Count differing tiles — must have at least 10% different
        var diffCount = 0;
        for (var x = 0; x < 32; x++)
        for (var y = 0; y < 32; y++)
            if (map1[x, y] != map2[x, y]) diffCount++;

        Assert.That(diffCount, Is.GreaterThan(32 * 32 * 0.10),
            "Different seeds should produce substantially different maps.");
    }

    // ── Distribution ─────────────────────────────────────────────────────────

    [Test]
    public void Generate_WaterPresent_AcrossMultipleSeeds()
    {
        // Diamond-square distribution varies by seed on small 32×32 grids.
        // Test that SOME water tiles exist across a set of seeds, and that the
        // AVERAGE water percentage across seeds lands in a reasonable band.
        var seeds = new[] { 1, 42, 100, 200, 999, 12345, 0xDEAD };
        var totalTiles = 0;
        var totalWater = 0;

        foreach (var seed in seeds)
        {
            var map = HeightMapGenerator.Generate(32, 32, seed);
            for (var x = 0; x < 32; x++)
            for (var y = 0; y < 32; y++)
            {
                totalTiles++;
                if (map[x, y] == 0) totalWater++;
            }
        }

        var avgWaterPct = (double)totalWater / totalTiles;
        // Across 7 seeds the average should sit in a wide band (5%–45%)
        Assert.That(avgWaterPct, Is.InRange(0.05, 0.45),
            $"Average water percentage across seeds {avgWaterPct:P1} is outside expected range.");

        // At least one seed must produce some water tiles
        Assert.That(totalWater, Is.GreaterThan(0), "At least some water tiles should be generated.");
    }

    [Test]
    public void Generate_ContainsBothWaterAndLandTiles()
    {
        var map = HeightMapGenerator.Generate(32, 32, seed: 42);
        var hasWater = false;
        var hasLand  = false;
        for (var x = 0; x < 32; x++)
        for (var y = 0; y < 32; y++)
        {
            if (map[x, y] == 0) hasWater = true;
            if (map[x, y] >= 1) hasLand  = true;
        }
        Assert.That(hasWater, Is.True, "Generated map should contain water tiles.");
        Assert.That(hasLand,  Is.True, "Generated map should contain land tiles.");
    }

    // ── Forest generation ─────────────────────────────────────────────────────

    [Test]
    public void GenerateForest_ReturnsCorrectDimensions()
    {
        var forest = HeightMapGenerator.GenerateForest(32, 32, seed: 42);
        Assert.That(forest.GetLength(0), Is.EqualTo(32));
        Assert.That(forest.GetLength(1), Is.EqualTo(32));
    }

    [Test]
    public void GenerateForest_SameSeed_ProducesIdenticalMaps()
    {
        var f1 = HeightMapGenerator.GenerateForest(32, 32, seed: 77);
        var f2 = HeightMapGenerator.GenerateForest(32, 32, seed: 77);

        for (var x = 0; x < 32; x++)
        for (var y = 0; y < 32; y++)
            Assert.That(f1[x, y], Is.EqualTo(f2[x, y]),
                $"Same seed should produce identical forest maps at ({x},{y}).");
    }

    [Test]
    public void GenerateForest_NotAllTrue_NotAllFalse()
    {
        // Forest map should have a mix of true/false values — not all land is forest, not all is bare
        var forest = HeightMapGenerator.GenerateForest(32, 32, seed: 42);

        var trueCount  = 0;
        var falseCount = 0;
        for (var x = 0; x < 32; x++)
        for (var y = 0; y < 32; y++)
        {
            if (forest[x, y]) trueCount++; else falseCount++;
        }

        Assert.That(trueCount,  Is.GreaterThan(0), "Forest map should have some forested tiles.");
        Assert.That(falseCount, Is.GreaterThan(0), "Forest map should have some non-forested tiles.");
        // Roughly ~12% should be forest — wide tolerance 5%–35%
        var forestPct = (double)trueCount / (32 * 32);
        Assert.That(forestPct, Is.InRange(0.05, 0.35),
            $"Forest percentage {forestPct:P1} should be in reasonable range.");
    }

    // ── CityGrid integration ──────────────────────────────────────────────────

    [Test]
    public void ApplyHeightMap_Grid_ReflectsGeneratedValues()
    {
        var grid      = new CityGrid(32, 32);
        var heightMap = HeightMapGenerator.Generate(32, 32, seed: 100);
        grid.ApplyHeightMap(heightMap);

        for (var x = 0; x < 32; x++)
        for (var y = 0; y < 32; y++)
            Assert.That(grid.GetHeightLevel(x, y), Is.EqualTo(heightMap[x, y]),
                $"Grid HeightLevel at ({x},{y}) should match generated map.");
    }

    [Test]
    public void ComputeAverageHeight_ReflectsActualAverage()
    {
        var grid      = new CityGrid(32, 32);
        var heightMap = HeightMapGenerator.Generate(32, 32, seed: 42);
        grid.ApplyHeightMap(heightMap);

        // Manual average
        long sum = 0;
        for (var x = 0; x < 32; x++)
        for (var y = 0; y < 32; y++)
            sum += heightMap[x, y];
        var expected = (float)sum / (32 * 32);

        Assert.That(grid.AverageHeight, Is.EqualTo(expected).Within(0.001f),
            "AverageHeight should match the actual average of the height map.");
    }

    [Test]
    public void SetFlatTerrain_AllHeightsAreOne_NoForest()
    {
        var grid = new CityGrid(10, 10);
        // First set some terrain to non-default
        grid.SetHeightLevel(3, 3, 5);
        grid.SetForest(2, 2, true);

        grid.SetFlatTerrain();

        for (var x = 0; x < 10; x++)
        for (var y = 0; y < 10; y++)
        {
            Assert.That(grid.GetHeightLevel(x, y), Is.EqualTo(1),
                $"SetFlatTerrain should set all heights to 1 at ({x},{y}).");
            Assert.That(grid.HasForestAt(x, y), Is.False,
                $"SetFlatTerrain should clear all forests at ({x},{y}).");
        }
        Assert.That(grid.AverageHeight, Is.EqualTo(1.0f).Within(0.001f));
    }
}
