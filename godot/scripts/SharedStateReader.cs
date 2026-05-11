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
    private Toolbar _toolbar = null!;
    private GameOverPanel _gameOverPanel = null!;
    private bool _bankruptShown = false;
    private double _pollTimer = 0;
    private const double PollInterval = 0.05; // 20Hz polling

    /// <summary>Last grid received from the server. World.cs uses this for optimistic tile placement.</summary>
    public CityGrid? LastGrid { get; private set; }

    public override void _Ready()
    {
        _renderer      = GetNode<TilemapRenderer>("/root/World/TilemapRenderer");
        _hud           = GetNode<HudOverlay>("/root/World/HudOverlay");
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

        if (!File.Exists(_statePath)) return;

        try
        {
            var json = File.ReadAllText(_statePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var state = JsonSerializer.Deserialize<SharedState>(json, options);
            if (state == null || state.Tick == _lastTick) return;

            _lastTick = state.Tick;
            var grid = RebuildGrid(state);
            LastGrid = grid;
            _renderer.Refresh(grid);
            _hud.UpdateStats(state);
            _toolbar.SetPaused(state.Paused);

            // Bankrupt detection — show panel once and pause the server
            if (!_bankruptShown && state.GameState == "Bankrupt")
            {
                _bankruptShown = true;
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
        }
        return grid;
    }
}

// ── JSON DTOs ──────────────────────────────────────────────────────────────

public record SharedState(
    int Tick,
    bool Paused,
    int Population,
    double Balance,
    double TaxPerTick,
    double CommercialIncomePerTick,
    double MaintenancePerTick,
    double NetPerTick,
    double Happiness,
    string? MilestoneReached,
    string GameState,
    SharedTile[] Tiles
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
