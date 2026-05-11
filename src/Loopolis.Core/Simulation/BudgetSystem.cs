using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

public class BudgetSystem
{
    /// <summary>
    /// Maintenance cost per tile per tick.
    /// Costs emerge from what you build — not a flat fee.
    /// Power plants are expensive. Roads and zones are cheap but scale.
    /// </summary>
    public static readonly IReadOnlyDictionary<ZoneType, double> MaintenanceCostPerTile =
        new Dictionary<ZoneType, double>
        {
            { ZoneType.PowerPlant,    8.0 },
            { ZoneType.PowerLine,     0.5 },
            { ZoneType.Road,          1.0 },
            { ZoneType.Residential,   0.5 },
            { ZoneType.Commercial,    0.5 },
            { ZoneType.Industrial,    0.25 },
            { ZoneType.FireStation,   3.0 },
            { ZoneType.PoliceStation, 3.0 },
            { ZoneType.School,        5.0 },
        };

    private const double CommercialIncomePerReadyTile = 3.0;

    public double Balance { get; private set; }
    public double TaxRate { get; private set; } = 0.12;
    public int Population { get; private set; }
    public double LastMaintenanceCost { get; private set; }
    public double CommercialIncomePerTick { get; private set; }

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

    public double CalculateMaintenanceCost(CityGrid grid) =>
        grid.AllTiles()
            .Where(t => t.Zone != ZoneType.Empty)
            .Sum(t => MaintenanceCostPerTile.GetValueOrDefault(t.Zone, 0.0));

    /// <summary>
    /// Counts ready commercial tiles (powered + road access) and returns income at $3/tile/tick.
    /// </summary>
    public double CalculateCommercialIncome(CityGrid grid) =>
        grid.TilesOfType(ZoneType.Commercial)
            .Count(t => t.HasPower && t.HasRoadAccess)
        * CommercialIncomePerReadyTile;

    public void CollectTaxes() =>
        Balance += CalculateTaxIncome();

    /// <summary>
    /// Adds commercial income to Balance and stores the per-tick amount for reporting.
    /// </summary>
    public void CollectCommercialIncome(CityGrid grid)
    {
        CommercialIncomePerTick = CalculateCommercialIncome(grid);
        Balance += CommercialIncomePerTick;
    }

    /// <summary>
    /// Deducts maintenance costs based on what's currently built on the grid.
    /// Stores the cost for reporting/UI purposes.
    /// </summary>
    public void DeductMaintenance(CityGrid grid)
    {
        LastMaintenanceCost = CalculateMaintenanceCost(grid);
        Balance -= LastMaintenanceCost;
    }

    public void DeductCost(double amount) =>
        Balance -= amount;

    public bool IsInDeficit => Balance < 0;

    public double NetIncomePerTick => CalculateTaxIncome() + CommercialIncomePerTick - LastMaintenanceCost;

    public BudgetSnapshot Snapshot() =>
        new(Balance, TaxRate, Population, CalculateTaxIncome(), CommercialIncomePerTick, LastMaintenanceCost, NetIncomePerTick);
}

public record BudgetSnapshot(
    double Balance,
    double TaxRate,
    int Population,
    double TaxIncome,
    double CommercialIncome,
    double MaintenanceCost,
    double NetIncome
);
