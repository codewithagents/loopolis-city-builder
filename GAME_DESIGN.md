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

> Updated 2026-05-12. Power is no longer a prerequisite for *any* growth. It is the universal "density unlock" — basic 1×1 residential develops without it; everything else (larger residential, commercial, industrial productivity) requires it.

- Power plants generate electricity and broadcast it to adjacent tiles
- Power spreads through: roads, power lines, residential, commercial, industrial zones
- Empty tiles **break the chain** — you must route power deliberately
- **Unpowered 1×1 residential still develops** at half capacity and 70% tax yield ("Cottage" state). It exists to give the player something to watch grow before the first power plant.
- **Powered 1×1 residential** is the same building at full capacity and full tax ("Powered Cottage" state).
- **All larger residential tiers (2×2 townhouse, villas, apartments)** require power — they will not grow from an unpowered 1×1 cottage.
- **All commercial tiles** require power (no unpowered commerce; cash registers and lights are non-negotiable narratively).
- **Industrial tiles** can develop unpowered but generate no Output (just base jobs at a heavy productivity penalty — see Power & Industry below).
- **Services (Fire/Police/School/Hospital)** require power to function — unpowered services exist on the map but provide no coverage.

**Player experience:** "I zoned some homes along the starter road and they slowly filled with cottages. Then I built a power plant and the cottages doubled in size and tax — suddenly townhouses and shops became possible. Power isn't *required to play*; it's *required to grow*."

### Road Network
- Zones (R/C/I) need at least one adjacent road tile to develop
- Roads also conduct power — in early game, one road can serve both purposes
- Isolated zones (no adjacent road) produce no population, no tax

**Player experience:** "I can't just drop zones anywhere. I need to plan roads first. Roads are expensive to maintain, so I can't just blanket the map — I need efficient layouts."

### Zones

| Zone | Purpose | Needs to develop | Produces |
|---|---|---|---|
| Residential (1×1 Cottage) | Housing | Road access | Population (50% cap) → tax income (70%) |
| Residential (Powered Cottage) | Housing | Road + power | Population (full) → tax income (full) |
| Residential (2×2+) | Housing, higher tier | Road + power + tier conditions | Population → tax (scales with size) |
| Commercial | Business | Road + power + nearby residents | Jobs / customers → draws more residents |
| Industrial | Manufacturing | Road (powered for Output) | Jobs (unpowered) + Output (powered only) |

### Power & Industry (the legibility detail)

Industrial without power = "Bare Lot": minimum jobs (0.1× normal), Output = 0, no goods.
Industrial with power = "Factory": full jobs, full Output, full goods generation.

Why allow unpowered industrial at all? So the player can pre-zone industrial sites near the border road without losing the budget tick they'd lose by leaving the land idle. Powering them is the upgrade decision, not the existence decision.

### Budget

| Source | Formula | Notes |
|---|---|---|
| Tax income | Population × tax rate (9% default) × land value × power multiplier | Unpowered 1×1 res taxes at 0.7× |
| Maintenance — Power Plant | $10 / tick | Expensive to run, worth it if connected |
| Maintenance — Road | $1 / tick per tile | Scales with network size — every road costs |
| Maintenance — Zone (R/C/I) | $0.5 / tick per tile | Small but adds up at scale |
| Maintenance — Power Line | $0.5 / tick per tile | Cheaper than roads, but only conducts power |

**Tension:** A starter city (1 plant, a few roads, 3 zones) costs ~$15/tick to maintain but only earns ~$13.50/tick at full population. The player must grow to reach surplus. Adding one more residential zone tips the balance.

**Break-even point:** ~12 powered, road-accessible residential zones.

**Early-game flow under new power rules:**
- Tick 1: starting budget $4,000. 1 border road tile + 3 starter road tiles = $4/tick maintenance.
- Player zones a strip of residential along the starter road. ~3 tiles, $1.50/tick maintenance.
- Cottages start growing immediately at half capacity. Tax trickle begins (~$2/tick at full).
- Budget bleeds slowly — pressure to build a plant arrives organically (not as a wall).
- Player buys coal plant ($1000), routes power along the road spine. Cottages double in capacity/tax. Now ~+$4/tick. Time to expand commercial.

This is the SimCity 2000 onramp: power is *acceleration*, not *ignition*.

### Population

- Each ready residential zone supports 50 residents at full power, 25 unpowered
- Population grows toward capacity at 5%/tick when zones are ready
- Population declines only when capacity drops below current population (services lost, OR power lost on a previously-powered 2×2+)
- Zones that were never developed don't cause decline — they just sit empty

**Player experience:** "My city grows steadily when things are connected. If I lose power, the townhouses degrade back to cottages — but the cottages stay. People don't all flee; the city just shrinks to what's sustainable."

---

## Win and Lose Conditions

*(Defined — not yet implemented)*

**Lose:** Balance reaches $0 with no path to recovery (population=0, no income).

**Win milestones (proposed):**
- Bronze 500 residents — "Town"
- Silver 5,000 residents — "City"
- Gold 25,000 residents — "Metropolis"
- Trophy 100,000 residents — "Loopolis" (full win)

Each milestone unlocks new zone types or building options (design TBD).

---

## Progression Curve

### Early Game (0 – 500 residents)
- Empty map at new game: only the unerasable **border road** (south-center) and 2-3 starter road tiles extending into the map. No power plant, no zones, no services.
- First decision: *where to zone residential first*. Cottages grow without power, so the first 30-60 ticks are about watching tiny houses fill in along the spine while you plan the plant placement.
- Second decision: *where to place the power plant*. Near the border = cheap road extension, pollution near the migration anchor. Far from the border = preserves "downtown" but demands a longer power/road spine.
- Build out: 1 power plant, handful of roads, 3-10 residential zones along the spine. After power: townhouses, shops, factories all unlock.
- Budget is slightly negative — urgent pressure to grow
- Core challenge: connect power to zones AND put roads adjacent to them, while staying in reach of the border road for the migration bonus
- Reward: first moment the simulation shows population > 0 — cottages appearing within the first 30 ticks, then the visible "level-up" when power arrives

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

> **M1–M7 features (DemandSystem, SimEngine, Godot, Services, Happiness, Win conditions, Disasters, Building Growth, Pollution, Employment, Save/Load) — all shipped. See CLAUDE.md.**

### M8 — "Strategic Depth" (next)

| # | Feature | Why it matters |
|---|---|---|
| 1 | **Service Tiers & Specialization** | Police HQ / Central Fire / University / Hospital. Fills the empty City milestone unlock. Fewer buildings, denser coverage, real tradeoff: 3 cheap stations vs 1 powerful HQ. |
| 2 | **Power Capacity & Plant Variants** | Power becomes a budget (supply vs demand). Coal (cheap, polluting) vs Nuclear (expensive, no pollution, radiation if destroyed). Three viable power strategies. Brownouts visible in HUD. |
| 3 | **Land Value & Elevation** | Hill tiles become *premium real estate* instead of $50 surcharge. One luxury residential variant requires hillside. Inverts current "avoid hills" incentive into "fight for the hills." |
| 4 | **Traffic density + failure diagnosis** | (Shipped) Players need to see *why* zones don't grow. Diagnosis UI is the gateway to every other system being legible. |
| 5 | **Commute Happiness + Wider Mixed-Use Bonus** | Penalize zone segregation softly. Workers far from jobs are unhappy; mixed districts grow faster. Closes the loophole where a single spine road avoids all consequences. |
| 6 | **C/I Vitality HUD (Customers, Workers, Output)** | Commerce and industry get the same `current/capacity` narrative that residential has via Pop. Players can finally see at a glance whether their districts are thriving or stagnant. |
| 7 | **Power-as-density-unlock (P1)** | NEW. Cottages grow without power at 0.5× cap / 0.7× tax. Power upgrades them to full. 2×2+, commercial, and industrial productivity still require power. Removes the dead-start problem on empty-map onboarding. |

### M8.5 — "Feel Sprint" (next sprint — make the game *feel* good)

> Sprint focus: Loopolis sits at 4-5/10. The engineering is strong; the *experience* is flat. This sprint adds zero new systems and instead surfaces what's already happening so the player feels their decisions land. Goal: bring the score to 6-7/10 via feedback, identity, and reasons to replay.

| # | Feature | Why it matters |
|---|---|---|
| F1 | **Road Pulse on Build** | When the player places a road / power plant / zone, a ripple animates outward and every tile that becomes newly powered or newly ready briefly pulses gold. Makes the BFS that already runs visible. Cheapest possible "wow" because the data is already there. This is the Mini-Metro line-drawing satisfaction equivalent. |
| F2 | **Seeded Scenarios with Goals** | A scenario picker with 6-8 named challenges (Coastal Town, Industrial Heart, No Coal Allowed, The Hill). Each has fixed terrain seed, a goal (e.g. "5k pop in 1000 ticks"), and a gold/silver/bronze medal. Converts depth-that-exists into hours-of-replay. Single biggest score lever in the ideation pool. |
| F3 | **Building Birth Announcement** | When a building grows tier (cottage → townhouse → villa, shop → strip → mall), a floating label flies up from the tile ("Hillside Villa! +$4/tick") with a zone-specific chime. Converts the existing growth system into a steady drip of positive feedback the player currently never sees. Extended 2026-05-12 to also fire on Cottage → Powered Cottage transition. |

### M9 — "Specialization & Goods"

| # | Feature | Why it matters |
|---|---|---|
| 7 | **Wind Turbine** | Capstone of elevation + power capacity. Requires hill terrain. Many small turbines reward players who claimed elevated terrain. |
| 8 | **Resource Extraction & Goods Flow (minimal)** | Two resources only: Timber (Forest) + Grain (Flat). City-level supply pool (no truck routing). Commerce near extraction sites stocks goods; stocked residential gets +growth. Bonus-only, no shortage penalty. M8's "Output" metric becomes the source-of-truth for goods generation. |
| 9 | **Pollution heat map overlay** | Already in backlog. Becomes essential once coal/nuclear/wind force pollution-aware planning. |

### M10 — "The Graph Pivot" (architectural milestone — see Graph Architecture section)

> This is the biggest design shift since M1. The simulation moves from "tile distance" to "road graph distance" — roads become *the* infrastructure decision, not a power conduit that happens to be adjacent to zones. See the Graph Architecture section for the full spec.

