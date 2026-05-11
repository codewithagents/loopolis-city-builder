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
            { ZoneType.Avenue,        2.0 },
            { ZoneType.Residential,   0.5 },
            { ZoneType.Commercial,    0.5 },
            { ZoneType.Industrial,    0.25 },
            { ZoneType.FireStation,   3.0 },
            { ZoneType.PoliceStation, 3.0 },
            { ZoneType.School,        5.0 },
        };

    /// <summary>
    /// One-time placement cost when the player places a zone.
    /// Makes placement decisions meaningful — budget must cover the upfront cost.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> PlacementCosts =
        new Dictionary<string, double>
        {
            { "Residential",   50.0 },
            { "Commercial",   100.0 },
            { "Industrial",    75.0 },
            { "Road",          25.0 },
            { "Avenue",        50.0 },
            { "PowerLine",     40.0 },
            { "PowerPlant",   500.0 },
            { "FireStation",  300.0 },
            { "PoliceStation",300.0 },
            { "School",       400.0 },
            { "Erase",          0.0 },
        };

    /// <summary>Extra maintenance cost per tick per tile on a hill.</summary>
    /// <remarks>TODO: Apply per-tile hill maintenance in SimulationEngine or BudgetSystem tick.</remarks>
    public const double HillMaintenanceSurcharge = 0.25;

    private const double CommercialIncomePerReadyTile = 3.0;

    /// <summary>
    /// Returns the total placement cost for a zone, including any terrain surcharge.
    /// Forest adds $75 (clearing cost), Hill adds $50. Water is unbuildable and returns the base cost only.
    /// </summary>
    public static double GetPlacementCost(string zoneName, TerrainType terrain)
    {
        var baseCost = PlacementCosts.TryGetValue(zoneName, out var c) ? c : 0.0;
        var terrainSurcharge = terrain switch
        {
            TerrainType.Forest => 75.0,
            TerrainType.Hill   => 50.0,
            _                  => 0.0,
        };
        return baseCost + terrainSurcharge;
    }

    public double Balance { get; private set; }
    public double TaxRate { get; private set; } = 0.12;
    public double TaxModifier { get; private set; } = 0.0;
    public int Population { get; private set; }
    public double LastMaintenanceCost { get; private set; }
    public double CommercialIncomePerTick { get; private set; }

    public BudgetSystem(double initialBalance = 4_000)
    {
        Balance = initialBalance;
    }

    public void SetPopulation(int population) =>
        Population = Math.Max(0, population);

    public void SetTaxRate(double rate) =>
        TaxRate = Math.Clamp(rate, 0.0, 1.0);

    /// <summary>
    /// Sets the tax rate using a named level. Also adjusts the happiness modifier.
    /// Low → 8% tax, +0.05 happiness. Normal → 12% tax, 0.0 happiness. High → 16% tax, -0.10 happiness.
    /// </summary>
    public void SetTaxRate(string level)
    {
        switch (level)
        {
            case "low":
                TaxRate = 0.08;
                TaxModifier = +0.05;
                break;
            case "high":
                TaxRate = 0.16;
                TaxModifier = -0.10;
                break;
            default:
                TaxRate = 0.12;
                TaxModifier = 0.00;
                break;
        }
    }

    /// <summary>Returns true if the current balance can cover the given cost.</summary>
    public bool CanAfford(double cost) => Balance >= cost;

    /// <summary>Deducts the given cost from balance (placement fee).</summary>
    public void Charge(double cost) => Balance -= cost;

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
