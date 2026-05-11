# STATUS.md — Loopolis

> Project status, WIP tracker, and short-term task queue.
> Update this at the start and end of every session.

---

## Current Milestone

**Milestone 1 — Simulation Foundation** ✅ Complete (76 tests · 0 failures)
**Milestone 2 — First Playable** 🔄 In Progress
  - Phase 1 (static render + camera) ✅ Done
  - Phase 2 (player input + UI) ⬜ Next

---

## Systems Implemented

| System | Status | Tests |
|---|---|---|
| CityGrid | ✅ Done | 7 |
| PowerNetwork (BFS) | ✅ Done | 10 |
| RoadNetwork | ✅ Done | 13 |
| BudgetSystem (cost-per-tile) | ✅ Done | 18 |
| PopulationSystem | ✅ Done | 8 |
| SimulationRunner (CLI) | ✅ Done | — |
| DemandSystem (R/C/I) | ✅ Done | 11 |
| SimulationEngine (orchestrator) | ✅ Done | 8 |
| SimulationRunner (server mode) | ✅ Done | — |
| Godot TileMap renderer | ✅ Done (Phase 1 — Node2D _Draw) | — |
| Godot viewer mode (SharedStateReader) | ✅ Done | — |
| Player input (Godot) | ⬜ Next | — |

**Total: 76 tests · 0 failures · ~0.3s runtime**

---

## Short-Term Task Queue (Next 2–3 Sessions)

### Phase 2 — Player Input & UI (next session)
- [ ] Toolbar: zone selector (R / C / I / Road / PowerLine / Erase)
- [ ] Click-to-place: viewer mode writes to command.json, standalone mode modifies internal grid
- [ ] UI labels: population count, balance, net/tick, tick counter
- [ ] Budget panel: overlay showing income vs costs

### Backend depth (backlog)
- [ ] Happiness system — zone satisfaction multiplier on growth rate
- [ ] Water network — second utility, forces two-grid planning
- [ ] Services — fire/police/education affect zone capacity
- [ ] Zone density levels — low/medium/high density zoning
- [ ] Win conditions — milestone detection and feedback

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

---

## Build Commands

```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
export PATH="$DOTNET_ROOT:$PATH"

dotnet test                                                      # all tests
dotnet run --project src/Loopolis.Runner -- 500 default          # JSON report
dotnet run --project src/Loopolis.Runner -- 500 town             # larger city
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

*Last updated: 2026-05-11 — Session 5 (server mode + viewer mode, game balance confirmed)*
