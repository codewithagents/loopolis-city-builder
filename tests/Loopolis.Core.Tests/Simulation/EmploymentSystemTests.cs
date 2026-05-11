using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class EmploymentSystemTests
{
    private EmploymentSystem _employment = null!;

    [SetUp]
    public void SetUp() => _employment = new EmploymentSystem();

    // Helper: create an industrial tile with full activity, power, and road access
    private static void AddFullIndustrialTile(CityGrid grid, int x, int y)
    {
        grid.SetZone(x, y, ZoneType.Industrial);
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
        grid.SetPopulation(x, y, 50); // full activity
    }

    // ── Test 1: below threshold always full employment ─────────────────────

    [Test]
    public void BelowThreshold_AlwaysFullEmployment()
    {
        // pop=50 < FreeJobsThreshold(100), no industrial — no jobs needed
        var grid = new CityGrid(10, 10);

        var multiplier = _employment.Propagate(grid, totalPopulation: 50);

        Assert.That(_employment.RequiredJobs, Is.EqualTo(0));
        Assert.That(_employment.EmploymentRatio, Is.EqualTo(1.0));
        Assert.That(multiplier, Is.EqualTo(1.0));
    }

    // ── Test 2: no industrial above threshold → min multiplier ────────────

    [Test]
    public void NoIndustrial_AboveThreshold_MinMultiplier()
    {
        // pop=200, required=100, no jobs at all → ratio=0.0, floor at MinGrowthMultiplier
        var grid = new CityGrid(10, 10);

        var multiplier = _employment.Propagate(grid, totalPopulation: 200);

        Assert.That(_employment.AvailableJobs, Is.EqualTo(0));
        Assert.That(_employment.RequiredJobs, Is.EqualTo(100));
        Assert.That(_employment.EmploymentRatio, Is.EqualTo(0.0));
        Assert.That(multiplier, Is.EqualTo(EmploymentSystem.MinGrowthMultiplier));
    }

    // ── Test 3: sufficient industrial → ratio 1.0 ─────────────────────────

    [Test]
    public void SufficientIndustrial_FullRatio()
    {
        // pop=150, required=50, 3 full tiles × 50 activity × 0.4 = 60 jobs → ratio=1.0
        var grid = new CityGrid(10, 10);
        AddFullIndustrialTile(grid, 1, 1);
        AddFullIndustrialTile(grid, 2, 1);
        AddFullIndustrialTile(grid, 3, 1);

        var multiplier = _employment.Propagate(grid, totalPopulation: 150);

        Assert.That(_employment.AvailableJobs, Is.EqualTo(60));
        Assert.That(_employment.RequiredJobs, Is.EqualTo(50));
        Assert.That(_employment.EmploymentRatio, Is.EqualTo(1.0));
        Assert.That(multiplier, Is.EqualTo(1.0));
    }

    // ── Test 4: partial industrial → fractional ratio ─────────────────────

    [Test]
    public void PartialIndustrial_FractionalRatio()
    {
        // pop=200, required=100, 1 full tile = 20 jobs → ratio = 20/100 = 0.2
        var grid = new CityGrid(10, 10);
        AddFullIndustrialTile(grid, 5, 5);

        var multiplier = _employment.Propagate(grid, totalPopulation: 200);

        Assert.That(_employment.AvailableJobs, Is.EqualTo(20));
        Assert.That(_employment.RequiredJobs, Is.EqualTo(100));
        Assert.That(_employment.EmploymentRatio, Is.EqualTo(0.2).Within(0.001));
        // multiplier = max(0.2, 0.2) = 0.2
        Assert.That(multiplier, Is.EqualTo(EmploymentSystem.MinGrowthMultiplier));
    }

    // ── Test 5: industrial without power contributes no jobs ──────────────

    [Test]
    public void IndustrialWithoutPower_NoJobs()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Industrial);
        grid.SetRoadAccess(5, 5, true);
        // HasPower = false (default) — should not count
        grid.SetPopulation(5, 5, 50);

        _employment.Propagate(grid, totalPopulation: 200);

        Assert.That(_employment.AvailableJobs, Is.EqualTo(0));
    }

    // ── Test 6: industrial without road contributes no jobs ───────────────

    [Test]
    public void IndustrialWithoutRoad_NoJobs()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Industrial);
        grid.SetPower(5, 5, true);
        // HasRoadAccess = false (default) — should not count
        grid.SetPopulation(5, 5, 50);

        _employment.Propagate(grid, totalPopulation: 200);

        Assert.That(_employment.AvailableJobs, Is.EqualTo(0));
    }

    // ── Test 7: employment multiplier affects residential growth ──────────

    [Test]
    public void EmploymentMultiplier_AffectsResidentialGrowth()
    {
        // Directly tests the PopulationSystem multiplier parameter without full engine ticking.
        //
        // Approach: manually set up a grid with residential tiles and tick PopulationSystem
        // twice — once with multiplier=1.0 (good employment) and once with multiplier=0.2
        // (poor employment). The high-employment city should grow faster.

        var gridGood = new CityGrid(10, 10);
        var gridPoor = new CityGrid(10, 10);
        var popGood  = new PopulationSystem();
        var popPoor  = new PopulationSystem();

        // Set up 4 residential tiles on each grid, fully powered and road-accessible
        for (var x = 1; x <= 4; x++)
        {
            gridGood.SetZone(x, 5, ZoneType.Residential);
            gridGood.SetPower(x, 5, true);
            gridGood.SetRoadAccess(x, 5, true);

            gridPoor.SetZone(x, 5, ZoneType.Residential);
            gridPoor.SetPower(x, 5, true);
            gridPoor.SetRoadAccess(x, 5, true);
        }

        // Tick 30 times: good employment = full multiplier, poor employment = 0.2×
        for (var i = 0; i < 30; i++)
        {
            popGood.Tick(gridGood, employmentMultiplier: 1.0);
            popPoor.Tick(gridPoor, employmentMultiplier: EmploymentSystem.MinGrowthMultiplier);
        }

        Assert.That(popGood.Population, Is.GreaterThan(popPoor.Population),
            "Full employment (multiplier=1.0) should produce higher population than poor employment (multiplier=0.2)");
    }
}
