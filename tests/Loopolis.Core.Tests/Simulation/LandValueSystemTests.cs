using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class LandValueSystemTests
{
    private LandValueSystem _system = null!;

    [SetUp]
    public void SetUp() => _system = new LandValueSystem();

    // ── Terrain bonuses ──────────────────────────────────────────────────────

    [Test]
    public void HillTileHasHigherLandValue()
    {
        // Arrange: two isolated tiles — one Hill, one Flat, same conditions
        var grid = new CityGrid(10, 10);
        grid.SetTerrain(3, 3, TerrainType.Hill);
        // (6,6) stays Flat

        // Act
        _system.Propagate(grid);

        var hillValue = grid.GetTile(3, 3).LandValue;
        var flatValue = grid.GetTile(6, 6).LandValue;

        // Assert: Hill tile gets the +0.30 bonus
        Assert.That(hillValue, Is.GreaterThan(flatValue),
            "A Hill tile should have a higher land value than a Flat tile with otherwise equal conditions.");
        Assert.That(hillValue, Is.GreaterThanOrEqualTo(LandValueSystem.HillBonus).Within(0.001));
    }

    [Test]
    public void WaterAdjacentTileGetsWaterBonus()
    {
        // Arrange: tile at (5,5), Water tile at (5,7) which is Chebyshev distance 2 (within 3)
        var grid = new CityGrid(15, 15);
        grid.SetTerrain(5, 7, TerrainType.Water);

        // Act
        _system.Propagate(grid);

        var nearWaterValue = grid.GetTile(5, 5).LandValue;
        var farFromWaterValue = grid.GetTile(1, 1).LandValue; // far from water

        Assert.That(nearWaterValue, Is.GreaterThan(farFromWaterValue),
            "A tile near water should have higher land value.");
        Assert.That(nearWaterValue, Is.GreaterThanOrEqualTo(LandValueSystem.WaterAdjacentBonus).Within(0.001));
    }

    [Test]
    public void WaterTileItself_HasZeroLandValue()
    {
        // Arrange
        var grid = new CityGrid(10, 10);
        grid.SetTerrain(5, 5, TerrainType.Water);

        // Act
        _system.Propagate(grid);

        // Assert
        Assert.That(grid.GetTile(5, 5).LandValue, Is.EqualTo(0.0),
            "Water tiles should always have LandValue = 0.");
    }

    [Test]
    public void ForestAdjacentTileGetsForestBonus()
    {
        // Arrange: place 3 Forest tiles within Chebyshev distance 2 of (5,5)
        var grid = new CityGrid(15, 15);
        grid.SetTerrain(5, 4, TerrainType.Forest); // dy=1
        grid.SetTerrain(5, 3, TerrainType.Forest); // dy=2
        grid.SetTerrain(4, 4, TerrainType.Forest); // dx=-1, dy=1

        // Act
        _system.Propagate(grid);

        var forestAdjacentValue = grid.GetTile(5, 5).LandValue;
        var noForestValue       = grid.GetTile(12, 12).LandValue; // no forest nearby

        Assert.That(forestAdjacentValue, Is.GreaterThan(noForestValue),
            "A tile near forests should have higher land value.");
        // 3 forests × 0.08 = 0.24, but capped at 0.20
        Assert.That(forestAdjacentValue, Is.GreaterThanOrEqualTo(LandValueSystem.ForestBonusMax).Within(0.001));
    }

    [Test]
    public void ForestBonus_IsCappedAtMaxValue()
    {
        // Arrange: surround (5,5) with many forests
        var grid = new CityGrid(15, 15);
        for (var dx = -2; dx <= 2; dx++)
        for (var dy = -2; dy <= 2; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            grid.SetTerrain(5 + dx, 5 + dy, TerrainType.Forest);
        }

        // Act
        _system.Propagate(grid);

        // Forest contribution must not exceed the cap
        // The tile itself is Flat here so max forest contribution = ForestBonusMax
        var value = grid.GetTile(5, 5).LandValue;
        // Additional bonuses (LowPollution etc.) may push it above ForestBonusMax, so we check forest contribution
        // by using a tile surrounded only by forests (no other bonuses apply since no power/happiness set)
        // LowPollutionBonus always applies (pollution default 0), so expected = ForestBonusMax + LowPollutionBonus
        var expectedMin = LandValueSystem.ForestBonusMax + LandValueSystem.LowPollutionBonus;
        Assert.That(value, Is.GreaterThanOrEqualTo(expectedMin).Within(0.001));

        // Ensure the forest component itself is capped (not 24 forests × 0.08 = 1.92)
        // Value should not exceed 1.0 (cap)
        Assert.That(value, Is.LessThanOrEqualTo(1.0), "LandValue must be capped at 1.0.");
    }

    [Test]
    public void LowPollutionTileGetsBonus()
    {
        var grid = new CityGrid(10, 10);
        // Default pollution is 0.0 (< 0.1) so low-pollution bonus should apply
        _system.Propagate(grid);

        var value = grid.GetTile(5, 5).LandValue;
        Assert.That(value, Is.GreaterThanOrEqualTo(LandValueSystem.LowPollutionBonus).Within(0.001));
    }

    [Test]
    public void HighPollutionTile_DoesNotGetLowPollutionBonus()
    {
        var grid = new CityGrid(10, 10);
        grid.SetPollution(5, 5, 0.5); // pollution ≥ 0.1

        _system.Propagate(grid);

        // Clean tile at (3,3) should have the low-pollution bonus; (5,5) should not
        var cleanTileValue   = grid.GetTile(3, 3).LandValue;
        var pollutedTileValue = grid.GetTile(5, 5).LandValue;

        Assert.That(cleanTileValue, Is.GreaterThan(pollutedTileValue),
            "A clean tile should have higher land value than a polluted tile.");
    }

    // ── Tax income modifier ──────────────────────────────────────────────────

    [Test]
    public void HighLandValueResidentialPaysMoreTax()
    {
        // Arrange: grid with one residential tile; set high land value manually
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(5, 4, ZoneType.Residential);
        // Simulate high land value by setting terrain to Hill (will get 0.30+0.10 = 0.40 base,
        // then add happy tile bonus for ≥0.7 requirement)
        grid.SetTerrain(5, 4, TerrainType.Hill);
        grid.SetHappiness(5, 4, 0.8); // happiness > 0.7 for +0.10
        grid.SetLandValue(5, 4, 0.75); // manually set ≥ 0.7 threshold

        var budget = new BudgetSystem();
        budget.SetPopulation(100);

        // Act: land-value-aware tax
        var taxWithLV   = budget.CalculateTaxIncome(grid);
        var taxFlat     = budget.CalculateTaxIncome();

        // Assert: high land value (≥0.7) multiplier is 1.5× → should exceed flat
        Assert.That(taxWithLV, Is.GreaterThan(taxFlat),
            "High-land-value residential should pay more tax than the flat rate.");
    }

    [Test]
    public void LowLandValueResidentialPaysLessTax()
    {
        // Arrange: grid with one residential tile set to low land value (< 0.4)
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(5, 4, ZoneType.Residential);
        grid.SetLandValue(5, 4, 0.2); // < 0.4 → 0.8× multiplier

        var budget = new BudgetSystem();
        budget.SetPopulation(100);

        // Act
        var taxWithLV = budget.CalculateTaxIncome(grid);
        var taxFlat   = budget.CalculateTaxIncome();

        // Assert: low land value → 0.8× multiplier → less than flat
        Assert.That(taxWithLV, Is.LessThan(taxFlat),
            "Low-land-value residential should pay less tax than the flat rate.");
    }

    [Test]
    public void MidLandValueResidential_PaysFlatTax()
    {
        // Arrange: mid land value (0.4–0.7) → 1.0× multiplier → same as flat
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(5, 4, ZoneType.Residential);
        grid.SetLandValue(5, 4, 0.55); // mid range

        var budget = new BudgetSystem();
        budget.SetPopulation(100);

        var taxWithLV = budget.CalculateTaxIncome(grid);
        var taxFlat   = budget.CalculateTaxIncome();

        Assert.That(taxWithLV, Is.EqualTo(taxFlat).Within(0.001),
            "Mid-land-value residential should pay the same tax as the flat rate.");
    }

    [Test]
    public void NoResidentialTiles_TaxIncomeIsFlat()
    {
        var grid   = new CityGrid(10, 10); // no residential
        var budget = new BudgetSystem();
        budget.SetPopulation(200);

        var taxWithLV = budget.CalculateTaxIncome(grid);
        var taxFlat   = budget.CalculateTaxIncome();

        Assert.That(taxWithLV, Is.EqualTo(taxFlat).Within(0.001));
    }

    // ── res_villa_hillside_3x3 building conditions ───────────────────────────

    [Test]
    public void HillsideVillaRequiresHillTerrain()
    {
        // Arrange: a 3×3 footprint on Flat terrain — no hill tiles
        var grid = new CityGrid(15, 15);
        for (var dx = 0; dx < 3; dx++)
        for (var dy = 0; dy < 3; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            grid.SetTerrain(5 + dx, 5 + dy, TerrainType.Flat);
        }
        grid.SetZone(4, 5, ZoneType.Road);
        grid.SetZone(4, 6, ZoneType.Road);
        grid.SetZone(4, 7, ZoneType.Road);
        // Set high land value so only Hill is the blocking condition
        grid.SetLandValue(5, 5, 0.8);
        grid.SetLandValue(5, 6, 0.8);
        grid.SetLandValue(5, 7, 0.8);

        // Propagate road access
        var roadNetwork = new RoadNetwork();
        roadNetwork.Propagate(grid);

        var growthSystem = new BuildingGrowthSystem();
        growthSystem.Initialize(grid); // create 1×1 buildings on road-adjacent tiles

        // Fill to 80%+ capacity
        foreach (var building in grid.Buildings.Values.ToList())
        {
            var typeDef = BuildingCatalog.Find(building.TypeId)!;
            var cap85 = (int)(typeDef.MaxPopulation * 0.85) + 1;
            foreach (var (tx, ty) in building.Tiles())
                grid.SetPopulation(tx, ty, cap85);
        }

        growthSystem.TryGrow(grid, GameState.Town);

        // No 3×3 hillside villa should have formed — terrain is Flat
        var hillsideVillas = grid.Buildings.Values
            .Count(b => b.TypeId == "res_villa_hillside_3x3");
        Assert.That(hillsideVillas, Is.EqualTo(0),
            "res_villa_hillside_3x3 should not grow without Hill terrain.");
    }

    [Test]
    public void HillsideVillaRequiresLandValueThreshold()
    {
        // Arrange: footprint has Hill terrain but LandValue is 0 (below 0.7 threshold)
        var grid = new CityGrid(15, 15);
        for (var dx = 0; dx < 3; dx++)
        for (var dy = 0; dy < 3; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            grid.SetTerrain(5 + dx, 5 + dy, TerrainType.Hill);
        }
        grid.SetZone(4, 5, ZoneType.Road);
        grid.SetZone(4, 6, ZoneType.Road);
        grid.SetZone(4, 7, ZoneType.Road);
        // Do NOT set high land value — leave at default 0.0

        // Propagate road access
        var roadNetwork = new RoadNetwork();
        roadNetwork.Propagate(grid);

        var growthSystem = new BuildingGrowthSystem();
        growthSystem.Initialize(grid);

        // Fill buildings to 80%+ capacity to trigger growth
        foreach (var building in grid.Buildings.Values.ToList())
        {
            var typeDef = BuildingCatalog.Find(building.TypeId)!;
            var cap85 = (int)(typeDef.MaxPopulation * 0.85);
            foreach (var (tx, ty) in building.Tiles())
                grid.SetPopulation(tx, ty, cap85);
        }

        growthSystem.TryGrow(grid, GameState.Town);

        var hillsideVillas = grid.Buildings.Values.Count(b => b.TypeId == "res_villa_hillside_3x3");
        Assert.That(hillsideVillas, Is.EqualTo(0),
            "res_villa_hillside_3x3 should not grow when LandValue is below 0.7.");
    }

    [Test]
    public void HillsideVillaUnlocksAtTownMilestone()
    {
        // Verify the catalog entry requires Town milestone
        var target = BuildingCatalog.Find("res_villa_hillside_3x3");

        Assert.That(target, Is.Not.Null, "res_villa_hillside_3x3 must exist in the catalog.");
        Assert.That(target!.MinMilestone, Is.EqualTo(GameState.Town),
            "res_villa_hillside_3x3 should require at least Town milestone.");
    }

    [Test]
    public void HillsideVilla_DoesNotGrow_BeforeTownMilestone()
    {
        // Arrange: all conditions met (Hill terrain, high LandValue) but milestone is Active (not Town)
        var grid = new CityGrid(15, 15);
        for (var dx = 0; dx < 3; dx++)
        for (var dy = 0; dy < 3; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            grid.SetTerrain(5 + dx, 5 + dy, TerrainType.Hill);
            grid.SetLandValue(5 + dx, 5 + dy, 0.8);
        }
        grid.SetZone(4, 5, ZoneType.Road);
        grid.SetZone(4, 6, ZoneType.Road);
        grid.SetZone(4, 7, ZoneType.Road);

        // Propagate road access
        var roadNetwork = new RoadNetwork();
        roadNetwork.Propagate(grid);

        var growthSystem = new BuildingGrowthSystem();
        growthSystem.Initialize(grid);

        // Fill to 80%+ capacity
        foreach (var building in grid.Buildings.Values.ToList())
        {
            var typeDef = BuildingCatalog.Find(building.TypeId)!;
            var cap85 = (int)(typeDef.MaxPopulation * 0.85) + 1;
            foreach (var (tx, ty) in building.Tiles())
                grid.SetPopulation(tx, ty, cap85);
        }

        // Run at Active milestone — not Town yet
        growthSystem.TryGrow(grid, GameState.Active);

        var hillsideVillas = grid.Buildings.Values.Count(b => b.TypeId == "res_villa_hillside_3x3");
        Assert.That(hillsideVillas, Is.EqualTo(0),
            "res_villa_hillside_3x3 should not grow before Town milestone is reached.");
    }

    [Test]
    public void HillsideVilla_Grows_WhenAllConditionsMet()
    {
        // Arrange: 3×3 Residential footprint with Hill terrain and high LandValue, road access at Town milestone
        var grid = new CityGrid(15, 15);
        for (var dx = 0; dx < 3; dx++)
        for (var dy = 0; dy < 3; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            grid.SetTerrain(5 + dx, 5 + dy, TerrainType.Hill);
            grid.SetLandValue(5 + dx, 5 + dy, 0.8); // ≥ 0.7 threshold
        }
        // Road tiles along left side so all three rows get road access
        grid.SetZone(4, 5, ZoneType.Road);
        grid.SetZone(4, 6, ZoneType.Road);
        grid.SetZone(4, 7, ZoneType.Road);

        // Propagate road access so HasRoadAccess is set before Initialize()
        var roadNetwork = new RoadNetwork();
        roadNetwork.Propagate(grid);

        var growthSystem = new BuildingGrowthSystem();
        growthSystem.Initialize(grid);

        // Fill all seed buildings to >80% capacity
        foreach (var building in grid.Buildings.Values.ToList())
        {
            var typeDef = BuildingCatalog.Find(building.TypeId)!;
            var cap85 = (int)(typeDef.MaxPopulation * 0.85) + 1;
            foreach (var (tx, ty) in building.Tiles())
                grid.SetPopulation(tx, ty, cap85);
        }

        // Act: grow at Town milestone
        growthSystem.TryGrow(grid, GameState.Town);

        // Assert: at least one hillside villa grew
        var hillsideVillas = grid.Buildings.Values.Count(b => b.TypeId == "res_villa_hillside_3x3");
        Assert.That(hillsideVillas, Is.GreaterThanOrEqualTo(1),
            "res_villa_hillside_3x3 should grow when Hill terrain, LandValue ≥ 0.7, road access, and Town milestone are all met.");
    }

    // ── LandValueSystem aggregate methods ───────────────────────────────────

    [Test]
    public void AverageLandValue_EmptyGrid_ReturnsZero()
    {
        var grid = new CityGrid(10, 10);
        _system.Propagate(grid);

        Assert.That(_system.AverageLandValue(grid), Is.EqualTo(0.0));
    }

    [Test]
    public void MaxLandValue_HillTile_ReflectsHillBonus()
    {
        var grid = new CityGrid(10, 10);
        grid.SetTerrain(5, 5, TerrainType.Hill);
        _system.Propagate(grid);

        // Hill tile gets at least HillBonus + LowPollutionBonus (no happiness bonus since default Happiness=1.0 > 0.7)
        var maxVal = _system.MaxLandValue(grid);
        Assert.That(maxVal, Is.GreaterThanOrEqualTo(
            LandValueSystem.HillBonus + LandValueSystem.LowPollutionBonus + LandValueSystem.HighHappinessBonus).Within(0.001));
    }
}
