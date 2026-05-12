using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class RoadTrafficSystemTests
{
    private RoadTrafficSystem _traffic = null!;

    [SetUp]
    public void SetUp() => _traffic = new RoadTrafficSystem();

    // ── TrafficLoad calculation ──────────────────────────────────────────────

    [Test]
    public void RoadWithNoNeighbours_HasZeroTrafficLoad()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);

        _traffic.Propagate(grid);

        Assert.That(_traffic.GetTrafficLoad(5, 5), Is.EqualTo(0));
    }

    [Test]
    public void RoadWith4AdjacentZones_NotOverloaded()
    {
        // 4 zone tiles directly adjacent (Chebyshev-1) — below Road threshold of 6
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(5, 4, ZoneType.Residential);
        grid.SetZone(5, 6, ZoneType.Residential);
        grid.SetZone(4, 5, ZoneType.Commercial);
        grid.SetZone(6, 5, ZoneType.Industrial);

        _traffic.Propagate(grid);

        Assert.That(_traffic.GetTrafficLoad(5, 5), Is.EqualTo(4));
        Assert.That(_traffic.IsOverloaded(5, 5), Is.False);
    }

    [Test]
    public void RoadWith7AdjacentZones_IsOverloaded()
    {
        // Pack 7 zone tiles within Chebyshev distance 1 of road at (5,5) — exceeds threshold of 6
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Road);

        // All 8 Chebyshev-1 neighbours; place 7 as zones (one left as Road)
        var positions = new[] {
            (4, 4), (5, 4), (6, 4),
            (4, 5), (6, 5),
            (4, 6), (5, 6)
        };
        foreach (var (x, y) in positions)
            grid.SetZone(x, y, ZoneType.Residential);

        _traffic.Propagate(grid);

        Assert.That(_traffic.GetTrafficLoad(5, 5), Is.EqualTo(7));
        Assert.That(_traffic.IsOverloaded(5, 5), Is.True);
    }

    [Test]
    public void AvenueWith8AdjacentZones_IsNotOverloaded()
    {
        // Avenue threshold is 10 — filling all 8 Chebyshev-1 neighbours (max possible) still does not overload it
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Avenue);

        // All 8 Chebyshev-1 neighbours as zones
        var positions = new[] {
            (4, 4), (5, 4), (6, 4),
            (4, 5), (6, 5),
            (4, 6), (5, 6), (6, 6)
        };
        foreach (var (x, y) in positions)
            grid.SetZone(x, y, ZoneType.Residential);

        _traffic.Propagate(grid);

        Assert.That(_traffic.GetTrafficLoad(5, 5), Is.EqualTo(8));
        Assert.That(_traffic.IsOverloaded(5, 5), Is.False);
    }

    [Test]
    public void Avenue_NeverOverloads_MaxChebyshev1Load()
    {
        // Avenue threshold (10) exceeds the maximum possible Chebyshev-1 load (8),
        // so an avenue can never be marked overloaded no matter how dense the surrounding city is.
        var grid = new CityGrid(15, 15);
        grid.SetZone(7, 7, ZoneType.Avenue);

        // Fill all 8 directly-adjacent tiles with zones
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            grid.SetZone(7 + dx, 7 + dy, ZoneType.Residential);
        }

        _traffic.Propagate(grid);

        Assert.That(_traffic.GetTrafficLoad(7, 7), Is.EqualTo(8));
        Assert.That(_traffic.IsOverloaded(7, 7), Is.False);
    }

    [Test]
    public void ChebyshevDistance1_CountsAllZonesInSquare()
    {
        // Fill the entire 3×3 square (excluding center road) with residential
        var grid = new CityGrid(15, 15);
        grid.SetZone(7, 7, ZoneType.Road);

        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            grid.SetZone(7 + dx, 7 + dy, ZoneType.Residential);
        }

        _traffic.Propagate(grid);

        // 3x3 = 9 tiles minus road = 8 zone tiles
        Assert.That(_traffic.GetTrafficLoad(7, 7), Is.EqualTo(8));
    }

    [Test]
    public void ZonesAtChebyshev2Distance_NotCounted()
    {
        // Zones that are 2 tiles away (Chebyshev-2 but not Chebyshev-1) must NOT be counted
        var grid = new CityGrid(15, 15);
        grid.SetZone(7, 7, ZoneType.Road);

        // Place zones at Chebyshev distance 2 only (not 1)
        grid.SetZone(7, 5, ZoneType.Residential); // 2 above
        grid.SetZone(7, 9, ZoneType.Residential); // 2 below
        grid.SetZone(5, 7, ZoneType.Residential); // 2 left
        grid.SetZone(9, 7, ZoneType.Residential); // 2 right

        _traffic.Propagate(grid);

        Assert.That(_traffic.GetTrafficLoad(7, 7), Is.EqualTo(0));
    }

    [Test]
    public void OnlyRCIZonesCounted_InfrastructureIgnored()
    {
        // Non-zone tiles (PowerLine, PowerPlant, etc.) should NOT contribute to traffic load
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(5, 4, ZoneType.PowerLine);
        grid.SetZone(5, 6, ZoneType.PowerPlant);
        grid.SetZone(4, 5, ZoneType.FireStation);
        grid.SetZone(6, 5, ZoneType.Road); // another road — not counted

        _traffic.Propagate(grid);

        Assert.That(_traffic.GetTrafficLoad(5, 5), Is.EqualTo(0));
    }

    // ── Tile state mutation ──────────────────────────────────────────────────

    [Test]
    public void Propagate_SetsTrafficLoadOnRoadTile()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(5, 4, ZoneType.Residential);
        grid.SetZone(5, 6, ZoneType.Commercial);

        _traffic.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).TrafficLoad, Is.EqualTo(2));
    }

    [Test]
    public void Propagate_SetsTrafficLoadOnAvenueTile()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Avenue);
        grid.SetZone(5, 4, ZoneType.Residential);

        _traffic.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).TrafficLoad, Is.EqualTo(1));
    }

    [Test]
    public void Propagate_ClearsPreviousTrafficLoad()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(5, 4, ZoneType.Residential);

        _traffic.Propagate(grid); // load = 1
        grid.SetZone(5, 4, ZoneType.Empty); // remove zone
        _traffic.Propagate(grid); // should clear

        Assert.That(grid.GetTile(5, 5).TrafficLoad, Is.EqualTo(0));
    }

    // ── Growth modifier ──────────────────────────────────────────────────────

    [Test]
    public void OverloadedRoad_GrowthMultiplierIsReduced()
    {
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Road);

        // Add 7 directly-adjacent zones (Chebyshev-1) to overload the road (threshold = 6)
        var positions = new[] {
            (4, 4), (5, 4), (6, 4),
            (4, 5), (6, 5),
            (4, 6), (5, 6)
        };
        foreach (var (x, y) in positions)
            grid.SetZone(x, y, ZoneType.Residential);

        _traffic.Propagate(grid);

        // An adjacent residential tile should get a reduced growth multiplier
        Assert.That(_traffic.GetGrowthMultiplier(grid, 4, 5), Is.EqualTo(0.7).Within(0.001));
    }

    [Test]
    public void NonOverloadedRoad_GrowthMultiplierIsOne()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(4, 5, ZoneType.Residential);

        _traffic.Propagate(grid);

        // One zone → traffic load 1 → not overloaded → multiplier = 1.0
        Assert.That(_traffic.GetGrowthMultiplier(grid, 4, 5), Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void OverloadedRoad_HappinessModifierIsNegative()
    {
        var grid = new CityGrid(15, 15);
        grid.SetZone(5, 5, ZoneType.Road);

        // 7 directly-adjacent zones (Chebyshev-1) — overloads road (threshold = 6)
        var positions = new[] {
            (4, 4), (5, 4), (6, 4),
            (4, 5), (6, 5),
            (4, 6), (5, 6)
        };
        foreach (var (x, y) in positions)
            grid.SetZone(x, y, ZoneType.Residential);

        _traffic.Propagate(grid);

        // Adjacent residential at (4,5) should get -0.10 happiness penalty
        Assert.That(_traffic.GetHappinessModifier(grid, 4, 5), Is.EqualTo(-0.10).Within(0.001));
    }

    [Test]
    public void NonOverloadedRoad_HappinessModifierIsZero()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(4, 5, ZoneType.Residential);

        _traffic.Propagate(grid);

        Assert.That(_traffic.GetHappinessModifier(grid, 4, 5), Is.EqualTo(0.0).Within(0.001));
    }

    // ── Avenue as Road substitute ────────────────────────────────────────────

    [Test]
    public void Avenue_ConductsPower()
    {
        // Avenue should behave like Road for power network purposes
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant);
        grid.SetZone(5, 6, ZoneType.Avenue);
        grid.SetZone(5, 7, ZoneType.Residential);

        var powerNetwork = new PowerNetwork();
        powerNetwork.Propagate(grid);

        Assert.That(grid.GetTile(5, 6).HasPower, Is.True);
        Assert.That(grid.GetTile(5, 7).HasPower, Is.True);
    }

    [Test]
    public void Avenue_GrantsRoadAccessToAdjacentZones()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Avenue);
        grid.SetZone(6, 5, ZoneType.Residential);

        var roadNetwork = new RoadNetwork();
        roadNetwork.Propagate(grid);

        Assert.That(grid.GetTile(6, 5).HasRoadAccess, Is.True);
    }

    // ── Summary stats ────────────────────────────────────────────────────────

    [Test]
    public void OverloadedRoadCount_CountsOnlyOverloadedTiles()
    {
        var grid = new CityGrid(15, 15);
        // Overloaded road at (5,5) — 7 directly-adjacent zones exceeds threshold of 6
        grid.SetZone(5, 5, ZoneType.Road);
        var positions = new[] {
            (4, 4), (5, 4), (6, 4),
            (4, 5), (6, 5),
            (4, 6), (5, 6)
        };
        foreach (var (x, y) in positions)
            grid.SetZone(x, y, ZoneType.Residential);

        // Non-overloaded road at (12, 12)
        grid.SetZone(12, 12, ZoneType.Road);
        grid.SetZone(12, 11, ZoneType.Residential);

        _traffic.Propagate(grid);

        Assert.That(_traffic.OverloadedRoadCount, Is.EqualTo(1));
    }

    [Test]
    public void AvgTrafficLoad_CalculatedAcrossAllRoadAndAvenueTiles()
    {
        var grid = new CityGrid(10, 10);
        // Road at (5,5) with traffic load 2
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(5, 4, ZoneType.Residential);
        grid.SetZone(5, 6, ZoneType.Commercial);

        // Avenue at (8,5) with traffic load 0
        grid.SetZone(8, 5, ZoneType.Avenue);

        _traffic.Propagate(grid);

        // avg = (2 + 0) / 2 = 1.0
        Assert.That(_traffic.AvgTrafficLoad, Is.EqualTo(1.0).Within(0.001));
    }
}
