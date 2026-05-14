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
| M8 Service Tiers (PoliceHQ/FireHQ/Hospital) | ✅ Done | 28 (ServiceTierTests) |
| Player input (Godot) | ✅ Done | — |
| Stats HUD overlay (Godot) | ✅ Done | — |
| Toolbar + zone selector (Godot) | ✅ Done | — |
| RoadTrafficSystem | ✅ Done | 22 |
| Avenue (ZoneType) | ✅ Done | (in RoadTrafficSystemTests) |
| CoalPlant/NuclearPlant (ZoneType) | ✅ Done | (in PowerCapacitySystemTests) |
| PowerCapacitySystem | ✅ Done | 27 |
| LandValueSystem | ✅ Done | 21 |
| CommutePenalty (HappinessSystem) | ✅ Done | 14 |
| HeightMapGenerator (diamond-square) | ✅ Done | 17 |
| CityGrid HeightLevel/Forest/Plateau/Cliff | ✅ Done | 13 (added) |
| RoadGraph (G1) | ✅ Done | 34 |
| G2 Road-based service coverage | ✅ Done | 17 new (RoadGraph + HappinessSystem) |
| G3 Worker Flows + Real Traffic | ✅ Done | 18 new (WorkerFlowSystemTests) |
| G4 Service Capacity Model | ✅ Done | 26 new (ServiceCapacityTests) |
| P1 Power-as-Density Unlock | ✅ Done | 14 new (BuildingGrowthSystemTests + UnpoweredSystemsTests) |
| BuildingDegradationSystem | ✅ Done | 9 new (BuildingDegradationTests) |
| Tick-108 crash fixes | ✅ Done | 12 new (CityGridTests + BuildingGrowthSystemTests + SimulationEngineTests) |
| Park zone (ZoneType.Park) | ✅ Done | 13 new (ParkSystemTests — upgraded spec) |
| IsActiveBrownout (brownout Tier-1 fix) | ✅ Done | 4 new (PowerCapacitySystemTests) |

| BuildingCatalog terrain-conditional industrial | ✅ Done | 6 new (TerrainConditionalIndustrialTests) |
| HighTierBuildings (res_highrise_6x6, com_office_4x4, ind_complex_4x4) | ✅ Done | 16 new (HighTierBuildingTests) |
| PolicySystem (GreenCity/IndustrialHub/CommercialBoost/OpenCity) | ✅ Done | 29 new (PolicySystemTests) |
| Policy rebalance + CommercialBoost cap fix + OpenCity capacity mechanism | ✅ Done | 10 new (PolicyBalanceTests) |
| Happiness distress decay (LowHappinessDecayThreshold=0.30, 30-tick grace, 1.5%/tick) | ✅ Done | 4 new (PolicyBalanceTests) |
| ScenarioEngine + ScenarioLibrary + ScenarioDefinition | ✅ Done | 14 new (ScenarioEngineTests) |
| BuildingGrowthIntegration flaky fix | ✅ Done | FireStation added to test grid (prevents random fire demolition) |
| 3 Challenge Scenarios (island_chain, narrow_valley, river_delta) | ✅ Done | 28 new (ScenarioLibraryExtendedTests) |

| ManualUpgradeSystem | ✅ Done | 11 new (ManualUpgradeSystemTests) |
| CityStatisticsSystem | ✅ Done | 24 new (CityStatisticsTests) |

| M11-P1 PetitionSystem (DistrictNamer + PetitionSystem + wiring) | ✅ Done | 27 new (PetitionSystemTests) |
| M11-P2 CharterSystem (Town era: Merchant/Industrial/Civic) | ✅ Done | 33 new (CharterSystemTests) |
| M11-P3 ServiceFatigueSystem (service fatigue rule 1) | ✅ Done | 24 new (ServiceFatigueSystemTests) |
| M11-P4 City era charters (InnovationHub/GreenCanopy/TradeCorridors) | ✅ Done | 26 new (CityCharterSystemTests) |
| M11-P5 Metropolis era charters (NexusCity/GreenUtopia/EmpireOfSteel) | ✅ Done | 29 new (MetropolisCharterSystemTests) |

