namespace Loopolis.Core.Simulation;

public class BudgetSystem
{
    public double Balance { get; private set; }
    public double TaxRate { get; private set; } = 0.09;
    public int Population { get; private set; }

    public BudgetSystem(double initialBalance = 10_000)
    {
        Balance = initialBalance;
    }

    public void SetPopulation(int population) =>
        Population = Math.Max(0, population);

    public void SetTaxRate(double rate) =>
        TaxRate = Math.Clamp(rate, 0.0, 1.0);

    public double CalculateTaxIncome() =>
        Population * TaxRate;

    public void CollectTaxes() =>
        Balance += CalculateTaxIncome();

    public void DeductCost(double amount) =>
        Balance -= amount;

    public bool IsInDeficit => Balance < 0;

    public BudgetSnapshot Snapshot() => new(Balance, TaxRate, Population, CalculateTaxIncome());
}

public record BudgetSnapshot(
    double Balance,
    double TaxRate,
    int Population,
    double ProjectedIncome
);
