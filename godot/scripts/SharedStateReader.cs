using Godot;
using System;
using System.IO;
using System.Text.Json;
using Loopolis.Core.Grid;

namespace LoopolisGodot;

/// <summary>
/// Polls godot/shared/state.json written by SimulationRunner --server.
/// When state changes, rebuilds CityGrid, refreshes the renderer, and updates the HUD.
/// Godot runs as a pure viewer — no simulation logic here.
/// </summary>
public partial class SharedStateReader : Node
{
    private string _statePath = "";
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

    /// <summary>Session ID from the first state.json read. Used to stamp outgoing commands.</summary>
    public string? SessionId => _sessionId;

    public override void _Ready()
    {
        _renderer      = GetNode<TilemapRenderer>("/root/World/TilemapRenderer");
        _hud           = GetNode<HudOverlay>("/root/World/HudOverlay");
        _hintOverlay   = GetNode<HintOverlay>("/root/World/HintOverlay");
        _toolbar       = GetNode<Toolbar>("/root/World/Toolbar");
        _gameOverPanel = GetNode<GameOverPanel>("/root/World/GameOverPanel");

        // Resolve path relative to Godot project directory
        var projectDir = ProjectSettings.GlobalizePath("res://");
        _statePath = Path.Combine(projectDir, "shared", "state.json");

        GD.Print($"[viewer] Watching: {_statePath}");
    }

    public override void _Process(double delta)
    {
        _pollTimer += delta;
        if (_pollTimer < PollInterval) return;
        _pollTimer = 0;

        if (_sessionExpired) return;
        if (!File.Exists(_statePath)) return;

        try
        {
            var json = File.ReadAllText(_statePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var state = JsonSerializer.Deserialize<SharedState>(json, options);
            if (state == null || state.Tick == _lastTick) return;

            // Session tracking: latch on first read, detect replacement on subsequent reads
            if (_sessionId == null)
            {
                _sessionId = state.SessionId;
            }
            else if (state.SessionId != null && state.SessionId != _sessionId)
            {
                GD.Print("[viewer] stale state.json — session mismatch. Stopping viewer polling.");
                _sessionExpired = true;
                return;
            }

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
                // Ask the runner to pause so the city freezes at the moment of bankruptcy
                try
                {
                    var projectDir = ProjectSettings.GlobalizePath("res://");
                    var commandPath = System.IO.Path.Combine(projectDir, "shared", "command.json");
                    System.IO.File.WriteAllText(commandPath, "{\"cmd\":\"pause\"}");
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
                    var projectDir = ProjectSettings.GlobalizePath("res://");
                    var commandPath = System.IO.Path.Combine(projectDir, "shared", "command.json");
                    System.IO.File.WriteAllText(commandPath, "{\"cmd\":\"pause\"}");
                }
                catch { /* runner may not be listening */ }
            }
        }
        catch (Exception)
        {
            // File being written atomically — retry next poll
        }
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
    string? SessionId = null
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