**Total: 923 tests · 0 failures · ~9s runtime**   *(+ 70 UI tests = 993 combined)*

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
| 2026-05-11 | default (server) | pauseOnEvent skip: ran 179 ticks into a 2000-tick skip, stopped on FireBreak. state.json shows pauseReason="FireBreak", ticksRun=179, paused=true. buildings/buildingSummary/happinessBreakdown/employment/nextMilestone all present. | Fix: ServerState record was referencing non-existent BuildingInfo type and missing enriched fields — updated to match WriteState call signature. |
| 2026-05-11 | all | All large scenarios go bankrupt. Root cause: residential zones at distance from roads can't develop (only 11-18 of 45-75 R tiles are ready). Maintenance costs (industrial, roads, powerlines) dwarf tax income. | Backlog: balance tuning or larger commercial footprint |
| 2026-05-11 | default | Traffic system: 2 road tiles, avg load = 4.0 (8 zone tiles / 2 roads), neither overloaded (threshold 8). System is dormant on light grids as designed — only activates at 9+ zone tiles per road. Avenue threshold of 16 gives meaningful upgrade path. | Working correctly |
| 2026-05-11 | default + services | M8 service tiers: PoliceHQ/FireHQ radius-10 confirmed, Hospital event penalty halving confirmed in tests. default survives 500t (+$2.24/tick), services bankrupt as pre-existing (layout issue). All 245 tests green. | Working correctly |
| 2026-05-11 | default | M8 Phase 2 power variants: CoalPlant/NuclearPlant added. Legacy PowerPlant tiles continue to work (alias). default scenario: 500 ticks, pop 102, balance +$4,989, +$2.24/tick. PowerCapacitySystem: 500 MW supply / 19 MW demand = ratio 26x, no brownout. Legacy PowerPlant now emits pollution at 0.4 strength (avg 0.068 on default grid). Brownout correctly triggers when 102 industrial tiles (510 MW) exceed 1 coal plant (500 MW). NuclearPlant correctly blocked below 500 pop. All 272 tests green. | Working correctly |
| 2026-05-14 | powered_start | BUG: Commercial zones never grew — C tiles stuck oscillating at 0-1 activity forever. Root cause: decline check in PopulationSystem (line 163) was a plain `if`, not `else if`. It ran unconditionally AFTER the growth branch, overwriting newPop with current*(1-0.02). For current=1, adj=0: growth set newPop=2, decline overwrote to 0. Oscillated 0→1→0 each tick pair. Fix: changed growth/decline to if/else-if so they are mutually exclusive. Commercial now grows to capacity in ~50 ticks at minimum rate. 2 regression tests added. 506 tests green. | Fixed — `if` → `else if` in commercial growth branch |
| 2026-05-14 | deep audit | BUG (HIGH): HappinessSystem._neglect stale state — erasing a tile via fire damage left its neglect entry (up to -0.20 happiness) in the per-coordinate dict. A new zone at the same position inherited the full penalty from tick 1. Fixed: ClearNeglect(x,y) called from EraseTile. | Fixed |
| 2026-05-14 | deep audit | BUG (HIGH): PopulationSystem._unhappyTicks stale state — same problem. A tile rebuilt where a distressed zone was demolished immediately entered distress mode, suppressing the minGrowth=1 floor and applying 1.5%/tick decay. Fixed: ClearUnhappyTicks(x,y) called from EraseTile. | Fixed |
| 2026-05-14 | deep audit | BUG (MEDIUM): SimulationEngine.NotifyCharterMilestonesIfNeeded — if city population jumps Active/Town→Metropolis in one tick (tests, scenarios), MetropolisCharterPending was never set because guard required _previousMilestoneState==City exactly. Broadened to also cover Active and Town. | Fixed |
| 2026-05-14 | deep audit | BUG (LOW doc): HappinessSystem docstring + GetNeglect XML comment said neglect cap is 0.3 but code caps at 0.20. Fixed both comments. | Fixed |

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