| # | Feature | Why it matters |
|---|---|---|
| G1 | **Road Graph extraction (G1)** | Build the graph data structure from road tiles. Cache it. Rebuild only on topology change. Foundation for everything below. No player-visible change yet — just a substrate. |
| G2 | **Service reachability via graph (G2)** | Fire/Police/School/Hospital coverage stops being a Chebyshev radius. Coverage flows along roads. Disconnect a service from the city → coverage gap. Connect a service to a new district via one road tile → instant coverage. Highest legibility win in the project. |
| G3 | **Districts + Agent Flows (G3)** | Auto-cluster contiguous same-zone tiles into districts. Worker flows go R-district → I-district via shortest road path. Commute distance replaces Manhattan with graph distance. Unemployment emerges naturally when no path exists. |
| G4 | **Service Capacity (seats/beds/hours) (G4)** | Schools have SEATS, hospitals have BEDS, police have PATROL HOURS. Services drain along the graph until they run out. "School is full" becomes a real, visible decision — build another one or upgrade. |
| G5 | **Traffic on segments (G5)** | Aggregate flows produce per-segment load. Overloaded roads slow commutes, which lowers happiness. Avenues/highways become *the* upgrade for chokepoints, not just bigger numbers. |

### M11+ — "Polish & Steam"

| # | Feature | Why it matters |
|---|---|---|
| 10 | **Win screen / scenario mode** | Loopolis milestone celebration; optional victory conditions for replay. |
| 11 | **Nuclear meltdown event** | Only after EventSystem supports warning ticks — must be reactable, not RNG-punishing. |
| 12 | **Steam publishing** | Export, store page, achievements, trailer. |

### Feel/QoL backlog (next-tier candidates, not in M8.5)

| # | Feature | Notes |
|---|---|---|
| B1 | **Time Travel Slider** | Ring-buffered 200-tick autosave history + UI scrubber + "branch from here." High value, defer until F1/F2/F3 ship and we see whether failure-frustration is still a problem. |
| B2 | **Stat Graphs Drawer** | Sparkline charts of pop/balance/happiness/workers/customers over time. Diagnostic gold. Defer until we see whether the City Health panel is enough. |
| B3 | **Smart Camera Bookmarks** | 1-9 to bookmark; Tab to cycle trouble spots. Becomes valuable only once cities are large enough to need it. |
| B4 | **The Mayor's Inbox** | Citizen letters generated from sim state. Strong identity feature; risk is text feeling repetitive. Pairs naturally with B6 (city naming). Consider for M9. |
| B5 | **Daily Map Seed** | One global seed per real-world day, score saved locally. Habit-loop feature. Defer until F2 (scenarios) proves replay format works. |
| B6 | **Name Your City + Districts** | Mayor + city name on new game; districts auto-named at Town milestone. Cheap identity win. Strong pair with B4. Promote to M9 if F1-F3 land well. |
| B7 | **Agent Co-Mayor Mode** | Watch the agent play; pause and take over. Unique-to-Loopolis hook the game keeps asking for. Large effort; defer until the agent policy is reliably good. M10+. |

### Rejected / deferred outright

- **Tier-3 services** — micromanagement, no new decisions
- **4-resource supply chain in M9** — overscope; defer extra resources to playtested M10
- **Multi-step processing chains** (extract→process→sell) — turns Loopolis into Factorio
- **Negative-happiness-on-goods-shortage** — failure not legible enough at introduction
- **Meltdown event without warning ticks** — illegible RNG punishment
- **Commute as hard employment cap** — illegible cascade (commute → unemployment → happiness → pop). Replaced with direct happiness penalty.
- **Commercial foot-traffic income scaling** — punishes the wrong zone (commercial, already the favored one) and the offense is segregated industrial. Folded into the wider mixed-use radius instead.
- **Mixed-use bonus increase as standalone solution** — pure buff with no cost; violates "features without tradeoffs are just options." Kept only as supplement to Commute Happiness.
- **Per-citizen agent simulation** — out of scope forever. Loopolis is a planner game. Agent flows are aggregate per-district, not per-person.
- **Full A* per-worker pathfinding every tick** — combinatorially insane at 256². Replaced with cached centroid-to-centroid shortest paths in the Graph Architecture spec.
- **"Powered 1×1" as a separate building type in the catalog** (rejected 2026-05-12) — Doubles catalog size for the only quality that varies (power). Instead: same building TypeId, `IsPowered` state toggles capacity/tax multipliers. One building, two states. Same pattern as "ready to develop" — a state, not an entity.
- **Letting commercial develop unpowered** (rejected 2026-05-12) — Commerce without power has no narrative basis (a closed shop with no lights is not a "low-tier shop"), and gives the player a way to grow tax base indefinitely without ever building a plant. Commerce remains a power-gated unlock; this is the player's first deliberate "I need power now" moment.
- **Industrial unpowered = no jobs at all** (rejected 2026-05-12) — Too punitive on pre-zoning. Bare-minimum jobs at 0.1× lets the player stake industrial sites near the border without losing budget; powering them is the *productivity* decision, not the *existence* decision.

---

## M8 Feature Specs

### Service Tiers & Specialization

| Building | Tier | Radius (Manhattan) | Cost | Maint/tick | Unlock |
|---|---|---|---|---|---|
| Fire Station | 1 | 4 | $400 | $2 | Always |
| Central Fire | 2 | 8 | $1500 | $5 | City (5000) |
| Police Station | 1 | 4 | $400 | $2 | Always |
| Police HQ | 2 | 8 | $1500 | $5 | City (5000) |
| School | 1 | 5 | $400 | $2 | Always |
| University | 2 | 10 | $2000 | $6 | City (5000); gates future high-rise residential |
| Hospital | new | 6 | $1200 | $4 | City (5000); reduces event-driven happiness penalties by 50% in coverage |

**Tradeoff:** "Do I keep my 3 cheap stations or consolidate to one HQ? HQ saves clicks and covers a denser core, but costs more upfront and creates a single point of failure."

**Legibility:** Each service shows its radius on hover. Coverage gaps already render in the existing overlay system.

### Power Capacity & Plant Variants

**Capacity model:**
- Every PowerPlant has `Capacity` (kW).
- Every powered building has `Draw` (kW). R=1, C=2, I=3, services=2, tier-2 services=5.
- City-level supply vs demand. If `supply < demand`, brownout penalty applies proportionally: zones with the "powered" flag but no real capacity flicker, stop growing, and slow-decay at -2%/tick.
- HUD shows `Power: 850 / 1100 kW` with green/yellow/red color.

**Plant variants:**

| Plant | Build cost | Maint | Capacity | Pollution | Notes |
|---|---|---|---|---|---|
| Coal Plant (existing) | $1000 | $8/tick | 800 kW | 5-tile radius (uses existing PollutionSystem) | Default early-game option |
| Nuclear Plant | $4000 | $15/tick | 1600 kW | None while running | If destroyed/abandoned: leaves a 5-tile radiation footprint that persists 200 ticks |
| Wind Turbine | $600 | $1/tick | 200 kW | None | M9 — requires tile elevation ≥ 2 |

**Tradeoff:** "Build cheap coal and deal with pollution, or save up for nuclear and grow into the headroom, or scatter wind turbines across the hills if I claimed them."

**Legibility:** Brownout state has a distinct visual (flickering electric icon). Event log: `Brownout in north district — supply 800/1100 kW`. Different from "no power at all" (dark icon).

### Land Value & Elevation (minimal)

**Mechanic:**
- Reuse existing `TerrainType.Hill` as elevation tier 2 (no new enum yet).
- One new building: `res_villa_hillside_3x3` — requires ≥1 Hill tile in its footprint. Tax modifier 1.5×. Unlocks at Town.
- The existing $50 Hill surcharge stays — but now hills are *desirable*, so the surcharge becomes an investment cost rather than a penalty.

**Tradeoff:** "Do I burn this hill on a regular townhouse, save it for a hillside villa, or leave it forest for happiness?"

**Legibility:** Hovering a Hill tile shows "Hillside villa eligible: ✓". If the player places a non-villa zone, they see what they gave up.

**V2 (deferred):** Multi-tier elevation (0–3), peak terrain, view-dependence, penthouse 4×4 variant.

### Commute Happiness + Wider Mixed-Use Bonus

**Problem solved:** A player could segregate industrial to the far south, residential to the north, connect with one spine road, and face zero penalty. Traffic density doesn't trigger because the spine touches few zones at any point. Compact mixed-use layouts had no mechanical reward beyond pollution avoidance.

**Mechanic — Commute Happiness:**

For each developed residential tile, find the nearest powered industrial tile (Manhattan distance). Commercial tiles count as job sources at half weight (effective distance = actual × 2). Apply happiness modifier:

| Nearest job source (Manhattan) | Happiness modifier |
|---|---|
| 0–8 | 0 |
| 9–14 | −0.10 |
| 15+ | −0.20 |
| Residential pop < 50 anywhere in city | 0 (early-game grace) |

Tooltip on residential shows commute distance and which job cluster it points to. Joins existing happiness breakdown (service coverage, pollution, etc.) — no new mental model for the player.

**Mechanic — Wider Mixed-Use Bonus:**

The existing commercial-adjacency residential growth multiplier (1.5×) extends from Chebyshev-1 to Chebyshev-3. Magnitude unchanged. This converts an accidental bonus into a deliberate design pattern.

**Tradeoff:** "Segregated zones are cheap to plan and avoid pollution mixing, but unhappy residents cap your population. Mixed districts grow faster but require pollution management."

**Legibility:** Happiness drop is the existing primary feedback signal. Tooltip line `Commute: 12 tiles to nearest jobs (−0.10)` makes the cause explicit. Soft penalty — segregation is suboptimal, not impossible.

**Why soft, not hard:** A hard employment cap creates a multi-system death spiral (commute → unemployment → happiness → pop) that's illegible without a per-tile overlay. Direct happiness modifier rides the existing pipeline.

**Numbers flagged for balancer:** Manhattan-8/14/15 thresholds, −0.10/−0.20 magnitudes, Chebyshev-3 bonus radius. Validate on default + town + a custom "spine layout" scenario before locking.

### C/I Vitality HUD (Customers, Workers, Output)

**Problem solved:** Residential has a clear vitality narrative — `Pop 5000/5550`, visible building growth, milestone progress. Commerce shows only income; industrial shows only jobs. Players can't tell if a commercial strip is thriving or starving without inspection.

**Mechanic — Three parallel HUD rows:**

```
Pop:       5,000 / 5,550        [residential]
Workers:   2,980 / 4,900  (61%) [industrial]
Customers: 1,840 / 3,200  (58%) [commercial]
```

