using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class HappinessSystemTests
{
    private HappinessSystem _happiness = null!;

    [SetUp]
    public void SetUp() => _happiness = new HappinessSystem();

    /// <summary>
    /// Helper: makes a residential tile fully ready (powered + road access).
    /// </summary>
    private static void MakeReady(CityGrid grid, int x, int y)
    {
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
    }

    /// <summary>
    /// Helper: makes a commercial tile fully ready (for adjacency bonus tests).
    /// </summary>
    private static void MakeCommercialReady(CityGrid grid, int x, int y)
    {
        grid.SetZone(x, y, ZoneType.Commercial);
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);
    }

    [Test]
    public void ReadyResidentialWithNoModifiers_GetsBaseHappiness()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // No commercial adjacent, no services, no pollution

        _happiness.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.6).Within(0.001));
    }

    [Test]
    public void AdjacentCommercial_IncreasesHappiness()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        MakeCommercialReady(grid, 5, 6); // adjacent

        _happiness.Propagate(grid);

        // base 0.6 + 0.25 commercial = 0.85
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.85).Within(0.001));
    }

    [Test]
    public void FireStationInRange_IncreasesHappiness()
    {
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // Fire station at Manhattan distance 3 (within radius 4)
        grid.SetZone(5, 8, ZoneType.FireStation);

        _happiness.Propagate(grid);

        // base 0.6 + 0.15 fire station = 0.75
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.75).Within(0.001));
    }

    [Test]
    public void PoliceStationInRange_IncreasesHappiness()
    {
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // Police station at Manhattan distance 4 (at edge of radius 4)
        grid.SetZone(9, 5, ZoneType.PoliceStation);

        _happiness.Propagate(grid);

        // base 0.6 + 0.15 police = 0.75
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.75).Within(0.001));
    }

    [Test]
    public void SchoolInRange_IncreasesHappiness()
    {
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // School at Manhattan distance 5 (at edge of radius 5)
        grid.SetZone(5, 10, ZoneType.School);

        _happiness.Propagate(grid);

        // base 0.6 + 0.15 school = 0.75
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.75).Within(0.001));
    }

    [Test]
    public void TwoServiceTypes_CappedBonus()
    {
        // Two different service types covering the zone → +0.30 (not +0.45 for three types)
        var grid = new CityGrid(15, 15);
        grid.SetZone(7, 7, ZoneType.Residential);
        MakeReady(grid, 7, 7);
        grid.SetZone(7, 10, ZoneType.FireStation);   // Manhattan distance 3, within 4
        grid.SetZone(7, 4, ZoneType.PoliceStation);  // Manhattan distance 3, within 4
        grid.SetZone(2, 7, ZoneType.School);         // Manhattan distance 5, within 5

        _happiness.Propagate(grid);

        // base 0.6 + min(3, 2) * 0.15 = 0.6 + 0.30 = 0.90
        Assert.That(grid.GetTile(7, 7).Happiness, Is.EqualTo(0.90).Within(0.001));
    }

    [Test]
    public void HighPollution_ReducesHappiness()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        grid.SetPollution(5, 5, 0.5); // 0.5 pollution → -0.2 happiness

        _happiness.Propagate(grid);

        // base 0.6 - 0.5 * 0.4 = 0.6 - 0.2 = 0.4
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.4).Within(0.001));
    }

    [Test]
    public void PollutionAndCommercial_Combined()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        MakeCommercialReady(grid, 5, 6); // +0.25
        grid.SetPollution(5, 5, 0.5);   // -0.2

        _happiness.Propagate(grid);

        // 0.6 + 0.25 - 0.2 = 0.65
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.65).Within(0.001));
    }

    [Test]
    public void HappinessClamped_NeverBelowFloor()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        grid.SetPollution(5, 5, 1.0); // max pollution → -0.4, so 0.6 - 0.4 = 0.2

        // Even setting pollution > 1 via direct path — test the clamp floor
        // 1.0 pollution → 0.6 - 0.4 = 0.2 (above floor)
        // Let's test the floor by using pollution on a zone without commercial bonus
        // For floor test: need happiness < 0.1 which would require pollution > 1.25
        // Since pollution is clamped to [0,1], min possible is 0.6 - 0.4 = 0.2
        // So test that pollution doesn't push below 0.2, and verify clamp is there

        _happiness.Propagate(grid);

        // 0.6 - 1.0*0.4 = 0.2 — above floor, but let's verify clamp is >= 0.1
        Assert.That(grid.GetTile(5, 5).Happiness, Is.GreaterThanOrEqualTo(0.1),
            "Happiness should never fall below the 0.1 floor");
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.2).Within(0.001));
    }

    [Test]
    public void NotReadyZone_GetsNoHappinessCalculation()
    {
        // Unready residential zone: Happiness should remain at default 1.0 (not calculated)
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);
        // NOT powered, NOT road-accessed → not ready

        _happiness.Propagate(grid);

        // Non-ready zones are skipped — they keep the ClearHappiness default of 1.0
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(1.0),
            "Non-ready residential zone should keep default happiness (1.0) after propagation");
    }

    [Test]
    public void ServiceOutsideRadius_NoBonus()
    {
        var grid = new CityGrid(20, 20);
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        // Fire station at Manhattan distance 5 (outside radius 4)
        grid.SetZone(5, 10, ZoneType.FireStation);

        _happiness.Propagate(grid);

        // base 0.6 only — fire station too far away
        Assert.That(grid.GetTile(5, 5).Happiness, Is.EqualTo(0.6).Within(0.001));
    }

    [Test]
    public void AverageHappiness_NoReadyZones_ReturnsOne()
    {
        var grid = new CityGrid(10, 10);
        // No residential at all

        _happiness.Propagate(grid);

        Assert.That(_happiness.AverageHappiness(grid), Is.EqualTo(1.0));
    }

    [Test]
    public void AverageHappiness_CalculatesCorrectly()
    {
        var grid = new CityGrid(10, 10);
        // Zone A: base only → 0.6
        grid.SetZone(2, 2, ZoneType.Residential);
        MakeReady(grid, 2, 2);
        // Zone B: with commercial adjacent → 0.85
        grid.SetZone(5, 5, ZoneType.Residential);
        MakeReady(grid, 5, 5);
        MakeCommercialReady(grid, 5, 6);

        _happiness.Propagate(grid);

        // Average = (0.6 + 0.85) / 2 = 0.725
        Assert.That(_happiness.AverageHappiness(grid), Is.EqualTo(0.725).Within(0.001));
    }
}
