using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Loopolis.Core.Grid;

namespace LoopolisGodot;

/// <summary>
/// Polls godot/shared/state-{sessionId}.json written by SimulationRunner --server.
/// When state changes, rebuilds CityGrid, refreshes the renderer, and updates the HUD.
/// Godot runs as a pure viewer — no simulation logic here.
///
/// Two modes:
///   Known session  — caller invokes SetSessionId() before _Ready; reads state-{id}.json directly.
///   Discovery mode — no session set; scans shared/ for a recently-written state-*.json file.
/// </summary>
public partial class SharedStateReader : Node
{
    private string _sharedDir = "";
    private string _stateFile = "";
    private int _lastTick = -1;
    private TilemapRenderer _renderer = null!;
    private HudOverlay _hud = null!;
    private HintOverlay _hintOverlay = null!;
    private Toolbar _toolbar = null!;
    private GameOverPanel _gameOverPanel = null!;
    private CityHealthPanel _cityHealth = null!;
    private TopBar _topBar = null!;
    private ScenarioResultPanel _scenarioResultPanel = null!;
    private bool _bankruptShown = false;
    private bool _abandonedShown = false;
    private bool _winShown = false;
    private bool _scenarioCompleteShown = false;
    private bool _scenarioFailedShown   = false;
    private double _pollTimer = 0;
    private const double PollInterval = 0.05; // 20Hz polling

    // Session tracking
    private string? _sessionId;
    private bool _sessionExpired;

    // Building birth tracking — detect when new buildings appear between ticks
    private readonly System.Collections.Generic.HashSet<string> _knownBuildingIds = new();

    /// <summary>
    /// Fired when a building appears for the first time.
    /// Parameters: typeId (e.g. "res_townhouse_2x2"), anchorX, anchorY.
    /// </summary>
    public event Action<string, int, int>? BuildingBorn;

    /// <summary>
    /// Fired once per tick with all new building typeIds created this tick (from state.LastNewBuildingTypeIds).
    /// Used by World.cs to batch building-birth toasts.
    /// Parameters: typeIds array, currentTick.
    /// </summary>
    public event Action<string[], int>? BuildingsBorn;

    /// <summary>
    /// Fired once per tick when petition activity occurred (new or resolved petitions).
    /// Used by World.cs to show petition toasts.
    /// Parameters: the full SharedState (needed to look up petition text).
    /// </summary>
    public event Action<SharedState>? PetitionsThisTick;

    /// <summary>
    /// Fired when one or more buildings degrade (crumble) this tick.
    /// Parameters: typeId (e.g. "res_townhouse_2x2"), anchorX, anchorY.
    /// The position is the anchor of the first tile of the building that was removed,
    /// approximated from the tile list since degraded buildings are already gone.
    /// </summary>
    public event Action<string, int, int>? BuildingDegraded;

    /// <summary>
    /// Fired once when the first grid is received from the server.
    /// Parameters: gridWidth, gridHeight.
    /// Used to fit the camera to the actual server map size.
    /// </summary>
    public event Action<int, int>? FirstGridReady;

    private bool _firstGridFired = false;

    /// <summary>Last grid received from the server. World.cs uses this for optimistic tile placement.</summary>
    public CityGrid? LastGrid { get; private set; }

    /// <summary>Last state received from the server. World.cs uses this for the growth tooltip.</summary>
    public SharedState? LastState { get; private set; }

    /// <summary>Session ID resolved via SetSessionId or discovery. Used to stamp outgoing commands.</summary>
    public string? SessionId => _sessionId;

    /// <summary>
    /// Pre-configure a known session ID (e.g. when Godot launched the server and captured its output).
    /// Must be called before _Ready() or during the first process frame.
    /// </summary>
    public void SetSessionId(string id)
    {
        _sessionId = id;
        if (_sharedDir.Length > 0)
            _stateFile = Path.Combine(_sharedDir, $"state-{id}.json");
    }

