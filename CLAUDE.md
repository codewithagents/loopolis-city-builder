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

**Run tests:**
```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
export PATH="$DOTNET_ROOT:$PATH"
dotnet test
```

**Run simulation (agent feedback loop):**
```bash
dotnet run --project src/Loopolis.Runner -- 500 default
dotnet run --project src/Loopolis.Runner -- 1000 powered_start
dotnet run --project src/Loopolis.Runner -- 500 no_power
```
Output is JSON — designed for agent analysis.

## Core Systems (all done)

1. ✅ CityGrid — tile grid, zone placement, adjacency, terrain (Water blocks placement)
2. ✅ TerrainType — Flat/Hill/Forest/Water. Forest +$75, Hill +$50 placement surcharge
3. ✅ BudgetSystem — tax income, costs, balance, deficit. Default start: $4,000
4. ✅ PopulationSystem — wave-based development (road-adjacent first, interior unlocks at pop≥25)
5. ✅ PowerNetwork — BFS flood-fill from power plants; services (Fire/Police/School) are conductors
6. ✅ RoadNetwork — direct adjacency validation
7. ✅ DemandSystem — commercial adjacency boosts residential growth (1.5×)
8. ✅ PollutionSystem — industrial tiles emit pollution, reduces happiness of nearby residential
9. ✅ HappinessSystem — service coverage, neglect decay, tax modifier, event penalty, unemployment
10. ✅ MilestoneSystem — thresholds Village/Town(500)/City(5k)/Metropolis(25k)/Loopolis(100k), bankruptcy, abandonment
11. ✅ EventSystem — 4 events (FireBreak/CrimeWave/PowerOutage/DemandSlump), 2%/tick trigger, 60-tick honeymoon
12. ✅ EmploymentSystem — industrial activity → jobs (0.4/unit, 20 jobs/full tile), residential growth throttled above pop 100
13. ✅ SimulationEngine — orchestrates all systems per tick
14. ✅ SaveSystem (Persistence) — Capture/Serialize/Deserialize/RestoreGrid; terrain seed saves exact map

**182 tests · 0 failures**

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