| 2026-05-11 | default | M8 Phase 3 land value: flat-terrain residential gets 0.8× tax multiplier → 12.24→9.79/tick at pop 102. LandValue avg=0.16, max=0.25 on default grid (no hills, minimal power at tick 1). landValueAvg/landValueMax in coverageSummary confirmed. res_villa_hillside_3x3 in catalog, HillTerrain+MinLandValue conditions working. | Working correctly |
| 2026-05-11 | town | Commute penalty + Chebyshev-3 mixed-use: town scenario avg happiness 0.537 at tick 0 (down from 0.6 baseline) — industrial at y=16-24 is ~10+ tiles from residential y=10-14, triggering −0.10 commute tier. happinessBreakdown.commutePenalty = −0.0462 in live state.json. default scenario: commutePenalty = 0.0 (no industrial). | Working correctly |
| 2026-05-12 | generated_map seed=99 | Height system: 63 water tiles (6.1%), 50 elevated, 42 plateaus on 32×32 grid. TerrainSummary in state.json (averageHeight=1.01). All existing scenarios (default/town/mixed/no_power/no_roads) unchanged — SetFlatTerrain() ensures immunity to procedural terrain. Cliff road constraint rejects height diff >1. | Working correctly |

| 2026-05-12 | default | G1 road graph: RoadGraph data structure added as pure foundation layer. 500-tick default run unchanged — pop 200, balance +$9,885, +$12.40/tick. Zero simulation impact (data structure only). roadGraphNodes field confirmed in state.json. | — |
| 2026-05-12 | city_path | G2 road-based service coverage: happiness=0.863 (Active, 500t). Services at (8,16), (14,16), (16,10) are road-adjacent → connected via road graph → all residential tiles covered. Runner wiring fixed: place_zone/erase/place_rect/erase_rect now via engine.PlaceTile/EraseTile. services scenario redesigned with road-adjacent services → happiness 0.704. | — |

| 2026-05-12 | town | G3 worker flows: at tick 31 with 347 pop, workersRouted=75, averageCommuteDistance=6.25, unroutedWorkers=0, overloadedEdges=0. Industrial tiles in town scenario (8×9 block at x=17-24, y=16-24) have road access via y=15 road. Workers route from R tiles through road graph edges to nearest I entry node. Edge traffic accumulates correctly — chokepoint detection ready. | — |

| 2026-05-12 | city_path | G4 service capacity: at tick 5 — schoolSeatsTotal=200, schoolSeatsUsed=96, schoolCoveragePercent=0.7368. Police 300/Fire 400 capacity present. Coverage correctly partial — only tiles within road-graph radius AND within capacity are covered. Hospital=0 (no hospital in city_path scenario). All 437 tests green. | — |
| 2026-05-12 | default | Border road + empty start: new default is empty — only border connection at (16,31) + 3 starter road tiles. 500-tick run: pop=0 (no zones yet), balance $3,000 ($4,000 − 500 × $2/tick for 4 roads). IsBorderConnection persists through save/load; EraseTile is a no-op on border tiles; 1.2× migration multiplier confirmed in tests (R-tile at distance 3 grows faster than without border graph). 455 tests green. | — |
| 2026-05-12 | no_power | P1 power-as-density: no_power scenario now shows pop=25 (cottage cap), TaxIncome=2.26/tick (0.7× unpowered modifier applied), balance +0.86/tick. Unpowered industrial: 0 pollution, 2 placeholder jobs. powered_start comparison: pop=236, TaxIncome=22.66/tick — ~10× more productive. 2×2+ buildings correctly blocked without power. BuildingDegradationSystem wired: multi-tile buildings degrade 2%/tick when requirements fail. 473 tests green. | — |
| 2026-05-12 | stress_test | Tick-108 crash fixes: BuildingGrowthSystem.TryGrow now null-guards catalog lookup (no NullReferenceException on unknown TypeId). CityGrid.SetZone(Empty) now calls EraseBuildingAt to demolish multi-tile buildings atomically — fixes orphaned Buildings entries when Godot standalone erases tiles via SetZone directly. stress_test: 200 growth ticks + 200 degradation ticks → no crash, city goes Bankrupt (expected — 96 unpowered buildings + high costs). 485 tests green. | — |

