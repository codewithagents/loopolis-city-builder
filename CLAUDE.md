# Loopolis — CLAUDE.md

## What Is This

A SimCity-style indie city builder targeting Steam release.
Built by Benjamin + Cairn (AI agent) with agent-driven feedback loops as a core development strategy.

## Architecture

```
src/Loopolis.Core/        — Pure C# simulation logic. Zero Godot dependencies.
src/Loopolis.Runner/      — CLI headless simulation runner for agent feedback loops.
tests/Loopolis.Core.Tests/ — NUnit tests. Run with: dotnet test
godot/                    — Godot 4 project (presentation layer only).
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

## Core Systems (build order)

1. ✅ CityGrid — tile grid, zone placement, adjacency
2. ✅ BudgetSystem — tax income, costs, balance, deficit
3. ✅ PopulationSystem — growth/decline based on powered residential zones
4. ⬜ PowerNetwork — BFS propagation from power plants
5. ⬜ RoadNetwork — connectivity validation
6. ⬜ DemandSystem — R/C/I demand curves
7. ⬜ SimulationEngine — orchestrates all systems per tick

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
- Game Engine: Godot 4 (.NET edition) — not yet installed
- Target: Steam (Windows / Mac / Linux)
