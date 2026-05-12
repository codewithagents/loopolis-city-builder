# Loopolis — CLAUDE.md

## What Is This

A SimCity-style indie city builder targeting Steam release.
Built by Benjamin + Cairn (AI agent) with agent-driven feedback loops as a core development strategy.

## Architecture

```
src/Loopolis.Core/        — Pure C# simulation logic. Zero Godot dependencies.
src/Loopolis.Runner/      — CLI headless runner + persistent server mode.
tests/Loopolis.Core.Tests/ — NUnit tests. Run with: dotnet test
godot/                    — Godot 4 project (presentation layer only).
  shared/                 — file-based IPC: state.json (Runner→Godot), command.json (Godot→Runner)
```

**Rule:** Core logic never imports Godot. Godot reads Core state and renders it.

## Development

**Run tests (Core logic — 504 tests):**
```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
export PATH="$DOTNET_ROOT:$PATH"
dotnet test
```

**Check Godot scripts compile (catch missing methods before opening editor):**
```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
export PATH="$DOTNET_ROOT:$PATH"
cd godot && dotnet build Loopolis.Godot.csproj && cd ..
```

**Run both (recommended after any Godot script change):**
```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
export PATH="$DOTNET_ROOT:$PATH"
dotnet test && cd godot && dotnet build Loopolis.Godot.csproj && cd ..
```

**Run simulation (agent feedback loop):**
```bash
dotnet run --project src/Loopolis.Runner -- 500 default
dotnet run --project src/Loopolis.Runner -- 1000 powered_start
dotnet run --project src/Loopolis.Runner -- 500 no_power
```
Output is JSON — designed for agent analysis.

## Core Systems (all done)

1. ✅ CityGrid — tile grid, zone placement, adjacency, terrain (Water blocks placement). Each tile has optional `BuildingId`.
2. ✅ TerrainType — height levels (0=water, 1=flat, 2+=elevated). Forest flag per tile. Surcharges: forest +$75, height≥2 +$50.
3. ✅ BudgetSystem — tax income, costs, balance, deficit. Land-value tax multiplier. Default start: $4,000
4. ✅ PopulationSystem — road-edge only (no interior spawns). Growth gated on `tile.BuildingId != null`.
5. ✅ PowerNetwork — BFS flood-fill from power plants; services (Fire/Police/School) are conductors
6. ✅ RoadNetwork — direct adjacency validation
7. ✅ DemandSystem — commercial adjacency boosts residential growth (1.5×, Chebyshev-3 radius)
8. ✅ PollutionSystem — industrial tiles emit pollution, reduces happiness of nearby residential
9. ✅ HappinessSystem — service coverage (road-graph reachability), neglect decay (cap 0.20), tax modifier, commute penalty, unemployment
10. ✅ MilestoneSystem — thresholds Town(500)/City(5k)/Metropolis(25k)/Loopolis(100k), bankruptcy, abandonment (threshold 0.25, 50-tick window)
11. ✅ EventSystem — 4 events (FireBreak/CrimeWave/PowerOutage/DemandSlump), 2%/tick trigger, 60-tick honeymoon
12. ✅ EmploymentSystem — industrial activity → jobs (0.4/unit, 20 jobs/full tile), residential growth throttled above pop 100
13. ✅ SimulationEngine — orchestrates all systems per tick
14. ✅ SaveSystem (Persistence) v2 — Capture/Serialize/Deserialize/RestoreGrid; terrain seed saves exact map; buildings persisted
15. ✅ BuildingGrowthSystem — road-adjacent zone tiles → 1×1 base buildings; ≥80% capacity → tries to grow to next tier
16. ✅ BuildingCatalog — 13 building types across R/C/I + hillside villa (see below)
17. ✅ RoadTrafficSystem — real worker-flow edge traffic (not heuristic). Road capacity 80 workers, Avenue 200.
18. ✅ PowerCapacitySystem — MW supply vs demand; brownout penalty when supply < demand
19. ✅ LandValueSystem — per-tile float: plateau +0.35, forest +0.08, low pollution, high happiness, power
20. ✅ **RoadGraph (G1)** — weighted Dijkstra graph built from road tiles. `GetDistance`, `IsReachable`, `GetConnectedComponent`, `ShortestPathSourceMap`. Road=1.0 weight, Avenue=0.5.
21. ✅ **Service Coverage (G2)** — coverage uses road-graph distance, not Manhattan. Radii: Fire/Police=8.0, School=10.0, Hospital=12.0. No road = no coverage.
22. ✅ **Worker Flows (G3)** — R→I worker routing via road graph. Edge traffic accumulates per tick. Chokepoints see real congestion. `WorkerFlowSystem` exposes workersRouted, averageCommuteDistance, unroutedWorkers, overloadedEdges.
23. ✅ **Service Capacity (G4)** — School=200 seats, Police=300, Fire=400 bldgs, Hospital=80 beds. Closest-first drain. Capacity shown in HUD.
24. ✅ HeightMapGenerator — diamond-square, dynamic canvas (33→129 for 128×128 maps). Scenarios: 32×32 (default), 64×64 (generated_map), 128×128 (generated_128).
25. ✅ Border Road — `IsBorderConnection` flag (unerasable). One Regional Highway enters from center-south edge. R-tiles within graph-distance 12 get 1.2× growth multiplier. `ExternalAnchor` in RoadGraph for future worker routing.
26. ✅ Power-as-Density Unlock (P1) — `res_house_1x1` forms from road access only (no power). Unpowered cottage: capacity 25, 0.7× tax. Powered: capacity 50, full tax. All 2×2+ buildings require all footprint tiles powered. Unpowered industrial: 2 placeholder jobs, zero pollution.
27. ✅ BuildingDegradationSystem — Multi-tile buildings that lose power or road access have 2% chance/tick to demolish back to bare zone. `LastDegradedBuildings` on engine. `BuildingCatalog.GetZoneForBuilding()` helper.