| 2026-05-12 | cottage_start | Balance fixes: GrowthRate 0.05→0.07 (+40%), IndustrialGrowthRate 0.025→0.05 (2×). cottage_start scenario (7R + road + 7C, no power): pop=105 at tick 500 (all cottages at 15 pop cap). AverageNeglect metric added to HappinessSystem and exposed in happinessBreakdown JSON. 4 new tests. 489 total. | — |
| 2026-05-12 | city_path | Park tiles: park at (11,13) near residential center. city_path happiness=0.901 (up from ~0.863 — park bonus confirmed). cottage_start runs 500 ticks cleanly (no brownout pause with supply=0). IsActiveBrownout guards early-game: false when no plant. 15 new tests. 504 total. | — |

| 2026-05-14 | mixed | Terrain-conditional industrial: existing flat scenarios fully unaffected (warehouses still form). New ind_mill_2x2 / ind_quarry_2x2 verified in unit tests. PollutionStrength per-building type confirmed: mill source tile 0.55, quarry 1.65, warehouse 1.0. No per-scenario observable change because all named scenarios use SetFlatTerrain(). New types only activate when players place industrial on forest/elevated terrain in live play. 512 tests green. | — |

| 2026-05-14 | default | Seeded scenarios: 5 scenarios implemented (fresh_start/river_valley/hill_town/city_challenge/metro_run). ScenarioEngine.CheckCompletion confirmed stateless — medal logic works correctly per unit tests. SimulationEngine.ActiveScenario is null by default (sandbox). SharedState scenario fields all serialize to camelCase via Runner. Godot layer reads via PropertyNameCaseInsensitive. Engine scenario tracking not exercised by CLI test scenarios (need ActiveScenario set externally). | No fixes needed — clean integration |
| 2026-05-14 | city_path | Park zone upgraded: Chebyshev-3 radius, +0.08/tile additive, capped +0.20, $3/tick maintenance. city_path happiness=0.906 (up from 0.863 pre-park). One park at (11,13) contributes +0.08 to all R-tiles within Chebyshev-3. BudgetSystem Park maintenance updated to $3. SharedState ParkTiles field added. BuildingGrowthIntegration flaky test fixed (fire station prevents random demolition). 528 tests green. | Fixed flaky test, upgraded park design |

| 2026-05-14 | default | 5 new scenarios (coastal_town/polluted_legacy/forest_reserve/boom_town/founders_challenge) added to ScenarioLibrary (10 total). LeaderboardSystem: Save/Load per-scenario personal best — Gold>Silver>Bronze, same medal lower tick wins. SharedState gains WorkingAge+EmploymentWarning+PersonalBestMedal+PersonalBestTick. HudOverlay employment ratio line (green/orange/red). World.cs fires low-employment toast at <40%, resets at >55%. 6 new tests. | All green, Godot build clean |

| 2026-05-14 | powered_start | PolicySystem: all 4 policies wired into simulation. No active policies = zero regression. 500 ticks: pop=291, balance=$11,158 (+$15.14/tick), happiness=0.896. GreenCity/IndustrialHub/CommercialBoost/OpenCity modifiers confirmed via unit tests. set_policy command + ServerState policy fields ready for Godot IPC. Fixed pre-existing 0xDELTA1 hex literal and stale ScenarioLibrary count assertion. | — |

| 2026-05-14 | powered_start | CityStatisticsSystem: 67-tick run — populationTrend=↑, balanceTrend=↑, happinessTrend=→ (stable 0.896). peakPopulation=291 correctly never decreases. statsHistory last 10 entries with all 9 fields populated. PopulationGrowthRate=0.0117 (correctly positive during growth phase). | Working correctly |

| 2026-05-14 | integration | CityAdvisor + PlaythroughTests: 4 test failures fixed. (1) CityAdvisor locale: all string.Format calls now use CultureInfo.InvariantCulture — German locale was producing "0,80" instead of "0.80". (2) TutorialScenario: industrial layout redesigned with south road spur (y=19) + 3 N-S spurs + 13 industrial tiles at y=20 — far enough from residential (distance 6) to avoid pollution distress, enough jobs (260) to sustain pop growth to 500+. Commute distances kept ≤ 10 for all tiles. (3) GreenCity: EmploymentSystem now skips the factory-job throttle when zero industrial tiles exist — R+C-only city gets full 1.0 employment multiplier. Test passes at pop>300. (4) HappinessDecay: PopulationSystem now suppresses minGrowth=1 guarantee when a tile is in active persistent distress (alreadyDistressed=true) — decay can overcome growth and produce net population decline as designed. New EmploymentSystemTests.IndustrialExistsButNotAccessible confirms throttle still fires when inaccessible industrial exists. | — |

