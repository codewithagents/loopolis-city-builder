using Godot;
using System;
using System.IO;
using System.Text.Json;
using Loopolis.Core.Grid;

namespace LoopolisGodot;

/// <summary>
/// Polls godot/shared/state.json written by SimulationRunner --server.
/// When state changes, rebuilds CityGrid and refreshes the renderer.
/// Godot runs as a pure viewer — no simulation logic here.
/// </summary>
public partial class SharedStateReader : Node
{
    private string _statePath = "";
    private int _lastTick = -1;
    private TilemapRenderer _renderer = null!;
    private double _pollTimer = 0;
    private const double PollInterval = 0.05; // 20Hz polling

    public override void _Ready()
    {
        _renderer = GetNode<TilemapRenderer>("/root/World/TilemapRenderer");

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
            _renderer.Refresh(grid);
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
    double MaintenancePerTick,
    double NetPerTick,
    double Happiness,
    string? MilestoneReached,
    SharedTile[] Tiles
);

public record SharedTile(
    int X,
    int Y,
    string Zone,
    bool HasPower,
    bool HasRoadAccess
);
