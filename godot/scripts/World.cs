using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace LoopolisGodot;

public partial class World : Node2D
{
    private CityGrid _grid = null!;
    private SimulationEngine _engine = null!;
    private double _tickTimer = 0;
    private double _tickInterval = 0.5; // seconds per sim tick (default: 2 ticks/sec)
    private float _ticksPerSecond = 2.0f;

    private TilemapRenderer _renderer = null!;
    private HudOverlay _hud = null!;
    private HintOverlay _hintOverlay = null!;
    private Toolbar _toolbar = null!;
    private TileTooltip _tooltip = null!;
    private GameOverPanel _gameOverPanel = null!;
    private EventLog _eventLog = null!;
    private bool _viewerMode = false;
    private string _sharedDir = "";
    private SharedStateReader? _reader; // viewer mode only, for optimistic rendering

    // Standalone mode state for HUD updates
    private int _standaloneTick = 0;
    private bool _standalonePaused = false;
    private bool _gameOver = false;
    private BudgetSystem? _budget;
    private PopulationSystem? _population;
    private string _taxLevel = "normal";

    // Event log tracking fields
    private int _lastLoggedPop = 0;
    private bool _wasNegative = false;
    private bool _warnedLowBalance = false;
    private bool _loggedCapacity = false;
    private string? _lastLoggedMilestone;
    private string? _lastLoggedBanner;

    // Server process tracking (static — survives scene changes)
    private static long _serverPid = -1;

    public static void SetServerPid(long pid) { _serverPid = pid; }

    public static void KillServerIfRunning()
    {
        if (_serverPid >= 0)
        {
            try { Godot.OS.Kill((int)_serverPid); }
            catch { /* process may have already exited */ }
            _serverPid = -1;
        }
    }

    // Coverage radius overlay tracking
    private int _coverageRadius = 0;
    private Color _coverageColor = Colors.Transparent;
    private int _lastCoverageHoverX = -1;
    private int _lastCoverageHoverY = -1;

    // Drag-to-place (paint mode) state
    private bool _isPlacing = false;
    private Vector2I _lastPlacedTile = new(-1, -1);

    public override void _Ready()
    {
        _renderer      = GetNode<TilemapRenderer>("TilemapRenderer");
        _hud           = GetNode<HudOverlay>("HudOverlay");
        _hintOverlay   = GetNode<HintOverlay>("HintOverlay");
        _toolbar       = GetNode<Toolbar>("Toolbar");
        _tooltip       = GetNode<TileTooltip>("TileTooltip");
        _gameOverPanel = GetNode<GameOverPanel>("GameOverPanel");
        _eventLog      = GetNode<EventLog>("EventLog");

        // Wire toolbar signals
        _toolbar.ZoneSelected       += OnZoneSelected;
        _toolbar.PauseToggled       += OnPauseToggled;
        _toolbar.NewGameRequested   += OnNewGameRequested;
        _toolbar.TaxRateChanged     += OnTaxRateChanged;
        _toolbar.SpeedChanged       += OnSpeedChanged;
        _toolbar.MainMenuRequested  += () =>
        {
            KillServerIfRunning();
            GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        };

        // Wire game-over panel
        _gameOverPanel.NewGameRequested += OnNewGameRequested;

        // Update HUD with initial selection
        _hud.SetSelectedZone(_toolbar.SelectedZone);

        // Resolve project directory for IPC files
        var projectDir = ProjectSettings.GlobalizePath("res://");
        _sharedDir = Path.Combine(projectDir, "shared");

        // Clean up stale state files (older than 5 seconds) left by previous sessions
        if (Directory.Exists(_sharedDir))
        {
            foreach (var f in Directory.GetFiles(_sharedDir, "state-*.json"))
            {
                if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalSeconds > 5)
                {
                    GD.Print($"[world] Deleting stale {Path.GetFileName(f)}");
                    try { File.Delete(f); } catch { /* non-critical */ }
                }
            }
        }

        // Check for a live state file (written within the last 2 seconds) to enter viewer mode
        var liveStateFile = FindLiveStateFile(_sharedDir);
        if (liveStateFile != null)
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
        _grid = new CityGrid(64, 64);
        GenerateTerrain(_grid);
        SeedStarterCity(_grid);