| 2026-05-14 | powered_start | PetitionSystem: 500-tick powered_start run — pop=2400, balance=$96,619, happiness=0.83, GameState=Town. PetitionSystem runs every tick with no regression on simulation outcomes. Power petition suppressed (city is fully powered), happiness > 0.45 so no happiness petition. Services petition checked via road-graph distance. No petitions fire on well-run city as designed. | Working correctly |

| 2026-05-14 | powered_start | CharterSystem: 500-tick powered_start run — pop=2400, balance=$96,698, GameState=Town. TownCharterPending correctly fires when Town milestone is reached. Merchant charter: +30% commercial growth confirmed via 1-tick integration test (activity=25 vs control=24 at starting pop=23 with adjacentResidential=150). Industrial charter: +35% industrial growth + 10 bonus jobs confirmed. Civic charter: +3 service radius + 2× park happiness confirmed via unit tests. select_charter IPC command wired. state.json gains townCharterPending, activeCharter, activeCharterDescription fields. | Working correctly |

| 2026-05-14 | city_path + powered_start | ServiceFatigueSystem: city_path (Active, pop=357) and powered_start (Town, pop=2400) both ran 500 ticks with zero regression — fatigue dormant as designed below City milestone. Decay confirmed at 0.002/tick in unit tests. 300-tick integration test: capacity reaches 0.40 (clamped minimum), NeedsRenovation=true. | Working correctly |

| 2026-05-14 | powered_start | Seeded deterministic RNG: EventSystem now takes a seeded Random (default new Random()). SimulationEngine constructor accepts optional seed (null → Environment.TickCount). Seed is distributed: EventSystem uses Random(seed), BuildingDegradationSystem uses Random(seed+1). All 14 test files updated to pass seed:42 → every SimulationEngine call in tests is deterministic. CharterSystem SelectCharter(None) bug fixed: no-op guard added. 12 new tests (CityAdvisor, ServiceFatigue, Petitions, CharterSystem edge cases). TutorialScenario passes with peak population tracking. 819 tests green. | Working correctly |

| 2026-05-14 | powered_start + city_path | Scenario layout fix: both scenarios now reach City milestone (5,000 pop). Root causes: (1) interior R tiles in 2-row strips had no road adjacency (only directly road-adjacent tiles get buildings), (2) employment stall — with few industrial tiles, employmentMultiplier drops to 0.2 (MinGrowthMultiplier) which makes house rawGrowth round to 0 → freeze. Fix: 3 E-W roads + vertical spurs create a grid so ALL 4 R rows are road-adjacent. No industrial → EmploymentSystem returns 1.0 multiplier. Starting balance raised to $10k/$8k. powered_start: City at tick 15, pop=14,600, happiness=0.699. city_path: City at tick 16, pop=9,800, happiness=0.793. | Fixed — ScenarioSetup.cs only |

| 2026-05-14 | all | AdvisorBar: persistent HUD strip showing CityAdvisor hints. Bottom of screen, full width, color-coded by priority (Good=gray, Tip=cyan, Warning=yellow, Critical=red). Updates every standalone tick from engine.CurrentAdvice. Toggle with B key. Created as programmatic CanvasLayer — no .tscn changes needed. All 9 advisory rules already existed in CityAdvisor.cs; this just surfaces them to the player. | Done — AdvisorBar.cs (new), World.cs + WorldInput.cs + ShortcutsPanel.cs |

