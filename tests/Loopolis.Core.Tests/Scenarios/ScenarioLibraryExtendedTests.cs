using Loopolis.Core.Grid;
using Loopolis.Core.Scenarios;

namespace Loopolis.Core.Tests.Scenarios;

/// <summary>
/// Tests for the three new challenge scenarios (island_chain, narrow_valley, river_delta)
/// and their associated terrain generators.
/// </summary>
[TestFixture]
public class ScenarioLibraryExtendedTests
{
    // ── Scenario count ────────────────────────────────────────────────────────

    [Test]
    public void ScenarioLibrary_HasAtLeastFourteenScenarios()
    {
        Assert.That(ScenarioLibrary.All.Count, Is.GreaterThanOrEqualTo(14));
    }

    // ── New scenarios have non-null TerrainSeed ───────────────────────────────

    [Test]
    public void IslandChain_HasNonNullTerrainSeed()
    {
        var scenario = ScenarioLibrary.Find("island_chain");
        Assert.That(scenario, Is.Not.Null, "island_chain scenario must exist");
        Assert.That(scenario!.TerrainSeed, Is.Not.Null, "island_chain must have a TerrainSeed");
        Assert.That(scenario.TerrainSeed, Is.EqualTo("island_chain"));
    }

    [Test]
    public void NarrowValley_HasNonNullTerrainSeed()
    {
        var scenario = ScenarioLibrary.Find("narrow_valley");
        Assert.That(scenario, Is.Not.Null, "narrow_valley scenario must exist");
        Assert.That(scenario!.TerrainSeed, Is.Not.Null, "narrow_valley must have a TerrainSeed");
        Assert.That(scenario.TerrainSeed, Is.EqualTo("narrow_valley"));
    }

    [Test]
    public void RiverDelta_HasNonNullTerrainSeed()
    {
        var scenario = ScenarioLibrary.Find("river_delta");
        Assert.That(scenario, Is.Not.Null, "river_delta scenario must exist");
        Assert.That(scenario!.TerrainSeed, Is.Not.Null, "river_delta must have a TerrainSeed");
        Assert.That(scenario.TerrainSeed, Is.EqualTo("river_delta"));
    }

    // ── Scenario dimensions and goals ────────────────────────────────────────

    [Test]
    public void IslandChain_HasCorrectDimensionsAndGoal()
    {
        var scenario = ScenarioLibrary.Find("island_chain")!;
        Assert.That(scenario.MapWidth,              Is.EqualTo(64));
        Assert.That(scenario.MapHeight,             Is.EqualTo(64));
        Assert.That(scenario.Goal.TargetPopulation, Is.EqualTo(2_500));
        Assert.That(scenario.StartingBalance,       Is.EqualTo(6_500));
        Assert.That(scenario.TickLimit,             Is.EqualTo(700));
    }

    [Test]
    public void NarrowValley_HasCorrectDimensionsAndGoal()
    {
        var scenario = ScenarioLibrary.Find("narrow_valley")!;
        Assert.That(scenario.MapWidth,              Is.EqualTo(128));
        Assert.That(scenario.MapHeight,             Is.EqualTo(128));
        Assert.That(scenario.Goal.TargetPopulation, Is.EqualTo(7_500));
        Assert.That(scenario.StartingBalance,       Is.EqualTo(7_000));
        Assert.That(scenario.TickLimit,             Is.EqualTo(1_200));
    }

    [Test]
    public void RiverDelta_HasCorrectGoal()
    {
        var scenario = ScenarioLibrary.Find("river_delta")!;
        Assert.That(scenario.Goal.TargetPopulation, Is.EqualTo(3_000));
        Assert.That(scenario.MapWidth,              Is.EqualTo(64));
        Assert.That(scenario.MapHeight,             Is.EqualTo(64));
        Assert.That(scenario.StartingBalance,       Is.EqualTo(5_000));
        Assert.That(scenario.TickLimit,             Is.EqualTo(800));
    }

    // ── Medal thresholds are valid ────────────────────────────────────────────

    [Test]
    public void IslandChain_MedalsAreOrdered()
    {
        var m = ScenarioLibrary.Find("island_chain")!.Medals;
        Assert.That(m.Gold,   Is.LessThan(m.Silver), "Gold < Silver");
        Assert.That(m.Silver, Is.LessThan(m.Bronze), "Silver < Bronze");
        Assert.That(m.Bronze, Is.LessThanOrEqualTo(700), "Bronze <= TickLimit");
    }

