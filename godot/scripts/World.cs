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
    private TileTooltip _tooltip = null!;
    private GameOverPanel _gameOverPanel = null!;
    private bool _viewerMode = false;
    private string _commandPath = "";
    private SharedStateReader? _reader; // viewer mode only, for optimistic rendering

    // Standalone mode state for HUD updates
    private int _standaloneTick = 0;
    private bool _standalonePaused = false;
    private bool _gameOver = false;
    private BudgetSystem? _budget;
    private PopulationSystem? _population;

    public override void _Ready()
    {
        _renderer     = GetNode<TilemapRenderer>("TilemapRenderer");
        _hud          = GetNode<HudOverlay>("HudOverlay");
        _toolbar      = GetNode<Toolbar>("Toolbar");
        _tooltip      = GetNode<TileTooltip>("TileTooltip");
        _gameOverPanel = GetNode<GameOverPanel>("GameOverPanel");

        // Wire toolbar signals
        _toolbar.ZoneSelected     += OnZoneSelected;
        _toolbar.PauseToggled     += OnPauseToggled;
        _toolbar.NewGameRequested += OnNewGameRequested;

        // Wire game-over panel
        _gameOverPanel.NewGameRequested += OnNewGameRequested;

        // Update HUD with initial selection
        _hud.SetSelectedZone(_toolbar.SelectedZone);

        // Resolve project directory for IPC files
        var projectDir = ProjectSettings.GlobalizePath("res://");
        var statePath  = Path.Combine(projectDir, "shared", "state.json");
        _commandPath   = Path.Combine(projectDir, "shared", "command.json");

        if (File.Exists(statePath))
        {
            GD.Print("[world] Viewer mode — SimulationRunner is driving the simulation.");
            _reader = new SharedStateReader();
            AddChild(_reader);
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
        SeedStarterCity(_grid);

        _budget     = new BudgetSystem(initialBalance: 10_000);
        _population = new PopulationSystem();
        var power   = new PowerNetwork();
        var roads   = new RoadNetwork();
        var demand  = new DemandSystem();

        _engine   = new SimulationEngine(_grid, _budget, _population, power, roads, demand);
        _gameOver = false;

        _renderer.Refresh(_grid);
        PushStandaloneHudUpdate();
    }

    /// <summary>
    /// Pre-seed a minimal working city so new players immediately see the
    /// power → road → residential chain in action.
    ///
    /// Layout centred around (14,14)–(16,15):
    ///   PowerPlant  at (15,13)
    ///   Road        at (15,14) and (15,15)
    ///   Residential at (14,14), (16,14), (14,15), (16,15)
    /// </summary>
    private static void SeedStarterCity(CityGrid grid)
    {
        grid.SetZone(15, 13, ZoneType.PowerPlant);
        grid.SetZone(15, 14, ZoneType.Road);
        grid.SetZone(15, 15, ZoneType.Road);
        grid.SetZone(14, 14, ZoneType.Residential);
        grid.SetZone(16, 14, ZoneType.Residential);
        grid.SetZone(14, 15, ZoneType.Residential);
        grid.SetZone(16, 15, ZoneType.Residential);
    }

    public override void _Process(double delta)
    {
        // Tooltip always updates (both modes)
        UpdateTooltip();

        if (_viewerMode) return; // SharedStateReader handles everything else

        if (_standalonePaused) return;
        if (_gameOver) return;

        _tickTimer += delta;
        if (_tickTimer >= TickInterval)
        {
            _tickTimer = 0;
            _engine.Tick();
            _standaloneTick++;
            _renderer.Refresh(_grid);
            PushStandaloneHudUpdate();

            // Bankrupt check
            if (_engine.MilestoneSystem.CurrentState == Loopolis.Core.Simulation.GameState.Bankrupt)
            {
                _gameOver = true;
                _standalonePaused = true;
                _toolbar.SetPaused(true);
                _gameOverPanel.ShowBankrupt(_standaloneTick, _budget!.Balance, _population!.Population);
            }
        }
    }

    private void UpdateTooltip()
    {
        var localPos = _renderer.GetLocalMousePosition();
        var tileX = (int)(localPos.X / TilemapRenderer.TileSize);
        var tileY = (int)(localPos.Y / TilemapRenderer.TileSize);

        var grid = _viewerMode ? _reader?.LastGrid : _grid;
        if (grid == null || tileX < 0 || tileX >= 32 || tileY < 0 || tileY >= 32)
        {
            _tooltip.Hide();
            return;
        }

        var tile = grid.GetTile(tileX, tileY);
        if (tile.Zone == Loopolis.Core.Grid.ZoneType.Empty)
        {
            _tooltip.Hide();
            return;
        }

        _tooltip.ShowFor(tile, GetViewport().GetMousePosition());
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

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            var zone = key.Keycode switch
            {
                Key.Key1 => "Residential",
                Key.Key2 => "Commercial",
                Key.Key3 => "Industrial",
                Key.Key4 => "Road",
                Key.Key5 => "PowerLine",
                Key.Key6 => "PowerPlant",
                Key.Key7 => "FireStation",
                Key.Key8 => "PoliceStation",
                Key.Key9 => "School",
                Key.Key0 => "Erase",
                _ => null
            };
            if (zone != null) _toolbar.SelectZone(zone);

            if (key.Keycode == Key.Space)
                OnPauseToggled();
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
            _standaloneTick   = 0;
            _standalonePaused = false;
            _gameOver         = false;
            _toolbar.SetPaused(false);
            _gameOverPanel.Hide();
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
            // Optimistic rendering: update visuals immediately, server confirms on next tick
            if (_reader?.LastGrid is { } optimisticGrid)
            {
                if (selectedZone == "Erase")
                    optimisticGrid.SetZone(tileX, tileY, ZoneType.Empty);
                else if (System.Enum.TryParse<ZoneType>(selectedZone, out var optimisticZone))
                    optimisticGrid.SetZone(tileX, tileY, optimisticZone);
                _renderer.Refresh(optimisticGrid);
            }

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

        var gameStateName = _engine.MilestoneSystem.CurrentState.ToString();
        var state = new SharedState(
            Tick:                      _standaloneTick,
            Paused:                    _standalonePaused,
            Population:                _population.Population,
            Balance:                   snapshot.Balance,
            TaxPerTick:                snapshot.TaxIncome,
            CommercialIncomePerTick:   _budget.CommercialIncomePerTick,
            MaintenancePerTick:        snapshot.MaintenanceCost,
            NetPerTick:                snapshot.NetIncome,
            Happiness:                 happiness,
            MilestoneReached:          milestone,
            GameState:                 gameStateName,
            Tiles:                     System.Array.Empty<SharedTile>()
        );
        _hud.UpdateStats(state);
    }
}
