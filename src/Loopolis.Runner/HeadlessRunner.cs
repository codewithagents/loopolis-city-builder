using System.Text.Json;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Runner;

/// <summary>
/// Headless CLI mode: simulate N ticks and print a JSON report (or ASCII grid).
/// </summary>
static class HeadlessRunner
{
    public static void Run(string[] args)
    {
        var ticks    = args.Length > 0 ? int.Parse(args[0]) : 500;
        var cliScene = args.Length > 1 ? args[1] : "default";
        var asciiMode = args.Contains("--ascii");
        var cliSeedIdx  = Array.IndexOf(args, "--seed");
        var cliSeed     = cliSeedIdx >= 0 && cliSeedIdx + 1 < args.Length ? int.Parse(args[cliSeedIdx + 1]) : 0;

        var (cliGrid, cliEngine) = ScenarioSetup.Setup(cliScene, cliSeed);
        var budget     = cliEngine.Budget;
        var population = cliEngine.Population;
        var powerNetwork = cliEngine.PowerNetwork;

        var tickHistory = new List<TickSnapshot>();

        for (var tick = 0; tick < ticks; tick++)
        {
            // stress_test: after 200 ticks remove the power plant to trigger degradation
            if (cliScene == "stress_test" && tick == 200)
            {
                cliEngine.EraseTile(0, 0); // erase CoalPlant at (0,0)
                Console.Error.WriteLine($"[stress_test] Removed power plant at tick {tick}");
            }

            cliEngine.Tick();

            if (tick % 100 == 0 || tick == ticks - 1)
            {
                tickHistory.Add(new TickSnapshot(
                    Tick: tick,
                    Population: population.Population,
                    Balance: Math.Round(budget.Balance, 2),
                    IsInDeficit: budget.IsInDeficit,
                    TaxIncome: Math.Round(budget.LastTaxIncome, 2),
                    MaintenanceCost: Math.Round(budget.LastMaintenanceCost, 2),
                    NetPerTick: Math.Round(budget.NetIncomePerTick, 2),
                    AverageHappiness: Math.Round(cliEngine.HappinessSystem.AverageHappiness(cliGrid), 3),
                    AveragePollution: Math.Round(cliEngine.PollutionSystem.AveragePollution(cliGrid), 3)
                ));
            }
        }

        // --- Output ---
        if (asciiMode)
        {
            AsciiRenderer.Render(cliGrid, budget, population, cliScene, ticks);
        }
        else
        {
            var residentialTiles      = cliGrid.TilesOfType(ZoneType.Residential).ToList();
            var poweredResidential    = residentialTiles.Count(t => t.HasPower);
            var roadAccessResidential = residentialTiles.Count(t => t.HasRoadAccess);
            var readyResidential      = residentialTiles.Count(t => t.IsReadyToDevelop);

            var report = new SimulationReport(
                Scenario: cliScene,
                TotalTicks: ticks,
                FinalPopulation: population.Population,
                FinalBalance: Math.Round(budget.Balance, 2),
                Survived: !budget.IsInDeficit,
                ResidentialZones: residentialTiles.Count,
                PoweredResidentialZones: poweredResidential,
                RoadAccessResidentialZones: roadAccessResidential,
                ReadyResidentialZones: readyResidential,
                PoweredTiles: powerNetwork.PoweredTileCount,
                CommercialZones: cliGrid.TilesOfType(ZoneType.Commercial).Count(),
                IndustrialZones: cliGrid.TilesOfType(ZoneType.Industrial).Count(),
                AveragePollution: Math.Round(cliEngine.PollutionSystem.AveragePollution(cliGrid), 3),
                AverageHappiness: Math.Round(cliEngine.HappinessSystem.AverageHappiness(cliGrid), 3),
                GameState: cliEngine.MilestoneSystem.CurrentState.ToString(),
                MilestonesReached: cliEngine.MilestoneSystem.Reached.Select(m => $"{m.Name} {m.Emoji} (tick {m.ReachedAtTick})").ToList(),
                History: tickHistory,
                FinalTick: ticks,
                TownCharterPending: cliEngine.Charters.TownCharterPending,
                CityCharterPending: cliEngine.Charters.CityCharterPending,
                MetropolisCharterPending: cliEngine.Charters.MetropolisCharterPending
            );
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

// ── ASCII Renderer ────────────────────────────────────────────────────────────

static class AsciiRenderer
{
    private const string Reset  = "\x1b[0m";
    private const string Green  = "\x1b[32m";
    private const string Blue   = "\x1b[34m";
    private const string Yellow = "\x1b[33m";
    private const string Red    = "\x1b[31m";
    private const string Cyan   = "\x1b[36m";
    private const string White  = "\x1b[37m";
    private const string Gray   = "\x1b[90m";
    private const string BgGray = "\x1b[100m";

    public static void Render(CityGrid grid, BudgetSystem budget, PopulationSystem pop, string scenario, int ticks)
    {
        Console.Clear();
        Console.WriteLine($"  ╔══════════════════════════════════════╗");
        Console.WriteLine($"  ║  L O O P O L I S  — {scenario,-14} ║");
        Console.WriteLine($"  ╚══════════════════════════════════════╝");
        Console.WriteLine();

        // Grid
        for (var y = 0; y < grid.Height; y++)
        {
            Console.Write("  ");
            for (var x = 0; x < grid.Width; x++)
            {
                var tile = grid.GetTile(x, y);
                var (symbol, color) = tile.Zone switch
                {
                    ZoneType.Residential  => ("R", Green),
                    ZoneType.Commercial   => ("C", Blue),
                    ZoneType.Industrial   => ("I", Yellow),
                    ZoneType.Road         => ("░", Gray),
                    ZoneType.PowerPlant   => ("P", Red),
                    ZoneType.CoalPlant    => ("K", Red),
                    ZoneType.NuclearPlant => ("N", White),
                    ZoneType.PowerLine    => ("╌", Cyan),
                    ZoneType.Park         => ("P", Green),
                    _                     => ("·", Gray),
                };
                Console.Write($"{color}{symbol}{Reset}");
            }
            Console.WriteLine();
        }

        // Legend
        Console.WriteLine();
        Console.WriteLine($"  {Green}R{Reset} Residential  {Blue}C{Reset} Commercial  {Yellow}I{Reset} Industrial");
        Console.WriteLine($"  {Red}P{Reset} Power Plant  {Cyan}╌{Reset} Power Line  {Gray}░{Reset} Road  {Gray}·{Reset} Empty");
        Console.WriteLine();

        // Stats
        var balanceColor = budget.IsInDeficit ? Red : Green;
        var survived     = budget.IsInDeficit ? $"{Red}✗ BANKRUPT{Reset}" : $"{Green}✓ SURVIVED{Reset}";

        Console.WriteLine($"  ┌─── After {ticks} ticks ──────────────────┐");
        Console.WriteLine($"  │  Population : {pop.Population,8}                │");
        Console.WriteLine($"  │  Balance    : {balanceColor}${budget.Balance,+12:N0}{Reset}          │");
        Console.WriteLine($"  │  Tax/tick   : ${budget.LastTaxIncome,10:N2}                │");
        Console.WriteLine($"  │  Status     : {survived,-28}│");
        Console.WriteLine($"  │                                          │");
        Console.WriteLine($"  │  Zones ▸  {Green}R{Reset}:{grid.TilesOfType(ZoneType.Residential).Count(),3}  " +
                          $"{Blue}C{Reset}:{grid.TilesOfType(ZoneType.Commercial).Count(),3}  " +
                          $"{Yellow}I{Reset}:{grid.TilesOfType(ZoneType.Industrial).Count(),3}        │");
        Console.WriteLine($"  └──────────────────────────────────────────┘");
        Console.WriteLine();
    }
}
