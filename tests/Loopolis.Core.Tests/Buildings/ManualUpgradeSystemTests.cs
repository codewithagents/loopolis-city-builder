using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Buildings;

[TestFixture]
public class ManualUpgradeSystemTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a minimal engine for integration tests that need full system wiring.
    /// </summary>
    private static SimulationEngine MakeEngine(CityGrid grid, double initialBalance = 10_000)
    {
        var budget = new BudgetSystem(initialBalance);
        var pop    = new PopulationSystem();
        var power  = new PowerNetwork();
        var roads  = new RoadNetwork();
        var demand = new DemandSystem();
        return new SimulationEngine(grid, budget, pop, power, roads, demand);
    }

    /// <summary>
    /// Place a powered, road-accessible 1×1 residential building at (x, y).
    /// Returns the building ID.
    /// </summary>
    private static string PlacePoweredHouse(CityGrid grid, int x, int y)
    {
        grid.SetZone(x, y, ZoneType.Residential);
        grid.SetPower(x, y, true);
        grid.SetRoadAccess(x, y, true);

        var id = Guid.NewGuid().ToString("N")[..8];
        var building = new Building(id, "res_house_1x1", ZoneType.Residential, x, y, 1, 1);
        grid.Buildings[id] = building;
        grid.SetBuildingId(x, y, id);
        return id;
    }

    // ── Test 1: GetUpgradeCost returns correct value for a known type ─────────

    [Test]
    public void GetUpgradeCost_ResHouse_Returns600()
    {
        var cost = ManualUpgradeSystem.GetUpgradeCost("res_house_1x1");
        Assert.That(cost, Is.EqualTo(600));
    }

    // ── Test 2: GetUpgradeCost returns null for already-max-tier building ─────

    [Test]
    public void GetUpgradeCost_ResHighrise_ReturnsNull()
    {
        var cost = ManualUpgradeSystem.GetUpgradeCost("res_highrise_6x6");
        Assert.That(cost, Is.Null,
            "res_highrise_6x6 is the max residential tier — no further upgrade possible");
    }

    // ── Test 3: GetUpgradeCost returns null for ind_complex_4x4 ──────────────

    [Test]
    public void GetUpgradeCost_IndComplex_ReturnsNull()
    {
        var cost = ManualUpgradeSystem.GetUpgradeCost("ind_complex_4x4");
        Assert.That(cost, Is.Null,
            "ind_complex_4x4 is the max industrial tier — no further upgrade possible");
    }

    // ── Test 4: TryUpgrade fails when no building at location ─────────────────

    [Test]
    public void TryUpgrade_NoBuildingAtLocation_Fails()
    {
        var grid     = new CityGrid(10, 10);
        var budget   = new BudgetSystem(10_000);
        var milestones = new MilestoneSystem();

        grid.SetZone(5, 5, ZoneType.Residential);  // zoned but no building placed

        var (success, reason, newTypeId) = ManualUpgradeSystem.TryUpgrade(grid, 5, 5, budget, milestones);

        Assert.That(success, Is.False);
        Assert.That(reason, Is.Not.Null.And.Not.Empty);
        Assert.That(newTypeId, Is.Null);
    }

    // ── Test 5: TryUpgrade fails when balance < cost ──────────────────────────

    [Test]
    public void TryUpgrade_InsufficientFunds_FailsWithFundsReason()
    {
        var grid       = new CityGrid(10, 10);
        var budget     = new BudgetSystem(initialBalance: 100);   // cost is 600
        var milestones = new MilestoneSystem();

        // Set up a 2×2 block so the 2×2 upgrade can fit
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(4 + dx, 4 + dy, ZoneType.Residential);
            grid.SetPower(4 + dx, 4 + dy, true);
            grid.SetRoadAccess(4 + dx, 4 + dy, true);
        }
        PlacePoweredHouse(grid, 4, 4);  // place house at anchor, overwrite zone/building

        var (success, reason, _) = ManualUpgradeSystem.TryUpgrade(grid, 4, 4, budget, milestones);

        Assert.That(success, Is.False);
        Assert.That(reason, Does.Contain("funds").IgnoreCase);
    }

    // ── Test 6: TryUpgrade on a max-tier building returns "cannot be upgraded" ─

    [Test]
    public void TryUpgrade_MaxTierBuilding_FailsWithNoUpgradePath()
    {
        var grid     = new CityGrid(15, 15);
        var budget   = new BudgetSystem(100_000);
        var milestones = new MilestoneSystem();

        // Place a 6×6 highrise manually
        for (var dx = 0; dx < 6; dx++)
        for (var dy = 0; dy < 6; dy++)
        {
            grid.SetZone(2 + dx, 2 + dy, ZoneType.Residential);
            grid.SetPower(2 + dx, 2 + dy, true);
            grid.SetRoadAccess(2 + dx, 2 + dy, true);
        }
        var hiId = Guid.NewGuid().ToString("N")[..8];
        var highrise = new Building(hiId, "res_highrise_6x6", ZoneType.Residential, 2, 2, 6, 6);
        grid.Buildings[hiId] = highrise;
        foreach (var (tx, ty) in highrise.Tiles()) grid.SetBuildingId(tx, ty, hiId);

        var (success, reason, _) = ManualUpgradeSystem.TryUpgrade(grid, 2, 2, budget, milestones);

        Assert.That(success, Is.False);
        Assert.That(reason, Does.Contain("cannot be upgraded").IgnoreCase);
    }

    // ── Test 7: Successful upgrade — budget is reduced by upgrade cost ─────────

    [Test]
    public void TryUpgrade_Success_ReducesBudgetByCost()
    {
        const double initialBalance = 5_000;
        const int upgradeCost = 600;  // res_house_1x1 → next tier

        var grid     = new CityGrid(10, 10);
        var budget   = new BudgetSystem(initialBalance);
        var milestones = new MilestoneSystem();

        // Place 2×2 block of powered residential so the 2×2 townhouse can fit
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(3 + dx, 3 + dy, ZoneType.Residential);
            grid.SetPower(3 + dx, 3 + dy, true);
            grid.SetRoadAccess(3 + dx, 3 + dy, true);
        }
        // Place the 1×1 house at the anchor tile (3,3)
        var id = Guid.NewGuid().ToString("N")[..8];
        var house = new Building(id, "res_house_1x1", ZoneType.Residential, 3, 3, 1, 1);
        grid.Buildings[id] = house;
        grid.SetBuildingId(3, 3, id);

        var (success, _, _) = ManualUpgradeSystem.TryUpgrade(grid, 3, 3, budget, milestones);

        Assert.That(success, Is.True, "Upgrade should succeed with sufficient funds and powered 2×2 block");
        Assert.That(budget.Balance, Is.EqualTo(initialBalance - upgradeCost).Within(0.01));
    }

    // ── Test 8: Successful upgrade — old building gone, new building exists ────

    [Test]
    public void TryUpgrade_Success_OldBuildingRemovedNewBuildingCreated()
    {
        var grid     = new CityGrid(10, 10);
        var budget   = new BudgetSystem(10_000);
        var milestones = new MilestoneSystem();

        // Place 2×2 powered residential block
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(3 + dx, 3 + dy, ZoneType.Residential);
            grid.SetPower(3 + dx, 3 + dy, true);
            grid.SetRoadAccess(3 + dx, 3 + dy, true);
        }
        var oldId = Guid.NewGuid().ToString("N")[..8];
        var house = new Building(oldId, "res_house_1x1", ZoneType.Residential, 3, 3, 1, 1);
        grid.Buildings[oldId] = house;
        grid.SetBuildingId(3, 3, oldId);

        var (success, _, newTypeId) = ManualUpgradeSystem.TryUpgrade(grid, 3, 3, budget, milestones);

        Assert.That(success, Is.True);
        Assert.That(grid.Buildings.ContainsKey(oldId), Is.False, "Old 1×1 building should be removed");
        Assert.That(newTypeId, Is.Not.Null.And.Not.EqualTo("res_house_1x1"),
            "New building should be a larger type");
        Assert.That(grid.Buildings.Values.Any(b => b.TypeId == newTypeId), Is.True,
            "New building should exist in grid.Buildings");
    }

    // ── Test 9: res_townhouse_2x2 upgrade requires City milestone conditions ───
    //   This test verifies that a townhouse → apartment upgrade fails if the milestone
    //   state is Active (no City reached yet — apartment requires GameState.City).

    [Test]
    public void TryUpgrade_TownhouseToApartment_RequiresCityMilestoneOrBlocks()
    {
        var grid     = new CityGrid(15, 15);
        var budget   = new BudgetSystem(50_000);
        var milestones = new MilestoneSystem();  // CurrentState = Active (no City milestone)

        // Place a 4×4 powered residential block so apartment can fit
        for (var dx = 0; dx < 4; dx++)
        for (var dy = 0; dy < 4; dy++)
        {
            grid.SetZone(2 + dx, 2 + dy, ZoneType.Residential);
            grid.SetPower(2 + dx, 2 + dy, true);
            grid.SetRoadAccess(2 + dx, 2 + dy, true);
        }

        // Place services close enough for apartment condition (within Manhattan 10)
        grid.SetZone(1, 2, ZoneType.School);
        grid.SetZone(1, 3, ZoneType.PoliceStation);
        grid.SetZone(1, 4, ZoneType.FireStation);

        // Place a 2×2 townhouse at anchor (2,2)
        var twId = Guid.NewGuid().ToString("N")[..8];
        var townhouse = new Building(twId, "res_townhouse_2x2", ZoneType.Residential, 2, 2, 2, 2);
        grid.Buildings[twId] = townhouse;
        foreach (var (tx, ty) in townhouse.Tiles()) grid.SetBuildingId(tx, ty, twId);

        // Without City milestone — should fail
        var (success, _, _) = ManualUpgradeSystem.TryUpgrade(grid, 2, 2, budget, milestones);
        Assert.That(success, Is.False,
            "Townhouse → apartment requires City milestone (GameState.City) — should fail without it");
    }

    // ── Test 10: SimulationEngine.ManualUpgrade returns success result with cost ─

    [Test]
    public void SimulationEngine_ManualUpgrade_ReturnsSuccessWithCost()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid, initialBalance: 10_000);

        // Place 2×2 powered residential block
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            grid.SetZone(3 + dx, 3 + dy, ZoneType.Residential);
            grid.SetPower(3 + dx, 3 + dy, true);
            grid.SetRoadAccess(3 + dx, 3 + dy, true);
        }
        var houseId = Guid.NewGuid().ToString("N")[..8];
        var house = new Building(houseId, "res_house_1x1", ZoneType.Residential, 3, 3, 1, 1);
        grid.Buildings[houseId] = house;
        grid.SetBuildingId(3, 3, houseId);

        var result = engine.ManualUpgrade(3, 3);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Cost, Is.EqualTo(600));
        Assert.That(result.NewBuildingTypeId, Is.Not.Null.And.Not.EqualTo("res_house_1x1"));
    }

    // ── Test 11: SimulationEngine.ManualUpgrade returns failure when no building ─

    [Test]
    public void SimulationEngine_ManualUpgrade_NoBuilding_ReturnsFalse()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);

        var result = engine.ManualUpgrade(5, 5);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Reason, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Cost, Is.Null);
    }
}