    public override void _Ready()
    {
        _renderer            = GetNode<TilemapRenderer>("/root/World/TilemapRenderer");
        _hud                 = GetNode<HudOverlay>("/root/World/HudOverlay");
        _hintOverlay         = GetNode<HintOverlay>("/root/World/HintOverlay");
        _toolbar             = GetNode<Toolbar>("/root/World/Toolbar");
        _gameOverPanel       = GetNode<GameOverPanel>("/root/World/GameOverPanel");
        _cityHealth          = GetNode<CityHealthPanel>("/root/World/CityHealthPanel");
        _topBar              = GetNode<TopBar>("/root/World/TopBar");
        _scenarioResultPanel = GetNode<ScenarioResultPanel>("/root/World/ScenarioResultPanel");

        // Resolve the shared directory path from the Godot project directory
        var projectDir = ProjectSettings.GlobalizePath("res://");
        _sharedDir = Path.Combine(projectDir, "shared");

        if (_sessionId != null)
        {
            // Known session mode — SetSessionId was called before _Ready
            _stateFile = Path.Combine(_sharedDir, $"state-{_sessionId}.json");
            GD.Print($"[viewer] Known session {_sessionId}. Watching: {_stateFile}");
        }
        else
        {
            // Discovery mode — will scan in _Process until a live file appears
            GD.Print($"[viewer] Discovery mode. Scanning: {_sharedDir}/state-*.json");
        }
    }