| 2026-05-14 | city_path | City era charters: three permanent specializations unlocked at City milestone (5,000 pop). InnovationHub: residential capacity +20%, tax rate +8%. GreenCanopy: pollution penalty ×0.5, park radius Chebyshev-3→5. TradeCorridors: commercial growth ×1.25, land value +8%. All effects wired into simulation: PopulationSystem, BudgetSystem, HappinessSystem, LandValueSystem. CharterChoicePanel extended to dual-era (Town/City). City charter pending state serialized through IPC. CityCharterPending triggers panel in standalone + viewer modes. 26 new tests (CityCharterSystemTests). 845+70 tests = 915 total, 0 failures. | Done — CharterSystem + CharterType + CharterLibrary + HappinessSystem + SimulationEngine + Godot wiring |

| 2026-05-14 | metropolis | Metropolis era charters: three end-game specializations unlocked at Metropolis milestone (25,000 pop). NexusCity: service radius +5, residential capacity +30%, tax +8% (efficiency city). GreenUtopia: pollution ×0.1, park happiness ×3.0, park radius +3 (near-zero pollution with GreenCanopy stack = ×0.05). EmpireOfSteel: industrial growth ×1.60, +25 jobs/tile, commercial ×1.30 (stacks with Merchant ×1.30 + TradeCorridors ×1.25 = ×2.11 total). CharterChoicePanel extended to triple-era with IsForMetropolisEra flag. Godot UI: electric-blue/vivid-green/forge-red color scheme. 29 new tests (MetropolisCharterSystemTests). Full suite: 874 · 0 failures. | Done — CharterType + CharterSystem + CharterLibrary + SimulationEngine + Runner IPC + Godot full stack |

| 2026-05-14 | refactor | Core refactoring pass: (1) EmpireOfSteel now grants +10% land value via new MetropolisLandValueBonus property — was a silent gameplay omission. (2) Dead `TickFired` field removed from PendingEventResponse (was always 0, never read). (3) CharterSystem gains 10 Effective* properties that stack all eras; SimulationEngine updated at 8 call-sites to use them (no behavior change except Fix 1). (4) NotifyCharterMilestonesIfNeeded() extracted from Tick() — reduces Tick() by 18 lines. (5) PickUncoveredServiceEvent() extracted in EventSystem — eliminates duplicated guard logic across small/large city branches. 44 new tests (CharterSystemEffectiveTests + MetropolisCharterSystemTests additions). | — |

| 2026-05-14 | refactor | Godot refactoring pass: (1) World.cs 6-copy charter panel lifecycle pattern → `UpdateCharterPanelForEra()` helper; 30 net lines removed. (2) CharterChoicePanel gains computed `IsForTownEra` — all three eras now explicit. (3) WorldEvents.cs `OnCharterSelected` routing uses `isTownEra` variable instead of implicit else-fallback. (4) GreenUtopia pollution cap raised ×0.1→×0.25 — prevents GreenCanopy+GreenUtopia = ×0.05 exploit (now ×0.125 combined). (5) metro_test runner scenario added — dense 7-road grid, $50k start, reaches Metropolis at tick 52, pop 54k, MetropolisCharterPending=true confirmed. | — |

| 2026-05-14 | bug audit | Deep bug hunt (agentic + scenario sweep). **5 bugs fixed:** (1) HIGH: `_neglect` dictionary leaked stale state across tile erasures — new zones at demolition sites started with −0.20 happiness penalty. (2) HIGH: `_unhappyTicks` same pattern — new zones at fire/erase sites immediately entered distress, suppressing minGrowth guarantee. (3) MEDIUM: `NotifyMetropolisMilestone` required exact `City` previous state — jump Active→Metropolis or Town→Metropolis silently skipped the charter prompt. (4) MEDIUM (Runner): `ReadyResidentialZones` used powered predicate — no_power scenario reported 0 ready zones with 25 residents, self-contradicting JSON. Fixed to `IsReadyToDevelopUnpowered`. (5) MEDIUM (Runner): `Survived` used `balance >= 0` — abandoned city with positive cash falsely reported Survived=true. Fixed to check `gameState not in {Bankrupt, Abandoned}`. Also: GreenUtopia stacking exploit fixed (×0.1→×0.25), EmpireOfSteel land value wired (+10%). 5 new regression tests. | 923 Core + 70 UI = 993 tests · 0 failures |

*Last updated: 2026-05-14 — Bug audit complete; 993 tests · 0 failures*
