# STATUS.md — Loopolis

> Project status, WIP tracker, and short-term task queue.
> Update this at the start and end of every session.

---

## Current Milestone

**Milestone 1 — Simulation Foundation** ✅ Complete (76 tests · 0 failures)
**Milestone 2 — First Playable** ✅ Complete
  - Phase 1 (static render + camera) ✅ Done
  - Phase 2 (player input + UI) ✅ Done
**Milestone 3 — Simulation Depth** ✅ Complete (108 tests · 0 failures)
  - PollutionSystem ✅ Done
  - HappinessSystem ✅ Done
  - MilestoneSystem ✅ Done
  - Services (FireStation, PoliceStation, School) ✅ Done

---

## Systems Implemented

| System | Status | Tests |
|---|---|---|
| CityGrid | ✅ Done | 7 |
| PowerNetwork (BFS) | ✅ Done | 10 |
| RoadNetwork | ✅ Done | 13 |
| BudgetSystem (cost-per-tile) | ✅ Done | 18 |
| PopulationSystem | ✅ Done | 9 |
| SimulationRunner (CLI) | ✅ Done | — |
| DemandSystem (R/C/I) | ✅ Done | 11 |
| SimulationEngine (orchestrator) | ✅ Done | 8 |
| SimulationRunner (server mode) | ✅ Done | — |
| Godot TileMap renderer | ✅ Done (Phase 1 — Node2D _Draw) | — |
| Godot viewer mode (SharedStateReader) | ✅ Done | — |
| PollutionSystem | ✅ Done | 8 |
| HappinessSystem | ✅ Done | 13 |
| MilestoneSystem | ✅ Done | 11 |
| Services (FireStation/PoliceStation/School) | ✅ Done | (in HappinessSystem tests) |
| Player input (Godot) | ✅ Done | — |
| Stats HUD overlay (Godot) | ✅ Done | — |
| Toolbar + zone selector (Godot) | ✅ Done | — |

**Total: 108 tests · 0 failures · ~0.06s runtime**

---

## Short-Term Task Queue (Next 2–3 Sessions)

### Phase 2 — Player Input & UI ✅ Complete (Session 7)
- [x] Toolbar: zone selector (R / C / I / Road / PowerLine / Plant / Fire / Police / School / Erase)
- [x] Click-to-place: viewer mode writes command.json, standalone mode modifies internal grid
- [x] UI labels: population, balance, net/tick (green/red), tick counter, happiness, selected zone
- [x] Budget breakdown: Tax/tick | Costs/tick displayed in HUD
- [x] Milestone notification: banner fades in/out when Town/City/Metropolis/Loopolis reached
- [x] Pause/Resume + New Game buttons in toolbar
- [x] SharedState extended: happiness + milestoneReached added
- [x] erase command + new_game command added to Runner

### Balance + UX fixes ✅ Complete (Session 7 continued)
- [x] TaxRate 0.09 → 0.12 (default scenario now +$5/tick at capacity, was -$1.5/tick)
- [x] PowerPlant maintenance $10 → $8
- [x] Industrial maintenance $0.50 → $0.25 (no income path — halved is fair)
- [x] Bankruptcy fires at balance < -$10,000 (inhabited cities can now go bankrupt)
- [x] GameState added to SharedState ("Active"/"Town"/"Bankrupt"/etc.)
- [x] Starting balance $5k → $10k (standalone + new_game command)
- [x] Tile hover tooltip: hover any tile to see power/road status + "Ready to develop" or "Needs power + road"
- [x] GameOver panel: "CITY BANKRUPT" overlay with stats + contextual tip + New Game button
- [x] Seeded starter city: 1PP + 2 Roads + 4 Residential at center — shows the chain working immediately
- [x] Optimistic rendering: click-to-place in viewer mode is instant (no 1-tick lag)
- [x] Window 1440×900, tile size 32px, camera re-centered

### Backlog (next sessions)
- [ ] Pollution heat map overlay — red intensity shows pollution levels per tile
- [ ] Water network — second utility system (forces two-grid planning)
- [ ] Zone density levels — low/medium/high density (different capacity + cost)
- [ ] Industrial income path — commercial adjacency tax boost so industrial is worth building
- [ ] Population ceiling buster — once zones fill, give player new tools to grow (density upgrade, new zones)
- [ ] Win screen — Loopolis milestone celebration UI

### Visualization improvements (backlog)
- [ ] Powered vs unpowered — stronger visual (dim + icon vs bright)
- [ ] Demand indicator — show which residential zones have commercial boost
- [ ] Population density per zone (color intensity scales with occupancy)
- [ ] Road/power line visual distinction (power line thinner, different color)
- [ ] City centering — camera auto-centers on active tiles at startup

### Session +2 ✅ Complete
- [x] Install Godot 4 .NET edition (godotengine.org) — installed at `/Applications/Godot_mono.app`
- [x] Scaffold `godot/` project folder
- [x] First TileMap renderer: reads CityGrid, renders color-coded tiles (Node2D + _Draw, no atlas needed)
- [x] Camera: pan with mouse drag, zoom with scroll wheel
- [x] Viewer mode: World.cs detects state.json, adds SharedStateReader, polls at 20Hz
- [x] Server mode: Runner persistent loop, command.json protocol, atomic state.json writes

