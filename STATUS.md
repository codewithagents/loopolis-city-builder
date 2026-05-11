# STATUS.md — Loopolis

> Project status, WIP tracker, and short-term task queue.
> Update this at the start and end of every session.

---

## Current Milestone

**Milestone 1 — Simulation Foundation** 🔄 In Progress

Goal: All core simulation systems built, tested, agent feedback loop operational.
Next milestone: Milestone 2 — First Playable (Godot visual + player input).

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
| Godot TileMap renderer | ⬜ Blocked (Godot not installed) | — |
| Player input (Godot) | ⬜ Blocked | — |

**Total: 76 tests · 0 failures · ~0.3s runtime**

---

## Short-Term Task Queue (Next 2–3 Sessions)

### Session Next
- [x] `DemandSystem` — R/C/I zones need each other to grow beyond base capacity (done)
- [x] `SimulationEngine` — orchestrator class replacing manual system wiring in Runner (done)
- [ ] Run loopolis-designer agent against GAME_DESIGN.md for design review

### Session +2
- [ ] Install Godot 4 .NET edition (godotengine.org)
- [ ] Scaffold `godot/` project folder
- [ ] First TileMap renderer: reads CityGrid, renders color-coded tiles
- [ ] Camera: pan with mouse drag, zoom with scroll wheel

### Session +3
- [ ] Player input: click to place zones, draw roads
- [ ] Basic UI: budget panel, population counter, zone selector toolbar
- [ ] Wire SimulationEngine to Godot game loop

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
```

---

## Known Open Design Questions

See `GAME_DESIGN.md` → Open Design Questions section.

---

*Last updated: 2026-05-11 — Session 3*
