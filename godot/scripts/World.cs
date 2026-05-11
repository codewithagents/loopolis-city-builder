using Godot;
using System.IO;
using System.Text.Json;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace LoopolisGodot;

public partial class World : Node2D
{
    private CityGrid _grid = null!;
    private SimulationEngine _engine = null!;
    private double _tickTimer = 0;
    private const double TickInterval = 0.5; // seconds per sim tick

    private TilemapRenderer _renderer = null!;
    private HudOverlay _hud = null!;
    private Toolbar _toolbar = null!;
    private bool _viewerMode = false;
    private string _commandPath = "";

    // Standalone mode state for HUD updates
    private int _standaloneTick = 0;
    private bool _standalonePaused = false;
    private BudgetSystem? _budget;
    private PopulationSystem? _population;

    public override void _Ready()
    {
        _renderer = GetNode<TilemapRenderer>("TilemapRenderer");
        _hud      = GetNode<HudOverlay>("HudOverlay");
        _toolbar  = GetNode<Toolbar>("Toolbar");

        // Wire toolbar signals
        _toolbar.ZoneSelected    += OnZoneSelected;
        _toolbar.PauseToggled    += OnPauseToggled;
        _toolbar.NewGameRequested += OnNewGameRequested;

        // Update HUD with initial selection
        _hud.SetSelectedZone(_toolbar.SelectedZone);

        // Resolve project directory for IPC files
        var projectDir = ProjectSettings.GlobalizePath("res://");
        var statePath  = Path.Combine(projectDir, "shared", "state.json");
        _commandPath   = Path.Combine(projectDir, "shared", "command.json");

        if (File.Exists(statePath))
        {
            GD.Print("[world] Viewer mode — SimulationRunner is driving the simulation.");
            var reader = new SharedStateReader();
            AddChild(reader);
            _viewerMode = true;
            return;
        }

        // Standalone mode — run own simulation
        GD.Print("[world] Standalone mode — running own simulation.");
        SetupStandaloneSimulation();
    }

    private void SetupStandaloneSimulation()
    {
        _grid = new CityGrid(32, 32);
        // Blank grid — let the player build from scratch

        _budget     = new BudgetSystem(initialBalance: 5_000);
        _population = new PopulationSystem();
        var power   = new PowerNetwork();
        var roads   = new RoadNetwork();
        var demand  = new DemandSystem();

        _engine = new SimulationEngine(_grid, _budget, _population, power, roads, demand);

        _renderer.Refresh(_grid);
        PushStandaloneHudUpdate();
    }

    public override void _Process(double delta)
    {
        if (_viewerMode) return; // SharedStateReader handles updates

        if (_standalonePaused) return;

        _tickTimer += delta;
        if (_tickTimer >= TickInterval)
        {
            _tickTimer = 0;
            _engine.Tick();
            _standaloneTick++;
            _renderer.Refresh(_grid);
            PushStandaloneHudUpdate();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                HandlePlaceTile();
            }
        }
    }

    // ── Toolbar signal handlers ────────────────────────────────────────────

    private void OnZoneSelected(string zoneName)
    {
        _hud.SetSelectedZone(zoneName);
    }

    private void OnPauseToggled()
    {
        if (_viewerMode)
        {
            WriteCommand("{\"cmd\":\"pause\"}");
            // Toolbar button text is updated when state.json reflects new paused state
        }
        else
        {
            _standalonePaused = !_standalonePaused;
            _toolbar.SetPaused(_standalonePaused);
            PushStandaloneHudUpdate();
        }
    }

    private void OnNewGameRequested()
    {
        if (_viewerMode)
        {
            WriteCommand("{\"cmd\":\"new_game\"}");
        }
        else
        {
            SetupStandaloneSimulation();
            _standaloneTick    = 0;
            _standalonePaused  = false;
            _toolbar.SetPaused(false);
        }
    }

    // ── Click-to-place ─────────────────────────────────────────────────────

    private void HandlePlaceTile()
    {
        // Get mouse position in TilemapRenderer local coordinates
        var localPos = _renderer.GetLocalMousePosition();
        var tileX = (int)(localPos.X / TilemapRenderer.TileSize);
        var tileY = (int)(localPos.Y / TilemapRenderer.TileSize);

        if (tileX < 0 || tileX >= 32 || tileY < 0 || tileY >= 32) return;

        var selectedZone = _toolbar.SelectedZone;

        if (_viewerMode)
        {
            string cmd;
            if (selectedZone == "Erase")
                cmd = $"{{\"cmd\":\"erase\",\"x\":{tileX},\"y\":{tileY}}}";
            else
                cmd = $"{{\"cmd\":\"place_zone\",\"x\":{tileX},\"y\":{tileY},\"zone\":\"{selectedZone}\"}}";
            WriteCommand(cmd);
        }
        else
        {
            if (selectedZone == "Erase")
            {
                _grid.SetZone(tileX, tileY, ZoneType.Empty);
            }
            else
            {
                if (System.Enum.TryParse<ZoneType>(selectedZone, out var zoneType))
                    _grid.SetZone(tileX, tileY, zoneType);
            }
            _renderer.Refresh(_grid);
        }
    }

    private void WriteCommand(string json)
    {
        try { File.WriteAllText(_commandPath, json); }
        catch { /* ignore — Runner may not be listening */ }
    }

    // ── Standalone HUD sync ────────────────────────────────────────────────

    private void PushStandaloneHudUpdate()
    {
        if (_budget == null || _population == null) return;

        var snapshot  = _budget.Snapshot();
        var happiness = _engine.HappinessSystem.AverageHappiness(_grid);
        var milestone = _engine.MilestoneSystem.LatestMilestone?.Name;

        var state = new SharedState(
            Tick:                _standaloneTick,
            Paused:              _standalonePaused,
            Population:          _population.Population,
            Balance:             snapshot.Balance,
            TaxPerTick:          snapshot.TaxIncome,
            MaintenancePerTick:  snapshot.MaintenanceCost,
            NetPerTick:          snapshot.NetIncome,
            Happiness:           happiness,
            MilestoneReached:    milestone,
            Tiles:               System.Array.Empty<SharedTile>()
        );
        _hud.UpdateStats(state);
    }
}
