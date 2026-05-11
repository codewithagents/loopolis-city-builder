using Godot;
using System;
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
    private bool _bankruptShown = false;
    private bool _abandonedShown = false;
    private double _pollTimer = 0;
    private const double PollInterval = 0.05; // 20Hz polling

    // Session tracking
    private string? _sessionId;
    private bool _sessionExpired;

    /// <summary>Last grid received from the server. World.cs uses this for optimistic tile placement.</summary>
    public CityGrid? LastGrid { get; private set; }

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
        _renderer      = GetNode<TilemapRenderer>("/root/World/TilemapRenderer");
        _hud           = GetNode<HudOverlay>("/root/World/HudOverlay");
        _hintOverlay   = GetNode<HintOverlay>("/root/World/HintOverlay");
        _toolbar       = GetNode<Toolbar>("/root/World/Toolbar");
        _gameOverPanel = GetNode<GameOverPanel>("/root/World/GameOverPanel");

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
            var grid = RebuildGrid(state);
            LastGrid = grid;
            _renderer.Refresh(grid);
            _hud.UpdateStats(state);
            _hintOverlay.UpdateHints(state);
            _toolbar.SetPaused(state.Paused);

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
        }
        catch (Exception)
        {
            // File being written atomically — retry next poll
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

    private static CityGrid RebuildGrid(SharedState state)
    {
        var grid = new CityGrid(32, 32);
        foreach (var tile in state.Tiles)
        {
            var zoneType = Enum.Parse<ZoneType>(tile.Zone);
            grid.SetZone(tile.X, tile.Y, zoneType);
            if (tile.HasPower) grid.SetPower(tile.X, tile.Y, true);
            if (tile.HasRoadAccess) grid.SetRoadAccess(tile.X, tile.Y, true);
            if (tile.Population > 0) grid.SetPopulation(tile.X, tile.Y, tile.Population);
            if (tile.PollutionLevel > 0f) grid.SetPollution(tile.X, tile.Y, tile.PollutionLevel);
            if (tile.Happiness < 1f) grid.SetHappiness(tile.X, tile.Y, tile.Happiness);
        }
        return grid;
    }
}

// ── JSON DTOs ──────────────────────────────────────────────────────────────

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
    string? NextMilestoneName = null,
    int NextMilestoneTarget = 0,
    string? ActiveEventName = null,
    string? ActiveEventDescription = null,
    string? LatestEventBanner = null,
    double TaxModifier = 0.0,
    string? SessionId = null,
    int AvailableJobs = 0,
    int RequiredJobs = 0,
    double EmploymentRatio = 1.0,
    double EventHappinessPenalty = 0.0   // live penalty from EventSystem — data-driven, not string-matched
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
    bool HasDemandBoost     // commercial adjacency boost active for this tile
);
