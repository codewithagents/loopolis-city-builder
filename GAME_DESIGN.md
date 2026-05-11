# Loopolis — Game Design Document

> Written in player-facing language. For implementation details see CLAUDE.md.
> Owner: loopolis-designer agent. Update this file when design decisions are made.

---

## Elevator Pitch

A city builder where every decision has visible, mechanical consequences.
Place a power plant, run roads, zone land — watch your city grow or collapse
based on the systems you've connected. Small enough to be approachable in
five minutes, deep enough to keep you thinking for hours.

---

## Core Fantasy

You are the city planner. You don't control individual citizens — you
control infrastructure and zoning, and the city emerges from your choices.
A badly-placed road means a whole neighborhood stays dark. A city without
power means no one moves in. Every mistake is legible. Every win is earned.

---

## Core Gameplay Loop

```
Plan → Build → Zone → Watch → React → Expand → repeat
```

1. **Plan** — survey the empty grid, think about where infrastructure goes
2. **Build** — place roads, power plants, power lines
3. **Zone** — designate residential, commercial, industrial areas
4. **Watch** — the simulation runs; population grows or stagnates
5. **React** — budget bleeding? expand. Power not reaching zones? run more lines.
6. **Expand** — add zones, services, infrastructure at larger scale

---

## Mechanics Reference

### Power System
- Power plants generate electricity and broadcast it to adjacent tiles
- Power spreads through: roads, power lines, residential, commercial, industrial zones
- Empty tiles **break the chain** — you must route power deliberately
- Zones without power never develop — they sit empty forever

**Player experience:** "I need to connect my power plant to my zones. I can run it through roads (free conductor) or dedicated power lines (costs more but goes anywhere)."

### Road Network
- Zones (R/C/I) need at least one adjacent road tile to develop
- Roads also conduct power — in early game, one road can serve both purposes
- Isolated zones (no adjacent road) produce no population, no tax

**Player experience:** "I can't just drop zones anywhere. I need to plan roads first. Roads are expensive to maintain, so I can't just blanket the map — I need efficient layouts."

### Zones

| Zone | Purpose | Needs to develop | Produces |
|---|---|---|---|
| Residential | Housing | Power + road access | Population → tax income |
| Commercial | Business | Power + road + nearby residents *(planned)* | Jobs → draws more residents |
| Industrial | Manufacturing | Power + road + nearby commercial *(planned)* | Supports commercial, generates pollution |

### Budget

| Source | Formula | Notes |
|---|---|---|
| Tax income | Population × tax rate (9% default) | Scales with how many people live in your city |
| Maintenance — Power Plant | $10 / tick | Expensive to run, worth it if connected |
| Maintenance — Road | $1 / tick per tile | Scales with network size — every road costs |
| Maintenance — Zone (R/C/I) | $0.5 / tick per tile | Small but adds up at scale |
| Maintenance — Power Line | $0.5 / tick per tile | Cheaper than roads, but only conducts power |

**Tension:** A starter city (1 plant, a few roads, 3 zones) costs ~$15/tick to maintain but only earns ~$13.50/tick at full population. The player must grow to reach surplus. Adding one more residential zone tips the balance.

**Break-even point:** ~12 powered, road-accessible residential zones.

### Population

- Each ready residential zone supports 50 residents maximum
- Population grows toward capacity at 5%/tick when zones are ready
- Population declines only when capacity drops below current population (services lost)
- Zones that were never developed don't cause decline — they just sit empty

**Player experience:** "My city grows steadily when things are connected. If I lose power, people leave — but slowly enough that I can fix it. Zones I haven't connected yet don't hurt me, they're just waiting."

---

## Win and Lose Conditions

*(Defined — not yet implemented)*

**Lose:** Balance reaches $0 with no path to recovery (population=0, no income).

**Win milestones (proposed):**
- 🥉 500 residents — "Town"
- 🥈 5,000 residents — "City"
- 🥇 25,000 residents — "Metropolis"
- 🏆 100,000 residents — "Loopolis" (full win)

Each milestone unlocks new zone types or building options (design TBD).

---

## Progression Curve

### Early Game (0 – 500 residents)
- 1 power plant, handful of roads, 3-10 residential zones
- Budget is slightly negative — urgent pressure to grow
- Core challenge: connect power to zones AND put roads adjacent to them
- Reward: first moment the simulation shows population > 0

### Mid Game (500 – 10,000 residents)
- Multiple power sources, planned road networks
- Commercial zones come online — jobs attract more residents
- Budget positive but fragile — one bad expansion bankrupts you
- Core challenge: R/C/I balance, road efficiency, power coverage gaps

### Late Game (10,000+ residents)
- Industrial zones support commercial scale
- Services (fire, police, education) affect zone happiness and capacity
- Core challenge: density optimization, traffic routing, disaster response
- Budget comfortable — now optimizing for growth speed and polish

---

## Planned Features (Priority Order)

| # | Feature | Why it matters |
|---|---|---|
| 1 | **DemandSystem** | R/C/I zones demand each other. Without jobs (commercial), residential growth stalls. Real strategic depth. |
| 2 | **SimulationEngine** | Orchestrator that drives all systems in correct tick order. Replaces manual wiring in Runner. |
| 3 | **Godot visual renderer** | The game becomes playable. TileMap, camera, basic UI. |
| 4 | **Player input** | Zone placement, road drawing, power line routing in Godot. |
| 5 | **Water network** | Second utility like power — forces players to plan two grids. |
| 6 | **Services** | Fire, police, education. Affect zone happiness/capacity. |
| 7 | **Happiness system** | Zones have satisfaction that multiplies growth rate. |
| 8 | **Win conditions** | Milestone-based progression with unlocks. |
| 9 | **Disasters** | Fire, economic collapse — optional, adds replayability. |
| 10 | **Steam publishing** | Export, store page, achievements, trailer. |

---

## Design Principles

1. **Every decision visible** — place a tile, see the consequence in the simulation immediately
2. **No hidden complexity** — mechanics should be learnable by observation, not by reading a wiki
3. **Meaningful tradeoffs** — more roads = more maintenance. More power plants = more coverage + more cost. Player must choose.
4. **Fail forward** — losing a city should teach you something specific. "I ran out of money because my power plant cost $10/tick and my city only had 2 zones" is a good failure.
5. **Scope discipline** — features must deepen the core loop, not just add more things to click

---

## Open Design Questions

- What's the map size? Fixed grid or scrolling world?
- Is there a time pressure (year/season system) or is it turn-based?
- Do roads have traffic / capacity limits?
- Should disasters be optional (sandbox) or forced (challenge mode)?
- What is the unique hook that makes Loopolis different from SimCity 2000?

---

## Session Notes

| Date | Decision |
|---|---|
| 2026-05-10 | Architecture: clean backend (Loopolis.Core) + Godot frontend. Core has zero Godot dependency. |
| 2026-05-10 | Language: C# (.NET 10) for backend, Godot 4 .NET edition for frontend. |
| 2026-05-10 | Cost model: per-tile maintenance, not flat fee. Emerged from feedback loop. |
| 2026-05-11 | Population model: decline only when capacity drops, not from inactive zones. Feedback loop finding. |