    public override void _Process(double delta)
    {
        _pollTimer += delta;
        if (_pollTimer < PollInterval) return;
        _pollTimer = 0;

        if (_sessionExpired) return;

        // Discovery mode: scan for a live state file if we don't have a session yet
        if (_sessionId == null)
        {
            var found = FindLiveStateFile(_sharedDir);
            if (found == null) return;

            var name = Path.GetFileNameWithoutExtension(found);
            _sessionId = name.Replace("state-", "");
            _stateFile = found;
            GD.Print($"[viewer] Discovered session {_sessionId}. Watching: {_stateFile}");
        }

        if (!File.Exists(_stateFile)) return;

        try
        {
            var json = File.ReadAllText(_stateFile);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var state = JsonSerializer.Deserialize<SharedState>(json, options);
            if (state == null || state.Tick == _lastTick) return;

            _lastTick = state.Tick;
            var (grid, heightMap, forestMap) = RebuildGrid(state);
            LastGrid  = grid;
            LastState = state;

            // Notify camera to fit to map on first tick
            if (!_firstGridFired)
            {
                _firstGridFired = true;
                FirstGridReady?.Invoke(grid.Width, grid.Height);
            }

            DetectBuildingBirths(state);
            DetectBuildingDegradation(state);

            // Fire batched building-birth toasts via LastNewBuildingTypeIds (engine-tracked, not scan-detected)
            if (state.LastNewBuildingTypeIds != null && state.LastNewBuildingTypeIds.Length > 0)
                BuildingsBorn?.Invoke(state.LastNewBuildingTypeIds, state.Tick);

            // Fire petition toast notifications when petition activity occurred this tick
            if ((state.NewPetitionThisTick != null && state.NewPetitionThisTick.Length > 0) ||
                (state.ResolvedPetitionThisTick != null && state.ResolvedPetitionThisTick.Length > 0))
                PetitionsThisTick?.Invoke(state);
            _renderer.RefreshWithHeight(grid, heightMap, forestMap);
            _renderer.SetBrownout(state.Power?.IsBrownout ?? false);
            _renderer.SetFireTile(state.EventTileX, state.EventTileY);
            _renderer.SetDegradedServices(state.DegradedServices);
            _hud.UpdateStats(state);
            _topBar.UpdateStats(state);
            _hintOverlay.UpdateHints(state);
            _cityHealth.UpdateWarnings(state);
            _toolbar.SetPaused(state.Paused);
            _toolbar.UpdateMilestoneLocks(state.Population);
            _toolbar.UpdateDisabledZones(state.DisabledZones);

            // Bankrupt detection — show panel once and pause the server
            if (!_bankruptShown && state.GameState == "Bankrupt")
            {
                _bankruptShown = true;
                _hintOverlay.SetGameOver();
                _gameOverPanel.ShowBankrupt(state);
                try
                {
                    var commandPath = Path.Combine(_sharedDir, $"command-{_sessionId}.json");
                    File.WriteAllText(commandPath, "{\"cmd\":\"pause\"}");
                }
                catch { /* runner may not be listening */ }
            }

            // Abandoned detection — show panel once and pause the server
            if (!_abandonedShown && state.GameState == "Abandoned")
            {
                _abandonedShown = true;
                _hintOverlay.SetGameOver();
                _gameOverPanel.ShowAbandoned(state.Tick, state.Population, state.Happiness);
                try
                {
                    var commandPath = Path.Combine(_sharedDir, $"command-{_sessionId}.json");
                    File.WriteAllText(commandPath, "{\"cmd\":\"pause\"}");
                }
                catch { /* runner may not be listening */ }
            }

            // Win detection — show panel once when Loopolis milestone reached
            if (!_winShown && state.GameState == "Loopolis")
            {
                _winShown = true;
                _gameOverPanel.ShowWin(state.Tick, state.Population, state.Balance);
                _hintOverlay.SetGameOver();
                try
                {
                    var commandPath = Path.Combine(_sharedDir, $"command-{_sessionId}.json");
                    File.WriteAllText(commandPath, "{\"cmd\":\"pause\"}");
                }
                catch { /* runner may not be listening */ }
            }

            // Scenario complete — show result panel once (edge detection)
            if (!_scenarioCompleteShown && state.ScenarioComplete && !string.IsNullOrEmpty(state.ActiveScenarioId))
            {
                _scenarioCompleteShown = true;
                try
                {
                    var commandPath = Path.Combine(_sharedDir, $"command-{_sessionId}.json");
                    File.WriteAllText(commandPath, "{\"cmd\":\"pause\"}");
                }
                catch { /* runner may not be listening */ }
                _scenarioResultPanel.ShowComplete(
                    scenarioName:     state.ActiveScenarioName ?? state.ActiveScenarioId ?? "",
                    medal:            state.MedalEarned ?? "Bronze",
                    population:       state.Population,
                    targetPop:        state.ScenarioTargetPopulation,
                    ticksUsed:        state.Tick,
                    activeScenarioId: state.ActiveScenarioId ?? "");
            }

            // Scenario failed — show result panel once (edge detection)
            if (!_scenarioFailedShown && state.ScenarioFailed && !string.IsNullOrEmpty(state.ActiveScenarioId))
            {
                _scenarioFailedShown = true;
                try
                {
                    var commandPath = Path.Combine(_sharedDir, $"command-{_sessionId}.json");
                    File.WriteAllText(commandPath, "{\"cmd\":\"pause\"}");
                }
                catch { /* runner may not be listening */ }
                _scenarioResultPanel.ShowFailed(
                    scenarioName:     state.ActiveScenarioName ?? state.ActiveScenarioId ?? "",
                    population:       state.Population,
                    targetPop:        state.ScenarioTargetPopulation,
                    activeScenarioId: state.ActiveScenarioId ?? "");
            }
        }
        catch (Exception)
        {
            // File being written atomically — retry next poll
        }
    }

    /// <summary>
    /// Compares the current state's building list against previously seen building IDs.
    /// Any ID that is new fires the BuildingBorn event.
    /// The first tick is used to populate the initial set without firing events.
    /// </summary>
    private void DetectBuildingBirths(SharedState state)
    {
        if (state.Buildings == null) return;

        // On the very first tick received, populate the baseline without emitting events.
        if (_knownBuildingIds.Count == 0 && _lastTick <= 0)
        {
            foreach (var b in state.Buildings)
                _knownBuildingIds.Add(b.Id);
            return;
        }

        foreach (var b in state.Buildings)
        {
            if (_knownBuildingIds.Add(b.Id))
            {
                // New building not seen before — fire birth event
                BuildingBorn?.Invoke(b.TypeId, b.X, b.Y);
            }
        }
    }