- **Pop** — unchanged.
- **Workers** — re-label of existing Jobs metric (`employed / total industrial capacity`). No new sim work.
- **Customers** — new commercial pool. Per commercial tile capacity (200 customers; same shape as residential's 50 cap). Customer count = sum of residential population within Chebyshev-3 of the commercial tile. Below 40% utilization, commercial growth halts. Above 80%, commercial tries to grow tier (matches residential's existing 80% growth rule).

**Mechanic — Output metric:**

Industrial tiles produce `Output: N units/tick`. Output per tile = `workers × productivity`, where productivity = 1.0 baseline × (avg happiness of source residential) × (1 / commute_multiplier). Interim effect until M9 lands: Output × $0.01 = passive "Goods" income line (so the number matters before supply chains exist). At ~150 output, that's ~$1.5/tick — flavor, not gameplay-changing.

When M9 ships, Output becomes the actual generation rate for Timber/Grain — no rework needed.

**Mechanic — Tile occupancy tint:**

A single shading rule applied uniformly to all three zone types: tiles tint darker when utilization < 40% ("dim"), normal at 40–80%, brighter at > 80% ("hot"). Folds into the existing backlog item "Population density per zone (color intensity scales with occupancy)" — make it apply to C and I too.

**Tradeoff:** Surfaces existing tradeoffs that were previously invisible — over-zoning commercial leads to vacancy (visible), under-zoning industrial throttles residential growth (visible).

**Legibility:** Three rows share the same `current/capacity (%)` format. Player learns one pattern, reads all three. "Customers 200/3200 (6%)" instantly communicates "this commercial district has no nearby residents." "Workers 4900/4900 (100%)" instantly communicates "residential is throttled by lack of jobs."