        _budget     = new BudgetSystem(); // default $4,000 starting balance
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
    /// Layout centred around (32,30)–(32,31):
    ///   PowerPlant  at (32,29)
    ///   Road        at (32,30) and (32,31)
    ///   Residential at (31,30), (33,30), (31,31), (33,31)
    /// </summary>
    private static void SeedStarterCity(CityGrid grid)
    {
        grid.SetZone(32, 29, ZoneType.PowerPlant);
        grid.SetZone(32, 30, ZoneType.Road);
        grid.SetZone(32, 31, ZoneType.Road);
        grid.SetZone(31, 30, ZoneType.Residential);
        grid.SetZone(33, 30, ZoneType.Residential);
        grid.SetZone(31, 31, ZoneType.Residential);
        grid.SetZone(33, 31, ZoneType.Residential);
    }

    private static void GenerateTerrain(CityGrid grid, int seed = 0)
    {
        int w = grid.Width;
        int h = grid.Height;

        // Height noise — determines land/water/hills
        var heightNoise = new FastNoiseLite();
        heightNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        heightNoise.Seed = seed == 0 ? (int)(Time.GetTicksMsec() % int.MaxValue) : seed;
        heightNoise.Frequency = 0.04f; // large blobs

        // Forest noise — separate layer for tree coverage
        var forestNoise = new FastNoiseLite();
        forestNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        forestNoise.Seed = heightNoise.Seed + 1;
        forestNoise.Frequency = 0.08f;

        // Small random offset so the island isn't always perfectly centred
        var rng = new System.Random(heightNoise.Seed);
        float offX = (float)(rng.NextDouble() - 0.5) * 8; // ±4 tiles
        float offY = (float)(rng.NextDouble() - 0.5) * 8;

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                float nx = heightNoise.GetNoise2D(x, y); // -1 to 1

                // Island falloff: edges become water (gentler — less aggressive shrinkage)
                float dx = (x - w * 0.5f - offX) / (w * 0.5f);
                float dy = (y - h * 0.5f - offY) / (h * 0.5f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float height = nx - dist * 0.5f;

                TerrainType terrain;
                if (height < -0.45f)
                    terrain = TerrainType.Water;
                else if (height > 0.4f)
                    terrain = TerrainType.Hill;
                else
                {
                    // Forest on mid-elevation land
                    float fn = forestNoise.GetNoise2D(x, y);
                    terrain = fn > 0.2f ? TerrainType.Forest : TerrainType.Flat;
                }

                grid.SetTerrain(x, y, terrain);
            }
        }

