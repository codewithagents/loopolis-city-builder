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
    public void ElevatedTile_GetsElevatedBonus()
    {
        // Arrange: elevated tile (height ≥ 2) vs. flat tile — otherwise same conditions
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(3, 3, 3); // elevated — gets ElevatedBonus
        // (6,6) stays at height=1 (flat)

        _system.Propagate(grid);

        var elevatedValue = grid.GetTile(3, 3).LandValue;
        var flatValue     = grid.GetTile(6, 6).LandValue;

        Assert.That(elevatedValue, Is.GreaterThan(flatValue),
            "An elevated tile should have higher land value than a flat tile.");
        Assert.That(elevatedValue, Is.GreaterThanOrEqualTo(LandValueSystem.ElevatedBonus).Within(0.001));
    }

    [Test]
    public void PlateauTile_GetsHigherBonusThanPlainElevated()
    {
        // A plateau (all cardinal neighbours within height diff 1) gets PlateauBonus (0.35)
        // A cliff-edge elevated tile (has a steep neighbour) gets only ElevatedBonus (0.20)
        var grid = new CityGrid(10, 10);

        // Plateau at (2,2): all neighbours at height 3
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
            grid.SetHeightLevel(2 + dx, 2 + dy, 3);

        // Plain elevated at (7,7): height 3 but neighbour at (7,8) is height 1 → cliff
        grid.SetHeightLevel(7, 7, 3);
        grid.SetHeightLevel(7, 8, 1); // diff 2 → not a plateau

        _system.Propagate(grid);

        var plateauValue  = grid.GetTile(2, 2).LandValue;
        var elevatedValue = grid.GetTile(7, 7).LandValue;

        Assert.That(plateauValue, Is.GreaterThan(elevatedValue),
            "Plateau bonus (0.35) should yield higher land value than plain elevated bonus (0.20).");
        Assert.That(plateauValue, Is.GreaterThanOrEqualTo(LandValueSystem.PlateauBonus).Within(0.001));
    }

    [Test]
    public void WaterAdjacentTileGetsWaterBonus()
    {
        // Arrange: tile at (5,5), Water tile at (5,7) which is Chebyshev distance 2 (within 3)
        var grid = new CityGrid(15, 15);
        grid.SetHeightLevel(5, 7, 0); // water

        _system.Propagate(grid);

        var nearWaterValue    = grid.GetTile(5, 5).LandValue;
        var farFromWaterValue = grid.GetTile(1, 1).LandValue; // far from water

        Assert.That(nearWaterValue, Is.GreaterThan(farFromWaterValue),
            "A tile near water should have higher land value.");
        Assert.That(nearWaterValue, Is.GreaterThanOrEqualTo(LandValueSystem.WaterAdjacentBonus).Within(0.001));
    }

    [Test]
    public void WaterTileItself_HasZeroLandValue()
    {
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(5, 5, 0); // water

        _system.Propagate(grid);

        Assert.That(grid.GetTile(5, 5).LandValue, Is.EqualTo(0.0),
            "Water tiles should always have LandValue = 0.");
    }

    [Test]
    public void ForestAdjacentTileGetsForestBonus()
    {
        // Arrange: place 3 Forest tiles within Chebyshev distance 2 of (5,5)
        var grid = new CityGrid(15, 15);
        grid.SetForest(5, 4, true); // dy=1
        grid.SetForest(5, 3, true); // dy=2
        grid.SetForest(4, 4, true); // dx=-1, dy=1

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
        // Arrange: surround (5,5) with many forests via SetForest
        var grid = new CityGrid(15, 15);
        for (var dx = -2; dx <= 2; dx++)
        for (var dy = -2; dy <= 2; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            grid.SetForest(5 + dx, 5 + dy, true);
        }

        _system.Propagate(grid);

        var value = grid.GetTile(5, 5).LandValue;
        // LowPollutionBonus always applies (pollution default 0)
        var expectedMin = LandValueSystem.ForestBonusMax + LandValueSystem.LowPollutionBonus;
        Assert.That(value, Is.GreaterThanOrEqualTo(expectedMin).Within(0.001));
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
        grid.SetHeightLevel(5, 4, 3); // elevated — gets land value bonus
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
        // Arrange: a 3×3 footprint on Flat terrain (height=1) — no elevated tiles
        var grid = new CityGrid(15, 15);
        for (var dx = 0; dx < 3; dx++)
        for (var dy = 0; dy < 3; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            grid.SetHeightLevel(5 + dx, 5 + dy, 1); // explicitly flat
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
        // Arrange: footprint has elevated terrain (height ≥ 2) but LandValue is 0 (below 0.7 threshold)
        var grid = new CityGrid(15, 15);
        for (var dx = 0; dx < 3; dx++)
        for (var dy = 0; dy < 3; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            grid.SetHeightLevel(5 + dx, 5 + dy, 3); // elevated
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
        // Arrange: all conditions met (elevated terrain, high LandValue) but milestone is Active (not Town)
        var grid = new CityGrid(15, 15);
        for (var dx = 0; dx < 3; dx++)
        for (var dy = 0; dy < 3; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            grid.SetHeightLevel(5 + dx, 5 + dy, 3); // elevated
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
        // Arrange: 3×3 Residential footprint with elevated terrain and high LandValue, road access at Town milestone
        var grid = new CityGrid(15, 15);
        for (var dx = 0; dx < 3; dx++)
        for (var dy = 0; dy < 3; dy++)
        {
            grid.SetZone(5 + dx, 5 + dy, ZoneType.Residential);
            grid.SetHeightLevel(5 + dx, 5 + dy, 3); // elevated (≥2 → HasHillTerrain passes)
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
    public void MaxLandValue_ElevatedTile_ReflectsElevatedBonus()
    {
        // A plain elevated tile (height=3, surrounded by lower tiles so it's a cliff edge, not a plateau)
        // gets ElevatedBonus, not PlateauBonus.
        var grid = new CityGrid(10, 10);
        grid.SetHeightLevel(5, 5, 3); // elevated; neighbours stay at 1 → cliff edge, not plateau
        _system.Propagate(grid);

        // Elevated tile gets at least ElevatedBonus + LowPollutionBonus + HighHappinessBonus
        var maxVal = _system.MaxLandValue(grid);
        Assert.That(maxVal, Is.GreaterThanOrEqualTo(
            LandValueSystem.ElevatedBonus + LandValueSystem.LowPollutionBonus + LandValueSystem.HighHappinessBonus).Within(0.001));
    }
}
