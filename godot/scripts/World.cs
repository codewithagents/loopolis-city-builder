using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Loopolis.Core.Grid;
using Loopolis.Core.Persistence;
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
	private Camera _camera = null!;
	private HudOverlay _hud = null!;
	private HintOverlay _hintOverlay = null!;
	private CityHealthPanel _cityHealth = null!;
	private Toolbar _toolbar = null!;
	private TileTooltip _tooltip = null!;
	private GameOverPanel _gameOverPanel = null!;
	private EventLog _eventLog = null!;
	private TopBar _topBar = null!;
	private ToastSystem _toastSystem = null!;
	private bool _viewerMode = false;
	private string _sharedDir = "";
	private SharedStateReader? _reader; // viewer mode only, for optimistic rendering

	// Standalone mode state for HUD updates
	private int _standaloneTick = 0;
	private bool _standalonePaused = false;
	private bool _buildModePaused = false; // true when pause was triggered by selecting a build tool
	private bool _gameOver = false;
	private BudgetSystem? _budget;
	private PopulationSystem? _population;
	private string _taxLevel = "normal";
	private int _terrainSeed = 0;

	// Last known SharedState (for passing city-wide stats to tooltip)
	private SharedState? _lastState;

	// Building birth tracking for standalone mode
	private readonly System.Collections.Generic.HashSet<string> _standaloneKnownBuildingIds = new();

	// Event log tracking fields
	private int _lastLoggedPop = 0;
	private bool _wasNegative = false;
	private bool _warnedLowBalance = false;
	private bool _loggedCapacity = false;
	private string? _lastLoggedMilestone;
	private string? _lastLoggedBanner;

	// Toast deduplication flags
	private bool _brownoutToastShown = false;
	private bool _employmentToastShown = false;

	// Tutorial hint progression
	private int _tutorialHintIndex = 0;
	private static readonly string[] TutorialHints =
	{
		"💡 Zone homes along roads to attract residents. Power unlocks bigger buildings.",
		"💡 Connect a Coal Plant with Power Lines — powered zones grow faster.",
		"💡 Build Fire Station, Police, and School — happiness keeps your city growing.",
		"💡 Milestones: 500 pop = Town · 5k = City · 25k = Metropolis · 100k = Loopolis"
	};

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

	// Active overlay mode
	private OverlayMode _activeOverlay = OverlayMode.None;

	// Coverage radius overlay tracking
	private int _coverageRadius = 0;
	private Color _coverageColor = Colors.Transparent;
	private int _lastCoverageHoverX = -1;
	private int _lastCoverageHoverY = -1;

	// Rectangle zone painting state
	private bool _isRectPainting = false;
	private Vector2I _rectStart   = new(-1, -1);
	private Vector2I _rectEnd     = new(-1, -1);

	public override void _Ready()
	{
		_renderer      = GetNode<TilemapRenderer>("TilemapRenderer");
		_camera        = GetNode<Camera>("Camera");
		_hud           = GetNode<HudOverlay>("HudOverlay");
		_hintOverlay   = GetNode<HintOverlay>("HintOverlay");
		_cityHealth    = GetNode<CityHealthPanel>("CityHealthPanel");
		_toolbar       = GetNode<Toolbar>("Toolbar");
		_tooltip       = GetNode<TileTooltip>("TileTooltip");
		_gameOverPanel = GetNode<GameOverPanel>("GameOverPanel");
		_eventLog      = GetNode<EventLog>("EventLog");

		// Instantiate TopBar and ToastSystem
		_topBar = new TopBar();
		AddChild(_topBar);

		_toastSystem = new ToastSystem();
		AddChild(_toastSystem);

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
		_toolbar.StatsToggled   += () => _hud.Toggle();
		_toolbar.OverlayChanged += mode => ToggleOverlay((OverlayMode)mode);

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
			_reader.BuildingBorn      += (typeId, ax, ay) => SpawnBuildingBirthLabel(typeId, ax, ay);
			_reader.BuildingDegraded  += (typeId, ax, ay) => SpawnBuildingCrumbleLabel(typeId, ax, ay);
			_reader.FirstGridReady    += (w, h) => _camera.FitToMap(w, h);
			AddChild(_reader);
			_viewerMode = true;
			return;
		}

		// Standalone mode — run own simulation
		GD.Print("[world] Standalone mode — running own simulation.");
		SetupStandaloneSimulation();
	}

	private const int StandaloneMapSize = 64; // standalone default — 64×64 grid with procedural terrain

	private void SetupStandaloneSimulation()
	{
		_grid        = new CityGrid(StandaloneMapSize, StandaloneMapSize);
		_terrainSeed = (int)(GD.Randi() % int.MaxValue);

		_budget     = new BudgetSystem(); // default $4,000 starting balance
		_population = new PopulationSystem();
		var power   = new PowerNetwork();
		var roads   = new RoadNetwork();
		var demand  = new DemandSystem();

		_engine   = new SimulationEngine(_grid, _budget, _population, power, roads, demand);
		_gameOver = false;

		SetupDefaultNewGame();

		_renderer.Refresh(_grid);
		// Fit the camera to show the full map including the south-edge border road
		_camera.FitToMap(StandaloneMapSize, StandaloneMapSize);
		PushStandaloneHudUpdate();
	}

	/// <summary>
	/// Initialises a new standalone game with procedural terrain, a border connection
	/// at the centre of the south edge, and three starter road tiles extending north
	/// from it.  No power plant, no pre-placed zones.
	/// </summary>
	private void SetupDefaultNewGame()
	{
		// Procedural terrain — hills, water, forests based on random seed
		GenerateTerrain(_grid, _terrainSeed);

		// Border connection — centre of south edge, unerasable Regional Highway
		_grid.PlaceBorderConnection(StandaloneMapSize / 2, StandaloneMapSize - 1);

		// 3 starter road tiles extending north from the border
		_grid.SetZone(StandaloneMapSize / 2, StandaloneMapSize - 2, ZoneType.Road);
		_grid.SetZone(StandaloneMapSize / 2, StandaloneMapSize - 3, ZoneType.Road);
		_grid.SetZone(StandaloneMapSize / 2, StandaloneMapSize - 4, ZoneType.Road);

		// Seed road graph so the border connection registers as an ExternalAnchor
		_engine.SeedRoadGraphFromGrid();
	}

	private static void GenerateTerrain(CityGrid grid, int seed)
	{
		var w = grid.Width;
		var h = grid.Height;

		// Use the Core's diamond-square height map generator for procedural terrain.
		var heightMap = HeightMapGenerator.Generate(w, h, seed, roughness: 0.55f);
		var forestMap = HeightMapGenerator.GenerateForest(w, h, seed);

		grid.ApplyHeightMap(heightMap);
		grid.ApplyForestMap(forestMap);

		// Guarantee a flat, non-forest starter area around center (so SeedStarterCity always works).
		var cx = w / 2;
		var cy = h / 2;
		for (var x = cx - 6; x <= cx + 6; x++)
		for (var y = cy - 6; y <= cy + 6; y++)
		{
			grid.SetHeightLevel(x, y, 1);
			grid.SetForest(x, y, false);
		}
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

			// Snapshot building IDs and typeIds before tick so we can detect births and degradation
			var priorBuildingIds = new System.Collections.Generic.HashSet<string>(_grid.Buildings.Keys);
			var priorBuildingTypes = new System.Collections.Generic.Dictionary<string, (string TypeId, int AnchorX, int AnchorY)>();
			foreach (var kvp in _grid.Buildings)
				priorBuildingTypes[kvp.Key] = (kvp.Value.TypeId, kvp.Value.AnchorX, kvp.Value.AnchorY);

			try
			{
				_engine.Tick();
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[Loopolis] Tick crash at tick {_engine.TickCount}: {ex.GetType().Name}: {ex.Message}");
				GD.PrintErr(ex.StackTrace);

				// Pause immediately so the player sees the error
				_standalonePaused = true;
				_toolbar.SetPaused(true);

				// Write crash log to user data dir
				try
				{
					var logPath = Path.Combine(OS.GetUserDataDir(), "loopolis_crash.log");
					File.AppendAllText(logPath,
						$"[{DateTime.Now:HH:mm:ss}] Tick {_engine.TickCount}: {ex}\n\n");
					GD.Print($"[Loopolis] Crash log written to: {logPath}");
				}
				catch (Exception logEx)
				{
					GD.PrintErr($"[Loopolis] Could not write crash log: {logEx.Message}");
				}

				_hud.ShowErrorBanner($"Tick {_engine.TickCount}: {ex.Message}");
				return; // skip the rest of this tick's processing
			}
			_standaloneTick++;

			// Detect new buildings spawned this tick
			foreach (var kvp in _grid.Buildings)
			{
				if (!priorBuildingIds.Contains(kvp.Key) && _standaloneKnownBuildingIds.Add(kvp.Key))
					SpawnBuildingBirthLabel(kvp.Value.TypeId, kvp.Value.AnchorX, kvp.Value.AnchorY);
			}

			// Detect buildings demolished by degradation this tick.
			// Any building that existed before the tick but is now gone was degraded.
			var hadDegradation = false;
			foreach (var removedId in priorBuildingIds)
			{
				if (!_grid.Buildings.ContainsKey(removedId))
				{
					hadDegradation = true;
					_standaloneKnownBuildingIds.Remove(removedId);
					if (priorBuildingTypes.TryGetValue(removedId, out var info))
						SpawnBuildingCrumbleLabel(info.TypeId, info.AnchorX, info.AnchorY);
				}
			}
			if (hadDegradation)
				_cityHealth.NotifyDegradation();

			_renderer.Refresh(_grid);
			UpdateNeglectMap();
			PushStandaloneHudUpdate();

			// Bankrupt check
			if (_engine.MilestoneSystem.CurrentState == Loopolis.Core.Simulation.GameState.Bankrupt)
			{
				_gameOver = true;
				_standalonePaused = true;
				_toolbar.SetPaused(true);
				_gameOverPanel.ShowBankrupt(_standaloneTick, _budget!.Balance, _population!.Population);
				_hintOverlay.SetGameOver();
				_toastSystem.SetGameOver();
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
				_toastSystem.SetGameOver();
			}

			// Win condition — Loopolis (100k population)
			if (_engine.MilestoneSystem.CurrentState == Loopolis.Core.Simulation.GameState.Loopolis)
			{
				_gameOver = true;
				_standalonePaused = true;
				_toolbar.SetPaused(true);
				_gameOverPanel.ShowWin(_standaloneTick, _population!.Population, _budget!.Balance);
				_hintOverlay.SetGameOver();
				_toastSystem.SetGameOver();
			}
		}
	}

	private void UpdateTooltip()
	{
		var localPos = _renderer.GetLocalMousePosition();
		var tileX = (int)(localPos.X / TilemapRenderer.TileSize);
		var tileY = (int)(localPos.Y / TilemapRenderer.TileSize);

		var grid = _viewerMode ? _reader?.LastGrid : _grid;
		var gridW = grid?.Width ?? StandaloneMapSize;
		var gridH = grid?.Height ?? StandaloneMapSize;
		if (grid == null || tileX < 0 || tileX >= gridW || tileY < 0 || tileY >= gridH)
		{
			_tooltip.Hide();
			return;
		}

		var tile = grid.GetTile(tileX, tileY);
		if (tile.Zone == Loopolis.Core.Grid.ZoneType.Empty)
		{
			// Show terrain tooltip — pass height/forest from renderer's maps for rich display
			var h = _renderer.GetTileHeight(tileX, tileY);
			var isForest = _renderer.GetTileForest(tileX, tileY);
			// Show tooltip whenever there is interesting terrain data (height != 1 or forest or water)
			if (h != 1 || isForest || tile.Terrain != Loopolis.Core.Grid.TerrainType.Flat)
				_tooltip.ShowForEmptyTerrain(tile, GetViewport().GetMousePosition(), h, isForest);
			else
				_tooltip.Hide();
			return;
		}

		// Resolve the most recent state for the tooltip growth checklist
		var currentState = _viewerMode ? _reader?.LastState : _lastState;

		// Multi-tile building: show one unified tooltip, no road-connectivity warning
		if (tile.BuildingId != null && grid.Buildings.TryGetValue(tile.BuildingId, out var building))
		{
			// Sum population across all tiles that belong to this building
			var totalPop = 0;
			foreach (var (bx, by) in building.Tiles())
			{
				if (grid.IsInBounds(bx, by))
					totalPop += grid.GetTile(bx, by).Population;
			}

			// Use the anchor tile for power/happiness context
			var anchorTile = grid.GetTile(building.AnchorX, building.AnchorY);
			_tooltip.ShowForBuilding(building, totalPop, anchorTile, GetViewport().GetMousePosition(), currentState);
			return;
		}

		_tooltip.ShowFor(tile, GetViewport().GetMousePosition(), currentState);
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

		var coverageGrid = _viewerMode ? _reader?.LastGrid : _grid;
		var cgW = coverageGrid?.Width ?? StandaloneMapSize;
		var cgH = coverageGrid?.Height ?? StandaloneMapSize;
		if (tileX < 0 || tileX >= cgW || tileY < 0 || tileY >= cgH)
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
			if (cx >= 0 && cx < cgW && cy >= 0 && cy < cgH)
				tiles.Add((cx, cy));
		}

		_renderer.SetCoverageHighlight(tiles, _coverageColor);
	}

	/// <summary>
	/// For Road and Avenue zones, constrains a drag rectangle to a 1-tile-wide line
	/// along the dominant axis.  Returns (constrainedStart, constrainedEnd).
	/// For all other zones, the inputs are returned unchanged.
	/// </summary>
	private (Vector2I start, Vector2I end) ConstrainToLineIfRoad(Vector2I start, Vector2I end)
	{
		var zone = _toolbar.SelectedZone;
		if (zone != "Road" && zone != "Avenue") return (start, end);

		var dx = System.Math.Abs(end.X - start.X);
		var dy = System.Math.Abs(end.Y - start.Y);

		if (dx >= dy)
			// Horizontal line: lock Y to the start row
			return (start, new Vector2I(end.X, start.Y));
		else
			// Vertical line: lock X to the start column
			return (start, new Vector2I(start.X, end.Y));
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Left)
			{
				if (mb.Pressed)
				{
					// Start rectangle selection
					_rectStart      = GetTileUnderMouse();
					_rectEnd        = _rectStart;
					_isRectPainting = true;
					var (cs, ce) = ConstrainToLineIfRoad(_rectStart, _rectEnd);
					_renderer.SetRectPreview(cs, ce, GetZonePreviewColor());
				}
				else if (_isRectPainting)
				{
					// Commit: fill all tiles in the selected rectangle (line for Road/Avenue)
					_isRectPainting = false;
					_renderer.ClearRectPreview();

					var (cs, ce) = ConstrainToLineIfRoad(_rectStart, _rectEnd);
					var minX = System.Math.Min(cs.X, ce.X);
					var maxX = System.Math.Max(cs.X, ce.X);
					var minY = System.Math.Min(cs.Y, ce.Y);
					var maxY = System.Math.Max(cs.Y, ce.Y);

					for (var ty = minY; ty <= maxY; ty++)
					for (var tx = minX; tx <= maxX; tx++)
						HandlePlaceTile(new Vector2I(tx, ty));
				}
			}
			else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
			{
				// Right-click cancels in-progress rectangle
				CancelRectPainting();
			}
		}

		if (@event is InputEventMouseMotion && _isRectPainting)
		{
			_rectEnd = GetTileUnderMouse();
			var (cs, ce) = ConstrainToLineIfRoad(_rectStart, _rectEnd);
			_renderer.SetRectPreview(cs, ce, GetZonePreviewColor());
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

			// Tab switches: Z = Zones, S = Services, U = Utilities
			// (New sidebar: 0=Zones, 1=Services, 2=Utilities, 3=Overlays)
			// Guard against Ctrl combinations (Ctrl+S = Save)
			if (!key.CtrlPressed)
			{
				if (key.Keycode == Key.Z) { _toolbar.SwitchToTab(0); return; }
				if (key.Keycode == Key.S) { _toolbar.SwitchToTab(1); return; }
				if (key.Keycode == Key.U) { _toolbar.SwitchToTab(2); return; }
			}

			var zone = key.Keycode switch
			{
				// Number row — original shortcuts
				Key.Key1 => "Residential",
				Key.Key2 => "Commercial",
				Key.Key3 => "Industrial",
				Key.Key4 => "Road",
				Key.Key5 => "Avenue",
				Key.Key6 => "PowerPlant",
				Key.Key7 => "FireStation",
				Key.Key8 => "PoliceStation",
				Key.Key9 => "School",
				Key.Key0 => "Erase",
				// Letter shortcuts — select zone AND auto-switch to its tab
				Key.R => "Residential",
				Key.C => "Commercial",
				Key.I => "Industrial",
				_ => null
			};
			if (zone != null)
			{
				// Pressing the same tool key again toggles it off
				if (_toolbar.SelectedZone == zone)
				{
					_toolbar.DeselectAll();
				}
				else
				{
					_toolbar.SwitchToTabForZone(zone);
					_toolbar.SelectZone(zone);
				}
			}

			// Escape: cancel in-progress rectangle painting, deselect tool, and resume if build-mode paused
			if (key.Keycode == Key.Escape)
			{
				CancelRectPainting();

				// If a tool is selected, deselect it (this also auto-resumes via OnZoneSelected)
				if (!string.IsNullOrEmpty(_toolbar.SelectedZone) && _toolbar.SelectedZone != "Empty")
					_toolbar.DeselectAll();

				return;
			}

			if (key.Keycode == Key.Space)
				OnPauseToggled();

			// F1–F5: overlay modes
			if (key.Keycode == Key.F1) { ToggleOverlay(OverlayMode.Happiness);  return; }
			if (key.Keycode == Key.F2) { ToggleOverlay(OverlayMode.Traffic);    return; }
			if (key.Keycode == Key.F3) { ToggleOverlay(OverlayMode.Coverage);   return; }
			if (key.Keycode == Key.F4) { ToggleOverlay(OverlayMode.LandValue);  return; }
			if (key.Keycode == Key.F5) { ToggleOverlay(OverlayMode.Pollution);  return; }

			// F12 dismisses the error banner
			if (key.Keycode == Key.F12)
			{
				_hud.DismissErrorBanner();
				return;
			}

			// F9 dumps a debug summary to the Godot console (standalone mode only)
			if (key.Keycode == Key.F9 && !_viewerMode)
			{
				var lastDegraded = _engine.LastDegradedBuildings.Count > 0
					? string.Join(", ", _engine.LastDegradedBuildings)
					: "none";
				GD.Print($"[DEBUG] Tick: {_engine.TickCount} | Pop: {_population?.Population ?? 0} | Balance: ${_budget?.Balance:N0} | Happiness: {_engine.HappinessSystem.AverageHappiness(_grid):F2}");
				GD.Print($"[DEBUG] Buildings: {_grid.Buildings.Count} | RoadNodes: {_engine.RoadGraph.NodeCount} | RoadEdges: {_engine.RoadGraph.EdgeCount}");
				GD.Print($"[DEBUG] Jobs: {_engine.EmploymentSystem.AvailableJobs} available / {_engine.EmploymentSystem.RequiredJobs} required | EmploymentRatio: {_engine.EmploymentSystem.EmploymentRatio:F2}");
				GD.Print($"[DEBUG] LastDegraded: {lastDegraded}");
				return;
			}

			// Ctrl+S = Save, Ctrl+L = Load (standalone mode only)
			if (key.CtrlPressed && !_viewerMode && !_gameOver)
			{
				if (key.Keycode == Key.S) SaveGame();
				if (key.Keycode == Key.L) LoadGame();
				return;
			}
		}
	}

	// ── Toolbar signal handlers ────────────────────────────────────────────

	private void OnZoneSelected(string zoneName)
	{
		_hud.SetSelectedZone(zoneName);

		// Build-mode auto-pause: selecting any tool pauses the sim; deselecting resumes it.
		var toolSelected = !string.IsNullOrEmpty(zoneName) && zoneName != "Empty";
		if (toolSelected)
		{
			if (!_standalonePaused && !_buildModePaused)
			{
				_buildModePaused  = true;
				_standalonePaused = true;
				_toolbar.SetPaused(true);

				// Viewer mode: send pause command to server
				if (_viewerMode)
					WriteCommand("{\"cmd\":\"pause\"}");
			}
			_toolbar.SetBuildMode(true);
			if (!_viewerMode) PushStandaloneHudUpdate();
		}
		else
		{
			// Tool deselected — resume if we were the ones who paused
			if (_buildModePaused)
			{
				_buildModePaused  = false;
				_standalonePaused = false;
				_toolbar.SetPaused(false);

				// Viewer mode: send resume command to server
				if (_viewerMode)
					WriteCommand("{\"cmd\":\"resume\"}");
			}
			_toolbar.SetBuildMode(false);
			if (!_viewerMode) PushStandaloneHudUpdate();
		}

		// Update coverage radius for service buildings
		(_coverageRadius, _coverageColor) = zoneName switch
		{
			"FireStation"   => (4,  new Color(1f,    0.4f,  0.1f, 0.3f)),
			"FireHQ"        => (10, new Color(0.718f,0.110f,0.110f, 0.3f)),
			"PoliceStation" => (4,  new Color(0.2f,  0.4f,  1f,   0.3f)),
			"PoliceHQ"      => (10, new Color(0.102f,0.137f,0.494f, 0.3f)),
			"School"        => (5,  new Color(0.7f,  0.3f,  0.9f, 0.3f)),
			"Hospital"      => (8,  new Color(0.647f,0.839f,0.647f, 0.3f)),
			_               => (0,  Colors.Transparent)
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
			// If we're in build mode, Resume deselects the tool and resumes the sim
			if (_buildModePaused)
			{
				_buildModePaused = false;
				_toolbar.DeselectAll();
				_toolbar.SetBuildMode(false);
				WriteCommand("{\"cmd\":\"resume\"}");
			}
			else
			{
				WriteCommand("{\"cmd\":\"pause\"}");
			}
			// Toolbar button text is updated when state.json reflects new paused state
		}
		else
		{
			// If a tool is selected (build mode), Space always clears the tool and resumes.
			// This covers both: build-mode-paused (auto) and manually-paused-then-selected cases.
			var hasActiveTool = !string.IsNullOrEmpty(_toolbar.SelectedZone);
			if (_buildModePaused || hasActiveTool)
			{
				// Clear build mode and resume
				_buildModePaused  = false;
				_standalonePaused = false;
				_toolbar.DeselectAll();
				_toolbar.SetBuildMode(false);
				_toolbar.SetPaused(false);
				PushStandaloneHudUpdate();
			}
			else
			{
				_standalonePaused = !_standalonePaused;
				_toolbar.SetPaused(_standalonePaused);
				PushStandaloneHudUpdate();
			}
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
			_buildModePaused  = false;
			_gameOver         = false;
			_toolbar.DeselectAll();
			_toolbar.SetBuildMode(false);
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

	/// <summary>
	/// Cancels an in-progress rectangle painting and clears the preview overlay.
	/// Called on right-click or Escape.
	/// </summary>
	private void CancelRectPainting()
	{
		_isRectPainting = false;
		_rectStart      = new(-1, -1);
		_rectEnd        = new(-1, -1);
		_renderer.ClearRectPreview();
	}

	/// <summary>Returns the semi-transparent preview color for the currently selected zone.</summary>
	private Color GetZonePreviewColor()
	{
		return _toolbar.SelectedZone switch
		{
			"Residential" => new Color(0.2f,  0.7f,  0.2f,  0.40f),
			"Commercial"  => new Color(0.2f,  0.4f,  0.9f,  0.40f),
			"Industrial"  => new Color(0.9f,  0.8f,  0.1f,  0.40f),
			"Road"        => new Color(0.5f,  0.5f,  0.5f,  0.40f),
			"Avenue"      => new Color(0.62f, 0.62f, 0.62f, 0.40f),
			"PowerPlant"  => new Color(0.9f,  0.3f,  0.1f,  0.40f),
			"CoalPlant"   => new Color(0.26f, 0.26f, 0.26f, 0.40f),
			"NuclearPlant"=> new Color(0.98f, 0.66f, 0.15f, 0.40f),
			"FireStation" => new Color(1.0f,  0.4f,  0.1f,  0.40f),
			"PoliceStation"=> new Color(0.2f, 0.4f,  1.0f,  0.40f),
			"School"      => new Color(0.7f,  0.3f,  0.9f,  0.40f),
			"Erase"       => new Color(0.6f,  0.15f, 0.15f, 0.40f),
			_             => new Color(1f,    1f,    1f,    0.25f),
		};
	}

	private void HandlePlaceTile(Vector2I tilePos)
	{
		var tileX = tilePos.X;
		var tileY = tilePos.Y;

		var selectedZone = _toolbar.SelectedZone;

		// Border connection guard: never allow painting or erasing a border connection tile.
		// The Core also enforces this, but checking here prevents sending useless commands (better UX).
		if (_viewerMode)
		{
			var stateTile = _reader?.LastState?.GetTile(tileX, tileY);
			if (stateTile?.IsBorderConnection == true) return;
		}
		else
		{
			if (tileX >= 0 && tileX < _grid.Width && tileY >= 0 && tileY < _grid.Height)
			{
				if (_grid.GetTile(tileX, tileY).IsBorderConnection) return;
			}
		}

		if (_viewerMode)
		{
			// Bounds-check against the viewer grid (size determined by server, may be 32×32–128×128)
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

			// Optimistic ripple for road/power zones (before server confirms)
			if (selectedZone is "Road" or "Avenue" or "PowerPlant" or "CoalPlant" or "NuclearPlant")
				SpawnRipple(tileX, tileY, selectedZone);
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
					// Immediately re-propagate road network so tooltips reflect current state
					// even when the simulation is paused (e.g. build mode).
					_engine.RoadNetwork.Propagate(_grid);
					Log($"[T:{_standaloneTick}] Placed {selectedZone} at ({tileX},{tileY})");

					// Ripple on road/power placement
					if (selectedZone is "Road" or "Avenue" or "PowerPlant" or "CoalPlant" or "NuclearPlant")
						SpawnRipple(tileX, tileY, selectedZone);
				}
			}
			_renderer.Refresh(_grid);
		}
	}

	private void Log(string text) => _eventLog.AddEntry(text);

	// ── Overlay toggle ─────────────────────────────────────────────────────

	private void ToggleOverlay(OverlayMode mode)
	{
		_activeOverlay = (_activeOverlay == mode) ? OverlayMode.None : mode;
		_renderer.ActiveOverlay = _activeOverlay;
		_renderer.QueueRedraw();
		_hud.ShowOverlayLegend(_activeOverlay);
	}

	// ── Visual effects ─────────────────────────────────────────────────────

	/// <summary>
	/// Spawns a RippleEffect centred on the tile at (tileX, tileY).
	/// Color is gold for power plants, light grey for roads.
	/// </summary>
	private void SpawnRipple(int tileX, int tileY, string zone)
	{
		var ripple = new RippleEffect();
		ripple.RippleColor = zone switch
		{
			"PowerPlant" or "CoalPlant" or "NuclearPlant" => new Color(1f, 0.835f, 0.31f),  // #FFD54F gold
			_                                              => new Color(0.690f, 0.745f, 0.773f), // #B0BEC5 light grey
		};
		_renderer.AddChild(ripple);
		var center = new Vector2(
			(tileX + 0.5f) * TilemapRenderer.TileSize,
			(tileY + 0.5f) * TilemapRenderer.TileSize);
		ripple.Start(center);
	}

	/// <summary>
	/// Spawns a BuildingBirthLabel floating above the given anchor tile.
	/// </summary>
	private void SpawnBuildingBirthLabel(string typeId, int anchorX, int anchorY)
	{
		var label = new BuildingBirthLabel();
		_renderer.AddChild(label);
		var center = new Vector2(
			(anchorX + 0.5f) * TilemapRenderer.TileSize,
			(anchorY + 0.5f) * TilemapRenderer.TileSize);
		label.Start(center, FormatBirthText(typeId));
	}

	/// <summary>
	/// Spawns a BuildingCrumbleLabel floating above the given anchor tile.
	/// </summary>
	private void SpawnBuildingCrumbleLabel(string typeId, int anchorX, int anchorY)
	{
		var label = new BuildingCrumbleLabel();
		_renderer.AddChild(label);
		var center = new Vector2(
			(anchorX + 0.5f) * TilemapRenderer.TileSize,
			(anchorY + 0.5f) * TilemapRenderer.TileSize);
		label.Start(center, FormatCrumbleText(typeId));
	}

	/// <summary>
	/// Converts a building TypeId to a degradation announcement string.
	/// e.g. "res_townhouse_2x2" → "⚠ Townhouse crumbled"
	/// </summary>
	private static string FormatCrumbleText(string typeId)
	{
		if (string.IsNullOrEmpty(typeId)) return "⚠ Building crumbled";

		// Strip zone prefix
		var s = typeId;
		foreach (var prefix in new[] { "res_", "com_", "ind_" })
		{
			if (s.StartsWith(prefix)) { s = s[prefix.Length..]; break; }
		}

		// Strip trailing _WxH
		var parts = s.Split('_');
		var nameParts = parts;
		if (parts.Length > 1)
		{
			var last = parts[^1];
			if (last.Contains('x') && last.Length <= 5)
				nameParts = parts[..^1];
		}

		// Title-case
		var name = string.Join(" ", System.Array.ConvertAll(nameParts,
			p => p.Length == 0 ? p : char.ToUpper(p[0]) + p[1..]));

		return $"⚠ {name} crumbled";
	}

	/// <summary>
	/// Converts a building TypeId to a short birth announcement string.
	/// e.g. "res_townhouse_2x2" → "+Townhouse", "res_apartment_4x4" → "+Apartment!"
	/// </summary>
	private static string FormatBirthText(string typeId)
	{
		// Strip zone prefix
		var s = typeId;
		foreach (var prefix in new[] { "res_", "com_", "ind_" })
		{
			if (s.StartsWith(prefix)) { s = s[prefix.Length..]; break; }
		}

		// Strip trailing _WxH
		var parts = s.Split('_');
		var nameParts = parts;
		if (parts.Length > 1)
		{
			var last = parts[^1];
			if (last.Contains('x') && last.Length <= 5)
				nameParts = parts[..^1];
		}

		// Title-case
		var name = string.Join(" ", System.Array.ConvertAll(nameParts,
			p => p.Length == 0 ? p : char.ToUpper(p[0]) + p[1..]));

		// Exclamation for largest tier buildings
		var exclaim = typeId.Contains("apartment") || typeId.Contains("shopping") || typeId.Contains("park")
			? "!" : "";

		return $"+{name}{exclaim}";
	}

	// ── Save / Load ────────────────────────────────────────────────────────

	private string GetSaveFilePath()
	{
		var saveDir = Path.Combine(ProjectSettings.GlobalizePath("res://"), "saves");
		Directory.CreateDirectory(saveDir);
		return Path.Combine(saveDir, "autosave.json");
	}

	private void SaveGame()
	{
		try
		{
			var save = SaveSystem.Capture(_engine, _grid, _terrainSeed, _taxLevel, _standaloneTick);
			var json = SaveSystem.Serialize(save);
			File.WriteAllText(GetSaveFilePath(), json);
			_eventLog.AddEntry("Game saved ✓");
			GD.Print($"[save] Saved to {GetSaveFilePath()} — {save.Tiles.Length} tiles, tick {save.Tick}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[save] Failed: {ex.Message}");
			_eventLog.AddEntry("Save failed!");
		}
	}

	private void LoadGame()
	{
		var path = GetSaveFilePath();
		if (!File.Exists(path))
		{
			_eventLog.AddEntry("No save file found.");
			return;
		}

		try
		{
			var json = File.ReadAllText(path);
			var save = SaveSystem.Deserialize(json);
			if (save == null) { _eventLog.AddEntry("Save file corrupted."); return; }

			// Rebuild the city from the save, using the persisted grid size
			var savedW   = save.GridWidth  > 0 ? save.GridWidth  : StandaloneMapSize;
			var savedH   = save.GridHeight > 0 ? save.GridHeight : StandaloneMapSize;
			_grid        = new CityGrid(savedW, savedH);
			_terrainSeed = save.TerrainSeed;
			GenerateTerrain(_grid, _terrainSeed);
			SaveSystem.RestoreGrid(_grid, save);

			_budget     = new BudgetSystem(save.Balance);
			_budget.SetTaxRate(save.TaxLevel);
			_taxLevel   = save.TaxLevel;
			_population = new PopulationSystem();
			var power   = new PowerNetwork();
			var roads   = new RoadNetwork();
			var demand  = new DemandSystem();

			_engine           = new SimulationEngine(_grid, _budget, _population, power, roads, demand);
			_standaloneTick   = save.Tick;
			_standalonePaused = false;
			_buildModePaused  = false;
			_gameOver         = false;

			_toolbar.DeselectAll();
			_toolbar.SetBuildMode(false);
			_toolbar.SetPaused(false);
			_toolbar.SetTaxRate(save.TaxLevel);
			_gameOverPanel.Hide();
			_renderer.Refresh(_grid);
			_camera.FitToMap(savedW, savedH);
			PushStandaloneHudUpdate();

			_eventLog.AddEntry($"Game loaded ✓ (tick {save.Tick})");
			GD.Print($"[load] Loaded from {path} — tick {save.Tick}, balance ${save.Balance:N0}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[load] Failed: {ex.Message}");
			_eventLog.AddEntry("Load failed!");
		}
	}

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

	// ── Neglect map update (standalone mode only) ──────────────────────────

	private void UpdateNeglectMap()
	{
		if (_engine == null || _grid == null) return;
		var w = _grid.Width;
		var h = _grid.Height;
		var map = new float[w, h];
		for (var x = 0; x < w; x++)
		for (var y = 0; y < h; y++)
			map[x, y] = (float)_engine.HappinessSystem.GetNeglect(x, y);
		_renderer.SetNeglectMap(map);
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

		// Zone counts for TopBar
		var resZones = System.Linq.Enumerable.Count(_grid.AllTiles(), t => t.Zone == Loopolis.Core.Grid.ZoneType.Residential);
		var comZones = System.Linq.Enumerable.Count(_grid.AllTiles(), t => t.Zone == Loopolis.Core.Grid.ZoneType.Commercial);
		var indZones = System.Linq.Enumerable.Count(_grid.AllTiles(), t => t.Zone == Loopolis.Core.Grid.ZoneType.Industrial);

		var gameStateName = _engine.MilestoneSystem.CurrentState.ToString();

		// Compute next milestone inline
		var currentPop = _population.Population;
		var nextM = System.Array.Find(MilestoneThresholds, m => m.Target > currentPop);
		var nextMilestoneName   = nextM != default ? $"{nextM.Name} {nextM.Emoji}" : null;
		var nextMilestoneTarget = nextM != default ? nextM.Target : 0;

		var activeEvent = _engine.EventSystem.ActiveEvent;

		// ── Diagnostic fields for CityHealthPanel ───────────────────────────

		// Coverage summary — use engine's LastServiceCoverage (road-graph distance, G4 capacity data).
		// This is computed each tick and has correct radii and capacity totals (School seats, Police, etc.)
		// Falls back to an empty summary on the very first tick before propagation runs.
		var zonedTiles = System.Linq.Enumerable.ToList(
			System.Linq.Enumerable.Where(_grid.AllTiles(),
				t => t.Zone is Loopolis.Core.Grid.ZoneType.Residential
						   or Loopolis.Core.Grid.ZoneType.Commercial
						   or Loopolis.Core.Grid.ZoneType.Industrial));
		var poweredZoned   = System.Linq.Enumerable.Count(zonedTiles, t => t.HasPower);
		var unpoweredZoned = zonedTiles.Count - poweredZoned;

		var sc = _engine.LastServiceCoverage ?? Loopolis.Core.Simulation.ServiceCoverageResult.Empty;
		var coverageSummary = new CoverageSummaryDto(
			PoweredZonedTilesCount:   poweredZoned,
			UnpoweredZonedTilesCount: unpoweredZoned,
			PoliceCoveragePercent:    sc.PoliceCoveragePercent,
			FireCoveragePercent:      sc.FireCoveragePercent,
			SchoolCoveragePercent:    sc.SchoolCoveragePercent,
			HospitalCoveragePercent:  sc.HospitalCoveragePercent,
			AvgPollution:             0.0,
			AvgHappiness:             0.0,
			SchoolSeatsUsed:          sc.SchoolSeatsUsed,
			SchoolSeatsTotal:         sc.SchoolSeatsTotal,
			PoliceCapacityUsed:       sc.PoliceCapacityUsed,
			PoliceCapacityTotal:      sc.PoliceCapacityTotal,
			FireCapacityUsed:         sc.FireCapacityUsed,
			FireCapacityTotal:        sc.FireCapacityTotal,
			HospitalBedsUsed:         sc.HospitalBedsUsed,
			HospitalBedsTotal:        sc.HospitalBedsTotal
		);

		// Happiness breakdown
		var happinessBreakdown = new HappinessBreakdownDto(
			ServiceCoverage:     0.0,  // simplified — Runner does full per-tile average
			TaxModifier:         _budget.TaxModifier,
			UnemploymentPenalty: 0.0,
			EventPenalty:        _engine.EventSystem.HappinessPenalty,
			NeglectDecay:        0.0
		);

		// Employment
		var unemploymentRate = 1.0 - _engine.EmploymentSystem.EmploymentRatio;
		var employmentDto = new EmploymentDto(
			Jobs:             _engine.EmploymentSystem.AvailableJobs,
			Workers:          _engine.EmploymentSystem.RequiredJobs,
			UnemploymentRate: unemploymentRate
		);

		// PauseReason: derive from game state for standalone (Runner sets this explicitly)
		string? pauseReason = gameStateName switch
		{
			"BankruptcyWarning" => "BankruptcyWarning",
			"AbandonmentWarning" => "AbandonmentWarning",
			_ => null
		};

		// Power capacity summary for HUD
		var pcs = _engine.PowerCapacitySystem;
		var powerState = new PowerStateDto(
			SupplyMW:      pcs.TotalSupplyMW,
			DemandMW:      pcs.TotalDemandMW,
			CapacityRatio: pcs.CapacityRatio,
			IsBrownout:    pcs.IsBrownout);

		// ────────────────────────────────────────────────────────────────────

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
			HasPowerPlant:             _grid.AllTiles().Any(t =>
				t.Zone == Loopolis.Core.Grid.ZoneType.CoalPlant ||
				t.Zone == Loopolis.Core.Grid.ZoneType.NuclearPlant ||
				t.Zone == Loopolis.Core.Grid.ZoneType.PowerPlant),
			NextMilestoneName:         nextMilestoneName,
			NextMilestoneTarget:       nextMilestoneTarget,
			ActiveEventName:           activeEvent?.Name,
			ActiveEventDescription:    activeEvent?.Description,
			LatestEventBanner:         _engine.LatestEventBanner,
			TaxModifier:               _budget.TaxModifier,
			AvailableJobs:             _engine.EmploymentSystem.AvailableJobs,
			RequiredJobs:              _engine.EmploymentSystem.RequiredJobs,
			EmploymentRatio:           _engine.EmploymentSystem.EmploymentRatio,
			EventHappinessPenalty:     _engine.EventSystem.HappinessPenalty,
			HappinessBreakdown:        happinessBreakdown,
			Employment:                employmentDto,
			CoverageSummary:           coverageSummary,
			PauseReason:               pauseReason,
			Power:                     powerState,
			ResZones:                  resZones,
			ComZones:                  comZones,
			IndZones:                  indZones
		);
		_lastState = state;
		_hud.UpdateStats(state);
		_topBar.UpdateStats(state);
		_hintOverlay.UpdateHints(state);
		_cityHealth.UpdateWarnings(state);
		_renderer.SetBrownout(pcs.IsBrownout);
		_renderer.SetFireTile(_engine.EventSystem.FireTileX, _engine.EventSystem.FireTileY);
		_toolbar.UpdateMilestoneLocks(_population.Population);
		UpdateEventLog(state, milestone);

		// ── Toast routing ──────────────────────────────────────────────────────

		// Brownout alert (deduplicated — cleared when brownout resolves)
		if (pcs.IsBrownout)
		{
			if (!_brownoutToastShown)
			{
				_brownoutToastShown = true;
				_toastSystem.AddAlert("⚡ Power brownout — add more power plants");
			}
		}
		else
		{
			_brownoutToastShown = false;
		}

		// Employment gap alert (deduplicated)
		if (_engine.EmploymentSystem.EmploymentRatio < 0.7 && _population.Population > 50)
		{
			if (!_employmentToastShown)
			{
				_employmentToastShown = true;
				_toastSystem.AddAlert("⚠ Jobs shortage — build more Industrial zones");
			}
		}
		else
		{
			_employmentToastShown = false;
		}

		// Milestone toast
		if (!string.IsNullOrEmpty(milestone) && milestone != _lastLoggedMilestone)
			_toastSystem.AddMilestone($"🏆 {milestone} reached!");

		// Event banner toast
		if (!string.IsNullOrEmpty(state.LatestEventBanner) && state.LatestEventBanner != _lastLoggedBanner)
			_toastSystem.AddEvent(state.LatestEventBanner);

		// Tutorial hints
		UpdateTutorialHint(state);
	}

	private void UpdateTutorialHint(SharedState state)
	{
		if (_tutorialHintIndex >= TutorialHints.Length) return;
		var shouldShow = _tutorialHintIndex switch
		{
			0 => state.Tick > 10,
			1 => state.Population > 5,
			2 => state.Happiness < 0.7 || state.Tick > 150,
			3 => state.Tick > 300,
			_ => false
		};
		if (shouldShow)
		{
			_toastSystem.AddHint(TutorialHints[_tutorialHintIndex]);
			_tutorialHintIndex++;
		}
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
