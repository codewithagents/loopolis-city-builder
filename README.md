# Loopolis

> A SimCity-style city builder where every decision has visible, mechanical consequences.

**Plan → Build → Zone → Watch → React → Expand → repeat**

Place a power plant, run roads, zone land — watch your city grow or collapse based on the systems you've built. Small enough to be approachable in five minutes, deep enough to keep you planning for hours.

---

## What makes it interesting

- **Power as a density unlock** — basic cottages grow without power; townhouses, apartments, and commercial strips need the grid connected. You feel the moment electricity arrives.
- **Services shape happiness** — Fire stations, police, schools, and hospitals each have real road-graph coverage radii. A neighborhood without a school shows it.
- **Era Charters** — at Town (500 pop), City (5 000 pop), and Metropolis (25 000 pop) you pick one of three permanent specializations. Merchant → Trade Corridors → Empire of Steel locks in a commercial identity for the whole run. Green Canopy → Green Utopia means pollution simply stops mattering. 27 distinct paths per scenario.
- **Events that demand decisions** — fires burn buildings down if you don't pay to intervene. Crime waves tank happiness. Power outages cascade. Every 60 ticks of calm ends.
- **Organic building growth** — you zone land; the simulation decides what grows. A 1×1 house upgrades to a 2×2 townhouse upgrades to a 4×4 apartment — if you've built what it needs.

---

## Systems

| System | Description |
|---|---|
| Power Network | BFS flood-fill from plants; roads and power lines conduct |
| Population | Road-adjacent growth; density unlocked by power |
| Commercial & Industrial | Activity-based; adjacency demand boosts |
| Pollution | Tile-level emission; happiness penalty per residential tile |
| Happiness | Service coverage, neglect decay, commute penalty, tax modifier, park bonus |
| Events | Fire, crime, power outage, demand slump — player intervention for $600–$1500 |
| Era Charters | 3 × Town + 3 × City + 3 × Metropolis = 27 permanent run identities |
| Policies | GreenCity / IndustrialHub / CommercialBoost / OpenCity — active modifiers |
| Service Fatigue | Capacity degrades over time; renovation decisions at City scale |
| Land Value | Per-tile float; plateaus, forests, parks, power, happiness all contribute |
| Road Traffic | Dijkstra worker routing; real edge load; chokepoints visible |
| Save / Load | Full round-trip: charters, fatigue, milestones, road graph all persisted |

**1 025 tests · 0 failures**

---

## Architecture

```
src/Loopolis.Core/        — Pure C# simulation. Zero Godot dependencies.
src/Loopolis.Runner/      — Headless CLI runner + persistent IPC server.
tests/                    — NUnit test suite (~10s full run).
godot/                    — Godot 4 presentation layer (renderer, UI, input).
  shared/                 — File-based IPC: state.json ← Runner, command.json → Runner
```

The simulation is a self-contained C# library. Godot reads state and renders it. The two layers communicate through JSON files — which also means agents and scripts can drive the simulation without opening the editor.

---

## Getting started

**Requirements**
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Godot 4.4.1 .NET edition](https://godotengine.org/download)

**Run tests**
```bash
dotnet test
```

**Play (standalone — Godot runs its own simulation)**
```bash
DOTNET_ROOT=/path/to/dotnet \
  /path/to/Godot_mono \
  --path godot/ --editor
# Open scenes/World.tscn → F5
```

**Play (server + viewer — simulation runs headless, Godot connects)**
```bash
# Terminal 1
dotnet run --project src/Loopolis.Runner -- server default --speed 2

# Terminal 2
DOTNET_ROOT=/path/to/dotnet \
  /path/to/Godot_mono \
  --path godot/ --editor
# Open scenes/World.tscn → F5
```

---

## Controls

| Key | Action |
|---|---|
| Click tile | Place selected zone |
| Middle-drag | Pan camera |
| Scroll | Zoom |
| P | Pause / Resume |
| B | Toggle Advisor Bar |
| V | City stats panel |
| M | Minimap |
| Ctrl+S / Ctrl+L | Save / Load |
| ? | All shortcuts |

---

## Headless / agent control

The runner accepts JSON commands over a shared file channel — no Godot required:

```bash
dotnet run --project src/Loopolis.Runner -- server default --speed 10 &
SESSION=$(grep 'session=' /tmp/loopolis.log | head -1 | sed 's/.*session=//')

# Place tiles
echo "{\"cmd\":\"place_zone\",\"x\":16,\"y\":15,\"zone\":\"Road\",\"sessionId\":\"$SESSION\"}" \
  > godot/shared/command-${SESSION}.json

# Skip 500 ticks (stops early on events or milestones)
echo "{\"cmd\":\"skip\",\"ticks\":500,\"pauseAfter\":true,\"sessionId\":\"$SESSION\"}" \
  > godot/shared/command-${SESSION}.json

# Select a charter once the Town milestone fires
echo "{\"cmd\":\"select_charter\",\"charter\":\"Merchant\",\"sessionId\":\"$SESSION\"}" \
  > godot/shared/command-${SESSION}.json
```

State is always available at `godot/shared/state-${SESSION}.json` — population, balance, happiness, charter state, building list, events, and more.

---

## Built with AI agents

Loopolis is developed at **[CodeWithAgents.de](https://www.codewithagents.de)** — a project exploring what software looks like when AI agents are first-class collaborators, not just autocomplete.

Every feature in this repo was designed, implemented, tested, and reviewed through an agentic workflow: a planning agent designs the feature, a simulator agent writes the C# and tests, a reviewer agent checks the diff, a release agent creates the PR, a balancer agent runs scenarios and checks the numbers, and a player agent plays the game and reports what feels wrong.

The headless IPC architecture exists specifically so agents can play and evaluate the simulation without human involvement.

---

## License

MIT — see [LICENSE](LICENSE).
