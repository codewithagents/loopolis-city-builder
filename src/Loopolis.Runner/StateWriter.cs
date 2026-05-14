using System.Text.Json;
using Loopolis.Core.Buildings;
using Loopolis.Core.Charters;
using Loopolis.Core.Grid;
using Loopolis.Core.Petitions;
using Loopolis.Core.Policies;
using Loopolis.Core.Simulation;

namespace Loopolis.Runner;

/// <summary>
/// Writes server state and overlay snapshots to disk via atomic rename.
/// </summary>
static class StateWriter
{
    public static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not find solution root (no .slnx/.sln found walking up from AppContext.BaseDirectory)");
    }

    public static void WriteStateWithError(
        string tmpPath,
        string statePath,
        SimulationEngine engine,
        CityGrid grid,
        bool paused,
        string sessionId,
        string errorMessage,
        List<string> recentEvents)
    {
        // Write state with an error field so the caller can surface what went wrong
        WriteState(tmpPath, statePath, engine, grid, paused, sessionId,
            pauseReason: null, ticksRun: null, recentEvents: recentEvents, error: errorMessage);
    }

    public static void WriteState(
        string tmpPath,
        string statePath,
        SimulationEngine engine,
        CityGrid grid,
        bool paused,
        string sessionId = "",
        string? pauseReason = null,
        int? ticksRun = null,
        List<string>? recentEvents = null,
        string? error = null,
        string? lastCommand = null,
        string? lastUpgradeResult = null)
    {
        // Build a lookup from buildingId → typeId for tile population
        var buildingTypeLookup = grid.Buildings.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.TypeId);

        var nonEmptyTiles = grid.AllTiles()
            .Where(t => t.Zone != ZoneType.Empty || t.HeightLevel != 1 || t.HasForest || t.IsBorderConnection)
            .Select(t => new TileState(
                t.X, t.Y, t.Zone.ToString(), t.HasPower, t.HasRoadAccess,
                t.Zone == ZoneType.Residential ? grid.GetPopulation(t.X, t.Y) : 0,
                Math.Round(t.PollutionLevel, 3),
                Math.Round(t.Happiness, 3),
                t.HasDemandBoost,
                t.BuildingId,
                t.BuildingId != null ? buildingTypeLookup.GetValueOrDefault(t.BuildingId) : null,
                t.TrafficLoad,
                t.Terrain != TerrainType.Flat ? t.Terrain.ToString() : null,
                t.HeightLevel,
                t.HasForest,
                t.IsBorderConnection))
            .ToList();

        // --- Enriched building info ---
        // Sum tile populations per building and look up capacity from catalog
        var buildingTilePopulations = new Dictionary<string, int>();
        foreach (var tile in grid.AllTiles())
        {
            if (tile.BuildingId == null || tile.Zone != ZoneType.Residential) continue;
            buildingTilePopulations.TryGetValue(tile.BuildingId, out var existing);
            buildingTilePopulations[tile.BuildingId] = existing + grid.GetPopulation(tile.X, tile.Y);
        }

        var enrichedBuildings = grid.Buildings.Values
            .Select(b =>
            {
                var typeDef = BuildingCatalog.Find(b.TypeId);
                var capacity = typeDef?.MaxPopulation ?? (b.TileCount * 50);
                buildingTilePopulations.TryGetValue(b.Id, out var pop);
                return new BuildingStateInfo(b.TypeId, b.AnchorX, b.AnchorY, b.Width, b.Height, pop, capacity);
            })
            .ToArray();

        // Building summary: typeId → count
        var buildingSummary = enrichedBuildings
            .GroupBy(b => b.TypeId)
            .ToDictionary(g => g.Key, g => g.Count());

        var residentialCount = grid.TilesOfType(ZoneType.Residential).Count();
        var maxCapacity = residentialCount * 50;
        var milestone = engine.MilestoneSystem.LatestMilestone;

        // Compute next milestone: find first threshold > current population
        var currentPop = engine.Population.Population;
        (string Name, string Emoji, int Target)[] milestoneThresholds =
        {
            ("Town",       "🥉", 500),
            ("City",       "🥈", 5_000),
            ("Metropolis", "🥇", 25_000),
            ("Loopolis",   "🏆", 100_000),
        };
        var nextMilestoneData = milestoneThresholds.FirstOrDefault(m => m.Target > currentPop);
        var nextMilestoneName   = nextMilestoneData != default ? $"{nextMilestoneData.Name} {nextMilestoneData.Emoji}" : null;
        var nextMilestoneTarget = nextMilestoneData != default ? nextMilestoneData.Target : 0;

        // --- Happiness breakdown (average across all ready residential tiles) ---
        var readyResidential = grid.TilesOfType(ZoneType.Residential)
            .Where(t => t.IsReadyToDevelop).ToList();

        double avgServiceCoverage    = 0;
        double avgNeglectDecay       = 0;
        if (readyResidential.Count > 0)
        {
            // Service coverage contribution: each covered service category adds +0.15, max 2 categories
            // PoliceHQ counts as PoliceStation, FireHQ counts as FireStation
            var services = grid.AllTiles()
                .Where(t => t.Zone is ZoneType.FireStation or ZoneType.PoliceStation or ZoneType.School
                                 or ZoneType.PoliceHQ or ZoneType.FireHQ or ZoneType.Hospital)
                .ToList();
            var serviceRadii = new Dictionary<ZoneType, float>
            {
                { ZoneType.FireStation,    8.0f },
                { ZoneType.PoliceStation,  8.0f },
                { ZoneType.School,        10.0f },
                { ZoneType.PoliceHQ,       8.0f },
                { ZoneType.FireHQ,         8.0f },
                { ZoneType.Hospital,      12.0f },
            };
            static ZoneType ServiceCat(ZoneType z) => z switch
            {
                ZoneType.PoliceHQ => ZoneType.PoliceStation,
                ZoneType.FireHQ   => ZoneType.FireStation,
                _                 => z,
            };

            foreach (var tile in readyResidential)
            {
                var coveredCategories = new HashSet<ZoneType>();
                foreach (var svc in services)
                {
                    var dist = engine.RoadGraph.GetDistanceViaRoads(grid, tile.X, tile.Y, svc.X, svc.Y);
                    if (serviceRadii.TryGetValue(svc.Zone, out var radius) && dist <= radius)
                        coveredCategories.Add(ServiceCat(svc.Zone));
                }
                avgServiceCoverage += Math.Min(coveredCategories.Count, 2) * 0.15;
                avgNeglectDecay    += engine.HappinessSystem.GetNeglect(tile.X, tile.Y);
            }
            avgServiceCoverage    /= readyResidential.Count;
            avgNeglectDecay       /= readyResidential.Count;
        }

        // Average commute penalty: sum of per-tile commute penalties / count of developed residential tiles
        var avgCommutePenalty = engine.HappinessSystem.AverageCommutePenalty(grid, currentPop, engine.RoadGraph);

        var happinessBreakdown = new HappinessBreakdown(
            ServiceCoverage:     Math.Round(avgServiceCoverage, 4),
            TaxModifier:         Math.Round(engine.Budget.TaxModifier, 4),
            UnemploymentPenalty: 0.0,   // employment affects growth rate, not happiness directly
            EventPenalty:        Math.Round(engine.EventSystem.HappinessPenalty, 4),
            NeglectDecay:        Math.Round(-avgNeglectDecay, 4),
            CommutePenalty:      Math.Round(avgCommutePenalty, 4),
            AverageNeglect:      Math.Round(engine.HappinessSystem.AverageNeglect(grid), 4)
        );

        // --- Coverage summary (power + services + pollution + happiness across all zoned tiles) ---
        var zonedTiles       = grid.AllTiles().Where(t => t.Zone is ZoneType.Residential or ZoneType.Commercial or ZoneType.Industrial).ToList();
        var poweredZoned     = zonedTiles.Count(t => t.HasPower);
        var unpoweredZoned   = zonedTiles.Count - poweredZoned;

        // Pre-compute service tiles and radii for coverage percentage
        // PoliceHQ (radius 8) counts as police coverage; FireHQ (radius 8) counts as fire coverage
        // Radii are in road-graph distance units (Road=1.0, Avenue=0.5 per edge)
        var covServiceRadii = new Dictionary<ZoneType, float>
        {
            { ZoneType.FireStation,    8.0f },
            { ZoneType.PoliceStation,  8.0f },
            { ZoneType.School,        10.0f },
            { ZoneType.PoliceHQ,       8.0f },
            { ZoneType.FireHQ,         8.0f },
            { ZoneType.Hospital,      12.0f },
        };
        var covServices = grid.AllTiles()
            .Where(t => covServiceRadii.ContainsKey(t.Zone))
            .ToList();

        int policeCovered = 0, fireCovered = 0, schoolCovered = 0, hospitalCovered = 0;
        double totalPollution = 0, totalHappiness = 0;
        foreach (var zt in zonedTiles)
        {
            if (covServices.Any(s => (s.Zone == ZoneType.PoliceStation || s.Zone == ZoneType.PoliceHQ)
                                     && engine.RoadGraph.GetDistanceViaRoads(grid, zt.X, zt.Y, s.X, s.Y) <= covServiceRadii[s.Zone]))
                policeCovered++;
            if (covServices.Any(s => (s.Zone == ZoneType.FireStation || s.Zone == ZoneType.FireHQ)
                                     && engine.RoadGraph.GetDistanceViaRoads(grid, zt.X, zt.Y, s.X, s.Y) <= covServiceRadii[s.Zone]))
                fireCovered++;
            if (covServices.Any(s => s.Zone == ZoneType.School
                                     && engine.RoadGraph.GetDistanceViaRoads(grid, zt.X, zt.Y, s.X, s.Y) <= covServiceRadii[ZoneType.School]))
                schoolCovered++;
            if (covServices.Any(s => s.Zone == ZoneType.Hospital
                                     && engine.RoadGraph.GetDistanceViaRoads(grid, zt.X, zt.Y, s.X, s.Y) <= covServiceRadii[ZoneType.Hospital]))
                hospitalCovered++;
            totalPollution  += zt.PollutionLevel;
            totalHappiness  += zt.Happiness;
        }
        var zonedCount = zonedTiles.Count;
        // G4: capacity-aware coverage from the last service coverage snapshot
        var capacityCoverage = engine.LastServiceCoverage;
        var coverageSummary = new CoverageSummary(
            PoweredZonedTilesCount:   poweredZoned,
            UnpoweredZonedTilesCount: unpoweredZoned,
            PoliceCoveragePercent:    zonedCount > 0 ? Math.Round((double)policeCovered    / zonedCount, 4) : 0.0,
            FireCoveragePercent:      zonedCount > 0 ? Math.Round((double)fireCovered      / zonedCount, 4) : 0.0,
            SchoolCoveragePercent:    zonedCount > 0 ? Math.Round((double)schoolCovered    / zonedCount, 4) : 0.0,
            HospitalCoveragePercent:  zonedCount > 0 ? Math.Round((double)hospitalCovered  / zonedCount, 4) : 0.0,
            AvgPollution:             zonedCount > 0 ? Math.Round(totalPollution  / zonedCount, 4) : 0.0,
            AvgHappiness:             zonedCount > 0 ? Math.Round(totalHappiness  / zonedCount, 4) : 0.0,
            OverloadedRoadCount:      engine.RoadTrafficSystem.OverloadedRoadCount,
            AvgTrafficLoad:           Math.Round(engine.RoadTrafficSystem.AvgTrafficLoad, 4),
            LandValueAvg:             Math.Round(engine.LandValueSystem.AverageLandValue(grid), 4),
            LandValueMax:             Math.Round(engine.LandValueSystem.MaxLandValue(grid), 4),
            SchoolSeatsUsed:          capacityCoverage?.SchoolSeatsUsed    ?? 0,
            SchoolSeatsTotal:         capacityCoverage?.SchoolSeatsTotal   ?? 0,
            PoliceCapacityUsed:       capacityCoverage?.PoliceCapacityUsed ?? 0,
            PoliceCapacityTotal:      capacityCoverage?.PoliceCapacityTotal ?? 0,
            FireCapacityUsed:         capacityCoverage?.FireCapacityUsed   ?? 0,
            FireCapacityTotal:        capacityCoverage?.FireCapacityTotal  ?? 0,
            HospitalBedsUsed:         capacityCoverage?.HospitalBedsUsed   ?? 0,
            HospitalBedsTotal:        capacityCoverage?.HospitalBedsTotal  ?? 0
        );

        // --- Employment ---
        var employmentState = new EmploymentState(
            Jobs:             engine.EmploymentSystem.AvailableJobs,
            Workers:          engine.EmploymentSystem.RequiredJobs,
            UnemploymentRate: Math.Round(1.0 - engine.EmploymentSystem.EmploymentRatio, 3)
        );

        // --- Next milestone object ---
        var nextMilestoneInfo = nextMilestoneData != default
            ? new NextMilestoneInfo(
                Name:                nextMilestoneData.Name,
                RequiredPopulation:  nextMilestoneData.Target,
                CurrentPopulation:   currentPop)
            : null;

        // --- Terrain summary ---
        var waterTileCount   = 0;
        var elevatedCount    = 0;
        var plateauCount     = 0;
        for (var ty = 0; ty < grid.Height; ty++)
        for (var tx = 0; tx < grid.Width; tx++)
        {
            var hl = grid.GetHeightLevel(tx, ty);
            if (hl <= 0) waterTileCount++;
            else if (hl >= 2)
            {
                elevatedCount++;
                if (grid.IsPlateau(tx, ty)) plateauCount++;
            }
        }
        var terrainSummary = new TerrainSummary(
            AverageHeight:    Math.Round(grid.AverageHeight, 3),
            WaterTileCount:   waterTileCount,
            ElevatedTileCount: elevatedCount,
            PlateauTileCount:  plateauCount);

        var activeEvent = engine.EventSystem.ActiveEvent;

        // --- Power capacity ---
        var pcs = engine.PowerCapacitySystem;
        var powerState = new PowerState(
            SupplyMW:      pcs.TotalSupplyMW,
            DemandMW:      pcs.TotalDemandMW,
            CapacityRatio: Math.Round(pcs.CapacityRatio, 4),
            IsBrownout:    pcs.IsBrownout);

        // --- Worker flow ---
        WorkerFlowState? workerFlowState = null;
        if (engine.LastWorkerFlow != null)
        {
            var wf = engine.LastWorkerFlow;
            workerFlowState = new WorkerFlowState(
                WorkersRouted:          wf.WorkersRouted,
                AverageCommuteDistance: Math.Round(wf.AverageCommuteDistance, 2),
                UnroutedWorkers:        wf.UnroutedWorkers,
                OverloadedEdges:        wf.OverloadedEdges);
        }

        // --- City statistics: last 10 snapshots + trend/peak values ---
        var statsHistory = engine.Statistics.History
            .TakeLast(10)
            .Select(s => new StatsSnapshot(
                s.Tick,
                s.Population,
                Math.Round(s.Balance, 2),
                Math.Round(s.AverageHappiness, 3),
                s.PoweredTiles,
                s.UnpoweredTiles,
                s.EmployedResidents,
                s.TotalJobs,
                Math.Round(s.AveragePollution, 3)))
            .ToArray();

        // --- Petition Inbox ---
        var petitionSystem  = engine.PetitionSystem;
        var currentTick     = engine.TickCount;
        var activePetitions = petitionSystem.ActivePetitions
            .Select(p => new PetitionState(
                p.Id, p.DistrictName, p.Text, p.Category,
                p.IssuedTick, p.DeadlineTick,
                UrgencyTicks: Math.Max(0, p.DeadlineTick - currentTick)))
            .ToArray();
        var newPetitionThisTick     = petitionSystem.NewThisTick.Select(p => p.DistrictName).ToArray();
        var resolvedPetitionThisTick = petitionSystem.RecentlyResolved.Select(p => p.DistrictName).ToArray();

        var state = new ServerState(
            Tick:                      engine.TickCount,
            Paused:                    paused,
            Population:                currentPop,
            MaxCapacity:               maxCapacity,
            Balance:                   Math.Round(engine.Budget.Balance, 2),
            TaxPerTick:                Math.Round(engine.Budget.LastTaxIncome, 2),
            CommercialIncomePerTick:   Math.Round(engine.Budget.CommercialIncomePerTick, 2),
            MaintenancePerTick:        Math.Round(engine.Budget.LastMaintenanceCost, 2),
            NetPerTick:                Math.Round(engine.Budget.NetIncomePerTick, 2),
            Happiness:                 Math.Round(engine.HappinessSystem.AverageHappiness(grid), 3),
            MilestoneReached:          milestone?.Name,
            Pollution:                 Math.Round(engine.PollutionSystem.AveragePollution(grid), 3),
            GameState:                 engine.MilestoneSystem.CurrentState.ToString(),
            Milestones:                engine.MilestoneSystem.Reached.Select(m => $"{m.Name} {m.Emoji} (tick {m.ReachedAtTick})").ToList(),
            Tiles:                     nonEmptyTiles,
            BuildingList:              enrichedBuildings,
            BuildingSummary:           buildingSummary,
            NextMilestoneName:         nextMilestoneName,
            NextMilestoneTarget:       nextMilestoneTarget,
            NextMilestone:             nextMilestoneInfo,
            ActiveEventName:           activeEvent?.Name,
            ActiveEventDescription:    activeEvent?.Description,
            LatestEventBanner:         engine.LatestEventBanner,
            TaxModifier:               engine.Budget.TaxModifier,
            SessionId:                 sessionId.Length > 0 ? sessionId : null,
            AvailableJobs:             engine.EmploymentSystem.AvailableJobs,
            WorkingAge:                currentPop,
            EmploymentRatio:           Math.Round(engine.EmploymentSystem.EmploymentRatio, 3),
            EmploymentWarning:         engine.EmploymentSystem.EmploymentRatio < 0.40 && currentPop > 50,
            RequiredJobs:              engine.EmploymentSystem.RequiredJobs,
            EventHappinessPenalty:     engine.EventSystem.HappinessPenalty,
            HappinessBreakdown:        happinessBreakdown,
            Employment:                employmentState,
            CoverageSummary:           coverageSummary,
            PauseReason:               pauseReason,
            TicksRun:                  ticksRun,
            RecentEvents:              recentEvents ?? new List<string>(),
            Error:                     error,
            Power:                     powerState,
            LastCommand:               lastCommand,
            Terrain:                   terrainSummary,
            RoadGraphNodes:            engine.RoadGraph.NodeCount,
            WorkerFlow:                workerFlowState,
            EventTileX:                engine.EventSystem.FireTileX,
            EventTileY:                engine.EventSystem.FireTileY,
            LastDegradedBuildings:     engine.LastDegradedBuildings?.ToArray(),
            LastNewBuildingTypeIds:    engine.LastNewBuildingTypeIds?.ToArray(),
            // Scenario tracking
            ActiveScenarioId:          engine.ActiveScenario?.Id,
            ActiveScenarioName:        engine.ActiveScenario?.Name,
            ScenarioTargetPopulation:  engine.ActiveScenario?.Goal.TargetPopulation ?? 0,
            ScenarioTickLimit:         engine.ActiveScenario?.TickLimit ?? 0,
            ScenarioBronzeTick:        engine.ActiveScenario?.Medals.Bronze ?? 0,
            ScenarioSilverTick:        engine.ActiveScenario?.Medals.Silver ?? 0,
            ScenarioGoldTick:          engine.ActiveScenario?.Medals.Gold   ?? 0,
            ScenarioComplete:          engine.ScenarioComplete,
            MedalEarned:               engine.MedalEarned,
            ScenarioFailed:            engine.ScenarioFailed,
            ParkTiles:                 grid.TilesOfType(ZoneType.Park).Count(),
            // Policy system state
            PolicyGreenCity:           engine.PolicySystem.IsActive(PolicyType.GreenCity),
            PolicyIndustrialHub:       engine.PolicySystem.IsActive(PolicyType.IndustrialHub),
            PolicyCommercialBoost:     engine.PolicySystem.IsActive(PolicyType.CommercialBoost),
            PolicyOpenCity:            engine.PolicySystem.IsActive(PolicyType.OpenCity),
            PolicyTotalCostPerTick:    engine.PolicySystem.GetCostPerTick(),
            LastUpgradeResult:         lastUpgradeResult,
            PendingEventType:          engine.PendingEventType,
            PendingEventCost:          engine.PendingEventCost,
            DisabledZones:             engine.ActiveScenario?.DisabledZones?.Select(z => z.ToString()).ToList(),
            // City statistics
            StatsHistory:              statsHistory,
            PeakPopulation:            engine.Statistics.PeakPopulation,
            PeakBalance:               Math.Round(engine.Statistics.PeakBalance, 2),
            PopulationTrend:           engine.Statistics.PopulationTrend(),
            HappinessTrend:            engine.Statistics.HappinessTrend(),
            BalanceTrend:              engine.Statistics.BalanceTrend(),
            PopulationGrowthRate:      engine.Statistics.PopulationGrowthRate,
            // Petition Inbox
            ActivePetitions:           activePetitions.Length > 0 ? activePetitions : null,
            NewPetitionThisTick:       newPetitionThisTick.Length > 0 ? newPetitionThisTick : null,
            ResolvedPetitionThisTick:  resolvedPetitionThisTick.Length > 0 ? resolvedPetitionThisTick : null,
            // Charter system
            TownCharterPending:        engine.Charters.TownCharterPending,
            ActiveCharter:             engine.Charters.ActiveCharter != CharterType.None
                                           ? engine.Charters.ActiveCharter.ToString()
                                           : null,
            ActiveCharterDescription:  engine.Charters.ActiveCharter != CharterType.None
                                           ? CharterLibrary.Find(engine.Charters.ActiveCharter)?.Effect
                                           : null
        );

        var options = new JsonSerializerOptions
        {
            WriteIndented          = true,
            PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        };
        var json = JsonSerializer.Serialize(state, options);
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, statePath, overwrite: true);
    }

    public static void WriteOverlay(
        string sharedDir,
        string sessionId,
        SimulationEngine engine,
        CityGrid grid,
        string overlayType)
    {
        var width  = grid.Width;
        var height = grid.Height;
        var tick   = engine.TickCount;

        // Pre-compute service tile list + radii (road-graph distance units)
        var serviceRadii = new Dictionary<ZoneType, float>
        {
            { ZoneType.FireStation,    8.0f },
            { ZoneType.PoliceStation,  8.0f },
            { ZoneType.School,        10.0f },
            { ZoneType.PoliceHQ,       8.0f },
            { ZoneType.FireHQ,         8.0f },
            { ZoneType.Hospital,      12.0f },
        };
        var services = grid.AllTiles()
            .Where(t => serviceRadii.ContainsKey(t.Zone))
            .ToList();

        var overlayTiles = new List<OverlayTile>(width * height);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var tile = grid.GetTile(x, y);
            double value;

            switch (overlayType)
            {
                case "power":
                    value = tile.HasPower ? 1.0 : 0.0;
                    break;

                case "police":
                {
                    // PoliceStation or PoliceHQ — road-graph distance coverage
                    var covered = services.Any(s =>
                        (s.Zone == ZoneType.PoliceStation || s.Zone == ZoneType.PoliceHQ) &&
                        engine.RoadGraph.GetDistanceViaRoads(grid, x, y, s.X, s.Y) <= serviceRadii[s.Zone]);
                    value = covered ? 1.0 : 0.0;
                    break;
                }

                case "fire":
                {
                    // FireStation or FireHQ — road-graph distance coverage
                    var covered = services.Any(s =>
                        (s.Zone == ZoneType.FireStation || s.Zone == ZoneType.FireHQ) &&
                        engine.RoadGraph.GetDistanceViaRoads(grid, x, y, s.X, s.Y) <= serviceRadii[s.Zone]);
                    value = covered ? 1.0 : 0.0;
                    break;
                }

                case "school":
                {
                    var covered = services.Any(s =>
                        s.Zone == ZoneType.School &&
                        engine.RoadGraph.GetDistanceViaRoads(grid, x, y, s.X, s.Y) <= serviceRadii[ZoneType.School]);
                    value = covered ? 1.0 : 0.0;
                    break;
                }

                case "hospital":
                {
                    var covered = services.Any(s =>
                        s.Zone == ZoneType.Hospital &&
                        engine.RoadGraph.GetDistanceViaRoads(grid, x, y, s.X, s.Y) <= serviceRadii[ZoneType.Hospital]);
                    value = covered ? 1.0 : 0.0;
                    break;
                }

                case "pollution":
                    value = Math.Round(tile.PollutionLevel, 4);
                    break;

                case "happiness":
                    value = Math.Round(tile.Happiness, 4);
                    break;

                case "traffic":
                {
                    // Only road/avenue tiles have meaningful traffic load.
                    // Value = TrafficLoad / overloadThreshold clamped to 0.0–1.0.
                    // 1.0 means at capacity; tiles > threshold are clamped to 1.0 (overloaded).
                    if (tile.Zone == ZoneType.Road || tile.Zone == ZoneType.Avenue)
                    {
                        var threshold = tile.Zone == ZoneType.Avenue ? 16.0 : 8.0;
                        value = Math.Clamp(tile.TrafficLoad / threshold, 0.0, 1.0);
                        value = Math.Round(value, 4);
                    }
                    else
                    {
                        value = 0.0;
                    }
                    break;
                }

                case "landvalue":
                    // Non-water tiles only; sparse encoding (skip value=0).
                    value = grid.GetHeightLevel(x, y) > 0
                        ? Math.Round(tile.LandValue, 4)
                        : 0.0;
                    break;

                default:
                    value = 0.0;
                    break;
            }

            // Sparse encoding: only emit tiles where value > 0
            if (value > 0.0)
                overlayTiles.Add(new OverlayTile(x, y, value));
        }

        // overlayTiles is sparse — zero-value tiles are omitted for compactness.
        // Readers should treat absent tiles as value=0.
        var overlayState = new OverlayState(overlayType, tick, width, height, overlayTiles);
        var options = new JsonSerializerOptions
        {
            WriteIndented        = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var overlayJson = JsonSerializer.Serialize(overlayState, options);

        var overlayFile = Path.Combine(sharedDir, $"overlay-{sessionId}.json");
        var overlayTmp  = Path.Combine(sharedDir, $"overlay-{sessionId}.tmp.json");
        File.WriteAllText(overlayTmp, overlayJson);
        File.Move(overlayTmp, overlayFile, overwrite: true);

        Console.WriteLine($"[query_overlay] overlay={overlayType}, tick={tick}, tiles={overlayTiles.Count} (sparse, non-zero only), written to {overlayFile}");
    }
}