    [Test]
    public void NarrowValley_MedalsAreOrdered()
    {
        var m = ScenarioLibrary.Find("narrow_valley")!.Medals;
        Assert.That(m.Gold,   Is.LessThan(m.Silver),  "Gold < Silver");
        Assert.That(m.Silver, Is.LessThan(m.Bronze),  "Silver < Bronze");
        Assert.That(m.Bronze, Is.LessThanOrEqualTo(1_200), "Bronze <= TickLimit");
    }

    [Test]
    public void RiverDelta_MedalsAreOrdered()
    {
        var m = ScenarioLibrary.Find("river_delta")!.Medals;
        Assert.That(m.Gold,   Is.LessThan(m.Silver), "Gold < Silver");
        Assert.That(m.Silver, Is.LessThan(m.Bronze), "Silver < Bronze");
        Assert.That(m.Bronze, Is.LessThanOrEqualTo(800), "Bronze <= TickLimit");
    }

    // ── HeightMapGenerator terrain shapes ────────────────────────────────────

    [Test]
    public void GenerateIslandChain_HasAtLeast30PercentWater()
    {
        var map       = HeightMapGenerator.GenerateIslandChain(64, 64);
        var total     = 64 * 64;
        var waterCount = 0;

        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
            if (map[x, y] == 0) waterCount++;

        var waterPct = (double)waterCount / total;
        Assert.That(waterPct, Is.GreaterThanOrEqualTo(0.30),
            $"Island Chain should have >= 30% water. Actual: {waterPct:P1}");
    }

    [Test]
    public void GenerateIslandChain_HasSomeLandTiles()
    {
        var map      = HeightMapGenerator.GenerateIslandChain(64, 64);
        var landCount = 0;

        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
            if (map[x, y] >= 1) landCount++;

        Assert.That(landCount, Is.GreaterThan(0), "Island Chain must have some land tiles");
    }

    [Test]
    public void GenerateIslandChain_IsDeterministic()
    {
        var map1 = HeightMapGenerator.GenerateIslandChain(64, 64);
        var map2 = HeightMapGenerator.GenerateIslandChain(64, 64);

        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
            Assert.That(map1[x, y], Is.EqualTo(map2[x, y]),
                $"Island Chain must be deterministic at ({x},{y})");
    }

    [Test]
    public void GenerateNarrowValley_EdgeTilesAreElevated()
    {
        var map = HeightMapGenerator.GenerateNarrowValley(128, 128);

        // Far-left and far-right columns (first 10, last 10) should all be elevated (h >= 2)
        var edgeElevated = true;
        for (var y = 0; y < 128 && edgeElevated; y++)
        {
            if (map[0, y] < 2) edgeElevated = false;
            if (map[127, y] < 2) edgeElevated = false;
        }
        Assert.That(edgeElevated, Is.True,
            "Narrow Valley: outermost edge columns (x=0, x=127) must all be elevated (height >= 2)");
    }

    [Test]
    public void GenerateNarrowValley_CenterColumnIsFlat()
    {
        var map = HeightMapGenerator.GenerateNarrowValley(128, 128);

        // Center column (x=64) should be mostly flat (h=1), with at most 10% elevated
        var flatCount     = 0;
        var elevatedCount = 0;
        for (var y = 0; y < 128; y++)
        {
            if (map[64, y] == 1) flatCount++;
            else elevatedCount++;
        }

        var flatPct = (double)flatCount / 128;
        Assert.That(flatPct, Is.GreaterThan(0.85),
            $"Narrow Valley center column (x=64) should be mostly flat. Flat: {flatPct:P1}");
    }

    [Test]
    public void GenerateNarrowValley_IsDeterministic()
    {
        var map1 = HeightMapGenerator.GenerateNarrowValley(128, 128);
        var map2 = HeightMapGenerator.GenerateNarrowValley(128, 128);

        for (var x = 0; x < 128; x++)
        for (var y = 0; y < 128; y++)
            Assert.That(map1[x, y], Is.EqualTo(map2[x, y]),
                $"Narrow Valley must be deterministic at ({x},{y})");
    }

    [Test]
    public void GenerateRiverDelta_HasSomeWaterTiles()
    {
        var map      = HeightMapGenerator.GenerateRiverDelta(64, 64);
        var waterCount = 0;

        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
            if (map[x, y] == 0) waterCount++;

        Assert.That(waterCount, Is.GreaterThan(0),
            "River Delta must have some water tiles (channels)");
    }