**Scope discipline — explicitly rejected:**
- Per-building progress bars
- Animated foot traffic / smoke effects
- Daily-revenue-per-shop as a top-level HUD metric (it's derivable, ship as tooltip only)
- Vacancy rate as a separate HUD metric (utilization% already communicates this)

### P1 — Power-as-Density-Unlock

**Problem solved:** With the new empty-map start, the current "power required for any growth" rule turns the first 5–10 minutes of every new game into the same single-track puzzle: build the $1000 plant before anything happens, on $4000 of starting cash. There is no alternative first move, no decision, and a brand-new player who zones first sees nothing for 60 ticks and reasonably concludes the game is broken.

This is the worst possible failure mode in a city builder: nothing happening, no diagnosis, no path forward. Inspired by SimCity 2000, where low-density develops without power and power is the *density unlock* — not the *existence* gate.

**Mechanic:**

1. **Unpowered `res_house_1x1`** ("Cottage" state): grows on any road-adjacent residential tile with no power. Capacity 25 (vs 50 powered). Tax multiplier 0.7× (on top of existing land value and rate). Same building TypeId, same footprint — only the IsPowered state differs.
2. **Powered `res_house_1x1`** ("Powered Cottage" state): the same building when its tile becomes powered. Capacity flips to 50; tax multiplier flips to 1.0×. No demolition, no rebuild — the state flips and the renderer updates the sprite/label.
3. **All other residential tiers (2×2 townhouse, villas, apartments, etc.)** still require power to grow. An unpowered 1×1 cottage CANNOT grow into a 2×2 townhouse — the cottage is the ceiling of the unpowered ladder.
4. **Commercial** still requires power, no exceptions. Commerce without power has no narrative justification and would let players skip the plant decision indefinitely.
5. **Industrial** can develop unpowered ("Bare Lot" state) but generates 0.1× jobs and 0 Output. Powering it converts it to "Factory" state with full jobs + full Output. Same pattern: state, not entity.
6. **Services** unpowered = no coverage. (Unchanged.)
7. **Power loss on a 2×2+ building:** building degrades back to 1×1 cottage(s) on the powered tiles, demolishes the rest. Pop drops to what cottages can hold. This is the existing "capacity drop → pop decline" pipeline — no new sim work.

**Tradeoff:**
- **Without power:** you can grow a city up to ~25 pop × N cottages. Tax trickles in slowly. You stay in the early-budget squeeze for longer but never zero-out.
- **With power:** you double capacity and tax on all your existing 1×1s, unlock commerce + industry productivity + larger tiers. Power costs $1000 up front and $8/tick maintenance.
- "Do I plant early (expensive, opens everything) or zone first (slow trickle, frees the plant cash for a better spot)?"

This is the first real strategic *choice* in the early game. There is no longer one correct first move.

**Legibility:**
- Cottage vs Powered Cottage have distinct sprites (existing "powered vs unpowered" tint already exists in the renderer — just needs the size/density indicator).
- F3 Building Birth Announcement fires on Cottage → Powered Cottage transition: `Powered Cottage! +$1/tick` floating label. The player *sees* the upgrade arrive when power reaches the tile.
- Tooltip on a Cottage tile reads: `Cottage (unpowered) — 25 cap, 70% tax. Build a power plant to upgrade.` Tooltip on a Powered Cottage reads: `Cottage (powered) — 50 cap, full tax. Townhouse upgrade pending — need 80% capacity.`
- The City Health panel breakdown line `Unpowered: N cottages capped at 50% growth` makes the cumulative cost visible.

**Tutorial hint rewrite:**
- Old: "Place a power plant — your city needs power to grow."
- New: "Zone homes along the road to attract residents. Build a power plant to unlock larger buildings and businesses."

The new hint frames power as *acceleration*, not *ignition*. Vanishes after the first plant is placed (existing logic).

**Numbers flagged for balancer:**
- Cottage capacity 25 (half of powered 50) — large enough to feel like a real downgrade, small enough that the player still cares about powering.
- Tax multiplier 0.7× — punchy enough to make the powered upgrade feel rewarding, not so harsh that unpowered Cottage gameplay collapses.
- Industrial unpowered job multiplier 0.1× — pre-zoning stays cheap but contributes almost nothing to employment.
- Test on empty-map default: do players naturally zone first, then plant, in playtest? If they still plant first by reflex, lower the cottage capacity to 20.

**Scope discipline — explicitly rejected:**
- Separate `res_house_1x1_unpowered` building in the catalog (doubles catalog for one state difference)
- Commercial unpowered "kiosk" tier (no narrative basis, breaks the "power is the commerce gate" learning moment)
- Per-tile flickering brownout animation on Cottages (visual noise; the sprite difference between cottage and powered cottage is the signal)
- Letting unpowered industrial generate any Output (Output → Goods → late-game supply chain; if unpowered industrial counted, the whole goods economy starts free)
- "Cottage" as a graduated unpowered tier ladder (1×1 unpowered → 2×2 unpowered → ...). Power is the gate to the upgrade ladder, period. One unpowered tier, then the gate.

**Verdict:** Recommended for M8. Small scope, high onboarding impact, sharpens the empty-map first move from "do the prerequisite" to "pick a strategy."

---

## M8.5 Feature Specs — "Feel Sprint"

### F1. Road Pulse on Build

**Problem solved:** The PowerNetwork BFS is one of the most elegant pieces of the simulation — and the player never sees it work. Placing a road that completes a power chain is mechanically identical to placing one that doesn't, from a feedback standpoint. The game feels static during the most consequential moments.

**Mechanic:**
- On every player place action (road / zone / power plant / power line), the renderer emits two visual events:
  1. **Cursor ripple** — a subtle 8-tile-radius ring expands and fades from the placed tile over 0.3s. Universal "I did a thing" feedback.
  2. **Newly-ready pulse** — any tile whose state changed *because of this placement* (newly powered, newly road-adjacent, newly ready-to-develop) gets a brief gold pulse for 0.6s.
- When a power plant is placed, the BFS reveal is *animated*: the newly-powered tiles light up in the order the BFS reaches them, over ~0.5s total. Roads first, then attached zones.
- Sound: a soft chime on cursor ripple, a brighter ping on newly-ready pulse. Both quiet enough to be ambient.

**Player experience:** "I placed one road tile and watched the gold pulse hop across four zones I'd already laid down. They're ready now. I can *see* the chain."

**Tradeoff this feature reveals (not creates):** Players will newly understand "why didn't anything light up?" → that road didn't complete a chain. Surfaces the existing chain-completion decision without adding new mechanics.

**Failure legibility:** A placement with *no* gold pulses is its own answer: "nothing changed because of this." Player learns to look for the pulse.

**Scope discipline — explicitly rejected:**
- Persistent glow on ready tiles (clutters the map; tile tint already handles this)
- Animated traffic flow (defer to M10 polish)
- Particle smoke from power plants (defer to M10 polish)
- Per-citizen sprites (out of scope forever — this is a planner game, not a sim game)

**Effort:** Small-to-medium — uses existing Godot tween + the existing power flood-fill event from PowerNetwork. Sim engine emits a `TilesChangedThisTick` list; renderer animates it.

### F2. Seeded Scenarios with Goals

**Problem solved:** Loopolis has six powerful sandbox systems and no reasons to play more than one game. Every session is identical: same map, same start, no win condition that feels different from the last one.

**Mechanic — Scenario picker on the main menu, 6-8 named challenges at launch:**

| Scenario | Map seed | Goal | Constraint | Gold / Silver / Bronze |
|---|---|---|---|---|
| Coastal Town | Water bisects map | Reach 1,500 pop | None | 800 / 1200 / 1500 ticks |
| Industrial Heart | Flat heartland | Reach 5,000 pop | ≥30% industrial tiles | 1000 / 1500 / 2000 ticks |
| Hill Country | Hill-dense terrain | Build 6 hillside villas | None | 700 / 1100 / 1500 ticks |
| The Green Pledge | Forest-heavy | Reach 5,000 pop | No coal plants allowed | 1200 / 1700 / 2200 ticks |
| Tight Budget | Standard | Reach Town (500) | Starting $2,000 (vs default $10,000) | 600 / 900 / 1200 ticks |
| Disaster Run | Forced-events scenario | Survive 1,500 ticks with pop ≥ 2,000 | Events every 100 ticks min | Survived / 1.5× / 2× target pop |
| The Hill | All terrain is hills | Reach 3,000 pop | Hill surcharge on every tile | 1000 / 1500 / 2000 ticks |
| Open Plains | Standard flat | Reach Metropolis (25,000) | None — long-form benchmark | 5000 / 8000 / 12000 ticks |

Each scenario has:
- A short narrative blurb ("The river splits Coastal Town. Bridges aren't a thing yet — make do.")
- A locked terrain seed so leaderboards are meaningful
- A clear goal banner pinned at the top of the HUD during play
- A medal screen on completion (or "Try again" on failure) showing tick count, peak pop, final balance

**Player experience:** "I won Coastal Town silver. I want gold. I think I know what to do differently — let me try again from the same seed."

**Tradeoff per scenario:** Each scenario forces a different *layout* strategy. The Green Pledge means no cheap coal — you must save for nuclear or design tight enough that coal isn't needed. The Hill means you can't avoid the surcharge — you must rush hillside villas to amortize it.

**Failure legibility:** Scenario goals are visible at all times. "Reach 5,000 pop" with a counter "1,840 / 5,000." When you fail, you see *which* number stopped moving and *why* (the existing City Health panel still runs).

**Scope discipline — explicitly rejected:**
- Branching story scenarios (turns Loopolis into Frostpunk; out of scope)
- Time-pressure timers shown as a doom counter (anxiety, not fun — use tick budget for medals only)
- Leaderboards in v1 (local medal storage only; online leaderboards M10+)
- Scenario-specific buildings (defeats the "one ruleset, many maps" promise)
- More than 8 scenarios at launch (curation > volume; expand based on which earn replays)

**Effort:** Medium — scenario loader exists. Needs: scenario-config schema (goal + medal thresholds + constraint flags), goal-evaluator system, scenario picker UI, medal screen UI, local medal-storage save file.

### F3. Building Birth Announcement

**Problem solved:** BuildingGrowthSystem is one of the most satisfying systems in the game. It runs silently. Players never know it just happened unless they're staring at the right tile at the right moment. The simulation rewards them — and the rewards are invisible.

**Mechanic:**
- When a building grows tier (1×1 → 2×2 → 3×3, or absorbs neighbors into a 4×4), the renderer spawns a floating label at the building's center: `Hillside Villa! +$4/tick` (1.2s fade-in/fade-out, slight upward drift).
- Label format: `[building display name]! +$N/tick` where the income figure is the *delta* from the previous tier.
- A short zone-specific chime plays:
  - Residential: soft warm bell
  - Commercial: bright two-note ping
  - Industrial: lower mechanical clack
- Chimes are throttled: max 3/second city-wide (no auditory chaos when a metropolis upgrades 20 buildings in one tick). Visual labels are not throttled but cap at 8 simultaneous on screen.
- The EventLog also gets a one-line entry: `Tick 412: Maple Heights — Townhouse → Villa`. Players can review the day's wins.
- **Extension (2026-05-12):** Same announcement fires on Cottage → Powered Cottage transition (`Powered Cottage! +$1/tick`). Same chime. Treats the power-upgrade as a tier event because it functionally is one.

**Player experience:** "I came back from the kitchen, and three little gold labels were floating up from my downtown. Something good happened while I was gone."

**Tradeoff this feature reveals (not creates):** The income delta tells the player *which* tier upgrades are worth the placement cost. A villa announcement shows +$4/tick; a townhouse only +$1/tick. Player learns the value gradient organically.

**Failure legibility:** Lack of announcements = lack of growth. A long quiet stretch is a diagnostic signal: "nothing's upgrading — check capacity and conditions."

**Scope discipline — explicitly rejected:**
- Detailed citizen reactions ("John moved in!") — defer to B4 Mayor's Inbox
- Cinematic camera-pan to the upgraded building (interrupts the player's flow)
- XP / level-up bars per building (over-gamifies what should feel organic)
- Different colors per upgrade type (the chime already differentiates; visual noise otherwise)

**Effort:** Small — BuildingGrowthSystem already emits per-tick growth events; renderer just needs a floating-label widget + chime player + EventLog hook.

---


## Scale & World Geometry (canonical)

> Added 2026-05-12. Settles the long-open "what's the map size" question and re-bases the entire building catalog.

### Tile unit

**1 tile = 8 m × 8 m.** Matches Cities: Skylines and modern indie city builders. Picked so that:

- 1×1 = 1 single-family suburban lot (intuitive)
- 1 tile of road = 1 lane (street). Avenue = 2 lanes of visual. Highway = 3 lanes of visual.
- A real city block (80–120 m) is 10–15 tiles → matches the largest growable building footprint.

**Lock this. All building footprints, road widths, and placement costs derive from 8 m/tile.**

### Map size progression

| Preset | Grid | Tiles | When |
|---|---|---|---|
| Hamlet | 64×64 | 4,096 | Tutorial / regression scenarios |
| Town | 128×128 | 16,384 | Default for most scenarios |
| City | 256×256 | 65,536 | Metropolis-target scenarios |
| Region | 512×512 | 262,144 | Loopolis-target sandbox, ship goal |

**Fixed per scenario, picked at new-game time. No land-unlock progression** (rejected — parallel meta-system, fails core-loop deepening test).

**Migration target order:** 128 → 256 → 512. Each step ships only after performance budget is met at the previous size.

### Multi-tile roads — class, not footprint

Road tiles stay **1×1 footprint**. New `RoadClass` property: Street / Avenue / Highway. Width is visual + capacity + cost. Multi-tile-wide *placement* explicitly rejected: curves and intersections become combinatorial nightmares.

| Class | Visual width | Capacity | Cost | Maint/tick | Speed multiplier |
|---|---|---|---|---|---|
| Street | 1 tile | 8 | $10/tile | $1 | 1× |
| Avenue | 2 tiles (shoulders) | 24 | $40/tile | $2 | 2× |
| Highway | 3 tiles (shoulders) | 60 | $100/tile | $4 | 4× |

**Highway constraint:** zones cannot be adjacent to highways. Highways are throughput-only. Forces the natural "highway spine + avenue arteries + street capillaries" pattern.

Placement tool stays line-drag (existing). Renderer paints the 1-tile centerline plus visual shoulders on adjacent half-tiles.

### Building catalog at scale

Existing 1×1 → 4×4 catalog stays valid for early/mid game. New tiers extend the top end:

| TypeId | Zone | Size | Cap | Unlock |
|---|---|---|---|---|
| `res_block_6x6` | R | 6×6 | 1,800 | Metropolis |
| `res_tower_8x8` | R | 8×8 | 3,200 | Metropolis + University coverage |
| `res_skyscraper_12x12` | R | 12×12 | 7,200 | Loopolis + downtown adjacency |
| `com_mall_6x6` | C | 6×6 | 7,200 customers | City |
| `com_district_8x8` | C | 8×8 | 12,800 customers | Metropolis |
| `ind_complex_6x6` | I | 6×6 | 720 workers | City |
| `ind_complex_8x8` | I | 8×8 | 1,280 workers | Metropolis |

**Capacity stays at 50 × tile count.** One formula the player can predict.

**Power plants become real facilities** (placed as fixed-size buildings, not zones):

| Plant | Size | Capacity |
|---|---|---|
| Coal Plant | 4×4 | 800 kW |
| Nuclear Plant | 6×6 | 1,600 kW |
| Wind Turbine | 3×3 | 200 kW |

**Downtown gravity rule:** buildings ≥ 6×6 only grow if ≥ 10 same-zone tiles within Chebyshev-4. Prevents scattered skyscrapers; high-density buildings cluster naturally. Legible via tooltip: "Growth requires 10 contiguous R tiles within 4 (currently: 7)."

### Performance budget

Targets at 512×512: 60 fps render, 10 sim ticks/sec, < 100 ms per sim tick.

| System | At 512² | Action required |
|---|---|---|
| PowerNetwork BFS | OK (sparse) | No change |
| RoadNetwork | OK | No change |
| BudgetSystem | 5 ms scan | Maintain running maintenance sum |
| PopulationSystem | OK with index | Add `DevelopedTiles` set |
| **PollutionSystem** | **6.5M ops/tick — broken** | **Rewrite as scatter-from-sources** |
| **HappinessSystem commute** | **2.5B ops/tick — broken** | **Rewrite to industrial cluster centroids** |
| **LandValueSystem** | **12.8M ops/tick — borderline** | **Rewrite as separable convolution or scatter** |
| DemandSystem | 2.5M ops/tick — borderline | Maintain commercial-proximity grid (O(1) lookup) |
| EmploymentSystem | OK | No change |
| BuildingGrowthSystem | OK | No change |
| MilestoneSystem | OK | No change |
| EventSystem | OK | No change |
| RoadTrafficSystem | OK | No change |

Renderer: switch from `Node2D._Draw` to Godot `TileMapLayer` before 256×256.

Save file: 128² ~500 KB raw, 512² ~8 MB raw / ~1 MB gzipped. Region-chunking deferred.

### 3-session migration roadmap

**Session 1 — "128 doesn't break anything":**
- Make grid size a scenario parameter; default new scenarios to 128×128, keep 32×32 for regression
- Switch renderer to `TileMapLayer`
- Validate all 342 tests pass at 128²
- Profile a tick; identify slowest system
- Ship criterion: 128 plays smoothly

**Session 2 — "Scale the offenders":**
- Rewrite PollutionSystem (scatter)
- Rewrite LandValueSystem (separable blur or scatter)
- Rewrite HappinessSystem commute (cluster centroids)
- Add `DevelopedTiles` index
- Bump default to 256×256
- Ship criterion: 256² hits 10 ticks/sec sustained

**Session 3 — "Real buildings + real roads":**
- Add `RoadClass` (Street / Avenue / Highway)
- Highway-no-adjacent-zones rule
- Multi-tile power plants (4×4 coal, 6×6 nuclear)
- New 6×6 / 8×8 R/C/I tiers
- Downtown-gravity growth constraint
- Ship criterion: a natural 256² city grows from edge suburbs to clustered downtown

After Session 3: benchmark 512×512. Promote to Region if perf holds; otherwise dedicated session 4.

### Explicit rejections

- Multi-tile-wide *road placement* (curves/intersections nightmare; class-not-width chosen instead)
- Land-unlock progression (parallel meta-system; pick map size at new game)
- Top-tier buildings without downtown gravity (turns map into scattered skyscrapers — illegible)
- Shipping 512² before the three big systems are rewritten (sim stutter; player thinks game is broken)
- Changing the 8 m/tile spec mid-development (lock it)

---

## Graph Architecture (M10 — the structural pivot)

> Added 2026-05-12. This is the biggest architectural decision the project will make. Everything below describes how Loopolis stops being a "tile-distance game" and becomes a "road-graph game" — the same shift SimCity 4 made between Rush Hour and the base game, and that Cities: Skylines lives or dies on.

### The diagnosis

Every "proximity" system in Loopolis today is a lie:

- A fire station "covers" tiles within Manhattan-4 — **even if there is no road from the station to the tile**.
- A worker's commute is `|x1−x2| + |y1−y2|` — **even if a river cuts the city in half**.
- Power floods through tiles but services don't, so a school in an unreachable cul-de-sac still teaches.

The grid is not the city. The road graph is the city. Roads should be the spine of every interaction, not a decoration that happens to also conduct power.

**The pivot:** rebuild the simulation around three layers:

```
Tiles        — raw placement (zones, terrain, buildings)
Road Graph   — extracted from road tiles (the network)
Districts    — auto-clustered zone regions (the demand/supply nodes)
```

All cross-tile interactions (services, commutes, traffic, supply) flow through the graph and the districts, not the raw tile grid.

### Layer 1 — Road Graph

**Nodes:** every road tile is a node. (We chose tile-nodes over intersection-nodes. Tile-nodes are dumber, slower, and right. Intersection-nodes are smaller but make road-class changes mid-segment painful, and `RoadTrafficSystem` already operates per-tile.)

**Edges:** undirected edges between road tiles that are 4-adjacent (N/S/E/W). Edge weight = `1 / speed_multiplier(road_class)`. Streets weight 1, Avenues 0.5, Highways 0.25. Shortest path = fastest path.

**Cliff blocks:** an edge does not exist if a `HeightLevel.Cliff` separates the two tiles (already enforced for placement; now enforced for connectivity). Water has no edge either way (zones can't be there).

**Highway adjacency rule (existing):** zones can't touch highways. Highways are an interior backbone of the graph — districts attach via avenues and streets only.

**Rebuild policy:**
- Maintained incrementally. Place a road → add a node + up to 4 edges. Erase a road → remove a node + edges + invalidate caches that touched it.
- Connected-components label cached per node. Rebuild only when topology changes, never per tick.
- Shortest-path queries between district centroids use a single-source Dijkstra per "service" or "district anchor," cached and invalidated on topology change.

**Numbers (sanity check at 256²):**
- Typical mid-game city: 1,500 road tiles, ~3,000 edges. Dijkstra from a single source is O(E log V) ≈ 30k ops. Cheap.
- Worst-case ship target (512², dense): ~10k road tiles. Still under 100k ops/source. Caching is essential, not catastrophic.

### Layer 2 — Districts (zone clusters)

**Definition:** a district is a connected component of same-zone tiles, found by flood-fill over 4-adjacency. Three district types: R-district, C-district, I-district. Adjacency for clustering is *tile-to-tile* (not road-graph) — districts are defined by what the player drew, not by what's connected.

**Anchor:** every district has a *road anchor* — the road tile closest to the district's centroid (or any road tile adjacent to a tile in the district if no road-adjacent tile is preferred). The anchor is the district's "address" in the road graph. If a district has no road anchor → it's an "isolated district" and produces no flow (matches existing road-adjacency rule for development).

**Naming:** districts are auto-numbered (R-1, R-2...). When B6 "Name Your City + Districts" ships, names replace numbers. The Town milestone is the natural moment to surface district identity.

**Sizing:** no hard cap. A 50-tile contiguous R-zone is one R-district. Players who blob zones get one mega-district; players who interleave get many small ones — both are valid layouts with different consequences (a mega-district has more workers at one anchor but worse commute averages within it).

**District count at scale (estimate):**
- 128²: 5–30 districts typical, 80 worst case
- 256²: 15–80 districts typical
- 512²: 40–200 districts typical
- Inter-district routing is `O(D²)` shortest-path queries in the worst case. At D=200, that's 40k pair-queries, each O(E log V). Mitigation: only re-route when topology changes; cache aggressively; for routing, use one Dijkstra per *district anchor* (D Dijkstras, not D²) and read off pair-distances from the distance map.

### Layer 3 — Agent Flows

**Worker flow (R-district → I-district):**

1. For each R-district, find the *cheapest reachable* I-district by road-graph distance from anchor to anchor.
2. The number of workers flowing on that route = `min(R-district's working-age pop, I-district's open jobs)`.
3. If no I-district is reachable from this R-district → flow is zero → all those workers are *structurally unemployed* (not "no jobs"; "no path to jobs"). Direct happiness penalty applies (-0.20 fixed). Distinct visual on the district badge: red broken-link icon.

**Shopper flow (R-district → C-district):**

1. For each R-district, find the *closest reachable* C-district.
2. Shoppers flowing = `min(R-district pop × 0.3, C-district open customer slots)`.
3. If no C-district is reachable → C-district doesn't fail; the R-district just doesn't get the mixed-use growth boost. (Shopping is a perk, not a requirement.)

**Why not full bipartite matching?** Because "every worker picks the optimal job" is a logistics game (Factorio, Anno). Loopolis is a planner game. The player decided where the roads go; the citizens take the obvious path. One R-district → one preferred I-district is legible. Splitting workers across multiple destinations is not.

**Commute distance feeds happiness:**

Replace existing Manhattan-distance commute thresholds with road-graph distance thresholds:

| Road-graph distance (R-anchor → I-anchor, weighted by road class) | Happiness modifier |
|---|---|
| 0–10 | 0 |
| 11–25 | −0.10 |
| 26+ | −0.20 |
| No path | −0.20 + red-link icon |

(Threshold numbers are first-pass; balancer pass required at 128² and 256². The shape of the function does not change.)

### Layer 4 — Services as graph reachability

This is the highest-payoff player-facing change. Services stop covering tiles by radius and start covering *districts via road distance*.

**Reachability:** a service building covers district X if X's anchor is within `service.range` weighted road-distance (Dijkstra from service's road-adjacent tile, distance ≤ range). The service's `range` replaces the existing Chebyshev radius.

| Service | Range (weighted road tiles) | Tradeoff |
|---|---|---|
| Fire Station | 12 | Fast response, small reach |
| Central Fire (HQ) | 24 | One station, dense core, single point of failure |
| Police Station | 12 | — |
| Police HQ | 24 | — |
| School | 16 | — |
| University | 28 | Gates high-rise residential |
| Hospital | 18 | Reduces event happiness penalty |

**Why weighted distance:** building one avenue extends every service's effective reach. Suddenly road-class upgrade is *also* a service-coverage decision. The player isn't just paying for faster commutes — they're extending fire coverage to that new outer district.

**Capacity model (the big one):**

Services don't have infinite throughput. They have a **resource pool** that drains as they serve districts:

| Service | Resource pool | Consumed by |
|---|---|---|
| Fire Station | 3 responses / tick | Each district covered with active risk = 1 response |
| Central Fire | 8 responses / tick | Same |
| Police Station | 200 patrol-hours | Each district drains `district_pop × 0.01` patrol-hours; closer drains less |
| Police HQ | 500 patrol-hours | Same |
| School | 200 seats | Each district drains `district_pop × school-age fraction` (~0.15) seats; closer first |
| University | 600 seats | Same |
| Hospital | 80 beds | Each district drains `district_pop × 0.04` beds; closer first |

**Drain ordering:** services serve their *closest* reachable district first, then the next-closest, until the pool is exhausted. A district that doesn't get a slot is **uncovered** and pays the existing happiness penalty (no school: -0.15, no police: -0.15, etc.).

**HUD readout per service:** `Maple School — 187 / 200 seats (94%) — covers 4 districts`. Click the service → outline-highlight which districts it serves, dim ones it doesn't reach. Click a district → highlight which services cover it.

**This creates three distinct decisions that don't exist today:**

1. **Capacity decision** — "My school is at 94%. Build a second, or upgrade to University?" Tier-up is a real choice, not flavor.
2. **Reach decision** — "There's an outer R-district my fire coverage doesn't reach. Do I build a second station closer, or run an avenue so the existing one reaches?"
3. **Failure cascade decision** — "If I lose the road to my Hospital, three districts go uncovered. Should I add a second route for redundancy?"

### Layer 5 — Traffic

Traffic stops being an abstract per-tile density and becomes a per-edge flow.

**Edge load:** for every R→I worker flow and R→C shopper flow, the route through the road graph contributes flow to each edge it traverses. Per-tick aggregation:

```
edge.load = sum over routes(workers_on_route + shoppers_on_route × 0.5)
```

**Edge capacity:** the road's class capacity (Street 8, Avenue 24, Highway 60).

**Congestion:** when `edge.load > edge.capacity`, the edge's effective weight increases proportionally (BPR-style: `weight × (1 + (load/cap)^4)`). This makes congested routes more expensive, which feeds back into commute distance, which feeds happiness — a clean, single-system feedback loop.

**Computational shortcut:** don't re-route every tick. Re-route only when the graph changes or every N=50 ticks ("rush-hour resnap"). Between rebuilds, use cached routes. Congestion grows as flows scale (population grows along existing routes), without requiring a full re-pathfind each tick.

**HUD overlay:** existing traffic-density tint is replaced with edge-load tint. Green ≤ 60% cap, yellow 60–100%, red > 100%. Hovering a road segment shows `Load 14 / 8 (180%) — commute +30%`. The chokepoint is *visible*, and the upgrade-to-Avenue button gives an immediate predicted-after-upgrade preview.

### What graph-Loopolis lets the player decide that today's game cannot

| Decision today | Decision after graph |
|---|---|
| "Where do I put zones?" | "How do I connect this district to the rest of the city?" |
| "Add another fire station for coverage." | "Extend the road to bring the existing fire station into reach, or build a new one — which is cheaper?" |
| "Industrial is too far — accept the commute penalty." | "Industrial is too far — but if I upgrade the spine to Avenue, the graph distance halves." |
| "School coverage is fine because the radius covers everyone." | "School is at 187/200 seats. Districts further out are uncovered." |
| "Traffic is high here, I guess." | "This single edge is the bottleneck for 5 districts. Upgrading these 4 tiles to Avenue solves five districts' commute penalty at once." |

Every one of these is a *layout decision* with a *legible cost* and a *visible consequence*. That is the SimCity 4 moment.

### Migration: 5-step plan, not 3

This is too big for 3 sessions. Honest sizing:

**G1 — Road Graph (1 session)**
- New `RoadGraph` class in Core. Nodes = road tiles; edges = adjacency; weights = road class.
- Incremental add/remove on place/erase. Connected-component labeling.
- API: `GetDistance(from, to)`, `IsReachable(from, to)`, `GetComponent(tile)`, `ShortestPathSourceMap(from)`.
- Tests: connectivity after cliff insertion, weight change on road class swap, component split on road erase.
- **Ship criterion:** All 342 existing tests still pass. RoadGraph emits the same connectivity info you'd get from BFS but cached. No player-visible change yet.
- **This is the foundation. Nothing else is built until it's solid.**

**G2 — Service Reachability (1 session, the big visible win)**
- Replace Chebyshev coverage in `HappinessSystem.GetServiceCoverage` with `RoadGraph.GetDistance(service_anchor, target) ≤ service.range`.
- Add `service.range` per service type (table in spec above).
- HUD overlay: hover a service → highlight covered districts. Hover an uncovered district → highlight the gap.
- **Ship criterion:** the first time the player erases a road and watches fire coverage on a district vanish. That moment alone makes the redesign worth it.
- **Risk:** existing scenarios calibrated for radius coverage may need tuning. Run all 8 scenarios and check medal feasibility. Pre-emptively bump service ranges so coverage area is roughly preserved at 128².

**G3 — Districts + Worker Flow (1 session)**
- Flood-fill `DistrictSystem` runs after zone changes. Each district gets: ID, zone type, member tiles, anchor (road-adjacent), pop/capacity/jobs/customers.
- `WorkerFlowSystem`: per R-district, find cheapest reachable I-district; record `WorkerFlow(R-id → I-id, count)`.
- Replace Manhattan commute distance in `HappinessSystem` with road-graph distance from R-anchor to I-anchor.
- Unemployment: workers with no reachable I-district → fixed -0.20 happiness + visual badge on district.
- **Ship criterion:** a city with a broken road between R and I shows "no path to jobs" and the player can fix it by placing one road tile. The whole "highway spine = jobs spine" idea becomes legible.

**G4 — Service Capacity (1 session)**
- Add resource pools per service (seats/beds/hours).
- Drain order: closest district first.
- HUD: `94% utilization` per service. Click → show served districts.
- Uncovered districts inherit existing service-absent penalty.
- **Ship criterion:** the player can build a second school *because of capacity*, not just because of radius.

**G5 — Edge Traffic (1 session)**
- Edge load aggregation from all worker/shopper routes.
- Congestion → edge weight inflation → commute penalty.
- HUD: per-edge load tint. Upgrade-to-Avenue button shows "Predicted load: 14/24 (58%)" before commit.
- **Ship criterion:** the existing `RoadTrafficSystem` retires in favor of graph-based traffic. Visual difference: chokepoints are individual edges, not regional blobs.

**Total: 5 sessions for graph-Loopolis. That is the right size for "the structural pivot of the game." Don't compress it. Each step ships independently and improves the game on its own.**

### Migration intersects with Scale roadmap

The Scale & World Geometry roadmap (3 sessions for 128→256→512) and the Graph migration (5 sessions G1–G5) are partially parallel:

- **Scale Session 1 (128² + TileMapLayer)** ships first. Pre-condition for everything below.
- **Graph G1 (Road Graph)** ships next — builds the substrate without changing gameplay.
- **Scale Session 2 (rewrite the 3 broken systems)** — but `HappinessSystem.commute` and `PollutionSystem` get *replaced* by the graph approach, not rewritten with the old approach. **Re-plan:** drop "Rewrite HappinessSystem commute (cluster centroids)" from Scale Session 2; it's superseded by Graph G3. Same with the traffic rewrite (superseded by Graph G5).
- **Graph G2 (Service Reachability)** — the big visible win.
- **Graph G3 (Districts + Worker Flow)** — completes the commute rework.
- **Scale Session 3 (Real buildings + RoadClass)** — RoadClass already feeds Graph weights; this becomes a quick polish step.
- **Graph G4 (Service Capacity)** — late, because services-as-radius is workable in the interim.
- **Graph G5 (Edge Traffic)** — final, because the old `RoadTrafficSystem` is already shipped.

**Effective order:** Scale S1 → Graph G1 → Graph G2 → Graph G3 → Scale S2 (only the parts not superseded) → Scale S3 → Graph G4 → Graph G5.

Total milestones from now to graph-Loopolis: 8 sessions. Honest. Doable. Each one ships a visible improvement.

### What this redesign is NOT

- **Not per-citizen agents.** Flows are aggregate per-district. Loopolis is a planner game; citizens are statistics, not characters. (Per-citizen sprites remain rejected forever.)
- **Not vehicle routing.** No trucks, no buses, no delivery agents. Goods flow at city level (M9 supply chain spec is unchanged). The graph routes *workers* and *shoppers* abstractly; it does not animate vehicles.
- **Not a logistics game.** The player draws roads. The simulation finds shortest paths. The player does not assign routes, schedule transit, or balance line capacity.
- **Not a replacement for the power grid.** PowerNetwork is its own BFS over a different conductor set (roads, power lines, zones). It keeps working. The Graph is the *agent* network, not the *power* network. (Open question: should they unify? Likely yes long-term, but the answer is "after G5 ships and we see if power-as-graph is the same shape." Don't pre-unify.)
- **Not Cities: Skylines traffic.** No lane-level routing. No traffic lights. No intersection types. One edge weight per road class. Anything more is out of scope.

### Numbers flagged for balancer (post-G1)

- Edge weights: Street 1, Avenue 0.5, Highway 0.25 — does the avenue feel impactful enough?
- Commute thresholds (road-graph): 10 / 25 — calibrated for 128². At 256² and 512², do these still feel right?
- Service ranges (12 / 16 / 18 / 24 / 28) — first-pass; will need scenario-level tuning so coverage is similar to today's Chebyshev radii at 128².
- Service capacities (3 fires / 200 seats / 80 beds / 200 patrol-hours) — first-pass; scenario-tested for "is this a tradeoff or a chore?"
- Worker-per-district fraction — currently assumes 100% of district pop is working-age. Probably 0.5–0.6 is closer; calibrate before G3 ships.
- BPR congestion exponent (4) — standard transport-economics value, but Loopolis is not a transport-econ sim. May need to drop to 2 for milder congestion.

### Why this is the right pivot now

1. **The scale pivot demands it.** At 256² and especially 512², Manhattan-distance scans cost billions of ops. Graph-based queries are sparse-graph operations; they scale.
2. **Every other system is already legible.** Power chains, building growth, milestones, scenarios — all clean. Distance is the last big lie in the simulation. Removing the lie unlocks the SimCity-4-tier decisions the game is currently faking.
3. **The Vitality HUD (M8) and Commute Happiness (M8) are pre-graph patches.** They surface a problem (zones are too far from jobs) using Manhattan. Once the graph ships, both stop being approximations and become exact. The HUD doesn't change shape; the numbers underneath it get correct.
4. **Cities: Skylines and SimCity 4 owe their depth to road graphs.** The genre's best moments are graph-driven. Loopolis cannot ship to Steam as "SimCity 2000 but worse." This is what makes it a 7+/10 game instead of a 5/10 game.

---
## Design Principles

1. **Every decision visible** — place a tile, see the consequence in the simulation immediately
2. **No hidden complexity** — mechanics should be learnable by observation, not by reading a wiki
3. **Meaningful tradeoffs** — more roads = more maintenance. More power plants = more coverage + more cost. Player must choose.
4. **Fail forward** — losing a city should teach you something specific. "I ran out of money because my power plant cost $10/tick and my city only had 2 zones" is a good failure.
5. **Scope discipline** — features must deepen the core loop, not just add more things to click

---

## Open Design Questions

- What's the map size? Fixed grid or scrolling world? *(Resolved 2026-05-12: Fixed per scenario. 4 presets — Hamlet 64×64 / Town 128×128 / City 256×256 / Region 512×512. See Scale & World Geometry.)*
- Is there a time pressure (year/season system) or is it turn-based?
- Do roads have traffic / capacity limits? *(Shipped — RoadTrafficSystem live)*
- Should disasters be optional (sandbox) or forced (challenge mode)?
- What is the unique hook that makes Loopolis different from SimCity 2000?
- **M8 new:** Does the player buy power plants from a menu, or does each plant variant have a separate toolbar button? (Probably toolbar — see "every decision visible".)
- **M8 new:** Should brownout zones lose population, or just stop growing? (Current spec: slow decay -2%/tick — needs playtest.)
- **M8 new:** Does the existing Hill $50 surcharge stay once villas exist, or does it become a "premium plot" upfront cost shown differently?
- **M8 new (commute):** Should the Manhattan-8 "no penalty" radius scale with map size? Current spec is fixed; larger maps may need calibration. *(Superseded by Graph G3 — road-graph distance replaces Manhattan.)*
- **M8 new (commute):** When a player has only residential + commercial (no industrial yet), commercial counts at half-weight. Is "half-weight" the right number, or should it be 1.0 in early game (pop < 500) and 0.5 in mid+?
- **M8 new (vitality HUD):** Should the Output metric have a small unit cost (e.g., industrial consumes $0.005 per output to produce) so that Output → Goods income isn't free money? Or accept the small bonus as flavor until M9?
- **M8 new (vitality HUD):** When commercial utilization hits 100%, should it generate a small *demand signal* that boosts nearby residential growth (a feedback loop), or is that double-counting with the mixed-use bonus?
- **M9 new:** When goods supply runs out mid-game, do residential buildings remember they had goods (regression-aware happiness) or treat it as fresh state each tick?
- **M9 new:** Should resource extraction buildings count as industrial (pollution, jobs) or be their own category?
- **M8.5 new (F1 pulse):** Should the BFS power-reveal animation be skippable / instant on fast forward (e.g., when the player is at 4× sim speed)? Likely yes — animation must never slow the sim.
- **M8.5 new (F1 pulse):** Should the gold "newly ready" pulse also fire when a zone becomes ready *via passive sim state change* (e.g., a service finishes construction), or only on direct player placement? Currently spec'd as player-placement only to avoid pulse-spam.
- **M8.5 new (F2 scenarios):** Do scenario medals contribute to a global "Mayor Rating" / unlock progression, or are they purely local pride? Lean toward pride-only for v1; unlock progression is its own design problem.
- **M8.5 new (F2 scenarios):** Should failed scenarios offer a one-click "Try again with same seed" or "Try again with new seed of same scenario"? Lean toward "same seed" by default — that's the replay value.
- **M8.5 new (F3 birth):** Should the announcement also fire on building *demolition* (e.g., a villa abandoning back to townhouse from neglect)? Currently spec'd as growth-only — abandonment is already covered by the EventLog and the City Health panel. Adding negative announcements risks "bad news spam."
- **M8.5 new (F3 birth):** Should announcements be tied to a per-tick global income readout ("+$12/tick this tick from growth!") or stay per-building? Lean per-building — the global figure is already in the budget HUD; per-building is the *story*, not the math.
- **Scale (roads):** Avenue/highway capacities (24 / 60) and cost ($40 / $100/tile) are first-pass numbers. Needs balancer pass at 128² before lock.
- **Scale (downtown gravity):** 10-same-zone-tiles-within-Chebyshev-4 threshold for tier-≥6 buildings is intuition, not data. Validate at 256² that skyscrapers cluster but not too aggressively.
- **Scale (power plants):** Coal 4×4 / Nuclear 6×6 placement — does the player select multi-tile footprint with cursor preview, or click center and the building auto-orients? Probably former. Confirm with godot-engineer.
- **Scale (highway-no-zone-adjacency):** Hard rule or soft penalty? Hard rule is more legible (placement blocked) but more frustrating. Lean hard at v1; soften if playtest complains.
- **Scale (commute thresholds):** Manhattan-8/14/15 commute happiness thresholds were calibrated for 32². At 128²/256² they will feel trivial. Scale linearly with map dimension or pick new fixed values? Balancer call. *(Largely superseded by Graph G3.)*
- **Scale (chebyshev radii):** Mixed-use bonus (Chebyshev-3), commercial customers (Chebyshev-3), pollution (5-tile) — all calibrated for 32². Scale or stay? Stay-fixed is more legible for the player; scale-with-map is more balanced. Lean stay-fixed and re-balance the magnitudes if needed.
- **Scale (save format):** 512² uncompressed is ~8 MB. Gzip immediately or wait for player complaint? Lean gzip-immediately (compression is cheap, save time matters).
- **Graph G1:** Tile-nodes vs intersection-nodes — chose tile-nodes for now. Will this hurt at 512² with 10k road tiles? Profile before assuming yes.
- **Graph G1:** When a road is erased mid-segment and splits a component in two, do all caches downstream invalidate at once (cheap but big GC), or lazily on next query (expensive query)? Lean eager invalidation — easier to reason about.
- **Graph G2:** Service ranges (12 / 16 / 18 / 24 / 28 weighted tiles) need calibration so 128² coverage is *roughly* equivalent to today's Chebyshev coverage. Avoid breaking existing scenario medals.
- **Graph G2:** Should the player see numerical road-distance to services in tooltips, or only the binary "covered / not covered"? Lean numeric — Loopolis players are systems-thinkers and the precise number teaches the road-class tradeoff.
- **Graph G3:** What counts as a district's "anchor" when multiple road tiles touch it? Closest-to-centroid is the spec; alternative is "highest road class" so an avenue-fronted district has a faster effective commute. Lean closest-to-centroid (simpler, less surprising).
- **Graph G3:** When an R-district is bigger than any single I-district can absorb, do excess workers just go unemployed (current spec) or do they overflow to the second-cheapest I-district? Lean current-spec — overflow logic creates illegible cascades and "why is this worker employed there?" questions.
- **Graph G3:** Should districts have a *visible boundary outline* on the map, or only on hover? Always-visible boundaries clutter the map; hover-only is invisible until needed. Lean hover-only + an optional "district overlay" toggle.
- **Graph G4:** Service drain order — closest-first is the spec. Alternative: proportional (every covered district gets `pool / count`). Closest-first creates the legible "outer district is uncovered" failure. Proportional creates the illegible "everyone is at 73% coverage" failure. Stay closest-first.
- **Graph G4:** When a service can't cover a district due to capacity (not distance), is that visually distinct from "can't reach due to distance"? They're different problems requiring different fixes. Lean yes — orange "capacity gap" icon vs red "reach gap" icon.
- **Graph G5:** BPR congestion exponent of 4 is correct for real-world traffic models. Loopolis is not realistic. Drop to 2 for milder, more legible congestion? Balancer call.
- **Graph G5:** Edge load tints on every road might clutter the map at 256². Show tints only above 60% load? Lean yes — green-everywhere is noise; only the chokepoints are interesting.
- **Graph overarching:** Should power propagation also move to the road graph (no more "power flows through zones")? Cleaner mental model — "graph carries everything." But it's a separate redesign and risks breaking power-line-as-alternative-conductor. Defer this question until after G5 ships.
- **Starter state:** Should the 2-3 pre-placed road tiles extending from the border road be erasable? Lean yes — they're just a visible hint, not a fixture. Only the border tile itself is unerasable. Player should be free to redesign the spine immediately if they want.
- **Border road (range):** What's the "near the border" road-graph distance N that triggers the 1.2× migration multiplier? First-pass: 8 weighted-road-tiles. Balancer pass needed at 128² to verify the bonus is real but doesn't trivialize layout. Open whether the falloff should be binary (within N → bonus, else nothing) or graded (linear interpolation 0–N).
- **Border road (post-G3):** Effective commute distance to external jobs = `graph_distance_to_border + K`. What's K? First-pass: 15 (matches the −0.20 unhappy threshold so external-only districts get the soft penalty, not the no-path red link). Tune at G3 ship time.
- **Border road (visual):** Renderer needs a unique sprite/marker for the border tile (downward arrow + "REGIONAL HIGHWAY" tag). Distinct enough that players never try to delete it, but not so loud that it dominates the map.
- **Starter state (cluster optimization):** Players will optimize layouts to hug the border road (migration bonus + future fallback jobs + future trade). Acceptable in v1 — it's a real incentive that emerges from the system. Counter in F2 scenarios by placing high-value terrain (forest, hills) far from the border on some maps. Don't pre-emptively nerf.
- **Starter state (legacy scenarios):** Existing scenarios (default/town/mixed/services/no_power/no_roads) were balanced with the seeded starter city. After this change, every scenario's first ~50 ticks looks different. F2 medal thresholds need re-calibration. Regression test scenarios (32² Hamlet) keep the old seeded starter for test stability.
- **P1 power-as-density (new 2026-05-12):** Should the unpowered Cottage tax multiplier be 0.7× (current spec) or 0.5× (matching capacity)? 0.7× preserves more early income; 0.5× makes the powered upgrade feel more decisive. Lean 0.7× — early income is the harder design constraint.
- **P1 power-as-density (new 2026-05-12):** When power is lost on a tile with a 2×2+ building, the building degrades back to cottages on the powered tiles only. Should the unpowered tiles in the footprint show "ruined" / "abandoned" sprites, or just revert to empty zone tiles? Lean empty zone — abandonment animation is a separate F3-style polish step.
- **P1 power-as-density (new 2026-05-12):** Should the `no_power` regression scenario still work, given that unpowered residential now grows? Lean yes — rename the scenario (or its semantics) to "no_power_no_growth_path" and verify cottages cap at the unpowered capacity. The scenario still tests that *higher tiers* gate on power; the no-power-no-anything semantics are gone forever.
- **P1 power-as-density (new 2026-05-12):** Does the brownout state (when supply < demand in M8 PowerCapacitySystem) treat unpowered cottages differently from powered ones? Lean no — brownout only applies to buildings that are *trying* to draw power. Unpowered cottages don't draw and don't brown out; they just sit at their unpowered ceiling.
- **P1 power-as-density (new 2026-05-12):** Should unpowered industrial generate any pollution? Lean no — pollution is a productivity-side externality; unpowered industrial at 0.1× jobs and 0 Output is just a parked lot. (Reduces the "stake industrial sites far from residential" anti-pattern.)

---

## Session Notes

| Date | Decision |
|---|---|
| 2026-05-10 | Architecture: clean backend (Loopolis.Core) + Godot frontend. Core has zero Godot dependency. |
| 2026-05-10 | Language: C# (.NET 10) for backend, Godot 4 .NET edition for frontend. |
| 2026-05-10 | Cost model: per-tile maintenance, not flat fee. Emerged from feedback loop. |
| 2026-05-11 | Population model: decline only when capacity drops, not from inactive zones. Feedback loop finding. |
| 2026-05-11 | M8 scoped: Service Tiers + Power Capacity (Coal+Nuclear) + Elevation (minimal). Three medium features beat one giant one. |
| 2026-05-11 | Power Capacity model: city-level supply vs demand pool, NOT per-plant routing. Brownouts must be HUD-visible or feature is rejected (violates "every decision visible"). |
| 2026-05-11 | Nuclear meltdown REJECTED for M8 — illegible RNG punishment. Replaced with persistent radiation footprint on destruction (visible, avoidable). Real meltdown event deferred until EventSystem supports warning ticks. |
| 2026-05-11 | Elevation scoped to minimal: reuse existing Hill terrain as tier 2, ship one luxury variant (`res_villa_hillside_3x3`), validate decision emerges before adding tiers/peaks/penthouses. |
| 2026-05-11 | Wind Turbine moved to M9 (depends on Elevation shipping in M8 and stabilizing). |
| 2026-05-11 | Service tiers capped at 2. Tier-3 rejected outright as micromanagement. |
| 2026-05-11 | Hospital added as NEW service category (not a school upgrade) — reduces event-driven happiness penalties in coverage. Distinguishes it from University (which gates high-rise res growth). |
| 2026-05-11 | Supply Chains scoped to M9 minimum: 2 resources (Timber, Grain), city-level pool, one-step chain, bonus-only (no shortage penalty in v1). Rejected: 4-resource launch, multi-step processing, negative-happiness-on-shortage. |
| 2026-05-11 | Supply chain abstraction: goods flow at city level, NOT building-to-building routing. Keeps Loopolis a city builder, not a logistics game. |
| 2026-05-11 | Zone segregation penalty: SOFT happiness modifier on long commutes, NOT hard employment cap. Hard cap creates illegible multi-system cascade; soft modifier rides existing happiness pipeline. Thresholds: ≤8 free, 9–14 −0.10, 15+ −0.20 (Manhattan, nearest powered industrial). Commercial counts as half-weight job source. Pop < 50 early-game grace. |
| 2026-05-11 | Mixed-use bonus: existing 1.5× commercial-adjacency residential growth EXTENDED from Chebyshev-1 to Chebyshev-3. Pure-buff alternatives rejected as standalone solution (no tradeoff); kept only as supplement to commute penalty so mixed-use becomes a deliberate pattern, not an accident. |
| 2026-05-11 | Commute hard employment cap REJECTED — illegible cascade (commute → unemployment → happiness → pop spiral the player can't trace). |
| 2026-05-11 | Commercial foot-traffic income scaling REJECTED — punishes commercial (already favored zone) when the offense is segregated industrial. Wrong target. |
| 2026-05-11 | C/I Vitality HUD: add `Workers: X/Y` (relabel of existing Jobs) and `Customers: X/Y` (new pool, Chebyshev-3 residential summation) as parallel rows to `Pop: X/Y`. Three identical-shape metrics so the player learns one pattern. |
| 2026-05-11 | Commercial Customer model: 200 customers per tile capacity, sourced from nearby residential within Chebyshev-3. Mirrors residential's existing 80%-utilization-triggers-growth rule. <40% utilization halts commercial growth. |
| 2026-05-11 | Industrial Output metric added. Interim effect: Output × $0.01 = passive Goods income (~$1.5/tick at typical scale — flavor not balance-changing). M9 supply chain hooks into Output as the canonical goods generation rate — no rework needed. |
| 2026-05-11 | Vitality visual: occupancy tint applied uniformly to R/C/I tiles (dim < 40%, normal 40–80%, hot > 80%). Folds existing residential-density-tint backlog item into a single cross-zone consistency rule. |
| 2026-05-11 | Rejected for vitality HUD: per-building progress bars, animated effects, daily-revenue-per-shop as top-level metric (tooltip only), separate vacancy rate (redundant with utilization%). Scope discipline. |
| 2026-05-12 | Blue-sky ideation session: generated 10 candidate features across QoL / "oh that's clever" / Challenge / Wildcard. Score-driving thesis: engineering is strong (4-5/10 today), missing feedback + identity + replay reasons. |
| 2026-05-12 | M8.5 "Feel Sprint" added as the sprint between M8 and M9. Sprint contains zero new simulation systems — purely surface-existing-state features. Goal: lift score to 6-7/10 via feel, identity, and replay. |
| 2026-05-12 | M8.5 top 3 selected: F1 Road Pulse on Build, F2 Seeded Scenarios with Goals, F3 Building Birth Announcement. Selection criterion: all three answer "how does the player know good things are happening?" at three different scales (instant / per-event / per-session). |
| 2026-05-12 | F1 Road Pulse scoped: ripple on every placement + gold pulse on newly-ready tiles + animated BFS power reveal. Skippable at high sim speed (TBD). Player-placement only, not passive state-change (avoids pulse-spam). |
| 2026-05-12 | F2 Seeded Scenarios scoped: 6-8 fixed-seed challenges with goal + constraint + gold/silver/bronze medals. Local medal storage only in v1 (online leaderboards M10+). Rejected: branching narratives, time-pressure doom counters, scenario-specific buildings. |
| 2026-05-12 | F3 Building Birth Announcement scoped: floating "+$N/tick" label on tier-up + zone-specific chime (throttled 3/sec) + EventLog entry. Rejected: demolition announcements (bad-news spam), per-building XP bars (over-gamifies), citizen reactions (defer to B4 Mayor's Inbox). |
| 2026-05-12 | Backlog candidates ranked: B1 Time Travel Slider, B2 Stat Graphs Drawer, B3 Camera Bookmarks, B4 Mayor's Inbox, B5 Daily Map Seed, B6 Name Your City + Districts, B7 Agent Co-Mayor Mode. Promotion criterion: M8.5 ships and we re-evaluate which gaps remain. |
| 2026-05-12 | Agent Co-Mayor Mode (B7) kept on backlog despite being the unique-hook candidate — large effort, depends on agent policy being demoably-good. Reconsider for M10+. |
| 2026-05-12 | Scale rethink: tile unit locked at 8 m × 8 m. Matches Cities: Skylines; 1×1 = 1 suburban lot; 1 tile road = 1 lane; full city block = 10–15 tiles. All catalog footprints derive from this. |
| 2026-05-12 | Map size: 4 fixed presets (Hamlet 64² / Town 128² / City 256² / Region 512²). Picked at new-game time. Land-unlock progression REJECTED — parallel meta-system fails core-loop deepening. |
| 2026-05-12 | Scale migration path: 128 → 256 → 512 across three sessions. Each step gates on perf budget being met at the previous size. 512 is ship target, not day-1. |
| 2026-05-12 | Roads: RoadClass property (Street / Avenue / Highway) on 1×1 tiles. Width is visual + capacity + cost, NOT footprint. Multi-tile-wide road placement REJECTED — curve/intersection combinatorics. Highways disallow adjacent zones — throughput-only spine. |
| 2026-05-12 | Power plants become real facilities: Coal 4×4 / Nuclear 6×6 / Wind 3×3. Placed as fixed-size buildings (not zones). Cost unchanged. Replaces current 1×1 plant absurdity. |
| 2026-05-12 | Building catalog extended: 6×6 / 8×8 / 12×12 R/C/I tiers added at Metropolis+. 50 × tile-count capacity formula preserved. Downtown-gravity rule for ≥6×6: must have ≥10 same-zone tiles within Chebyshev-4 — prevents scattered skyscrapers. |
| 2026-05-12 | Performance: 3 systems flagged for rewrite before 512² — PollutionSystem (scatter-from-sources), HappinessSystem commute (cluster centroids), LandValueSystem (separable convolution). Renderer must move from Node2D._Draw to TileMapLayer before 256². |
| 2026-05-12 | Resolved long-open question: "What's the map size?" Fixed per scenario. 4 presets. See Scale & World Geometry section. |
| 2026-05-12 | **Graph Architecture pivot (M10):** simulation moves from tile-distance to road-graph-distance for services, commutes, traffic. Three layers: Road Graph (tile-nodes + adjacency edges weighted by road class), Districts (auto-clustered same-zone components with road anchors), Agent Flows (R→I worker flow / R→C shopper flow via shortest path). Replaces Chebyshev-radius services and Manhattan-distance commute. |
| 2026-05-12 | Graph migration scoped as 5 sessions (G1 Road Graph → G2 Service Reachability → G3 Districts+Worker Flow → G4 Service Capacity → G5 Edge Traffic). NOT compressed to 3 — too large. Each session ships independently. |
| 2026-05-12 | Graph nodes = road tiles (not intersections). Rationale: simpler to reason about, RoadTrafficSystem already per-tile, road-class mid-segment changes are trivial. Tradeoff: bigger graph at 512² (~10k nodes) — profile before optimization. |
| 2026-05-12 | Service Capacity model (G4): seats / beds / patrol-hours / response slots per service. Drain order = closest-district-first. Uncovered districts inherit existing service-absent penalty. This is the source of the "build a 2nd school for capacity, not radius" decision. |
| 2026-05-12 | Worker flow rejection: bipartite matching across multiple I-districts REJECTED. One R-district routes 100% of workers to its single cheapest I-district. Splitting flows is logistics-game depth, not planner-game depth. Excess workers go unemployed (legible). |
| 2026-05-12 | Unemployment-via-no-path: separate from "no jobs exist." Distinct red "broken link" badge on the affected district. Fixed -0.20 happiness penalty. Player fix: build a road. |
| 2026-05-12 | Edge traffic (G5): per-edge load = sum of routes through edge. Congestion → BPR-style edge weight inflation → commute distance grows → happiness drops. One feedback loop, not three. Replaces current RoadTrafficSystem region tint. |
| 2026-05-12 | Power network NOT moved onto road graph in M10. Stays as its own BFS over its own conductor set. Open question deferred to after G5: should they unify? |
| 2026-05-12 | Migration interaction: Scale Session 2's "rewrite commute" and "rewrite traffic" tasks are SUPERSEDED by Graph G3 and G5 respectively. Do not double-rewrite. Effective order: Scale S1 → Graph G1 → G2 → G3 → Scale S2 (only pollution + landvalue rewrite) → Scale S3 → Graph G4 → G5. |
| 2026-05-12 | Per-citizen agent simulation REJECTED forever — Loopolis is a planner game. Aggregate per-district flows only. No vehicle routing, no transit lines, no traffic lights. One edge weight per road class. |
| 2026-05-12 | **Starting conditions overhaul:** Remove pre-placed power plant from starter city. New game state = empty map + unerasable border road at south-center + 2-3 pre-placed road tiles extending north as a "spine starter." Starting budget unchanged ($4,000). Rationale: today's seeded starter teaches by inspection but skips the most important first decision — where the power plant goes. Empty start makes plant placement a real spatial choice relative to the border anchor. |
| 2026-05-12 | **Border road introduced:** Single unerasable Road tile (not Highway) at fixed position `(width/2, height-1)`, flagged `IsBorderConnection=true`. Represents the city's connection to the outside world. Visible as a downward arrow / "REGIONAL HWY" label. Pre-M9 effect: 1.2× residential growth multiplier for R-tiles within road-graph distance N of the border (migration). Post-G3 effect: border becomes a virtual I-district anchor with infinite job capacity and a fixed long base commute (fallback employment). Post-M9 effect: goods export node for Timber/Grain. |
| 2026-05-12 | Border road scope discipline: ONE connection at v1 (not 2-4). Fixed south-center, not random or player-chosen — preserves scenario seeds. Regular Road, not Highway (Highway is a RoadClass deferred to Scale Session 3; the "no zones adjacent to highway" rule would conflict with the border being a development anchor). Multiple border roads deferred until "which border do I prioritize" is a real layout decision (post-G5). |
| 2026-05-12 | Regional-power-near-border REJECTED for v1 — adds parallel utility system with no clear failure legibility. Reconsider after empty-start playtest if onboarding is too steep. |
| 2026-05-12 | Onboarding mitigation: tutorial hint banner on first new game ("Place a power plant near the regional highway. Connect zones with roads. Watch your city grow.") that vanishes after first plant is placed. Plant-placement preview showing BFS power-flood radius before commit (natural F1 Road Pulse extension). |
| 2026-05-12 | **P1 Power-as-density-unlock (new M8 feature):** Power is no longer a prerequisite for *any* growth. Unpowered 1×1 residential ("Cottage") develops at 25 capacity / 0.7× tax. Powering it flips the same building to 50 capacity / 1.0× tax ("Powered Cottage"). All 2×2+ residential, all commercial, and industrial productivity (Output) still require power. Industrial unpowered = "Bare Lot" at 0.1× jobs / 0 Output. Removes the "do the prerequisite" wall on new game's empty start. SimCity 2000 precedent. |
| 2026-05-12 | P1 implementation: `IsPowered` is a STATE on the existing `res_house_1x1` building, NOT a new catalog entry. Same TypeId, two states. Same pattern as `tile.IsReadyToDevelop`. Doubling the catalog for one varying property explicitly rejected. |
| 2026-05-12 | P1 commercial rule: commerce remains hard-gated on power. Letting commercial develop unpowered removes the "I need a plant for shops" learning moment and lets the player skip the plant decision indefinitely. |
| 2026-05-12 | P1 industrial rule: unpowered industrial gets 0.1× jobs and 0 Output — exists for pre-zoning, doesn't function. Pollution is 0 when unpowered (productivity-side externality). Powering an unpowered I tile is the productivity decision; existence is decoupled. |
| 2026-05-12 | P1 tutorial hint rewrite: old "Place a power plant — your city needs power to grow" replaced with "Zone homes along the road to attract residents. Build a power plant to unlock larger buildings and businesses." Reframes power as acceleration, not ignition. |
| 2026-05-12 | P1 F3 extension: Building Birth Announcement now fires on Cottage → Powered Cottage transition. Same chime, "+$1/tick" floating label. Treats the power upgrade as a tier event because functionally it is one. |