        // Guarantee a flat starter area around center (so SeedStarterCity always works)
        int cx = w / 2, cy = h / 2;
        for (int x = cx - 6; x <= cx + 6; x++)
        for (int y = cy - 6; y <= cy + 6; y++)
            grid.SetTerrain(x, y, TerrainType.Flat);
    }

    public override void _Process(double delta)
    {
        // Tooltip always updates (both modes)
        UpdateTooltip();

        // Coverage radius overlay: update whenever mouse moves to a new tile
        UpdateCoverageHighlight();

        if (_viewerMode) return; // SharedStateReader handles everything else

        if (_standalonePaused) return;
        if (_gameOver) return;

        _tickTimer += delta;
        if (_tickTimer >= _tickInterval)
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
                _hintOverlay.SetGameOver();
            }

            // Abandoned check
            if (_engine.MilestoneSystem.CurrentState == Loopolis.Core.Simulation.GameState.Abandoned)
            {
                _gameOver = true;
                _standalonePaused = true;
                _toolbar.SetPaused(true);
                var happiness = _engine.HappinessSystem.AverageHappiness(_grid);
                _gameOverPanel.ShowAbandoned(_standaloneTick, _population!.Population, happiness);
                _hintOverlay.SetGameOver();
            }
        }
    }

    private void UpdateTooltip()
    {
        var localPos = _renderer.GetLocalMousePosition();
        var tileX = (int)(localPos.X / TilemapRenderer.TileSize);
        var tileY = (int)(localPos.Y / TilemapRenderer.TileSize);

        var grid = _viewerMode ? _reader?.LastGrid : _grid;
        if (grid == null || tileX < 0 || tileX >= 64 || tileY < 0 || tileY >= 64)
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

    private void UpdateCoverageHighlight()
    {
        if (_coverageRadius <= 0) return;

        var localPos = _renderer.GetLocalMousePosition();
        var tileX = (int)(localPos.X / TilemapRenderer.TileSize);
        var tileY = (int)(localPos.Y / TilemapRenderer.TileSize);

        // Only update if the hover tile changed
        if (tileX == _lastCoverageHoverX && tileY == _lastCoverageHoverY) return;
        _lastCoverageHoverX = tileX;
        _lastCoverageHoverY = tileY;

        if (tileX < 0 || tileX >= 64 || tileY < 0 || tileY >= 64)
        {
            _renderer.ClearCoverageHighlight();
            return;
        }

        // Generate all tiles within Chebyshev distance (square) of coverage radius
        var tiles = new System.Collections.Generic.List<(int, int)>();
        for (var dy = -_coverageRadius; dy <= _coverageRadius; dy++)
        for (var dx = -_coverageRadius; dx <= _coverageRadius; dx++)
        {
            var cx = tileX + dx;
            var cy = tileY + dy;
            if (cx >= 0 && cx < 64 && cy >= 0 && cy < 64)
                tiles.Add((cx, cy));
        }

        _renderer.SetCoverageHighlight(tiles, _coverageColor);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _isPlacing = true;
                var tilePos = GetTileUnderMouse();
                HandlePlaceTile(tilePos);
                _lastPlacedTile = tilePos;
            }
            else
            {
                _isPlacing = false;
                _lastPlacedTile = new(-1, -1);
            }
        }

        if (@event is InputEventMouseMotion && _isPlacing)
        {
            var tilePos = GetTileUnderMouse();
            if (tilePos != _lastPlacedTile)
            {
                HandlePlaceTile(tilePos);
                _lastPlacedTile = tilePos;
            }
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            // Shift+1/2/3/4 → speed controls (½×, 1×, 2×, 4×)
            if (key.ShiftPressed)
            {
                var speedChanged = true;
                if      (key.Keycode == Key.Key1) _toolbar.SetSpeed(0.5f);
                else if (key.Keycode == Key.Key2) _toolbar.SetSpeed(1.0f);
                else if (key.Keycode == Key.Key3) _toolbar.SetSpeed(2.0f);
                else if (key.Keycode == Key.Key4) _toolbar.SetSpeed(4.0f);
                else speedChanged = false;
                if (speedChanged) return;
            }

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

        // Update coverage radius for service buildings
        (_coverageRadius, _coverageColor) = zoneName switch
        {
            "FireStation"   => (4, new Color(1f, 0.4f, 0.1f, 0.3f)),
            "PoliceStation" => (4, new Color(0.2f, 0.4f, 1f, 0.3f)),
            "School"        => (5, new Color(0.7f, 0.3f, 0.9f, 0.3f)),
            _               => (0, Colors.Transparent)
        };

        // Reset hover tracking so coverage updates on next _Process
        _lastCoverageHoverX = -1;
        _lastCoverageHoverY = -1;

        if (_coverageRadius == 0)
            _renderer.ClearCoverageHighlight();
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

    private void OnTaxRateChanged(string level)
    {
        _taxLevel = level;
        if (_viewerMode)
        {
            WriteCommand($"{{\"cmd\":\"set_tax\",\"level\":\"{level}\"}}");
        }
        else
        {
            _budget?.SetTaxRate(level);
        }
    }

    private void OnSpeedChanged(float ticksPerSecond)
    {
        _ticksPerSecond = ticksPerSecond;
        _tickInterval = 1.0 / ticksPerSecond;
        if (_viewerMode && _reader?.SessionId != null)
            WriteCommand($"{{\"cmd\":\"set_speed\",\"ticksPerSecond\":{ticksPerSecond}}}");
    }

    // ── Click/drag-to-place ────────────────────────────────────────────────

    /// <summary>Returns the tile coordinate currently under the mouse cursor.</summary>
    private Vector2I GetTileUnderMouse()
    {
        var localPos = _renderer.GetLocalMousePosition();
        return new Vector2I(
            (int)(localPos.X / TilemapRenderer.TileSize),
            (int)(localPos.Y / TilemapRenderer.TileSize)
        );
    }

    private void HandlePlaceTile(Vector2I tilePos)
    {
        var tileX = tilePos.X;
        var tileY = tilePos.Y;

        var selectedZone = _toolbar.SelectedZone;

        if (_viewerMode)
        {
            // Bounds-check against the viewer grid (server uses 32×32; standalone uses 64×64)
            var viewerGrid = _reader?.LastGrid;
            if (viewerGrid == null) return;
            if (tileX < 0 || tileX >= viewerGrid.Width || tileY < 0 || tileY >= viewerGrid.Height) return;

            // Tile protection: skip if the tile is occupied and we are not erasing
            if (selectedZone != "Erase")
            {
                if (viewerGrid.GetTile(tileX, tileY).Zone != ZoneType.Empty)
                    return;
            }

            // Optimistic rendering: update visuals immediately, server confirms on next tick
            if (selectedZone == "Erase")
                viewerGrid.SetZone(tileX, tileY, ZoneType.Empty);
            else if (System.Enum.TryParse<ZoneType>(selectedZone, out var optimisticZone))
                viewerGrid.SetZone(tileX, tileY, optimisticZone);
            _renderer.Refresh(viewerGrid);

            string cmd;
            if (selectedZone == "Erase")
                cmd = $"{{\"cmd\":\"erase\",\"x\":{tileX},\"y\":{tileY}}}";
            else
                cmd = $"{{\"cmd\":\"place_zone\",\"x\":{tileX},\"y\":{tileY},\"zone\":\"{selectedZone}\"}}";
            WriteCommand(cmd);
        }
        else
        {
            // Bounds-check against the standalone grid
            if (tileX < 0 || tileX >= _grid.Width || tileY < 0 || tileY >= _grid.Height) return;

            // Tile protection: skip if the tile is occupied and we are not erasing
            if (selectedZone != "Erase")
            {
                if (_grid.GetTile(tileX, tileY).Zone != ZoneType.Empty)
                    return;
            }

            if (selectedZone == "Erase")
            {
                _grid.SetZone(tileX, tileY, ZoneType.Empty);
            }
            else
            {
                var terrain = _grid.GetTerrain(tileX, tileY);
                var placementCost = Loopolis.Core.Simulation.BudgetSystem.GetPlacementCost(selectedZone, terrain);
                if (_budget != null && !_budget.CanAfford(placementCost))
                {
                    // Flash the balance label red briefly to signal insufficient funds
                    _hud.FlashBalanceWarning();
                    return;
                }
                if (System.Enum.TryParse<ZoneType>(selectedZone, out var zoneType))
                {
                    _budget?.Charge(placementCost);
                    _grid.SetZone(tileX, tileY, zoneType);
                    Log($"[T:{_standaloneTick}] Placed {selectedZone} at ({tileX},{tileY})");
                }
            }
            _renderer.Refresh(_grid);
        }
    }

    private void Log(string text) => _eventLog.AddEntry(text);

    private void WriteCommand(string json)
    {
        try
        {
            var sessionId = _reader?.SessionId;
            if (sessionId == null) return; // session not yet resolved — drop command

            var commandFile = Path.Combine(_sharedDir, $"command-{sessionId}.json");
            File.WriteAllText(commandFile, json);
        }
        catch { /* ignore — Runner may not be listening */ }
    }

    /// <summary>
    /// Returns the most recently written state-*.json file in <paramref name="sharedDir"/>
    /// that was written within the last 2 seconds, or null if none exists.
    /// </summary>
    private static string? FindLiveStateFile(string sharedDir)
    {
        if (!Directory.Exists(sharedDir)) return null;
        return Directory.GetFiles(sharedDir, "state-*.json")
            .Where(f => (DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalSeconds < 2.0)
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .FirstOrDefault();
    }

    // ── Standalone HUD sync ────────────────────────────────────────────────

    // Milestone thresholds for standalone next-milestone computation
    private static readonly (string Name, string Emoji, int Target)[] MilestoneThresholds =
    {
        ("Town",       "🥉", 500),
        ("City",       "🥈", 5_000),
        ("Metropolis", "🥇", 25_000),
        ("Loopolis",   "🏆", 100_000),
    };

    private void PushStandaloneHudUpdate()
    {
        if (_budget == null || _population == null) return;

        var snapshot      = _budget.Snapshot();
        var happiness     = _engine.HappinessSystem.AverageHappiness(_grid);
        var milestone     = _engine.MilestoneSystem.LatestMilestone?.Name;
        var residentialCount = System.Linq.Enumerable.Count(_grid.TilesOfType(Loopolis.Core.Grid.ZoneType.Residential));
        var maxCapacity   = residentialCount * 50;

        var gameStateName = _engine.MilestoneSystem.CurrentState.ToString();

        // Compute next milestone inline
        var currentPop = _population.Population;
        var nextM = System.Array.Find(MilestoneThresholds, m => m.Target > currentPop);
        var nextMilestoneName   = nextM != default ? $"{nextM.Name} {nextM.Emoji}" : null;
        var nextMilestoneTarget = nextM != default ? nextM.Target : 0;

        var activeEvent = _engine.EventSystem.ActiveEvent;

        var state = new SharedState(
            Tick:                      _standaloneTick,
            Paused:                    _standalonePaused,
            Population:                _population.Population,
            MaxCapacity:               maxCapacity,
            Balance:                   snapshot.Balance,
            TaxPerTick:                snapshot.TaxIncome,
            CommercialIncomePerTick:   _budget.CommercialIncomePerTick,
            MaintenancePerTick:        snapshot.MaintenanceCost,
            NetPerTick:                snapshot.NetIncome,
            Happiness:                 happiness,
            MilestoneReached:          _engine.LatestEventBanner ?? milestone,
            GameState:                 gameStateName,
            Tiles:                     System.Array.Empty<SharedTile>(),
            NextMilestoneName:         nextMilestoneName,
            NextMilestoneTarget:       nextMilestoneTarget,
            ActiveEventName:           activeEvent?.Name,
            ActiveEventDescription:    activeEvent?.Description,
            LatestEventBanner:         _engine.LatestEventBanner,
            TaxModifier:               _budget.TaxModifier
        );
        _hud.UpdateStats(state);
        _hintOverlay.UpdateHints(state);
        UpdateEventLog(state, milestone);
    }

    private void UpdateEventLog(SharedState state, string? milestone)
    {
        var tick = _standaloneTick;
        var pop = (int)state.Population;

        // Population milestone every 50
        if (pop > 0 && pop / 50 > _lastLoggedPop / 50)
        {
            Log($"[T:{tick}] Population reached {(pop / 50) * 50}");
            _lastLoggedPop = pop;
        }

        // Milestone reached (e.g. Town, City, Metropolis)
        if (!string.IsNullOrEmpty(milestone) && milestone != _lastLoggedMilestone)
        {
            _lastLoggedMilestone = milestone;
            Log($"[T:{tick}] 🏆 {milestone}");
        }

        // City events (event banner)
        if (!string.IsNullOrEmpty(state.LatestEventBanner) && state.LatestEventBanner != _lastLoggedBanner)
        {
            _lastLoggedBanner = state.LatestEventBanner;
            Log($"[T:{tick}] {state.LatestEventBanner}");
        }

        // Net income turning negative
        if (state.NetPerTick < -1.0 && !_wasNegative)
        {
            Log($"[T:{tick}] ⚠ Spending more than earning");
            _wasNegative = true;
        }
        if (state.NetPerTick >= 0) _wasNegative = false;

        // Low balance warning
        if (state.Balance < 500 && state.Balance > 0 && !_warnedLowBalance)
        {
            Log($"[T:{tick}] ⚠ Low funds — ${state.Balance:N0} remaining");
            _warnedLowBalance = true;
        }
        if (state.Balance > 1000) _warnedLowBalance = false;

        // At capacity
        if (state.MaxCapacity > 0 && state.Population >= state.MaxCapacity - 5 && !_loggedCapacity)
        {
            Log($"[T:{tick}] ⚡ At capacity — build more residential zones");
            _loggedCapacity = true;
        }
        if (state.Population < state.MaxCapacity - 10) _loggedCapacity = false;
    }
}
