# Loopolis — Steam Release Roadmap

> Built by Benjamin + Cairn. Last updated: 2026-05-14.
> Current state: solid simulation backbone, flat visuals, no win conditions.
> Target: a game someone pays €9.99 for on Steam and recommends to a friend.

---

## The Gap

What we have vs. what Steam players expect:

| Area | Today | Steam-worthy |
|---|---|---|
| Goals | Sandbox only | Scenarios with medals, replay value |
| Feel | Functional | Juicy feedback — sounds, animations, events |
| Visuals | Colored tiles | Recognizable art style, readable at a glance |
| Content | 13 buildings, no parks | 25+ buildings, parks, districts, landmarks |
| Tutorial | Inline hints | 5-minute guided first city |
| Audio | None | Ambient city sounds, UI clicks, event stings |
| UX | Serviceable | Tooltips, keyboard map, settings screen |
| Steam | Nothing | Achievements, cloud saves, leaderboards |

---

## Milestones

### M8.5 — Feel Sprint *(in progress)*

Make the game feel alive. Zero new simulation. Pure feedback and goals.

- [x] F1 Road build pulse — flash newly placed road tiles
- [ ] F3 Building birth announcement — toast when a building grows
- [ ] F2 Seeded Scenarios — 5 scenarios, bronze/silver/gold medals
- [ ] F4 Scenario selection screen on Main Menu

**Exit criteria:** A new player can start a scenario, understand the goal, feel their city growing, and earn a medal.

---

### M9 — Content & Depth

Add strategic variety so every city feels different.

- [ ] Parks zone — happiness radius, no jobs, green visual
- [ ] Minimap — essential on 64×64+ maps
- [ ] City name (player-chosen, shown in TopBar and game-over screens)
- [ ] 3 more building tiers: landmark residential (8×8 skyscraper at Metropolis), commercial mall upgrade, industrial complex
- [ ] 10 total scenarios, including challenge maps (islands, narrow valleys, resource-rich maps)
- [ ] Scenario leaderboard (local, top 5 scores per scenario)

**Exit criteria:** Two playthroughs of the same scenario feel meaningfully different based on zone layout choices.

---

### M10 — Visual Identity

Give the game a look someone would screenshot and share.

- [ ] Procedural building sprites: instead of flat fills, draw layered shapes per building tier (windows, rooflines, chimneys for factories). No external artist needed — Godot `draw_*` calls.
- [ ] Zone color polish: residential warm cream/terracotta palette, commercial cool blue-white, industrial desaturated with type-specific accents
- [ ] Animated smoke on factories and quarries (simple particle-style using multiple small rects per tick)
- [ ] Tree sprites on forest tiles (small green circles arranged in clusters)
- [ ] Water shimmer (alternating light/dark strips that scroll slowly)
- [ ] Building growth animation: brief scale-up bounce when a new building spawns

**Exit criteria:** A screenshot of the game at T=300 looks like a deliberate art style, not a debug view.

---

### M11 — Audio

Sound transforms perceived quality more than any visual change.

- [ ] UI click sounds (every button press)
- [ ] Road placement "chunk" sound
- [ ] Building spawn "pop" sound
- [ ] Event stings (danger sound for fire/crime, bell for milestone)
- [ ] Ambient city loop (quiet hum that grows with population)
- [ ] Music: 2 tracks — calm exploration, and slightly urgent at low happiness

**Exit criteria:** Playing with sound on feels significantly better than muted.

---

### M12 — UX & Polish

Reduce friction so players don't quit before their first 5k population.

- [ ] Tile tooltip showing: zone type, building name, population, pollution level, power status, land value
- [ ] Keyboard shortcut card (press `?` to show)
- [ ] Settings panel: zoom speed, UI scale, sound volume, simulation speed default
- [ ] Full tutorial: 8-step guided build (road → zone → power → watch it grow → add service → survive event)
- [ ] End-game summary screen: city stats, best milestone reached, playtime, "replay" button
- [ ] Multiple save slots (3 slots, plus autosave)

**Exit criteria:** A person who has never played a city builder can get to Town milestone without reading any docs.

---

### M13 — Steam Integration

The final stretch before the store page goes live.

- [ ] Steam achievements (20 achievements: first city, first 1k pop, first Loopolis, survived a fire, etc.)
- [ ] Steam Cloud saves
- [ ] Global leaderboard (scenario medals)
- [ ] Export builds: Windows, Mac, Linux (Godot export templates)
- [ ] Store page assets: capsule image, screenshots, trailer (screen recording)
- [ ] Minimum viable store page: description, tags, pricing
- [ ] Launch checklist: content rating, region pricing, release date

**Exit criteria:** The game passes Steam review and has a 60%+ positive review rate in the first 30 days.

---

## Immediate Next Actions (this session)

1. ✅ Terrain-conditional industry (Timber Mill, Quarry)
2. 🔄 F3 Building birth announcements (toasts when buildings appear)
3. 🔄 F1 Road build pulse (visual flash on placement)
4. 🔄 Seeded Scenarios — Core implementation (5 scenarios, goal/medal tracking)
5. 🔄 Scenario selection screen + medal display (Godot)
6. ⏳ Parks zone (happiness source, green areas)
7. ⏳ Minimap

---

## Design Principles (don't break these)

1. **Legibility first** — every mechanic must have a visible cause and visible effect
2. **No transport** — no conveyor belts, no delivery trucks; supply is abstract
3. **Road graph is the spine** — all service/growth/traffic routes through roads
4. **Core stays pure C#** — zero Godot imports in simulation layer
5. **Terrain is a resource** — every biome should have a reason to exist
6. **Small team scope** — reject anything that requires an artist or a sound designer to implement correctly; procedural and code-driven is fine

---

*"A cairn is built stone by stone. So is a city."*
