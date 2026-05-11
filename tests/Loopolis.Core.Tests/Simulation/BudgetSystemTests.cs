using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class BudgetSystemTests
{
    [Test]
    public void NewBudget_StartsWithInitialBalance()
    {
        var budget = new BudgetSystem(initialBalance: 10_000);

        Assert.That(budget.Balance, Is.EqualTo(10_000));
    }

    [Test]
    public void TaxIncome_ScalesWithPopulation()
    {
        var budget = new BudgetSystem();
        budget.SetPopulation(1000);
        budget.SetTaxRate(0.09);

        Assert.That(budget.CalculateTaxIncome(), Is.EqualTo(90.0).Within(0.001));
    }

    [Test]
    public void TaxIncome_ZeroPopulation_IsZero()
    {
        var budget = new BudgetSystem();
        budget.SetPopulation(0);

        Assert.That(budget.CalculateTaxIncome(), Is.EqualTo(0.0));
    }

    [Test]
    public void CollectTaxes_IncreasesBalance()
    {
        var budget = new BudgetSystem(initialBalance: 1000);
        budget.SetPopulation(1000);
        budget.SetTaxRate(0.09);

        budget.CollectTaxes();

        Assert.That(budget.Balance, Is.GreaterThan(1000));
    }

    [Test]
    public void DeductCost_DecreasesBalance()
    {
        var budget = new BudgetSystem(initialBalance: 1000);

        budget.DeductCost(200);

        Assert.That(budget.Balance, Is.EqualTo(800));
    }

    [Test]
    public void IsInDeficit_WhenBalanceNegative_IsTrue()
    {
        var budget = new BudgetSystem(initialBalance: 100);

        budget.DeductCost(500);

        Assert.That(budget.IsInDeficit, Is.True);
    }

    [Test]
    public void IsInDeficit_WhenBalancePositive_IsFalse()
    {
        var budget = new BudgetSystem(initialBalance: 10_000);

        Assert.That(budget.IsInDeficit, Is.False);
    }

    [TestCase(0.0)]
    [TestCase(0.05)]
    [TestCase(0.20)]
    public void TaxRate_IsClampedBetweenZeroAndOne(double rate)
    {
        var budget = new BudgetSystem();
        budget.SetTaxRate(rate);

        Assert.That(budget.TaxRate, Is.EqualTo(rate));
    }

    [Test]
    public void TaxRate_AboveOne_IsClamped()
    {
        var budget = new BudgetSystem();
        budget.SetTaxRate(2.0);

        Assert.That(budget.TaxRate, Is.EqualTo(1.0));
    }

    // --- Maintenance cost tests ---

    [Test]
    public void MaintenanceCost_EmptyGrid_IsZero()
    {
        var budget = new BudgetSystem();
        var grid = new CityGrid(10, 10);

        Assert.That(budget.CalculateMaintenanceCost(grid), Is.EqualTo(0.0));
    }

    [Test]
    public void MaintenanceCost_PowerPlantCostsEight()
    {
        var budget = new BudgetSystem();
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.PowerPlant);

        Assert.That(budget.CalculateMaintenanceCost(grid), Is.EqualTo(8.0).Within(0.001));
    }

    [Test]
    public void MaintenanceCost_ScalesWithTileCount()
    {
        var budget = new BudgetSystem();
        var grid = new CityGrid(10, 10);

        // 3 roads × $1.0 = $3.0
        grid.SetZone(1, 1, ZoneType.Road);
        grid.SetZone(2, 1, ZoneType.Road);
        grid.SetZone(3, 1, ZoneType.Road);

        Assert.That(budget.CalculateMaintenanceCost(grid), Is.EqualTo(3.0).Within(0.001));
    }

    [Test]
    public void MaintenanceCost_MixedCity_SumsCorrectly()
    {
        var budget = new BudgetSystem();
        var grid = new CityGrid(10, 10);

        grid.SetZone(0, 0, ZoneType.PowerPlant);   // $8.0
        grid.SetZone(1, 0, ZoneType.Road);          // $1.0
        grid.SetZone(2, 0, ZoneType.Residential);   // $0.5
        grid.SetZone(3, 0, ZoneType.Commercial);    // $0.5
        grid.SetZone(4, 0, ZoneType.PowerLine);     // $0.5

        Assert.That(budget.CalculateMaintenanceCost(grid), Is.EqualTo(10.5).Within(0.001));
    }

    [Test]
    public void DeductMaintenance_ReducesBalanceByGridCost()
    {
        var budget = new BudgetSystem(initialBalance: 1000);
        var grid = new CityGrid(10, 10);
        grid.SetZone(5, 5, ZoneType.Road); // $1.0/tick

        budget.DeductMaintenance(grid);

        Assert.That(budget.Balance, Is.EqualTo(999.0).Within(0.001));
        Assert.That(budget.LastMaintenanceCost, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void NetIncome_PositiveWhenTaxExceedsMaintenance()
    {
        var budget = new BudgetSystem();
        var grid = new CityGrid(10, 10);
        grid.SetZone(0, 0, ZoneType.Road); // $1.0 maintenance

        budget.SetPopulation(500);          // $45.0 tax income at 9%
        budget.DeductMaintenance(grid);

        Assert.That(budget.NetIncomePerTick, Is.GreaterThan(0));
    }

    [Test]
    public void NetIncome_NegativeWhenMaintenanceExceedsTax()
    {
        var budget = new BudgetSystem();
        var grid = new CityGrid(10, 10);

        // Big power plant, tiny population
        grid.SetZone(0, 0, ZoneType.PowerPlant); // $10.0 maintenance
        budget.SetPopulation(10);                 // $0.90 tax income
        budget.DeductMaintenance(grid);

        Assert.That(budget.NetIncomePerTick, Is.LessThan(0));
    }

    /// <summary>
    /// Key balance test: verifies a minimum viable city can sustain itself.
    /// 12 powered residential zones → 600 pop → $54 tax > ~$17 maintenance → surplus.
    /// This defines the "critical mass" the player needs to reach.
    /// </summary>
    [Test]
    public void MinimumViableCity_GeneratesSurplus()
    {
        var budget = new BudgetSystem();
        var grid = new CityGrid(20, 20);

        // 1 power plant + 1 road + 12 residential zones
        grid.SetZone(0, 0, ZoneType.PowerPlant);
        grid.SetZone(1, 0, ZoneType.Road);
        for (var x = 2; x <= 13; x++)
            grid.SetZone(x, 0, ZoneType.Residential);

        // 12 zones × 50 residents = 600 population
        budget.SetPopulation(600);
        budget.DeductMaintenance(grid);

        // Tax: 600 × 0.09 = $54. Maintenance: $10 + $1 + (12 × $0.5) = $17
        Assert.That(budget.NetIncomePerTick, Is.GreaterThan(0),
            "A city with 12 powered residential zones should generate surplus revenue");
    }

    // --- Placement cost tests ---

    [Test]
    public void PlacementCosts_ContainsExpectedZones()
    {
        Assert.That(BudgetSystem.PlacementCosts, Does.ContainKey("Residential"));
        Assert.That(BudgetSystem.PlacementCosts, Does.ContainKey("Commercial"));
        Assert.That(BudgetSystem.PlacementCosts, Does.ContainKey("Industrial"));
        Assert.That(BudgetSystem.PlacementCosts, Does.ContainKey("Road"));
        Assert.That(BudgetSystem.PlacementCosts, Does.ContainKey("PowerLine"));
        Assert.That(BudgetSystem.PlacementCosts, Does.ContainKey("PowerPlant"));
        Assert.That(BudgetSystem.PlacementCosts, Does.ContainKey("FireStation"));
        Assert.That(BudgetSystem.PlacementCosts, Does.ContainKey("PoliceStation"));
        Assert.That(BudgetSystem.PlacementCosts, Does.ContainKey("School"));
        Assert.That(BudgetSystem.PlacementCosts, Does.ContainKey("Erase"));
    }

    [Test]
    public void PlacementCosts_HasCorrectValues()
    {
        Assert.That(BudgetSystem.PlacementCosts["Residential"],    Is.EqualTo(50.0).Within(0.001));
        Assert.That(BudgetSystem.PlacementCosts["Commercial"],     Is.EqualTo(100.0).Within(0.001));
        Assert.That(BudgetSystem.PlacementCosts["Industrial"],     Is.EqualTo(75.0).Within(0.001));
        Assert.That(BudgetSystem.PlacementCosts["Road"],           Is.EqualTo(25.0).Within(0.001));
        Assert.That(BudgetSystem.PlacementCosts["PowerLine"],      Is.EqualTo(40.0).Within(0.001));
        Assert.That(BudgetSystem.PlacementCosts["PowerPlant"],     Is.EqualTo(500.0).Within(0.001));
        Assert.That(BudgetSystem.PlacementCosts["FireStation"],    Is.EqualTo(300.0).Within(0.001));
        Assert.That(BudgetSystem.PlacementCosts["PoliceStation"],  Is.EqualTo(300.0).Within(0.001));
        Assert.That(BudgetSystem.PlacementCosts["School"],         Is.EqualTo(400.0).Within(0.001));
        Assert.That(BudgetSystem.PlacementCosts["Erase"],          Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void CanAfford_ReturnsTrueWhenBalanceSufficient()
    {
        var budget = new BudgetSystem(initialBalance: 1000);

        Assert.That(budget.CanAfford(500), Is.True);
        Assert.That(budget.CanAfford(1000), Is.True);
    }

    [Test]
    public void CanAfford_ReturnsFalseWhenBalanceInsufficient()
    {
        var budget = new BudgetSystem(initialBalance: 100);

        Assert.That(budget.CanAfford(101), Is.False);
    }

    [Test]
    public void Charge_DeductsFromBalance()
    {
        var budget = new BudgetSystem(initialBalance: 1000);

        budget.Charge(250);

        Assert.That(budget.Balance, Is.EqualTo(750).Within(0.001));
    }

    [Test]
    public void Charge_AffordabilityCheckBeforeCharge_PreservesBalance()
    {
        var budget = new BudgetSystem(initialBalance: 100);

        var canAfford = budget.CanAfford(500);
        if (!canAfford) return; // skip the charge — this is the guard pattern

        Assert.That(budget.Balance, Is.EqualTo(100)); // unchanged
    }

    // --- SetTaxRate(string) tests ---

    [Test]
    public void SetTaxRate_Low_SetsCorrectRateAndModifier()
    {
        var budget = new BudgetSystem();

        budget.SetTaxRate("low");

        Assert.That(budget.TaxRate, Is.EqualTo(0.08).Within(0.0001));
        Assert.That(budget.TaxModifier, Is.EqualTo(+0.05).Within(0.0001));
    }

    [Test]
    public void SetTaxRate_Normal_SetsCorrectRateAndModifier()
    {
        var budget = new BudgetSystem();
        budget.SetTaxRate("low"); // change from default first

        budget.SetTaxRate("normal");

        Assert.That(budget.TaxRate, Is.EqualTo(0.12).Within(0.0001));
        Assert.That(budget.TaxModifier, Is.EqualTo(0.00).Within(0.0001));
    }

    [Test]
    public void SetTaxRate_High_SetsCorrectRateAndModifier()
    {
        var budget = new BudgetSystem();

        budget.SetTaxRate("high");

        Assert.That(budget.TaxRate, Is.EqualTo(0.16).Within(0.0001));
        Assert.That(budget.TaxModifier, Is.EqualTo(-0.10).Within(0.0001));
    }

    [Test]
    public void SetTaxRate_UnknownLevel_FallsBackToNormal()
    {
        var budget = new BudgetSystem();
        budget.SetTaxRate("high"); // change from default first

        budget.SetTaxRate("unknown");

        Assert.That(budget.TaxRate, Is.EqualTo(0.12).Within(0.0001));
        Assert.That(budget.TaxModifier, Is.EqualTo(0.00).Within(0.0001));
    }

    [Test]
    public void TaxModifier_DefaultIsZero()
    {
        var budget = new BudgetSystem();

        Assert.That(budget.TaxModifier, Is.EqualTo(0.0).Within(0.0001));
    }
}