    [Test]
    public void GenerateRiverDelta_IsMostlyFlat()
    {
        var map      = HeightMapGenerator.GenerateRiverDelta(64, 64);
        var total    = 64 * 64;
        var flatCount = 0;

        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
            if (map[x, y] == 1) flatCount++;

        var flatPct = (double)flatCount / total;
        Assert.That(flatPct, Is.GreaterThan(0.50),
            $"River Delta should be mostly flat land. Flat: {flatPct:P1}");
    }

    [Test]
    public void GenerateRiverDelta_IsDeterministic()
    {
        var map1 = HeightMapGenerator.GenerateRiverDelta(64, 64);
        var map2 = HeightMapGenerator.GenerateRiverDelta(64, 64);

        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
            Assert.That(map1[x, y], Is.EqualTo(map2[x, y]),
                $"River Delta must be deterministic at ({x},{y})");
    }

    // ── GenerateNamed dispatch ────────────────────────────────────────────────

    [Test]
    public void GenerateNamed_IslandChain_MatchesDirectCall()
    {
        var named  = HeightMapGenerator.GenerateNamed("island_chain", 64, 64);
        var direct = HeightMapGenerator.GenerateIslandChain(64, 64);

        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
            Assert.That(named[x, y], Is.EqualTo(direct[x, y]),
                $"GenerateNamed('island_chain') must match GenerateIslandChain() at ({x},{y})");
    }

    [Test]
    public void GenerateNamed_NarrowValley_MatchesDirectCall()
    {
        var named  = HeightMapGenerator.GenerateNamed("narrow_valley", 128, 128);
        var direct = HeightMapGenerator.GenerateNarrowValley(128, 128);

        for (var x = 0; x < 128; x++)
        for (var y = 0; y < 128; y++)
            Assert.That(named[x, y], Is.EqualTo(direct[x, y]),
                $"GenerateNamed('narrow_valley') must match GenerateNarrowValley() at ({x},{y})");
    }

    [Test]
    public void GenerateNamed_RiverDelta_MatchesDirectCall()
    {
        var named  = HeightMapGenerator.GenerateNamed("river_delta", 64, 64);
        var direct = HeightMapGenerator.GenerateRiverDelta(64, 64);

        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
            Assert.That(named[x, y], Is.EqualTo(direct[x, y]),
                $"GenerateNamed('river_delta') must match GenerateRiverDelta() at ({x},{y})");
    }

    // ── Output dimensions ─────────────────────────────────────────────────────

    [Test]
    public void GenerateIslandChain_ReturnsCorrectDimensions()
    {
        var map = HeightMapGenerator.GenerateIslandChain(64, 64);
        Assert.That(map.GetLength(0), Is.EqualTo(64));
        Assert.That(map.GetLength(1), Is.EqualTo(64));
    }

    [Test]
    public void GenerateNarrowValley_ReturnsCorrectDimensions()
    {
        var map = HeightMapGenerator.GenerateNarrowValley(128, 128);
        Assert.That(map.GetLength(0), Is.EqualTo(128));
        Assert.That(map.GetLength(1), Is.EqualTo(128));
    }

    [Test]
    public void GenerateRiverDelta_ReturnsCorrectDimensions()
    {
        var map = HeightMapGenerator.GenerateRiverDelta(64, 64);
        Assert.That(map.GetLength(0), Is.EqualTo(64));
        Assert.That(map.GetLength(1), Is.EqualTo(64));
    }

    // ── All values in range 0–10 ──────────────────────────────────────────────

    [Test]
    public void GenerateIslandChain_AllValues_InRange_0_To_10()
    {
        var map = HeightMapGenerator.GenerateIslandChain(64, 64);
        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
            Assert.That(map[x, y], Is.InRange(0, 10),
                $"Height at ({x},{y}) = {map[x, y]} is outside [0, 10].");
    }

    [Test]
    public void GenerateNarrowValley_AllValues_InRange_0_To_10()
    {
        var map = HeightMapGenerator.GenerateNarrowValley(128, 128);
        for (var x = 0; x < 128; x++)
        for (var y = 0; y < 128; y++)
            Assert.That(map[x, y], Is.InRange(0, 10),
                $"Height at ({x},{y}) = {map[x, y]} is outside [0, 10].");
    }

    [Test]
    public void GenerateRiverDelta_AllValues_InRange_0_To_10()
    {
        var map = HeightMapGenerator.GenerateRiverDelta(64, 64);
        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
            Assert.That(map[x, y], Is.InRange(0, 10),
                $"Height at ({x},{y}) = {map[x, y]} is outside [0, 10].");
    }
}