---

## Agent Roster (project-local)

| Agent | Role | When to invoke |
|---|---|---|
| `loopolis-designer` | Game design research, feature proposals, GAME_DESIGN.md owner | Before planning new features |
| `loopolis-reviewer` | Qualitative game review — scores fun, balance, progression | After each feature batch |
| `loopolis-balancer` | Quantitative — runs SimulationRunner, tunes constants | After any simulation change |
| `loopolis-sim-engineer` | C# backend implementation, tests, technical foundation | All simulation work |
| `loopolis-godot-engineer` | Godot frontend, TileMap, UI, signals | All visual/UI work |

---

## Feedback Loop Findings Log

| Session | Scenario | Finding | Fix Applied |
|---|---|---|---|
| 2026-05-10 | default | Population = 0 (no power propagation) | Built PowerNetwork BFS |
| 2026-05-10 | default | Bankruptcy at tick 280 (flat $50/tick cost) | Built cost-per-tile model |
| 2026-05-10 | town | Power not reaching zones (line too short) | Extended power line to road network |
| 2026-05-11 | town | Pop = 0 despite 13 ready zones (inactive zone drag) | Fixed PopulationSystem decline logic |
| 2026-05-11 | town | Commercial strip NOT adjacent to residential (road separates them) — no demand boost in town scenario | Expected: town layout separates R and C with roads; boost confirmed working in default (1 of 3 R zones adjacent to C) |
| 2026-05-11 | default | DemandSystem confirmed: tick-0 pop=8 = (2×2.5)+(1×3.75)=8.75→8; boosted zone grows 50% faster | Working correctly |
| 2026-05-11 | server/viewer | 17R + 3C city: 850 pop max, +$46/tick surplus. Commercial adjacency boost confirmed live in Godot viewer. | — |
| 2026-05-11 | mixed | 3 polluted R zones (adjacent to industrial) → happiness 0.2 vs 0.6 safe baseline. Confirmed average 0.491 matches formula: (8×0.6 + 3×0.2)/11. Pollution gradient working. | — |
| 2026-05-11 | services | School at (15,12) covers some north R tiles (y=9 = distance 3, within 5). Happiness 0.667 average — mix of covered (0.75) and uncovered (0.6). Fire station at (15,18) too far from south R at y=21 (distance 3 from station to road). | Layout confirmed: service placement matters |
| 2026-05-11 | town | Reached Town milestone at tick 38 (500 pop). Industrial far enough (dx>3) from residential → zero pollution. Happiness = 0.6 baseline throughout. Bankrupt at -$54/tick due to large grid cost. | Balance tuning needed for large scenarios |
| 2026-05-11 | all | All large scenarios go bankrupt. Root cause: residential zones at distance from roads can't develop (only 11-18 of 45-75 R tiles are ready). Maintenance costs (industrial, roads, powerlines) dwarf tax income. | Backlog: balance tuning or larger commercial footprint |

---

## Build Commands

```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
export PATH="$DOTNET_ROOT:$PATH"

dotnet test                                                      # all tests (108)
dotnet run --project src/Loopolis.Runner -- 500 default          # JSON report
dotnet run --project src/Loopolis.Runner -- 500 town             # larger city (reaches Town milestone)
dotnet run --project src/Loopolis.Runner -- 500 mixed            # pollution gradient scenario
dotnet run --project src/Loopolis.Runner -- 500 services         # school + fire station coverage
dotnet run --project src/Loopolis.Runner -- 500 town --ascii     # visual map
dotnet run --project src/Loopolis.Runner -- 500 no_power         # verify power required
dotnet run --project src/Loopolis.Runner -- 500 no_roads         # verify roads required
dotnet run --project src/Loopolis.Runner -- server default --speed 2  # persistent server
```

## Godot Launch

```bash
# Launch Godot editor (viewer mode if state.json exists, standalone otherwise)
DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec \
  /Applications/Godot_mono.app/Contents/MacOS/Godot \
  --path /Users/benjamin.eckstein/IdeaProjects/private/loopolis/godot/ \
  --editor
# → Open scenes/World.tscn → Press F5
```

**Controls:**
- Middle-mouse drag: pan camera
- Scroll wheel: zoom in/out
- Tiles auto-refresh every 50ms (20Hz) in viewer mode, every 0.5s in standalone mode

**Tile colours:**
- Dark grey = empty
- Green = Residential (powered)
- Dark green = Residential (unpowered)
- Blue = Commercial (powered)
- Dark blue = Commercial (unpowered)
- Yellow = Industrial (powered)
- Grey = Road
- Red = Power Plant
- Cyan = Power Line

---

## Known Open Design Questions

See `GAME_DESIGN.md` → Open Design Questions section.

---

*Last updated: 2026-05-11 — Session 6 (PollutionSystem + HappinessSystem + MilestoneSystem + Services, 108 tests)*
