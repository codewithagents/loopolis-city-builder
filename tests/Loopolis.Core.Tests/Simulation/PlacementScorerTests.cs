using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class PlacementScorerTests
{
    private static CityGrid MakeGrid(int w = 15, int h = 15) => new(w, h);

    // ── Null / guard cases ───────────────────────────────────────────────────────

    [Test]
    public void WaterTile_ReturnsNull()
    {
        var grid = MakeGrid();
        grid.SetHeightLevel(5, 5, 0); // make water
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Residential);
        Assert.That(score, Is.Null, "Water tile should return null");
    }

    [Test]
    public void OccupiedTile_ReturnsNull()
    {
        var grid = MakeGrid();
        grid.SetZone(5, 5, ZoneType.Residential);
        // Tile is no longer empty — scoring the same position again returns null
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Commercial);
        Assert.That(score, Is.Null, "Occupied tile should return null");
    }

    [Test]
    public void RoadZone_ReturnsNull()
    {
        var grid = MakeGrid();
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Road);
        Assert.That(score, Is.Null, "Road placement returns null — no meaningful score");
    }

    [Test]
    public void AvenueZone_ReturnsNull()
    {
        var grid = MakeGrid();
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Avenue);
        Assert.That(score, Is.Null);
    }

    [Test]
    public void PowerPlantZone_ReturnsNull()
    {
        var grid = MakeGrid();
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.CoalPlant);
        Assert.That(score, Is.Null);
    }

    // ── Residential ─────────────────────────────────────────────────────────────

    [Test]
    public void Residential_BaseCase_PrimaryLabelContainsResidents()
    {
        var grid = MakeGrid();
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Residential);
        Assert.That(score, Is.Not.Null);
        Assert.That(score!.PrimaryLabel, Does.Contain("potential residents"));
        Assert.That(score.PrimaryLabel, Is.Not.Empty);
        Assert.That(score.SecondaryLabel, Is.Not.Null);
    }

    [Test]
    public void Residential_NotRoadAdjacent_LowerEstimateAndHint()
    {
        var grid = MakeGrid();
        // No roads at all
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Residential);
        Assert.That(score, Is.Not.Null);
        // Half capacity when no road adjacent
        Assert.That(score!.EstimatedPotentialResidents, Is.EqualTo(25),
            "No road adjacent → 50% base capacity");
        Assert.That(score.SecondaryLabel, Does.Contain("road"), "Should hint about road requirement");
    }

    [Test]
    public void Residential_RoadAdjacent_FullEstimate()
    {
        var grid = MakeGrid();
        grid.SetZone(5, 6, ZoneType.Road); // road directly below
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Residential);
        Assert.That(score, Is.Not.Null);
        Assert.That(score!.EstimatedPotentialResidents, Is.EqualTo(50),
            "Road adjacent → full base capacity");
    }

    [Test]
    public void Residential_NearCommercial_DemandBoostIncreaseEstimate()
    {
        var grid = MakeGrid();
        grid.SetZone(5, 6, ZoneType.Road);       // road for adjacency
        grid.SetZone(6, 5, ZoneType.Commercial); // commercial within radius 3
        var withoutCommercial = PlacementScorer.Score(grid, 5, 5, ZoneType.Residential)!.EstimatedPotentialResidents;

        // Now score a cell without commercial nearby (further away)
        var grid2 = MakeGrid();
        grid2.SetZone(5, 6, ZoneType.Road);
        var withoutBonus = PlacementScorer.Score(grid2, 5, 5, ZoneType.Residential)!.EstimatedPotentialResidents;

        Assert.That(withoutCommercial, Is.GreaterThan(withoutBonus),
            "Commercial nearby should boost residential estimate");
    }

    [Test]
    public void Residential_NearIndustrial_HighPollutionLabel()
    {
        var grid = MakeGrid();
        // Place 2 industrial tiles nearby
        grid.SetZone(4, 5, ZoneType.Industrial);
        grid.SetZone(6, 5, ZoneType.Industrial);
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Residential);
        Assert.That(score, Is.Not.Null);
        // PollutionExposure = 2 / 5.0 = 0.4 > 0.3 threshold
        Assert.That(score!.PollutionExposure, Is.GreaterThan(0.3f));
        Assert.That(score.SecondaryLabel, Does.Contain("pollution"));
    }

    [Test]
    public void Residential_CleanArea_IncomeEstimateInLabel()
    {
        var grid = MakeGrid();
        grid.SetZone(5, 6, ZoneType.Road);
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Residential);
        Assert.That(score, Is.Not.Null);
        // Should show income estimate, not pollution warning
        Assert.That(score!.SecondaryLabel, Does.Contain("/tick"));
    }

    // ── Commercial ──────────────────────────────────────────────────────────────

    [Test]
    public void Commercial_AwayFromResidential_LowDemandLabel()
    {
        var grid = MakeGrid();
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Commercial);
        Assert.That(score, Is.Not.Null);
        Assert.That(score!.SecondaryLabel, Does.Contain("Low demand"));
    }

    [Test]
    public void Commercial_NearResidential_IncomeBoostAndNeighborLabel()
    {
        var grid = MakeGrid();
        grid.SetZone(4, 5, ZoneType.Residential);
        grid.SetZone(6, 5, ZoneType.Residential);
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Commercial);
        Assert.That(score, Is.Not.Null);
        Assert.That(score!.EstimatedIncomePerTick, Is.GreaterThan(8.0),
            "Nearby residential should boost commercial income estimate");
        Assert.That(score.SecondaryLabel, Does.Contain("residential"));
    }

    [Test]
    public void Commercial_PrimaryLabelContainsTick()
    {
        var grid = MakeGrid();
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Commercial);
        Assert.That(score, Is.Not.Null);
        Assert.That(score!.PrimaryLabel, Does.Contain("/tick"));
    }

    // ── Industrial ──────────────────────────────────────────────────────────────

    [Test]
    public void Industrial_FlatTile_JobsLabel()
    {
        var grid = MakeGrid();
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Industrial);
        Assert.That(score, Is.Not.Null);
        Assert.That(score!.PrimaryLabel, Does.Contain("potential jobs"));
        Assert.That(score.EstimatedPotentialJobs, Is.EqualTo(20));
    }

    [Test]
    public void Industrial_ForestTile_TimberMillLabel()
    {
        var grid = MakeGrid();
        grid.SetForest(5, 5, true);
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Industrial);
        Assert.That(score, Is.Not.Null);
        Assert.That(score!.SecondaryLabel, Does.Contain("Timber Mill"));
    }

    [Test]
    public void Industrial_ElevatedTile_QuarryLabel()
    {
        var grid = MakeGrid();
        grid.SetHeightLevel(5, 5, 3); // elevated
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Industrial);
        Assert.That(score, Is.Not.Null);
        Assert.That(score!.SecondaryLabel, Does.Contain("Quarry"));
    }

    // ── Park ────────────────────────────────────────────────────────────────────

    [Test]
    public void Park_NearResidential_HappinessTileCountInLabel()
    {
        var grid = MakeGrid();
        grid.SetZone(4, 5, ZoneType.Residential);
        grid.SetZone(6, 5, ZoneType.Residential);
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Park);
        Assert.That(score, Is.Not.Null);
        Assert.That(score!.PrimaryLabel, Does.Contain("2"));  // 2 nearby residential
        Assert.That(score.SecondaryLabel, Does.Contain("$3/tick"));
    }

    [Test]
    public void Park_NoNearbyResidential_StillHasLabels()
    {
        var grid = MakeGrid();
        var score = PlacementScorer.Score(grid, 5, 5, ZoneType.Park);
        Assert.That(score, Is.Not.Null);
        Assert.That(score!.PrimaryLabel, Is.Not.Empty);
        Assert.That(score.SecondaryLabel, Does.Contain("$3/tick"));
    }

    // ── Labels are never null ───────────────────────────────────────────────────

    [Test]
    public void AllZoneTypes_LabelsAreNeverNullOrEmpty()
    {
        var grid = MakeGrid();
        var zones = new[] { ZoneType.Residential, ZoneType.Commercial, ZoneType.Industrial, ZoneType.Park };
        foreach (var zone in zones)
        {
            var score = PlacementScorer.Score(grid, 5, 5, zone);
            Assert.That(score, Is.Not.Null, $"Score for {zone} should not be null on empty flat tile");
            Assert.That(score!.PrimaryLabel, Is.Not.Null.And.Not.Empty,
                $"PrimaryLabel for {zone} must not be null or empty");
            Assert.That(score.SecondaryLabel, Is.Not.Null,
                $"SecondaryLabel for {zone} must not be null (may be empty string)");
        }
    }
}
