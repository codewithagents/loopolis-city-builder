using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Runner;

/// <summary>
/// Builds a CityGrid + SimulationEngine for a named scenario.
/// Shared between CLI (HeadlessRunner) and server (SimulationServer) modes.
/// </summary>
static class ScenarioSetup
{
    public static (CityGrid grid, SimulationEngine engine) Setup(string scenario, int terrainSeed = 0)
    {
        var grid       = new CityGrid(32, 32);
        var budget     = new BudgetSystem(); // default $4,000 starting balance
        var population = new PopulationSystem();
        var power      = new PowerNetwork();
        var roads      = new RoadNetwork();
        var demand     = new DemandSystem();

        switch (scenario)
        {
            case "generated_128":
            {
                // Procedurally generated 128×128 terrain using diamond-square.
                // Starts with only a border connection road at center of south edge.
                var seed = terrainSeed != 0 ? terrainSeed : 42;
                var g128 = new CityGrid(128, 128);
                var heightMap128 = Loopolis.Core.Grid.HeightMapGenerator.Generate(128, 128, seed);
                var forestMap128 = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(128, 128, seed);
                g128.ApplyHeightMap(heightMap128);
                g128.ApplyForestMap(forestMap128);
                // Border connection at center of south edge — force flat terrain so it can always be placed
                g128.SetHeightLevel(64, 127, 1);
                g128.PlaceBorderConnection(64, 127);
                // Starter road spine heading north — force flat terrain on each tile
                g128.SetHeightLevel(64, 126, 1); g128.SetZone(64, 126, ZoneType.Road);
                g128.SetHeightLevel(64, 125, 1); g128.SetZone(64, 125, ZoneType.Road);
                g128.SetHeightLevel(64, 124, 1); g128.SetZone(64, 124, ZoneType.Road);
                Console.WriteLine($"[generated_128] Border connection at (64,127), starter spine (64,126–124), seed={seed}");
                var engine128 = new SimulationEngine(g128, budget, population, power, roads, demand);
                engine128.SeedRoadGraphFromGrid();
                return (g128, engine128);
            }

            case "generated_map":
            {
                // Procedurally generated 64×64 terrain using diamond-square. Seed from CLI --seed arg.
                // Starts with only a border connection road at center of south edge.
                var seed = terrainSeed != 0 ? terrainSeed : 42;
                var g64 = new CityGrid(64, 64);
                var heightMap = Loopolis.Core.Grid.HeightMapGenerator.Generate(64, 64, seed);
                var forestMap = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(64, 64, seed);
                g64.ApplyHeightMap(heightMap);
                g64.ApplyForestMap(forestMap);
                // Border connection at center of south edge — force flat terrain so it can always be placed
                g64.SetHeightLevel(32, 63, 1);
                g64.PlaceBorderConnection(32, 63);
                // Starter road spine heading north — force flat terrain on each tile
                g64.SetHeightLevel(32, 62, 1); g64.SetZone(32, 62, ZoneType.Road);
                g64.SetHeightLevel(32, 61, 1); g64.SetZone(32, 61, ZoneType.Road);
                g64.SetHeightLevel(32, 60, 1); g64.SetZone(32, 60, ZoneType.Road);
                Console.WriteLine($"[generated_map] Border connection at (32,63), starter spine (32,62–60), seed={seed}");
                var engine64 = new SimulationEngine(g64, budget, population, power, roads, demand);
                engine64.SeedRoadGraphFromGrid();
                return (g64, engine64);
            }

            case "no_power":
                // Zones with roads but no power → no growth
                // Explicit flat terrain so road cliff constraint never fires
                grid.SetFlatTerrain();
                grid.SetZone(5, 5, ZoneType.Road);
                grid.SetZone(6, 5, ZoneType.Residential);
                grid.SetZone(7, 5, ZoneType.Residential);
                grid.SetZone(8, 5, ZoneType.Residential);
                break;

            case "no_roads":
                // Zones with power but no road access → no growth
                grid.SetFlatTerrain();
                grid.SetZone(5, 5, ZoneType.PowerPlant);
                grid.SetZone(6, 5, ZoneType.PowerLine);
                grid.SetZone(7, 5, ZoneType.Residential);  // powered but no road adjacent
                grid.SetZone(7, 6, ZoneType.Residential);
                grid.SetZone(7, 7, ZoneType.Residential);
                break;

            case "town":
                grid.SetFlatTerrain();
                // Roads form a cross
                for (var x = 5; x <= 25; x++) grid.SetZone(x, 15, ZoneType.Road);
                for (var y = 5; y <= 25; y++) grid.SetZone(15, y, ZoneType.Road);
                // Power plant + power line running all the way to the vertical road
                grid.SetZone(8, 8, ZoneType.PowerPlant);
                for (var x = 9; x <= 15; x++) grid.SetZone(x, 8, ZoneType.PowerLine);
                // Residential blocks (top-left quadrant, touching the vertical road at x=15)
                for (var x = 6; x <= 14; x++)
                for (var y = 10; y <= 14; y++)
                    if (grid.GetTile(x, y).Zone == ZoneType.Empty)
                        grid.SetZone(x, y, ZoneType.Residential);
                // Commercial strip along horizontal road (right side)
                for (var x = 16; x <= 24; x++)
                    grid.SetZone(x, 14, ZoneType.Commercial);
                // Industrial bottom-right quadrant — start at y=16 so it touches the road at y=15
                for (var x = 17; x <= 24; x++)
                for (var y = 16; y <= 24; y++)
                    grid.SetZone(x, y, ZoneType.Industrial);
                break;

            case "mixed":
                // Residential mixed with industrial nearby — tests pollution penalty on growth.
                //
                // Layout (32x32 grid):
                //   Power plant at (5,5), power line runs east to vertical road at x=15
                //   Main road: vertical at x=15 (y=3..28), horizontal at y=15 (x=5..28)
                //   Industrial block: south section (x=16..24, y=17..24) — near the road
                //   Residential north: safe distance from industrial (y=5..13)
                //   Residential polluted: immediately adjacent to industrial block (x=16..18, y=16 — one row away)
                //
                // Expected: northern R zones grow at base happiness; southern R zones near industrial
                //           suffer heavy pollution penalty → much slower growth

                grid.SetFlatTerrain();
                // Power
                grid.SetZone(5, 5, ZoneType.PowerPlant);
                for (var x = 6; x <= 15; x++) grid.SetZone(x, 5, ZoneType.PowerLine);

                // Roads: vertical at x=15, horizontal at y=15
                for (var y = 3; y <= 28; y++) grid.SetZone(15, y, ZoneType.Road);
                for (var x = 5; x <= 28; x++) grid.SetZone(x, 15, ZoneType.Road);

                // Industrial block: south-east (y=17..24)
                for (var x = 16; x <= 24; x++)
                for (var y = 17; y <= 24; y++)
                    grid.SetZone(x, y, ZoneType.Industrial);

                // Residential north (safe from pollution — ~8+ tiles from nearest industrial)
                for (var x = 6; x <= 14; x++)
                for (var y = 6; y <= 13; y++)
                    if (grid.GetTile(x, y).Zone == ZoneType.Empty)
                        grid.SetZone(x, y, ZoneType.Residential);

                // Polluted residential: just north of industrial, touching the road at y=15 (y=16 but road is there)
                // Place them at y=14 (one tile north of road), adjacent to industrial via road gap
                // Actually: industrial starts at y=17, so at y=16 is still fine; road is at y=15
                // R at y=14 is 3 tiles from industrial (y=17), which is right at the pollution radius edge
                // For stronger effect, place some R adjacent to x=15 road at y=16 (just below road)
                for (var x = 16; x <= 18; x++)
                    grid.SetZone(x, 16, ZoneType.Residential); // just 1 tile from industrial at y=17

                break;

            case "services":
                // City with school and fire station — tests road-graph service coverage bonus.
                //
                // Layout: compact road network with services placed on road-adjacent tiles
                //   CoalPlant at (2,15) powers the whole grid via road adjacency
                //   Main E-W road at y=15 (x=3..26)
                //   North spur at x=15 (y=9..15)
                //   Residential row north: (x=6..14, y=14) — adjacent to y=15 road
                //   Residential row south: (x=6..14, y=16) — adjacent to y=15 road
                //   School at (15,8): adjacent to north spur (15,9) — covers north residents
                //   FireStation at (16,15): adjacent to main road at (15,15) — covers south residents
                //
                // Road-graph coverage:
                //   North resident (x,14) road-neighbor = (x,15); school road-neighbor = (15,9)
                //   graph distance (x,15)→(15,15)→...→(15,9) ≤ 10.0 (School radius) → covered
                //   South resident (x,16) road-neighbor = (x,15); fire road-neighbor = (15,15)
                //   graph distance (x,15)→(15,15) ≤ 8.0 (FireStation radius) → covered
                //
                // Expected: north residents covered by School, south by FireStation → happiness 0.75+

                grid.SetFlatTerrain();
                // Power
                grid.SetZone(2, 15, ZoneType.CoalPlant);

                // Roads
                for (var x = 3; x <= 26; x++) grid.SetZone(x, 15, ZoneType.Road); // main E-W
                for (var y = 9; y <= 14; y++) grid.SetZone(15, y, ZoneType.Road);  // north spur

                // Residential: single row north and south of main road (all road-adjacent)
                for (var x = 6; x <= 14; x++) grid.SetZone(x, 14, ZoneType.Residential); // north of road, adj to road
                for (var x = 6; x <= 14; x++) grid.SetZone(x, 16, ZoneType.Residential); // south of road, adj to road

                // School: adjacent to top of north spur (15,9), covers all north residents via road graph
                // Road-graph: school road-neighbor=(15,9); north resident (x,14) road-neighbor=(x,15)
                // distance (x,15)→(15,15): |x-15| road edges, then (15,15)→(15,9): 6 road edges
                // max distance for (6,14): (6,15)→(15,15) = 9 + (15,9) = 6 → total 15.0 > 10.0 (School)
                // so place school closer: use PoliceStation at (15,8) adjacent to north spur (15,9)
                // but north residents at x=11..14 are within 0..4 + 6 = 6..10 → borderline
                // Use only x=11..14 for north to ensure coverage:
                for (var x = 6;  x <= 14; x++) grid.SetZone(x, 14, ZoneType.Empty); // clear previous
                for (var x = 11; x <= 14; x++) grid.SetZone(x, 14, ZoneType.Residential); // near x=15 spur
                grid.SetZone(15, 8, ZoneType.School);  // adj to (15,9); from (11,15): distance=4+(15,15→15,9)=4+6=10 ≤ 10

                // Fire station: on road at (16,15) (adjacent to (15,15)), covers south residents
                // From (11,16) road-neighbor=(11,15): distance (11,15)→(16,15)=5 → adjacent to station→0 (same neighbor)
                // Wait: fire station at (16,15) is itself a Road? No—place it ADJACENT to road.
                // FireStation at (16,14): adj to (16,15) road. From south resident (11,16) road-neighbor=(11,15)
                // dist (11,15)→(16,15)=5; then fire station neighbor=(16,15); total 5 ≤ 8 → covered
                grid.SetZone(16, 14, ZoneType.FireStation); // adj to main road (16,15)

                // Commercial east of road junction for demand boost
                for (var x = 17; x <= 22; x++) grid.SetZone(x, 16, ZoneType.Commercial);

                break;

            case "city_path":
                // Compact mixed-use foundation designed to reach City milestone (5,000 pop).
                grid.SetFlatTerrain();
                // Power — plant at (2,15), road starts at (3,15) immediately adjacent
                grid.SetZone(2, 15, ZoneType.CoalPlant);

                // Roads
                for (var x = 3; x <= 24; x++) grid.SetZone(x, 15, ZoneType.Road);    // main E-W spine
                for (var y = 11; y <= 14; y++) grid.SetZone(16, y, ZoneType.Road);    // north spur

                // Residential — west row (road access via y=15 main road)
                for (var x = 7; x <= 15; x++) grid.SetZone(x, 14, ZoneType.Residential);

                // Residential — east column (road access via x=16 north spur)
                for (var y = 11; y <= 14; y++) grid.SetZone(17, y, ZoneType.Residential);

                // Commercial south of main road — Chebyshev-3 demand boost to west + center residential
                for (var x = 9; x <= 12; x++) grid.SetZone(x, 16, ZoneType.Commercial);

                // Industrial — two tiles placed so every residential tile is within 8 Manhattan distance
                // (7,16): covers west residential; (16,16): covers east residential + center
                grid.SetZone(7,  16, ZoneType.Industrial);
                grid.SetZone(16, 16, ZoneType.Industrial);

                // Services
                grid.SetZone(8,  16, ZoneType.FireStation);    // covers west residential cluster (road-adjacent to spine)
                grid.SetZone(14, 16, ZoneType.PoliceStation);  // covers center + east residential (road-adjacent to spine)
                // School at (13,16): road-adjacent to (13,15). Graph distance to west residential (7,14) ≈ 7.
                // Graph distance to east residential (17,14) ≈ 5. Both well within School radius 10.0.
                grid.SetZone(13, 16, ZoneType.School);

                // Park near residential center — gives +0.08/tile happiness to Chebyshev-3 neighbours (cap +0.20)
                grid.SetZone(11, 13, ZoneType.Park);
                break;

            case "powered_start":
                // Like default but pre-built with fire+police coverage — tests mid-game growth without neglect cascade.
                grid.SetFlatTerrain();
                grid.SetZone(5, 12, ZoneType.CoalPlant);
                for (var x = 6; x <= 16; x++) grid.SetZone(x, 12, ZoneType.Road);
                // Residential north of road — two rows so res_townhouse_2x2 (2×2 footprint) can grow
                for (var x = 9; x <= 14; x++) grid.SetZone(x, 11, ZoneType.Residential);
                for (var x = 9; x <= 14; x++) grid.SetZone(x, 10, ZoneType.Residential);
                // Commercial south of road
                grid.SetZone(9,  13, ZoneType.Commercial);
                grid.SetZone(10, 13, ZoneType.Commercial);
                grid.SetZone(11, 13, ZoneType.Commercial);
                // Industrial far west, south of road (away from residential)
                grid.SetZone(6,  13, ZoneType.Industrial);
                grid.SetZone(7,  13, ZoneType.Industrial);
                // Services — pre-built so players see what covered growth looks like
                grid.SetZone(8,  13, ZoneType.FireStation);
                grid.SetZone(13, 13, ZoneType.PoliceStation);
                grid.SetZone(15, 11, ZoneType.School);
                break;

            case "stress_test":
            {
                // Dense 16×16 grid of R/C/I zones with roads + power.
                // Designed to exercise BuildingGrowthSystem and BuildingDegradationSystem under real pressure.
                // Phase 1 (ticks 0–199): city grows and buildings tier up.
                // Phase 2 (ticks 200–399): power plant removed → degradation fires for all multi-tile buildings.
                //
                // Layout (grid 32×32, zones in top-left 16×16 block):
                //   Power plant at (0,0)
                //   Horizontal road spine at y=1 (x=0..16)
                //   Vertical road spine at x=0 (y=0..16)
                //   Residential: x=1..8, y=2..9  (8×8 = 64 tiles)
                //   Commercial:  x=9..12, y=2..5  (4×4 = 16 tiles adjacent to R for demand boost)
                //   Industrial:  x=9..12, y=6..9  (4×4 = 16 tiles)
                //
                // Note: this scenario is used by "dotnet run -- 400 stress_test" where the runner
                // runs all 400 ticks. Power removal is done by the SimulationRunner wrapping this scenario.
                // For the base 400-tick run we keep the power plant in place so buildings grow first,
                // then use SetupStressTestPhase2 for the second half.
                grid.SetFlatTerrain();
                // Power plant + lines running along road spines
                grid.SetZone(0, 0, ZoneType.CoalPlant);
                for (var x = 1; x <= 16; x++) grid.SetZone(x, 0, ZoneType.PowerLine);
                // Roads: horizontal at y=1, vertical at x=0
                for (var x = 0; x <= 16; x++) grid.SetZone(x, 1, ZoneType.Road);
                for (var y = 2; y <= 16; y++) grid.SetZone(0, y, ZoneType.Road);
                // Residential block
                for (var x = 1; x <= 8; x++)
                for (var y = 2; y <= 9; y++)
                    grid.SetZone(x, y, ZoneType.Residential);
                // Commercial strip (demand boost for residential)
                for (var x = 9; x <= 12; x++)
                for (var y = 2; y <= 5; y++)
                    grid.SetZone(x, y, ZoneType.Commercial);
                // Industrial block
                for (var x = 9; x <= 12; x++)
                for (var y = 6; y <= 9; y++)
                    grid.SetZone(x, y, ZoneType.Industrial);
                // Services — placed just below residential block, adjacent to vertical road at x=0
                grid.SetZone(1, 10, ZoneType.FireStation);
                grid.SetZone(1, 11, ZoneType.PoliceStation);
                grid.SetZone(1, 12, ZoneType.School);
                break;
            }

            case "cottage_start":
                // Tests P1: road-only cottage growth, then power upgrade payoff.
                // NO power plant — residential grows as unpowered cottages (cap 25, 0.7× tax).
                grid.SetFlatTerrain();
                grid.PlaceBorderConnection(16, 31);
                for (var x = 13; x <= 19; x++) grid.SetZone(x, 30, ZoneType.Road);   // E-W road
                for (var x = 13; x <= 19; x++) grid.SetZone(x, 29, ZoneType.Residential); // R north of road
                for (var x = 13; x <= 19; x++) grid.SetZone(x, 31, ZoneType.Commercial);  // C south of road
                // No power plant — cottages should still appear
                break;

            case "island_chain":
            {
                // Island Chain challenge: 64×64 archipelago with ~40% water.
                var g64ic = new CityGrid(64, 64);
                var heightMapIc = Loopolis.Core.Grid.HeightMapGenerator.GenerateNamed("island_chain", 64, 64);
                var forestMapIc = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(64, 64, seed: 0xC0FFEE);
                g64ic.ApplyHeightMap(heightMapIc);
                g64ic.ApplyForestMap(forestMapIc);
                // Border connection at center of south edge — force flat terrain
                g64ic.SetHeightLevel(32, 63, 1);
                g64ic.PlaceBorderConnection(32, 63);
                // Starter road spine heading north
                g64ic.SetHeightLevel(32, 62, 1); g64ic.SetZone(32, 62, ZoneType.Road);
                g64ic.SetHeightLevel(32, 61, 1); g64ic.SetZone(32, 61, ZoneType.Road);
                g64ic.SetHeightLevel(32, 60, 1); g64ic.SetZone(32, 60, ZoneType.Road);
                var budgetIc = new BudgetSystem(initialBalance: 6_500);
                Console.WriteLine("[island_chain] Border connection at (32,63), starter spine (32,62–60)");
                var engineIc = new SimulationEngine(g64ic, budgetIc, population, power, roads, demand);
                engineIc.SeedRoadGraphFromGrid();
                return (g64ic, engineIc);
            }

            case "narrow_valley":
            {
                // Narrow Valley challenge: 128×128 map with mountain walls on east/west.
                var g128nv = new CityGrid(128, 128);
                var heightMapNv = Loopolis.Core.Grid.HeightMapGenerator.GenerateNamed("narrow_valley", 128, 128);
                var forestMapNv = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(128, 128, seed: 0xBADC0DE);
                g128nv.ApplyHeightMap(heightMapNv);
                g128nv.ApplyForestMap(forestMapNv);
                // Border connection at center of south edge — force flat terrain
                g128nv.SetHeightLevel(64, 127, 1);
                g128nv.PlaceBorderConnection(64, 127);
                // Starter road spine heading north
                g128nv.SetHeightLevel(64, 126, 1); g128nv.SetZone(64, 126, ZoneType.Road);
                g128nv.SetHeightLevel(64, 125, 1); g128nv.SetZone(64, 125, ZoneType.Road);
                g128nv.SetHeightLevel(64, 124, 1); g128nv.SetZone(64, 124, ZoneType.Road);
                var budgetNv = new BudgetSystem(initialBalance: 7_000);
                Console.WriteLine("[narrow_valley] Border connection at (64,127), starter spine (64,126–124)");
                var engineNv = new SimulationEngine(g128nv, budgetNv, population, power, roads, demand);
                engineNv.SeedRoadGraphFromGrid();
                return (g128nv, engineNv);
            }

            case "river_delta":
            {
                // River Delta challenge: 64×64 mostly flat with diagonal water channels.
                var g64rd = new CityGrid(64, 64);
                var heightMapRd = Loopolis.Core.Grid.HeightMapGenerator.GenerateNamed("river_delta", 64, 64);
                var forestMapRd = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(64, 64, seed: 0xDE17A1);
                g64rd.ApplyHeightMap(heightMapRd);
                g64rd.ApplyForestMap(forestMapRd);
                // Border connection at center of south edge — force flat terrain
                g64rd.SetHeightLevel(32, 63, 1);
                g64rd.PlaceBorderConnection(32, 63);
                // Starter road spine heading north
                g64rd.SetHeightLevel(32, 62, 1); g64rd.SetZone(32, 62, ZoneType.Road);
                g64rd.SetHeightLevel(32, 61, 1); g64rd.SetZone(32, 61, ZoneType.Road);
                g64rd.SetHeightLevel(32, 60, 1); g64rd.SetZone(32, 60, ZoneType.Road);
                var budgetRd = new BudgetSystem(initialBalance: 5_000);
                Console.WriteLine("[river_delta] Border connection at (32,63), starter spine (32,62–60)");
                var engineRd = new SimulationEngine(g64rd, budgetRd, population, power, roads, demand);
                engineRd.SeedRoadGraphFromGrid();
                return (g64rd, engineRd);
            }

            case "polluted_legacy":
            {
                // 64×64 procedural terrain with a pre-built industrial cluster in the center.
                // Player inherits a running industrial district and must clean it up and grow.
                var seed = terrainSeed != 0 ? terrainSeed : 42;
                var gPl = new CityGrid(64, 64);
                var heightMapPl = Loopolis.Core.Grid.HeightMapGenerator.Generate(64, 64, seed);
                var forestMapPl = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(64, 64, seed);
                gPl.ApplyHeightMap(heightMapPl);
                gPl.ApplyForestMap(forestMapPl);
                // Border connection at center of south edge — force flat terrain
                gPl.SetHeightLevel(32, 63, 1);
                gPl.PlaceBorderConnection(32, 63);
                // Starter road spine heading north
                gPl.SetHeightLevel(32, 62, 1); gPl.SetZone(32, 62, ZoneType.Road);
                gPl.SetHeightLevel(32, 61, 1); gPl.SetZone(32, 61, ZoneType.Road);
                gPl.SetHeightLevel(32, 60, 1); gPl.SetZone(32, 60, ZoneType.Road);

                var budgetPl = new BudgetSystem(initialBalance: 7_000);
                var enginePl = new SimulationEngine(gPl, budgetPl, population, power, roads, demand);
                enginePl.SeedRoadGraphFromGrid();

                // Set up pre-existing industrial cluster (inherited from old regime)
                var cx = gPl.Width / 2;   // 32
                var cy = gPl.Height / 2;  // 32

                // Flatten the center cluster area so placement always succeeds
                for (var fx = cx - 4; fx <= cx + 4; fx++)
                for (var fy = cy - 4; fy <= cy + 4; fy++)
                {
                    gPl.SetHeightLevel(fx, fy, 1);
                    gPl.SetForest(fx, fy, false);
                }

                // Power plant at center-north of cluster
                enginePl.PlaceTile(cx, cy - 3, ZoneType.CoalPlant);
                // Power lines southward through cluster
                for (var py = cy - 2; py <= cy + 2; py++)
                    enginePl.PlaceTile(cx, py, ZoneType.PowerLine);

                // Road through the center (E-W)
                for (var rx = cx - 3; rx <= cx + 3; rx++)
                    enginePl.PlaceTile(rx, cy, ZoneType.Road);

                // Industrial tiles north and south of road
                for (var ix = cx - 2; ix <= cx + 2; ix += 2)
                {
                    enginePl.PlaceTile(ix, cy - 1, ZoneType.Industrial);
                    enginePl.PlaceTile(ix, cy + 1, ZoneType.Industrial);
                }

                // Run a few ticks to let industry develop before player starts
                enginePl.PowerNetwork.Propagate(gPl);
                enginePl.RoadNetwork.Propagate(gPl);
                for (var t = 0; t < 20; t++) enginePl.Tick();

                // Restore starting balance AFTER setup costs (factories are already running)
                var plScenario = Loopolis.Core.Scenarios.ScenarioLibrary.Find("polluted_legacy");
                if (plScenario != null)
                    budgetPl.SetBalance(plScenario.StartingBalance);
                else
                    budgetPl.SetBalance(7_000);

                enginePl.ActiveScenario = Loopolis.Core.Scenarios.ScenarioLibrary.Find("polluted_legacy");
                Console.WriteLine($"[polluted_legacy] Pre-built industrial cluster at ({cx},{cy}), 20 warm-up ticks, balance reset to ${budgetPl.Balance:N0}");
                return (gPl, enginePl);
            }

            case "green_city":
            {
                // 64×64 procedural terrain — no industrial zones allowed (enforced by scenario DisabledZones).
                var seed = terrainSeed != 0 ? terrainSeed : 77;
                var gGc = new CityGrid(64, 64);
                var heightMapGc = Loopolis.Core.Grid.HeightMapGenerator.Generate(64, 64, seed);
                var forestMapGc = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(64, 64, seed);
                gGc.ApplyHeightMap(heightMapGc);
                gGc.ApplyForestMap(forestMapGc);
                gGc.SetHeightLevel(32, 63, 1);
                gGc.PlaceBorderConnection(32, 63);
                gGc.SetHeightLevel(32, 62, 1); gGc.SetZone(32, 62, ZoneType.Road);
                gGc.SetHeightLevel(32, 61, 1); gGc.SetZone(32, 61, ZoneType.Road);
                gGc.SetHeightLevel(32, 60, 1); gGc.SetZone(32, 60, ZoneType.Road);
                var budgetGc = new BudgetSystem(initialBalance: 6_000);
                Console.WriteLine($"[green_city] Border connection at (32,63), starter spine, seed={seed}");
                var engineGc = new SimulationEngine(gGc, budgetGc, population, power, roads, demand);
                engineGc.SeedRoadGraphFromGrid();
                engineGc.ActiveScenario = Loopolis.Core.Scenarios.ScenarioLibrary.Find("green_city");
                return (gGc, engineGc);
            }

            case "service_first":
            {
                // 64×64 procedural terrain — no commercial zones allowed (enforced by scenario DisabledZones).
                var seed = terrainSeed != 0 ? terrainSeed : 55;
                var gSf = new CityGrid(64, 64);
                var heightMapSf = Loopolis.Core.Grid.HeightMapGenerator.Generate(64, 64, seed);
                var forestMapSf = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(64, 64, seed);
                gSf.ApplyHeightMap(heightMapSf);
                gSf.ApplyForestMap(forestMapSf);
                gSf.SetHeightLevel(32, 63, 1);
                gSf.PlaceBorderConnection(32, 63);
                gSf.SetHeightLevel(32, 62, 1); gSf.SetZone(32, 62, ZoneType.Road);
                gSf.SetHeightLevel(32, 61, 1); gSf.SetZone(32, 61, ZoneType.Road);
                gSf.SetHeightLevel(32, 60, 1); gSf.SetZone(32, 60, ZoneType.Road);
                var budgetSf = new BudgetSystem(initialBalance: 5_000);
                Console.WriteLine($"[service_first] Border connection at (32,63), starter spine, seed={seed}");
                var engineSf = new SimulationEngine(gSf, budgetSf, population, power, roads, demand);
                engineSf.SeedRoadGraphFromGrid();
                engineSf.ActiveScenario = Loopolis.Core.Scenarios.ScenarioLibrary.Find("service_first");
                return (gSf, engineSf);
            }

            case "city_challenge":
            {
                // 64×64 generated terrain, limited $3,000 starting balance — "limited funds + big goal" scenario.
                var seed = terrainSeed != 0 ? terrainSeed : 42;
                var gCc = new CityGrid(64, 64);
                var heightMapCc = Loopolis.Core.Grid.HeightMapGenerator.Generate(64, 64, seed);
                var forestMapCc = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(64, 64, seed);
                gCc.ApplyHeightMap(heightMapCc);
                gCc.ApplyForestMap(forestMapCc);
                // Border connection at center of south edge — force flat terrain so it can always be placed
                gCc.SetHeightLevel(32, 63, 1);
                gCc.PlaceBorderConnection(32, 63);
                // Starter road spine heading north — force flat terrain on each tile
                gCc.SetHeightLevel(32, 62, 1); gCc.SetZone(32, 62, ZoneType.Road);
                gCc.SetHeightLevel(32, 61, 1); gCc.SetZone(32, 61, ZoneType.Road);
                gCc.SetHeightLevel(32, 60, 1); gCc.SetZone(32, 60, ZoneType.Road);
                var budgetCc = new BudgetSystem(initialBalance: 3_000);
                Console.WriteLine($"[city_challenge] Border connection at (32,63), starter spine (32,62–60), seed={seed}, balance=$3,000");
                var engineCc = new SimulationEngine(gCc, budgetCc, population, power, roads, demand);
                engineCc.SeedRoadGraphFromGrid();
                engineCc.ActiveScenario = Loopolis.Core.Scenarios.ScenarioLibrary.Find("city_challenge");
                return (gCc, engineCc);
            }

            case "boom_town":
            {
                // 64×64 generated terrain, generous $15,000 starting balance — "money is no constraint, build fast" scenario.
                var seed = terrainSeed != 0 ? terrainSeed : 42;
                var gBt = new CityGrid(64, 64);
                var heightMapBt = Loopolis.Core.Grid.HeightMapGenerator.Generate(64, 64, seed);
                var forestMapBt = Loopolis.Core.Grid.HeightMapGenerator.GenerateForest(64, 64, seed);
                gBt.ApplyHeightMap(heightMapBt);
                gBt.ApplyForestMap(forestMapBt);
                // Border connection at center of south edge — force flat terrain so it can always be placed
                gBt.SetHeightLevel(32, 63, 1);
                gBt.PlaceBorderConnection(32, 63);
                // Starter road spine heading north — force flat terrain on each tile
                gBt.SetHeightLevel(32, 62, 1); gBt.SetZone(32, 62, ZoneType.Road);
                gBt.SetHeightLevel(32, 61, 1); gBt.SetZone(32, 61, ZoneType.Road);
                gBt.SetHeightLevel(32, 60, 1); gBt.SetZone(32, 60, ZoneType.Road);
                var budgetBt = new BudgetSystem(initialBalance: 15_000);
                Console.WriteLine($"[boom_town] Border connection at (32,63), starter spine (32,62–60), seed={seed}, balance=$15,000");
                var engineBt = new SimulationEngine(gBt, budgetBt, population, power, roads, demand);
                engineBt.SeedRoadGraphFromGrid();
                engineBt.ActiveScenario = Loopolis.Core.Scenarios.ScenarioLibrary.Find("boom_town");
                return (gBt, engineBt);
            }

            default:
                // Empty new-game start with a border connection road from the south edge.
                // Player must build their own infrastructure.
                grid.SetFlatTerrain();
                // Border connection — center of south edge, unerasable Regional Highway
                grid.PlaceBorderConnection(16, 31);
                // Starter spine — 3 road tiles heading north from border
                grid.SetZone(16, 30, ZoneType.Road);
                grid.SetZone(16, 29, ZoneType.Road);
                grid.SetZone(16, 28, ZoneType.Road);
                break;
        }

        var engine = new SimulationEngine(grid, budget, population, power, roads, demand);
        engine.SeedRoadGraphFromGrid();   // seed road graph from any roads placed during scenario setup
        return (grid, engine);
    }
}