**473 tests · 0 failures**

## Building Catalog

Buildings grow organically from road edges. Interior tiles only develop if a building expands into them.

| TypeId | Zone | Size | Unlock | Special condition |
|--------|------|------|--------|------------------|
| `res_house_1x1` | R | 1×1 | Always | Road adjacent |
| `res_townhouse_2x2` | R | 2×2 | Always | Road access |
| `res_villa_2x3` | R | 2×3 | Town | Forest tile within 3 (Chebyshev) |
| `res_villa_3x2` | R | 3×2 | Town | Forest tile within 3 (Chebyshev) |
| `res_apartment_4x4` | R | 4×4 | City | School + Police + Fire coverage |
| `com_shop_1x1` | C | 1×1 | Always | Road adjacent |
| `com_strip_1x3` | C | 1×3 | Town | Road access |
| `com_strip_3x1` | C | 3×1 | Town | Road access |
| `com_shopping_3x3` | C | 3×3 | City | Road access |
| `ind_factory_1x1` | I | 1×1 | Always | Road adjacent |
| `ind_warehouse_2x2` | I | 2×2 | Town | Road access |
| `ind_park_4x2` | I | 4×2 | City | Road access |
| `ind_park_2x4` | I | 2×4 | City | Road access |

Growth trigger: building population ≥ 80% of capacity (tile count × 50).
Absorption: smaller buildings inside the new footprint are absorbed ("big eats small").
Erase behavior: erasing any tile in a multi-tile building demolishes the whole building.

## Running the Game

### Viewer mode (simulation server + Godot viewer)
```bash
# Terminal 1 — start simulation server
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
dotnet run --project src/Loopolis.Runner -- server default --speed 2

# Terminal 2 — launch Godot viewer
DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec \
  /Applications/Godot_mono.app/Contents/MacOS/Godot \
  --path /Users/benjamin.eckstein/IdeaProjects/private/loopolis/godot/ \
  --editor
# → Open scenes/World.tscn → Press F5
```

### Standalone mode (Godot runs its own simulation)
```bash
# Don't run the server. Just open Godot — no state.json → standalone mode.
DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec \
  /Applications/Godot_mono.app/Contents/MacOS/Godot \
  --path /Users/benjamin.eckstein/IdeaProjects/private/loopolis/godot/ \
  --editor
```

### Send commands to running server
```bash
# Use session-ID filenames — server prints "[loopolis] session=XXXXXXXX" on start
SESSION_ID=$(grep '\[loopolis\] session=' /tmp/loopolis.log | head -1 | sed 's/.*session=//' | tr -d '[:space:]')

echo "{\"cmd\":\"pause\",\"sessionId\":\"$SESSION_ID\"}"                             > godot/shared/command-${SESSION_ID}.json
echo "{\"cmd\":\"place_zone\",\"x\":10,\"y\":14,\"zone\":\"Road\",\"sessionId\":\"$SESSION_ID\"}" > godot/shared/command-${SESSION_ID}.json
echo "{\"cmd\":\"skip\",\"ticks\":500,\"pauseAfter\":true,\"sessionId\":\"$SESSION_ID\"}"         > godot/shared/command-${SESSION_ID}.json
echo "{\"cmd\":\"resume\",\"sessionId\":\"$SESSION_ID\"}"                            > godot/shared/command-${SESSION_ID}.json
```

### In-game Save/Load (standalone mode)
```
Ctrl+S  — save to godot/saves/autosave.json (EventLog shows "Game saved ✓")
Ctrl+L  — load from godot/saves/autosave.json (restores terrain, zones, pop, balance, tick)
```

### macOS .NET fix (one-time setup, already done)
```bash
ln -sf /opt/homebrew/opt/dotnet/libexec/host ~/.dotnet/host
```

## Agent Feedback Loop Protocol

When Cairn runs scenarios, output JSON is analyzed for:
- Did the city survive N ticks without bankruptcy?
- Population growth rate — too fast? too slow?
- Which systems are bottlenecks?
- Edge cases (negative numbers, infinite growth, etc.)

## Testing Philosophy

- Every Core class has a corresponding test file
- Test file mirrors source structure: `Grid/CityGridTests.cs` tests `Grid/CityGrid.cs`
- No test = no merge for Core logic
- Tests run in under 5 seconds — no Godot startup required

## Tech Stack

- Language: C# (.NET 10)
- Testing: NUnit
- Game Engine: Godot 4.4.1 .NET edition — installed at `/Applications/Godot_mono.app`
- Target: Steam (Windows / Mac / Linux)
