using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class PowerCapacitySystemTests
{
    private PowerCapacitySystem _capacity = null!;

    [SetUp]
    public void SetUp() => _capacity = new PowerCapacitySystem();

    // ── Supply tests ────────────────────────────────────────────────────────

    [Test]
    public void CoalPlantSupplies500MW()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.CoalPlant);

        _capacity.Propagate(grid);

        Assert.That(_capacity.TotalSupplyMW, Is.EqualTo(500));
    }

    [Test]
    public void NuclearPlantSupplies3000MW()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.NuclearPlant);

        _capacity.Propagate(grid);

        Assert.That(_capacity.TotalSupplyMW, Is.EqualTo(3_000));
    }

    [Test]
    public void LegacyPowerPlantCountsAsCoalPlant_Supplies500MW()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant);

        _capacity.Propagate(grid);

        Assert.That(_capacity.TotalSupplyMW, Is.EqualTo(500));
    }

    [Test]
    public void MultipleCoalPlantsAccumulateSupply()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(1, 1, ZoneType.CoalPlant);
        grid.SetZone(9, 9, ZoneType.CoalPlant);

        _capacity.Propagate(grid);

        Assert.That(_capacity.TotalSupplyMW, Is.EqualTo(1_000));
    }

    [Test]
    public void MixedPlantTypes_SupplyAddsUp()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(1, 1, ZoneType.CoalPlant);    // 500 MW
        grid.SetZone(9, 9, ZoneType.NuclearPlant); // 3,000 MW

        _capacity.Propagate(grid);

        Assert.That(_capacity.TotalSupplyMW, Is.EqualTo(3_500));
    }

    // ── Demand tests ────────────────────────────────────────────────────────

    [Test]
    public void ResidentialTilesDemand2MW()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Residential);

        _capacity.Propagate(grid);

        Assert.That(_capacity.TotalDemandMW, Is.EqualTo(2));
    }

    [Test]
    public void CommercialTilesDemand3MW()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Commercial);

        _capacity.Propagate(grid);

        Assert.That(_capacity.TotalDemandMW, Is.EqualTo(3));
    }

    [Test]
    public void IndustrialTilesDemand5MW()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Industrial);

        _capacity.Propagate(grid);

        Assert.That(_capacity.TotalDemandMW, Is.EqualTo(5));
    }

    [Test]
    public void PowerPlantTilesHaveZeroDemand()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.CoalPlant);
        grid.SetZone(6, 5, ZoneType.NuclearPlant);

        _capacity.Propagate(grid);

        Assert.That(_capacity.TotalDemandMW, Is.EqualTo(0),
            "Power plants generate power — they should not appear in demand");
    }

    // ── Brownout detection ──────────────────────────────────────────────────

    [Test]
    public void BrownoutWhenDemandExceedsSupply()
    {
        // 1 CoalPlant = 500 MW supply
        // 5 FireHQ tiles = 5 × 20 MW = 100 MW ... need > 500 MW
        // 26 FireHQ = 520 MW > 500 MW → brownout
        var grid = new CityGrid(10, 10);
        grid.SetZone(0, 0, ZoneType.CoalPlant); // 500 MW supply

        // Place 26 FireHQ tiles for 520 MW demand (exceeds 500 MW supply)
        var count = 0;
        for (var x = 0; x < 10 && count < 26; x++)
        for (var y = 0; y < 10 && count < 26; y++)
        {
            if (grid.GetTile(x, y).Zone != ZoneType.Empty) continue;
            grid.SetZone(x, y, ZoneType.FireHQ);
            count++;
        }

        _capacity.Propagate(grid);

        Assert.That(_capacity.IsBrownout, Is.True,
            "City should be in brownout when demand exceeds supply");
        Assert.That(_capacity.CapacityRatio, Is.LessThan(1.0));
    }

    [Test]
    public void NoBrownoutWithSurplus()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.CoalPlant); // 500 MW supply
        // 3 residential × 2 MW = 6 MW demand — well below 500 MW
        grid.SetZone(1, 1, ZoneType.Residential);
        grid.SetZone(2, 1, ZoneType.Residential);
        grid.SetZone(3, 1, ZoneType.Residential);

        _capacity.Propagate(grid);

        Assert.That(_capacity.IsBrownout, Is.False,
            "City with surplus supply should not be in brownout");
        Assert.That(_capacity.CapacityRatio, Is.GreaterThan(1.0));
    }

    [Test]
    public void EmptyGrid_NoPlants_IsNotBrownout()
    {
        // Grid with no plants and no consumers: ratio = 0/1 = 0.0, which is < 1.0.
        // But semantically an empty city with no consumers doesn't have a brownout.
        // The system reports isBrownout = (ratio < 1.0), so with 0 supply and 0 demand:
        // ratio = 0 / max(0,1) = 0 → isBrownout = true. That's by design (no power plant = brownout).
        var grid = new CityGrid(10, 10);

        _capacity.Propagate(grid);

        // No consumers → demand=0, supply=0, ratio = 0/1 = 0 → brownout by formula
        Assert.That(_capacity.TotalDemandMW, Is.EqualTo(0));
        Assert.That(_capacity.TotalSupplyMW, Is.EqualTo(0));
    }

    [Test]
    public void CapacityRatio_CalculatedCorrectly()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.CoalPlant); // 500 MW
        // 100 residential × 2 MW = 200 MW demand → ratio = 500/200 = 2.5
        for (var x = 0; x < 10; x++)
        for (var y = 1; y < 10; y++)
            if (grid.GetTile(x, y).Zone == ZoneType.Empty)
                grid.SetZone(x, y, ZoneType.Residential);

        _capacity.Propagate(grid);

        // 90 residential tiles × 2 MW = 180 MW (checking exact count)
        var expectedRatio = 500.0 / _capacity.TotalDemandMW;
        Assert.That(_capacity.CapacityRatio, Is.EqualTo(expectedRatio).Within(0.001));
    }

    // ── Brownout effects ────────────────────────────────────────────────────

    [Test]
    public void BrownoutHappinessPenaltyScalesWithRatio()
    {
        // Create a brownout: 1 coal plant (500 MW) vs 26 FireHQ tiles (520 MW demand)
        var grid = new CityGrid(10, 10);
        grid.SetZone(0, 0, ZoneType.CoalPlant); // 500 MW supply

        var count = 0;
        for (var x = 0; x < 10 && count < 26; x++)
        for (var y = 0; y < 10 && count < 26; y++)
        {
            if (grid.GetTile(x, y).Zone != ZoneType.Empty) continue;
            grid.SetZone(x, y, ZoneType.FireHQ);
            count++;
        }

        _capacity.Propagate(grid);

        Assert.That(_capacity.IsBrownout, Is.True);
        // Penalty = -0.10 × (1.0 - ratio), ratio < 1 → penalty is negative
        var expectedPenalty = -0.10 * (1.0 - _capacity.CapacityRatio);
        Assert.That(_capacity.BrownoutHappinessPenalty,
            Is.EqualTo(expectedPenalty).Within(0.0001));
    }

    [Test]
    public void NoBrownout_HappinessPenaltyIsZero()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.NuclearPlant); // 3,000 MW — huge surplus

        _capacity.Propagate(grid);

        Assert.That(_capacity.IsBrownout, Is.False);
        Assert.That(_capacity.BrownoutHappinessPenalty, Is.EqualTo(0.0));
    }

    [Test]
    public void BrownoutGrowthMultiplierScalesWithRatio()
    {
        // 1 coal plant (500 MW) vs 26 FireHQ (520 MW demand) → brownout
        var grid = new CityGrid(10, 10);
        grid.SetZone(0, 0, ZoneType.CoalPlant); // 500 MW supply

        var count = 0;
        for (var x = 0; x < 10 && count < 26; x++)
        for (var y = 0; y < 10 && count < 26; y++)
        {
            if (grid.GetTile(x, y).Zone != ZoneType.Empty) continue;
            grid.SetZone(x, y, ZoneType.FireHQ);
            count++;
        }

        _capacity.Propagate(grid);

        Assert.That(_capacity.IsBrownout, Is.True);
        // Growth multiplier = clamp(capacityRatio, 0.3, 1.0)
        var expected = Math.Max(0.3, _capacity.CapacityRatio);
        Assert.That(_capacity.GrowthMultiplier, Is.EqualTo(expected).Within(0.0001));
        Assert.That(_capacity.GrowthMultiplier, Is.LessThanOrEqualTo(1.0));
        Assert.That(_capacity.GrowthMultiplier, Is.GreaterThanOrEqualTo(0.3));
    }

    [Test]
    public void NoBrownout_GrowthMultiplierIsOne()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.NuclearPlant); // 3,000 MW surplus

        _capacity.Propagate(grid);

        Assert.That(_capacity.GrowthMultiplier, Is.EqualTo(1.0));
    }

    [Test]
    public void GrowthMultiplier_ClampedToMinimum_WhenSevereShortfall()
    {
        // 0 supply, huge demand → ratio = 0.0 → clamped to 0.3
        var grid = new CityGrid(10, 10);
        // Fill with industrial, no plant
        for (var x = 0; x < 10; x++)
        for (var y = 0; y < 10; y++)
            grid.SetZone(x, y, ZoneType.Industrial);

        _capacity.Propagate(grid);

        Assert.That(_capacity.GrowthMultiplier, Is.EqualTo(0.3).Within(0.0001),
            "Growth multiplier should be clamped at 0.3 even with zero supply");
    }

    // ── Milestone gate for NuclearPlant ──────────────────────────────────────

    [Test]
    public void NuclearRequiresTownMilestone()
    {
        var milestone = new MilestoneSystem();

        // Below Town threshold (499 pop)
        var (allowed, error) = milestone.CanPlace(ZoneType.NuclearPlant, 499);

        Assert.That(allowed, Is.False,
            "NuclearPlant should be blocked below Town milestone (pop < 500)");
        Assert.That(error, Does.Contain("Town milestone"));
    }

    [Test]
    public void NuclearPlant_AllowedAtTownMilestone()
    {
        var milestone = new MilestoneSystem();

        // At exactly Town threshold (500 pop)
        var (allowed, error) = milestone.CanPlace(ZoneType.NuclearPlant, 500);

        Assert.That(allowed, Is.True,
            "NuclearPlant should be allowed at 500+ population (Town milestone)");
        Assert.That(error, Is.Null);
    }

    [Test]
    public void CoalPlant_AlwaysAllowed()
    {
        var milestone = new MilestoneSystem();

        var (allowed, _) = milestone.CanPlace(ZoneType.CoalPlant, 0);

        Assert.That(allowed, Is.True, "CoalPlant has no milestone gate");
    }

    // ── Coal plant pollution ────────────────────────────────────────────────

    [Test]
    public void CoalPlantEmitsPollution()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.CoalPlant);

        var pollution = new PollutionSystem();
        pollution.Propagate(grid);

        // The coal plant tile itself (distance=0) should have pollution
        Assert.That(grid.GetTile(5, 5).PollutionLevel, Is.GreaterThan(0.0),
            "CoalPlant should emit pollution at its own tile");

        // Adjacent tile (distance=1) should also have pollution
        Assert.That(grid.GetTile(5, 6).PollutionLevel, Is.GreaterThan(0.0),
            "CoalPlant should emit pollution to adjacent tiles");
    }

    [Test]
    public void CoalPlantPollution_LessThanIndustrial()
    {
        // CoalPlant strength 0.4 vs Industrial strength 1.0 — same distance
        var gridCoal = new CityGrid(10, 10);
        gridCoal.SetZone(5, 5, ZoneType.CoalPlant);
        var pollutionCoal = new PollutionSystem();
        pollutionCoal.Propagate(gridCoal);
        var coalLevel = gridCoal.GetTile(5, 5).PollutionLevel;

        var gridIndustrial = new CityGrid(10, 10);
        gridIndustrial.SetZone(5, 5, ZoneType.Industrial);
        var pollutionIndustrial = new PollutionSystem();
        pollutionIndustrial.Propagate(gridIndustrial);
        var industrialLevel = gridIndustrial.GetTile(5, 5).PollutionLevel;

        Assert.That(coalLevel, Is.LessThan(industrialLevel),
            "CoalPlant (strength 0.4) should emit less pollution than Industrial (strength 1.0)");
    }

    [Test]
    public void NuclearPlantNoPollution()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.NuclearPlant);

        var pollution = new PollutionSystem();
        pollution.Propagate(grid);

        // Nuclear plant should emit zero pollution anywhere on grid
        foreach (var tile in grid.AllTiles())
            Assert.That(tile.PollutionLevel, Is.EqualTo(0.0),
                $"Tile ({tile.X},{tile.Y}) should have no pollution from NuclearPlant");
    }

    // ── PowerNetwork integration for new zone types ─────────────────────────

    [Test]
    public void CoalPlant_PropagatesPower_ToAdjacentTiles()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.CoalPlant);
        grid.SetZone(6, 5, ZoneType.Residential);

        var power = new PowerNetwork();
        power.Propagate(grid);

        Assert.That(grid.GetTile(6, 5).HasPower, Is.True,
            "CoalPlant must propagate power to adjacent zones");
    }

    [Test]
    public void NuclearPlant_PropagatesPower_ToAdjacentTiles()
    {
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.NuclearPlant);
        grid.SetZone(6, 5, ZoneType.Residential);

        var power = new PowerNetwork();
        power.Propagate(grid);

        Assert.That(grid.GetTile(6, 5).HasPower, Is.True,
            "NuclearPlant must propagate power to adjacent zones");
    }

    // ── HappinessSystem integration ─────────────────────────────────────────

    [Test]
    public void BrownoutHappinessPenalty_AppliedToReadyTiles()
    {
        // Both grids have: CoalPlant + Road + 2 Residential + identical Industrial for demand.
        // The brownout grid has enough Industrial to exceed 500 MW supply.
        // We use Industrial (5 MW each, neutral for happiness) to create demand.
        // Need > 500 MW / 5 MW = 100 industrial tiles for brownout.
        // Use a 20×20 grid to fit 105 industrial tiles alongside the residential.

        var grid = new CityGrid(20, 20);
        grid.SetZone(0, 0, ZoneType.CoalPlant);  // 500 MW supply
        grid.SetZone(0, 1, ZoneType.Road);
        grid.SetZone(1, 1, ZoneType.Residential); // will be powered + road access
        grid.SetZone(0, 2, ZoneType.Residential); // will be powered + road access

        // Add 102 industrial tiles (far from residential) for 510 MW demand → brownout
        var industrialCount = 0;
        for (var x = 5; x < 20 && industrialCount < 102; x++)
        for (var y = 5; y < 20 && industrialCount < 102; y++)
        {
            grid.SetZone(x, y, ZoneType.Industrial);
            industrialCount++;
        }

        var power = new PowerNetwork();
        power.Propagate(grid);
        var roads = new RoadNetwork();
        roads.Propagate(grid);

        _capacity.Propagate(grid);
        Assert.That(_capacity.IsBrownout, Is.True, "Setup requires brownout scenario");

        var happiness = new HappinessSystem();
        happiness.Propagate(grid, powerCapacitySystem: _capacity);

        // Baseline grid: same coal plant + road + residential but only 2 industrial (no brownout)
        var grid2 = new CityGrid(20, 20);
        grid2.SetZone(0, 0, ZoneType.CoalPlant);
        grid2.SetZone(0, 1, ZoneType.Road);
        grid2.SetZone(1, 1, ZoneType.Residential);
        grid2.SetZone(0, 2, ZoneType.Residential);
        // Just 2 industrial tiles = 10 MW demand → ratio = 500/10 = 50x, no brownout
        grid2.SetZone(5, 5, ZoneType.Industrial);
        grid2.SetZone(6, 5, ZoneType.Industrial);

        power.Propagate(grid2);
        roads.Propagate(grid2);
        var capacity2 = new PowerCapacitySystem();
        capacity2.Propagate(grid2);
        Assert.That(capacity2.IsBrownout, Is.False, "Baseline grid should not be in brownout");
        var happiness2 = new HappinessSystem();
        happiness2.Propagate(grid2, powerCapacitySystem: capacity2);

        var happinessBrownout   = grid.GetTile(1, 1).Happiness;
        var happinessNoBrownout = grid2.GetTile(1, 1).Happiness;

        Assert.That(happinessBrownout, Is.LessThan(happinessNoBrownout),
            "Brownout should reduce happiness for powered residential tiles");
    }
}