    /// <summary>
    /// Fires BuildingDegraded events for each typeId in state.LastDegradedBuildings.
    /// The anchor position is approximated as the center of the grid since the building
    /// is already gone — the floater will still appear roughly near the city center.
    /// A better position is derived by picking any residential tile near the grid center.
    /// </summary>
    private void DetectBuildingDegradation(SharedState state)
    {
        if (state.LastDegradedBuildings == null || state.LastDegradedBuildings.Length == 0) return;

        // Use the center of the grid as a fallback position for degradation floaters.
        // The crumble label position is less precise than birth labels (the building is gone)
        // but will still be visible somewhere over the city.
        var fallbackX = state.GridWidth  > 0 ? state.GridWidth  / 2 : 16;
        var fallbackY = state.GridHeight > 0 ? state.GridHeight / 2 : 16;

        foreach (var typeId in state.LastDegradedBuildings)
        {
            BuildingDegraded?.Invoke(typeId, fallbackX, fallbackY);
        }
    }

    /// <summary>
    /// Scans <paramref name="sharedDir"/> for state-*.json files written within the last 2 seconds.
    /// Returns the most recently written candidate, or null if none found.
    /// </summary>
    private static string? FindLiveStateFile(string sharedDir)
    {
        if (!Directory.Exists(sharedDir)) return null;
        var candidates = Directory.GetFiles(sharedDir, "state-*.json")
            .Where(f => (DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalSeconds < 2.0)
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .ToArray();
        return candidates.FirstOrDefault();
    }

    private static (CityGrid grid, int[,] heightMap, bool[,] forestMap) RebuildGrid(SharedState state)
    {
        // Infer grid dimensions from the tile data: find the max X and Y coordinates
        // present in the tile list (Runner omits empty flat tiles, so we derive size
        // from the largest coordinate seen, then round up to the next power-of-two or
        // use the GridWidth/GridHeight fields when present).
        int inferredW = state.GridWidth  > 0 ? state.GridWidth  : 32;
        int inferredH = state.GridHeight > 0 ? state.GridHeight : 32;
        // Guard: state.Tiles can be null when the server writes a state.json that omits the
        // field (e.g. first write on session start or mid-write truncation).
        if (state.Tiles == null) return (new CityGrid(inferredW, inferredH), new int[inferredW, inferredH], new bool[inferredW, inferredH]);
        foreach (var t in state.Tiles)
        {
            if (t.X + 1 > inferredW) inferredW = t.X + 1;
            if (t.Y + 1 > inferredH) inferredH = t.Y + 1;
        }

        var grid = new CityGrid(inferredW, inferredH);
        var heightMap = new int[inferredW, inferredH];
        var forestMap = new bool[inferredW, inferredH];

        // Default to height=1 (lowland) so pre-height state.json looks like flat terrain
        for (var x = 0; x < inferredW; x++)
        for (var y = 0; y < inferredH; y++)
            heightMap[x, y] = 1;

        foreach (var tile in state.Tiles)
        {
            // Set terrain first (before zone — water blocks SetZone)
            if (!string.IsNullOrEmpty(tile.Terrain) && tile.Terrain != "Flat")
            {
                if (Enum.TryParse<TerrainType>(tile.Terrain, out var terrainType))
                    grid.SetTerrain(tile.X, tile.Y, terrainType);
            }

            var zoneType = Enum.Parse<ZoneType>(tile.Zone);
            if (tile.IsBorderConnection)
                grid.PlaceBorderConnection(tile.X, tile.Y); // sets Road + IsBorderConnection
            else if (zoneType != ZoneType.Empty)
                grid.SetZone(tile.X, tile.Y, zoneType);
            if (tile.HasPower) grid.SetPower(tile.X, tile.Y, true);
            if (tile.HasRoadAccess) grid.SetRoadAccess(tile.X, tile.Y, true);
            if (tile.Population > 0) grid.SetPopulation(tile.X, tile.Y, tile.Population);
            if (tile.PollutionLevel > 0f) grid.SetPollution(tile.X, tile.Y, tile.PollutionLevel);
            if (tile.Happiness < 1f) grid.SetHappiness(tile.X, tile.Y, tile.Happiness);
            if (tile.BuildingId != null) grid.SetBuildingId(tile.X, tile.Y, tile.BuildingId);
            if (tile.TrafficLoad > 0) grid.SetTrafficLoad(tile.X, tile.Y, tile.TrafficLoad);
            if (tile.LandValue > 0f) grid.SetLandValue(tile.X, tile.Y, tile.LandValue);

            // Height: use tile.Height directly (defaults to 1 if missing in JSON).
            // Backward-compat: if Height is the default 1 but Terrain is Water, use 0.
            var h = tile.Height;
            if (h == 1 && tile.Terrain == "Water")
                h = 0;
            heightMap[tile.X, tile.Y] = h;
            grid.SetHeightLevel(tile.X, tile.Y, h);

            // Forest: use tile.HasForest (defaults to false if missing in JSON).
            // Backward-compat: if HasForest wasn't set but Terrain is Forest, treat as forest.
            var forest = tile.HasForest || tile.Terrain == "Forest";
            forestMap[tile.X, tile.Y] = forest;
            grid.SetForest(tile.X, tile.Y, forest);
        }

        // Restore building entities from the buildings list if present
        if (state.Buildings != null)
        {
            foreach (var b in state.Buildings)
            {
                if (!System.Enum.TryParse<ZoneType>(b.Zone, out var zone)) continue;
                var building = new Loopolis.Core.Buildings.Building(b.Id, b.TypeId, zone, b.X, b.Y, b.Width, b.Height);
                grid.Buildings[b.Id] = building;
            }
        }
        return (grid, heightMap, forestMap);
    }
}

// ── JSON DTOs ──────────────────────────────────────────────────────────────

public record BuildingInfo(
    string Id,
    string TypeId,
    string Zone,
    int X,
    int Y,
    int Width,
    int Height);

public record SharedState(
    int Tick,
    bool Paused,
    int Population,
    int MaxCapacity,
    double Balance,
    double TaxPerTick,
    double CommercialIncomePerTick,
    double MaintenancePerTick,
    double NetPerTick,
    double Happiness,
    string? MilestoneReached,
    string GameState,
    SharedTile[] Tiles,
    BuildingInfo[]? Buildings = null,
    int GridWidth  = 0,   // optional: explicit grid width from server (0 = infer from tile coords)
    int GridHeight = 0,   // optional: explicit grid height from server (0 = infer from tile coords)
    string? NextMilestoneName = null,
    int NextMilestoneTarget = 0,
    string? ActiveEventName = null,
    string? ActiveEventDescription = null,
    string? LatestEventBanner = null,
    double TaxModifier = 0.0,
    string? SessionId = null,
    int AvailableJobs = 0,
    int WorkingAge = 0,             // residential population count (same as Population for ratio calc)
    double EmploymentRatio = 0f,
    bool EmploymentWarning = false, // true when ratio < 0.40 and WorkingAge > 50
    int RequiredJobs = 0,
    double EventHappinessPenalty = 0.0,   // live penalty from EventSystem — data-driven, not string-matched
    // City health diagnostic fields (written by Runner, consumed by CityHealthPanel)
    HappinessBreakdownDto? HappinessBreakdown = null,
    EmploymentDto? Employment = null,
    CoverageSummaryDto? CoverageSummary = null,
    string? PauseReason = null,
    PowerStateDto? Power = null,            // power supply/demand from PowerCapacitySystem
    WorkerFlowDto? WorkerFlow = null,       // commute routing stats (G4)
    string[]? LastDegradedBuildings = null,  // typeIds of buildings demolished by degradation this tick
    string[]? LastNewBuildingTypeIds = null, // typeIds of buildings created by BuildingGrowthSystem this tick
    int EventTileX = -1,                    // X of tile currently on fire (-1 = none)
    int EventTileY = -1,                    // Y of tile currently on fire (-1 = none)
    bool HasPowerPlant = false,             // true when any CoalPlant/NuclearPlant/PowerPlant tile exists
    int ResZones = 0,                       // count of Residential zone tiles
    int ComZones = 0,                       // count of Commercial zone tiles
    int IndZones = 0,                       // count of Industrial zone tiles
    int ParkTiles = 0,                      // count of Park zone tiles
    // Scenario tracking fields (null/0 when in sandbox mode)
    string? ActiveScenarioId = null,        // e.g. "fresh_start"
    string? ActiveScenarioName = null,      // e.g. "Fresh Start"
    int ScenarioTargetPopulation = 0,       // 0 when no scenario active
    int ScenarioTickLimit = 0,              // 0 when no limit
    int ScenarioBronzeTick = 0,
    int ScenarioSilverTick = 0,
    int ScenarioGoldTick = 0,
    bool ScenarioComplete = false,
    string? MedalEarned = null,             // "Gold", "Silver", "Bronze", or null
    bool ScenarioFailed = false,            // true when tick limit exceeded without goal
    // Personal best from leaderboard (populated by World.cs / Runner from leaderboard.json)
    string? PersonalBestMedal = null,       // e.g. "Gold", "Silver", "Bronze", or null
    int PersonalBestTick = 0,              // tick count of personal best run (0 = no entry)
    // Policy system state (written by Runner, false/0 when no server or policy inactive)
    bool PolicyGreenCity = false,
    bool PolicyIndustrialHub = false,
    bool PolicyCommercialBoost = false,
    bool PolicyOpenCity = false,
    int PolicyTotalCostPerTick = 0,
    // Manual upgrade result from ManualUpgradeSystem (set by Runner after processing upgrade command)
    // Format: "ok:building_type_id:-cost" on success, "err:reason" on failure, null when no result pending
    string? LastUpgradeResult = null,
    // Event response system — set when an event fires and the player hasn't responded yet
    string? PendingEventType = null,
    int PendingEventCost = 0,
    // Zone constraints from active scenario (null = all zones allowed)
    List<string>? DisabledZones = null,
    // City statistics (written by Runner from CityStatisticsSystem)
    string PopulationTrend = "→",
    string HappinessTrend = "→",
    string BalanceTrend = "→",
    int PeakPopulation = 0,
    double PeakBalance = 0.0,
    float PopulationGrowthRate = 0f,
    List<StatsSnapshot>? StatsHistory = null,
    // Petition inbox fields (from PetitionSystem)
    PetitionEntry[]? ActivePetitions = null,
    string[]? NewPetitionThisTick = null,
    string[]? ResolvedPetitionThisTick = null,
    // Charter system (Town era)
    bool TownCharterPending = false,
    string? ActiveCharter = null,
    string? ActiveCharterDescription = null,
    // Service fatigue (City+ milestone)
    bool ServiceFatigueActive = false,
    ServiceFatigueEntry[]? DegradedServices = null
)
{
    /// <summary>
    /// Looks up the SharedTile at (x, y) from the Tiles array.
    /// Returns null if no tile exists at those coordinates, or if Tiles is null.
    /// </summary>
    public SharedTile? GetTile(int x, int y)
    {
        if (Tiles == null) return null;
        foreach (var t in Tiles)
            if (t.X == x && t.Y == y) return t;
        return null;
    }
}

/// <summary>Power supply vs. demand snapshot from PowerCapacitySystem.</summary>
public record PowerStateDto(
    int SupplyMW,
    int DemandMW,
    double CapacityRatio,
    bool IsBrownout
);

/// <summary>Happiness sub-components from HappinessSystem.GetBreakdown().</summary>
public record HappinessBreakdownDto(
    double ServiceCoverage,
    double TaxModifier,
    double UnemploymentPenalty,
    double EventPenalty,
    double NeglectDecay
);

/// <summary>Employment sub-fields for detailed unemployment diagnosis.</summary>
public record EmploymentDto(
    int Jobs,
    int Workers,
    double UnemploymentRate
);

/// <summary>Coverage summary for power, fire, police, school, hospital and pollution.</summary>
public record CoverageSummaryDto(
    int PoweredZonedTilesCount,
    int UnpoweredZonedTilesCount,
    double PoliceCoveragePercent,
    double FireCoveragePercent,
    double SchoolCoveragePercent,
    double HospitalCoveragePercent,
    double AvgPollution,
    double AvgHappiness,
    // Capacity fields (G4)
    int SchoolSeatsUsed = 0,
    int SchoolSeatsTotal = 0,
    int PoliceCapacityUsed = 0,
    int PoliceCapacityTotal = 0,
    int FireCapacityUsed = 0,
    int FireCapacityTotal = 0,
    int HospitalBedsUsed = 0,
    int HospitalBedsTotal = 0
);

/// <summary>Worker routing stats from the commute/road-flow system (G4).</summary>
public record WorkerFlowDto(
    int WorkersRouted,
    double AverageCommuteDistance,
    int UnroutedWorkers,
    int OverloadedEdges
);

public record SharedTile(
    int X,
    int Y,
    string Zone,
    bool HasPower,
    bool HasRoadAccess,
    int Population,         // per-zone population (0-50 for residential)
    float PollutionLevel,   // 0.0-1.0 from PollutionSystem
    float Happiness,        // 0.0-1.0 from HappinessSystem (per-tile)
    bool HasDemandBoost,    // commercial adjacency boost active for this tile
    string? BuildingId = null,
    string? BuildingType = null,
    int TrafficLoad = 0,    // RoadTrafficSystem load — only set for Road/Avenue tiles
    string? Terrain = null, // TerrainType as string ("Flat", "Hill", "Forest", "Water") — null means Flat
    int Height = 1,         // height level: ≤0 = water, 1 = lowland, 2 = midland, 3+ = highland/peak
    bool HasForest = false, // forest overlay (vegetation on top of elevation)
    bool IsBorderConnection = false, // border road tile — cannot be erased or overwritten
    float LandValue = 0f,   // 0.0-1.0 from LandValueSystem; 0 when not emitted by server
    // Growth-diagnosis fields (populated in RebuildGrid after tile list is processed)
    bool IsRoadAdjacent = false,  // true when any orthogonal neighbour is Road or Avenue (for 1×1 building formation)
    float HappinessValue = 0f     // per-tile happiness, copied from Happiness; explicit for growth diagnosis
);

/// <summary>
/// Per-tick statistics snapshot deserialized from the StatsHistory array in state.json.
/// Mirrors the Runner's StatsSnapshot record (camelCase JSON, case-insensitive deserialisation).
/// </summary>
public record StatsSnapshot(
    int Tick,
    int Population,
    double Balance,
    float AvgHappiness,
    float AvgPollution
);

/// <summary>
/// A single active petition from PetitionSystem, serialized into state.json by the Runner.
/// Mirrors the Runner's PetitionState record (camelCase JSON, case-insensitive deserialisation).
/// </summary>
public record PetitionEntry(
    string Id = "",
    string DistrictName = "",
    string Text = "",
    string Category = "",
    int IssuedTick = 0,
    int DeadlineTick = 0,
    int UrgencyTicks = 0
);

/// <summary>
/// A single service tile with its current fatigue capacity.
/// Mirrors the Runner's ServiceFatigueEntry record (camelCase JSON, case-insensitive deserialisation).
/// </summary>
public record ServiceFatigueEntry(
    int X = 0,
    int Y = 0,
    string Zone = "",
    double Capacity = 1.0,
    bool NeedsRenovation = false
);
