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
}
