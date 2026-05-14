using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Loopolis.Core.Grid;
using Loopolis.Core.Persistence;
using Loopolis.Core.Policies;
using Loopolis.Core.Scenarios;
using Loopolis.Core.Simulation;

namespace LoopolisGodot;

public partial class World : Node2D
{
	/// <summary>
	/// Set before loading World.tscn to start a specific scenario in standalone mode.
	/// Consumed (reset to null) when SetupStandaloneSimulation runs.
	/// </summary>
	public static string? PendingScenarioId { get; set; } = null;

	/// <summary>
	/// Player-chosen city name. Set from MainMenu before loading World.tscn.
	/// Defaults to "My City" if the player leaves the name field empty.
	/// </summary>
	public static string CityName { get; set; } = "My City";

	/// <summary>
	/// True when the Upgrade tool is active (G key or Toolbar button).
	/// Read by TilemapRenderer to draw gold highlights on upgradeable buildings.
	/// </summary>
	public static bool UpgradeToolActive { get; private set; } = false;
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
	private Minimap _minimap = null!;
	private AudioSystem _audio = null!;
	private ShortcutsPanel _shortcutsPanel = null!;
	private PolicyPanel _policyPanel = null!;
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

	// Upgrade tool: track last result seen from server (viewer mode) to avoid re-showing
	private string? _lastViewerUpgradeResult;

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
	private bool _employmentWarnedLow = false;     // warned when employment ratio < 0.40
	private bool _happinessWarnedLow = false;      // warned at <40%
	private bool _happinessWarnedCritical = false; // warned at <30% (close to abandonment)

	// Scenario completion tracking (standalone mode — fires overlay once)
	private bool _scenarioCompleteFired = false;
	private bool _scenarioFailedFired   = false;
	private ScenarioResultPanel? _scenarioResultPanel;

	// Event response panel
	private EventResponsePanel _eventResponsePanel = null!;
	private string? _lastShownEventType; // track which event we showed so we don't re-show after dismiss

	// Tutorial hint progression (passive hints — separate from the guided tutorial)
	private int _tutorialHintIndex = 0;
	private static readonly string[] TutorialHints =
	{
		"💡 Zone homes along roads to attract residents. Power unlocks bigger buildings.",
		"💡 Connect a Coal Plant with Power Lines — powered zones grow faster.",
		"💡 Build Fire Station, Police, and School — happiness keeps your city growing.",
		"💡 Milestones: 500 pop = Town · 5k = City · 25k = Metropolis · 100k = Loopolis"
	};

	// ── Guided tutorial state machine ──────────────────────────────────────
	private bool _tutorialActive   = false;
	private int  _tutorialStep     = 0;  // 1-5; 0 = not started
	private TutorialPanel _tutorialPanel = null!;
	private bool _tutorialStep1Done = false; // road placed
	private bool _tutorialStep2Done = false; // 2+ R zones adjacent to road
	private bool _tutorialStep3Done = false; // coal/power plant placed
	private bool _tutorialStep4Done = false; // at least 1 R zone tile HasPower
	private bool _tutorialStep5Done = false; // first building appeared
	private float _tutorialStepFlashTimer = 0f; // brief pause after CompleteStep before advancing

	// Server process tracking (static — survives scene changes)
	private static long _serverPid = -1;

	public static void SetServerPid(long pid) { _serverPid = pid; }

	// Static instance reference — needed for Toolbar to call TogglePolicies() without a direct reference
	private static World? _instance;

	/// <summary>
	/// Toggles the Upgrade tool on/off.
	/// When activating: selects the Upgrade zone in the toolbar (triggers build-mode pause).
	/// When deactivating: deselects all (triggers resume).
	/// </summary>
	private void ToggleUpgradeTool()
	{
		if (UpgradeToolActive)
		{
			// Deactivate: deselect the upgrade tool
			UpgradeToolActive = false;
			_toolbar.DeselectAll();
			_renderer.QueueRedraw();
		}
		else
		{
			// Activate upgrade tool — deselect any existing zone first
			_toolbar.ShowZonesTab();
			_toolbar.SelectZone("Upgrade");
			UpgradeToolActive = true;
			_renderer.QueueRedraw();
			_audio?.PlayUpgradeActivated();
		}
	}

	/// <summary>Toggles the policy panel open/closed. Called by Toolbar's Policies button.</summary>
	public static void TogglePolicies()
	{
		if (_instance == null) return;
		if (_instance._policyPanel.IsVisible)
			_instance._policyPanel.Hide();
		else
		{
			_instance._policyPanel.Show();
			_instance._policyPanel.Update(
				!_instance._viewerMode,
				_instance._engine,
				_instance._reader?.LastState,
				_instance._reader?.SessionId);
		}
	}

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
		_instance = this;
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

		// Minimap — bottom-right corner, press M to toggle
		_minimap = new Minimap();
		AddChild(_minimap);
		_minimap.SetCamera(_camera);

		// Audio system — procedural sound, zero external assets
		_audio = new AudioSystem();
		AddChild(_audio);

		// Tutorial panel (guided 5-step tutorial)
		_tutorialPanel = new TutorialPanel();
		AddChild(_tutorialPanel);

		// Scenario result panel (shown on complete/failed)
		_scenarioResultPanel = new ScenarioResultPanel();
		_scenarioResultPanel.PlayAgainRequested += (scenarioId) =>
		{
			PendingScenarioId = scenarioId;
			GetTree().ReloadCurrentScene();
		};
		_scenarioResultPanel.MainMenuRequested += () =>
		{
			KillServerIfRunning();
			GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
		};
		AddChild(_scenarioResultPanel);

		// Keyboard shortcuts panel (press '?' to toggle)
		_shortcutsPanel = new ShortcutsPanel();
		AddChild(_shortcutsPanel);

		// Policy panel (press 'O' to toggle)
		_policyPanel = new PolicyPanel();
		AddChild(_policyPanel);

		// Event response panel (layer 12 — shown when a crisis event fires)
		_eventResponsePanel = new EventResponsePanel();
		_eventResponsePanel.InterveneRequested += OnEventInterveneRequested;
		AddChild(_eventResponsePanel);

		// Wire toolbar signals
		_toolbar.ZoneSelected   += OnZoneSelected;
		_toolbar.PauseToggled   += OnPauseToggled;
		_toolbar.TaxRateChanged += OnTaxRateChanged;
		_toolbar.SpeedChanged   += OnSpeedChanged;
		_toolbar.StatsToggled   += () => _hud.Toggle();
		_toolbar.OverlayChanged += mode => ToggleOverlay((OverlayMode)mode);

		// Wire TopBar hamburger signals
		_topBar.NewGameRequested  += OnNewGameRequested;
		_topBar.MainMenuRequested += () =>
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
			_reader.BuildingBorn      += (typeId, ax, ay) =>
			{
				SpawnBuildingBirthLabel(typeId, ax, ay);
				// Spawn animation: look up building dimensions from last state
				var bInfo = _reader?.LastState?.Buildings;
				if (bInfo != null)
				{
					foreach (var b in bInfo)
					{
						if (b.X == ax && b.Y == ay && b.TypeId == typeId)
						{
							_renderer.AnimateBuildingSpawn(new Vector2I(ax, ay), b.Width, b.Height);
							break;
						}
					}
				}
			};
			_reader.BuildingDegraded  += (typeId, ax, ay) => SpawnBuildingCrumbleLabel(typeId, ax, ay);
			_reader.BuildingsBorn     += (typeIds, tick)  => FireBuildingBirthToasts(typeIds, tick);
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
		// Consume pending scenario selection (set by MainMenu before loading World.tscn)
		ScenarioDefinition? scenario = null;
		var pendingId = PendingScenarioId; // capture before consuming
		if (PendingScenarioId != null)
		{
			scenario = ScenarioLibrary.Find(PendingScenarioId);
			PendingScenarioId = null; // consume it
		}

		var mapSize         = scenario != null ? scenario.MapWidth  : StandaloneMapSize;
		var mapHeight       = scenario != null ? scenario.MapHeight : StandaloneMapSize;
		var startingBalance = scenario != null ? (double)scenario.StartingBalance : 4_000.0;

		_grid        = new CityGrid(mapSize, mapHeight);
		_terrainSeed = (int)(GD.Randi() % int.MaxValue);

		_budget     = new BudgetSystem(startingBalance);
		_population = new PopulationSystem();
		var power   = new PowerNetwork();
		var roads   = new RoadNetwork();
		var demand  = new DemandSystem();

		_engine   = new SimulationEngine(_grid, _budget, _population, power, roads, demand);
		_gameOver = false;

		// Attach scenario to engine so ScenarioEngine.CheckCompletion runs per tick
		if (scenario != null)
		{
			_engine.ActiveScenario = scenario;
			// Grey out zones that are disabled by this scenario
			_toolbar.UpdateDisabledZones(scenario.DisabledZones?.Select(z => z.ToString()).ToList());
		}
		else
		{
			_toolbar.UpdateDisabledZones(null);
		}

		// Reset scenario overlay tracking
		_scenarioCompleteFired = false;
		_scenarioFailedFired   = false;

		// Reset tutorial state
		_tutorialActive       = false;
		_tutorialStep         = 0;
		_tutorialStep1Done    = false;
		_tutorialStep2Done    = false;
		_tutorialStep3Done    = false;
		_tutorialStep4Done    = false;
		_tutorialStep5Done    = false;
		_tutorialStepFlashTimer = 0f;
		_tutorialPanel?.HideTutorial();

		SetupDefaultNewGame(mapSize, mapHeight);

		_renderer.Refresh(_grid);
		_minimap.UpdateFromGrid(_grid);
		_camera.FitToMap(mapSize, mapHeight);
		PushStandaloneHudUpdate();

		// Activate guided tutorial when the tutorial scenario is selected
		if (pendingId == "tutorial")
		{
			_tutorialActive   = true;
			_standalonePaused = true;
			_toolbar.SetPaused(true);
			AdvanceTutorial(1);
		}
	}

	/// <summary>
	/// Initialises a new standalone game with procedural terrain, a border connection
	/// at the centre of the south edge, and three starter road tiles extending north
	/// from it.  No power plant, no pre-placed zones.
	/// </summary>
	private void SetupDefaultNewGame(int mapW = StandaloneMapSize, int mapH = StandaloneMapSize)
	{
		// Procedural terrain — hills, water, forests based on random seed
		GenerateTerrain(_grid, _terrainSeed);

		// Ensure south-centre has land for border road + starter roads
		var bx = mapW / 2;
		for (var lx = bx - 2; lx <= bx + 2; lx++)
		for (var ly = mapH - 8; ly < mapH; ly++)
		{
			_grid.SetHeightLevel(lx, ly, 1);
			_grid.SetForest(lx, ly, false);
		}

		// Border connection — centre of south edge, unerasable Regional Highway
		_grid.PlaceBorderConnection(mapW / 2, mapH - 1);

		// 3 starter road tiles extending north from the border
		_grid.SetZone(mapW / 2, mapH - 2, ZoneType.Road);
		_grid.SetZone(mapW / 2, mapH - 3, ZoneType.Road);
		_grid.SetZone(mapW / 2, mapH - 4, ZoneType.Road);

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

		if (_viewerMode)
		{
			// Update minimap from the latest viewer grid (reader polls at 20 Hz; we update every frame
			// but minimap skips redraw when nothing changed via its own _Process QueueRedraw gate)
			var viewerGrid  = _reader?.LastGrid;
			var viewerState = _reader?.LastState;
			if (viewerGrid != null && viewerState != null)
				_minimap.UpdateFromState(viewerState, viewerGrid);

			// Keep policy panel live while open (server mode)
			if (_policyPanel.IsVisible)
				_policyPanel.Update(false, null, _reader?.LastState, _reader?.SessionId);

			// Poll for upgrade results from server
			PollViewerUpgradeResult();

			// Event response panel (viewer mode) — check latest server state
			var viewerStateCopy = _reader?.LastState;
			if (viewerStateCopy != null)
			{
				if (!string.IsNullOrEmpty(viewerStateCopy.PendingEventType) &&
				    _lastShownEventType != viewerStateCopy.PendingEventType)
				{
					_lastShownEventType = viewerStateCopy.PendingEventType;
					var canAfford = viewerStateCopy.Balance >= viewerStateCopy.PendingEventCost;
					_eventResponsePanel.ShowEvent(viewerStateCopy.PendingEventType, viewerStateCopy.PendingEventCost, canAfford);
					_audio?.PlayEventAlert();
				}
				else if (string.IsNullOrEmpty(viewerStateCopy.PendingEventType))
				{
					_lastShownEventType = null;
					_eventResponsePanel.Hide();
				}
			}

			return;
		}

		// Keep policy panel live while open (standalone mode)
		if (_policyPanel.IsVisible)
			_policyPanel.Update(true, _engine, null, null);

		// Tutorial: advance to next step after brief flash delay
		if (_tutorialActive && _tutorialStepFlashTimer > 0f)
		{
			_tutorialStepFlashTimer -= (float)delta;
			if (_tutorialStepFlashTimer <= 0f)
				AdvanceTutorialDelayed();
		}

		// Event response panel (standalone mode) — runs every frame so panel stays responsive
		if (_engine != null)
		{
			if (_engine.HasPendingEvent && _lastShownEventType != _engine.PendingEventType)
			{
				_lastShownEventType = _engine.PendingEventType;
				var canAfford = _engine.Budget.Balance >= _engine.PendingEventCost;
				_eventResponsePanel.ShowEvent(_engine.PendingEventType!, _engine.PendingEventCost, canAfford);
				_audio?.PlayEventAlert();
			}
			else if (!_engine.HasPendingEvent)
			{
				_lastShownEventType = null;
				_eventResponsePanel.Hide();
			}
		}

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
			var newBuildingTypeIdsThisTick = new System.Collections.Generic.List<string>();
			foreach (var kvp in _grid.Buildings)
			{
				if (!priorBuildingIds.Contains(kvp.Key) && _standaloneKnownBuildingIds.Add(kvp.Key))
				{
					SpawnBuildingBirthLabel(kvp.Value.TypeId, kvp.Value.AnchorX, kvp.Value.AnchorY);
					newBuildingTypeIdsThisTick.Add(kvp.Value.TypeId);
					// Spawn animation: building scales in from 0 → overshoot → 1.0
					_renderer.AnimateBuildingSpawn(
						new Vector2I(kvp.Value.AnchorX, kvp.Value.AnchorY),
						kvp.Value.Width, kvp.Value.Height);
				}
			}
			if (newBuildingTypeIdsThisTick.Count > 0)
				FireBuildingBirthToasts(newBuildingTypeIdsThisTick, _standaloneTick);

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
			_minimap.UpdateFromGrid(_grid);
			UpdateNeglectMap();
			PushStandaloneHudUpdate();

			// Tutorial step 5: first building birth (checked per tick when tutorial is active)
			if (_tutorialActive && _tutorialStep == 5 && newBuildingTypeIdsThisTick.Count > 0)
				CheckTutorialProgress();

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

			// Scenario complete (edge: fires once when ScenarioComplete first becomes true)
			if (_engine.ScenarioComplete && !_scenarioCompleteFired && !_gameOver)
			{
				_scenarioCompleteFired = true;
				_standalonePaused = true;
				_toolbar.SetPaused(true);
				_scenarioResultPanel?.ShowComplete(
					scenarioName:  _engine.ActiveScenario!.Name,
					medal:         _engine.MedalEarned ?? "Bronze",
					population:    _population!.Population,
					targetPop:     _engine.ActiveScenario.Goal.TargetPopulation,
					ticksUsed:     _standaloneTick,
					activeScenarioId: _engine.ActiveScenario.Id);
				// Save personal best to leaderboard
				try
				{
					LeaderboardSystem.Save(
						_engine.ActiveScenario.Id,
						_engine.MedalEarned ?? "Bronze",
						_standaloneTick,
						_population!.Population,
						GetLeaderboardPath());
				}
				catch { /* non-critical */ }
			}

			// Scenario failed (edge: fires once when ScenarioFailed first becomes true)
			if (_engine.ScenarioFailed && !_scenarioFailedFired && !_gameOver)
			{
				_scenarioFailedFired = true;
				_standalonePaused = true;
				_toolbar.SetPaused(true);
				_scenarioResultPanel?.ShowFailed(
					scenarioName: _engine.ActiveScenario!.Name,
					population:   _population!.Population,
					targetPop:    _engine.ActiveScenario.Goal.TargetPopulation,
					activeScenarioId: _engine.ActiveScenario.Id);
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
			var bldgHeight = _renderer.GetTileHeight(tileX, tileY);
			var bldgForest = _renderer.GetTileForest(tileX, tileY);
			_tooltip.ShowForBuilding(building, totalPop, anchorTile, GetViewport().GetMousePosition(), currentState, bldgHeight, bldgForest);
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
				// Upgrade tool: single-click only — no rectangle painting
				if (UpgradeToolActive)
				{
					if (mb.Pressed)
						HandlePlaceTile(GetTileUnderMouse());
					return;
				}

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

			// Tab switches: Z = Zones, S = Services, U = Utilities, X = Overlays
			// (New sidebar: 0=Zones, 1=Services, 2=Utilities, 3=Overlays)
			// Guard against Ctrl combinations (Ctrl+S = Save)
			if (!key.CtrlPressed)
			{
				if (key.Keycode == Key.Z) { _toolbar.ShowZonesTab();     return; }
				if (key.Keycode == Key.S) { _toolbar.ShowServicesTab();  return; }
				if (key.Keycode == Key.U) { _toolbar.ShowUtilitiesTab(); return; }
				if (key.Keycode == Key.X) { _toolbar.ShowOverlaysTab();  return; }
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
				Key.P => "Park",
				Key.W => "Road",
				Key.A => "Avenue",
				Key.E => "Erase",
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

			// '?' (or Shift+/) toggles the keyboard shortcuts panel
			if (key.Keycode == Key.Question || (key.Keycode == Key.Slash && key.ShiftPressed))
			{
				if (_shortcutsPanel.IsVisible) _shortcutsPanel.Hide();
				else _shortcutsPanel.Show();
				GetViewport().SetInputAsHandled();
				return;
			}

			// 'O' toggles the city policies panel
			if (key.Keycode == Key.O)
			{
				if (_policyPanel.IsVisible)
					_policyPanel.Hide();
				else
				{
					_policyPanel.Show();
					_policyPanel.Update(!_viewerMode, _engine, _reader?.LastState, _reader?.SessionId);
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			// G — toggle Upgrade tool
			if (key.Keycode == Key.G && !key.CtrlPressed)
			{
				ToggleUpgradeTool();
				GetViewport().SetInputAsHandled();
				return;
			}

			// H — toggle HUD detail stats panel
			if (key.Keycode == Key.H)
			{
				_hud.Toggle();
				return;
			}

			// Escape: close policy panel, then shortcuts panel, then upgrade tool, then cancel tool
			if (key.Keycode == Key.Escape)
			{
				if (_policyPanel.IsVisible)
				{
					_policyPanel.Hide();
					GetViewport().SetInputAsHandled();
					return;
				}

				if (_shortcutsPanel.IsVisible)
				{
					_shortcutsPanel.Hide();
					GetViewport().SetInputAsHandled();
					return;
				}

				// Exit upgrade tool before other tool deselection
				if (UpgradeToolActive)
				{
					UpgradeToolActive = false;
					_toolbar.DeselectAll();
					_renderer.QueueRedraw();
					GetViewport().SetInputAsHandled();
					return;
				}

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
		// Play a soft click for every toolbar zone/tool selection
		_audio?.PlayClick();

		// If the upgrade tool was active and another tool/zone was selected, deactivate it
		if (UpgradeToolActive && zoneName != "Upgrade")
		{
			UpgradeToolActive = false;
			_renderer.QueueRedraw();
		}
		// If the upgrade tool button was selected
		if (zoneName == "Upgrade" && !UpgradeToolActive)
		{
			UpgradeToolActive = true;
			_renderer.QueueRedraw();
		}

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
			_scenarioResultPanel?.Hide();
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

	/// <summary>
	/// Called when the player presses "Intervene" on the EventResponsePanel.
	/// Standalone: calls engine.RespondToCurrentEvent() directly.
	/// Viewer: sends event_respond command to the running server.
	/// </summary>
	private void OnEventInterveneRequested()
	{
		if (_viewerMode)
		{
			var sid = _reader?.SessionId;
			if (sid != null)
				WriteCommand($"{{\"cmd\":\"event_respond\",\"sessionId\":\"{sid}\"}}");
			_audio?.PlayIntervene();
			// Show a generic toast — we don't know the cost in viewer mode until next state tick
			_toastSystem.AddToast("Crisis intervention requested!", new Color(1f, 0.72f, 0.18f), 5f);
		}
		else
		{
			var cost = _engine.PendingEventCost;
			var eventType = _engine.PendingEventType;
			var success = _engine.RespondToCurrentEvent();
			if (success)
			{
				_audio?.PlayIntervene();
				var label = eventType switch
				{
					"FireBreak"   => "Fire contained",
					"CrimeWave"   => "Crime suppressed",
					"PowerOutage" => "Grid restored",
					"DemandSlump" => "Businesses subsidised",
					_             => "Crisis resolved",
				};
				_toastSystem.AddToast($"{label}! (-${cost:N0})", new Color(1f, 0.72f, 0.18f), 6f);
				_eventLog?.AddEntry($"Player intervened: {label} (-${cost:N0})");
			}
			else
			{
				_toastSystem.AddToast("Cannot afford intervention!", new Color(0.9f, 0.3f, 0.2f), 4f);
			}
		}
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
			"Park"        => new Color(0.30f, 0.72f, 0.25f, 0.40f),
			"Erase"       => new Color(0.6f,  0.15f, 0.15f, 0.40f),
			_             => new Color(1f,    1f,    1f,    0.25f),
		};
	}

	private void HandlePlaceTile(Vector2I tilePos)
	{
		var tileX = tilePos.X;
		var tileY = tilePos.Y;

		// Upgrade tool takes priority over normal zone placement
		if (UpgradeToolActive)
		{
			HandleUpgradeTile(tileX, tileY);
			return;
		}

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

			// Scenario zone restriction check (viewer mode — server enforces too, but block early)
			if (selectedZone != "Erase" && _reader?.LastState?.DisabledZones != null)
			{
				if (_reader.LastState.DisabledZones.Contains(selectedZone))
				{
					_toastSystem.AddToast($"⛔ {selectedZone} zones are disabled in this scenario", new Color(1f, 0.5f, 0.2f), 3f);
					return;
				}
			}

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

			// Road pulse: white-flash confirmation on newly placed road/avenue tiles
			if (selectedZone is "Road" or "Avenue")
			{
				_renderer.PulseRoad(new Vector2I(tileX, tileY));
				_audio.PlayRoadPlaced();
			}
			else if (selectedZone == "Erase")
			{
				_audio.PlayErase();
			}
			else if (selectedZone is "Residential" or "Commercial" or "Industrial" or "Park"
			      or "PowerPlant" or "CoalPlant" or "NuclearPlant"
			      or "FireStation" or "PoliceStation" or "School" or "Hospital"
			      or "FireHQ" or "PoliceHQ")
			{
				_audio.PlayZonePlaced();
			}
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
				_audio.PlayErase();
			}
			else
			{
				// Scenario zone restriction check (standalone mode)
				if (System.Enum.TryParse<ZoneType>(selectedZone, out var checkZone) && !_engine.IsZoneAllowed(checkZone))
				{
					_toastSystem.AddToast($"⛔ {selectedZone} zones are disabled in this scenario", new Color(1f, 0.5f, 0.2f), 3f);
					return;
				}

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

					// Road pulse: white-flash confirmation on newly placed road/avenue tiles
					if (selectedZone is "Road" or "Avenue")
					{
						_renderer.PulseRoad(new Vector2I(tileX, tileY));
						_audio.PlayRoadPlaced();
					}
					else if (selectedZone is "Residential" or "Commercial" or "Industrial" or "Park"
					      or "PowerPlant" or "CoalPlant" or "NuclearPlant"
					      or "FireStation" or "PoliceStation" or "School" or "Hospital"
					      or "FireHQ" or "PoliceHQ")
					{
						_audio.PlayZonePlaced();
					}
				}
			}
			_renderer.Refresh(_grid);
			// Check tutorial progress after every tile placement (standalone mode)
			if (_tutorialActive) CheckTutorialProgress();
		}
	}

	/// <summary>
	/// Attempts a manual upgrade on the tile at (tileX, tileY).
	/// In standalone mode: calls ManualUpgradeSystem directly (stubbed until Core adds it).
	/// In viewer mode: writes a manual_upgrade command to the server.
	/// </summary>
	private void HandleUpgradeTile(int tileX, int tileY)
	{
		if (_viewerMode)
		{
			// Viewer mode: send upgrade command to server
			var sessionId = _reader?.SessionId;
			if (sessionId == null) return;
			var cmd = $"{{\"cmd\":\"manual_upgrade\",\"x\":{tileX},\"y\":{tileY},\"sessionId\":\"{sessionId}\"}}";
			WriteCommand(cmd);
		}
		else
		{
			// Standalone mode
			if (_grid == null || _engine == null) return;
			if (tileX < 0 || tileX >= _grid.Width || tileY < 0 || tileY >= _grid.Height) return;

			var result = _engine.ManualUpgrade(tileX, tileY);
			if (result.Success)
			{
				_renderer.Refresh(_grid);
				_toastSystem.AddToast($"💰 Upgraded to {GetFriendlyBuildingName(result.NewBuildingTypeId!)} (-${result.Cost:N0})", new Color(1f, 0.85f, 0.2f), 5f);
			}
			else
			{
				_toastSystem.AddToast($"Can't upgrade: {result.Reason}", new Color(0.9f, 0.4f, 0.2f), 3f);
			}
		}
	}

	/// <summary>
	/// Polls the viewer state for upgrade results and shows toasts when a new result arrives.
	/// Called from _Process in viewer mode.
	/// </summary>
	private void PollViewerUpgradeResult()
	{
		var state = _reader?.LastState;
		if (state?.LastUpgradeResult == null) return;

		var result = state.LastUpgradeResult;
		if (result == _lastViewerUpgradeResult) return;
		_lastViewerUpgradeResult = result;

		if (result.StartsWith("ok:"))
		{
			// Format: "ok:building_type_id:-cost"
			var parts = result.Split(':');
			if (parts.Length >= 3)
			{
				var typeId = parts[1];
				if (int.TryParse(parts[2], out var cost))
				{
					var name = GetFriendlyBuildingName(typeId);
					_toastSystem.AddToast($"Upgraded to {name} (-${System.Math.Abs(cost):N0})", new Color(1f, 0.85f, 0.2f), 5f);
				}
			}
		}
		else if (result.StartsWith("err:"))
		{
			var reason = result.Length > 4 ? result[4..] : "Unknown error";
			_toastSystem.AddToast($"Can't upgrade: {reason}", new Color(0.8f, 0.5f, 0.2f), 4f);
		}
	}

	/// <summary>
	/// Returns (cost, targetName) for a given building typeId if it can be manually upgraded,
	/// or null if the type is at max tier.
	/// Costs match the ManualUpgradeSystem on the Core side.
	/// </summary>
	private static (int Cost, string TargetName)? GetUpgradeInfoForType(string typeId) => typeId switch
	{
		"res_house_1x1"      => (600,   "Townhouse"),
		"res_townhouse_2x2"  => (2000,  "Apartment Block"),
		"res_apartment_4x4"  => (8000,  "Highrise"),
		"com_shop_1x1"       => (800,   "Strip Mall"),
		"com_strip_1x3"      => (2500,  "Shopping Centre"),
		"com_strip_3x1"      => (2500,  "Shopping Centre"),
		"com_shopping_3x3"   => (6000,  "Office Tower"),
		"ind_factory_1x1"    => (1000,  "Warehouse"),
		"ind_warehouse_2x2"  => (3000,  "Industrial Park"),
		"ind_mill_2x2"       => (2500,  "Industrial Park"),
		"ind_quarry_2x2"     => (2500,  "Industrial Park"),
		_                    => null,
	};

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
	/// Fires building-birth toast notifications for a batch of new building typeIds.
	/// Deduplicates: 3+ of the same type → "3× Name built!" instead of 3 toasts.
	/// Throttles: only shows res_house_1x1 toasts before tick 50; higher-tier always shown.
	/// </summary>
	private void FireBuildingBirthToasts(System.Collections.Generic.IEnumerable<string> typeIds, int currentTick)
	{
		// One chime per tick batch regardless of how many buildings spawned
		_audio.PlayBuildingBorn();

		// Count occurrences per typeId
		var counts = new System.Collections.Generic.Dictionary<string, int>();
		foreach (var id in typeIds)
		{
			if (!counts.ContainsKey(id)) counts[id] = 0;
			counts[id]++;
		}

		foreach (var kvp in counts)
		{
			var typeId = kvp.Key;
			var count  = kvp.Value;

			// Throttle: don't show cottage toasts after tick 50
			if (typeId == "res_house_1x1" && currentTick > 50) continue;

			var friendlyName = GetFriendlyBuildingName(typeId);
			var (emoji, color) = GetBuildingToastStyle(typeId);

			string text;
			if (count >= 3)
				text = $"{emoji} {count}\xd7 {friendlyName} built!";
			else if (count == 2)
				text = $"{emoji} {friendlyName} (×2) built!";
			else
				text = $"{emoji} {friendlyName} built!";

			_toastSystem.AddToast(text, color, 5f);
		}
	}

	/// <summary>Returns (emoji, color) for a building toast based on zone and typeId.</summary>
	private static (string emoji, Color color) GetBuildingToastStyle(string typeId)
	{
		if (typeId.StartsWith("res_"))
			return ("🏘", new Color(1f, 0.75f, 0.35f));
		if (typeId.StartsWith("com_"))
			return ("🏪", new Color(0.4f, 0.8f, 1f));
		// Industrial specialisations
		if (typeId == "ind_mill_2x2")
			return ("🪵", new Color(0.55f, 0.9f, 0.3f));
		if (typeId == "ind_quarry_2x2")
			return ("⛏", new Color(0.8f, 0.75f, 0.5f));
		return ("🏭", new Color(0.9f, 0.85f, 0.3f));
	}

	/// <summary>Maps a building typeId to a friendly display name.</summary>
	private static string GetFriendlyBuildingName(string typeId) => typeId switch
	{
		"res_house_1x1"      => "Cottage",
		"res_townhouse_2x2"  => "Townhouse",
		"res_villa_2x3"      => "Villa",
		"res_villa_3x2"      => "Villa",
		"res_apartment_4x4"  => "Apartment Block",
		"com_shop_1x1"       => "Shop",
		"com_strip_1x3"      => "Strip Mall",
		"com_strip_3x1"      => "Strip Mall",
		"com_shopping_3x3"   => "Shopping Center",
		"ind_factory_1x1"    => "Factory",
		"ind_warehouse_2x2"  => "Warehouse",
		"ind_park_4x2"       => "Industrial Park",
		"ind_park_2x4"       => "Industrial Park",
		"ind_mill_2x2"       => "Timber Mill",
		"ind_quarry_2x2"     => "Quarry",
		_                    => typeId
	};

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
			_minimap.UpdateFromGrid(_grid);
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
		var resZones  = System.Linq.Enumerable.Count(_grid.AllTiles(), t => t.Zone == Loopolis.Core.Grid.ZoneType.Residential);
		var comZones  = System.Linq.Enumerable.Count(_grid.AllTiles(), t => t.Zone == Loopolis.Core.Grid.ZoneType.Commercial);
		var indZones  = System.Linq.Enumerable.Count(_grid.AllTiles(), t => t.Zone == Loopolis.Core.Grid.ZoneType.Industrial);
		var parkTiles = System.Linq.Enumerable.Count(_grid.AllTiles(), t => t.Zone == Loopolis.Core.Grid.ZoneType.Park);

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
		string? pauseReason = _buildModePaused ? "BuildMode"
			: _standalonePaused ? "Paused"
			: gameStateName switch
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
			WorkingAge:                _population.Population,
			EmploymentRatio:           _engine.EmploymentSystem.EmploymentRatio,
			EmploymentWarning:         _engine.EmploymentSystem.EmploymentRatio < 0.40f && _population.Population > 50,
			RequiredJobs:              _engine.EmploymentSystem.RequiredJobs,
			EventHappinessPenalty:     _engine.EventSystem.HappinessPenalty,
			HappinessBreakdown:        happinessBreakdown,
			Employment:                employmentDto,
			CoverageSummary:           coverageSummary,
			PauseReason:               pauseReason,
			Power:                     powerState,
			ResZones:                  resZones,
			ComZones:                  comZones,
			IndZones:                  indZones,
			ParkTiles:                 parkTiles,
			ActiveScenarioId:          _engine.ActiveScenario?.Id,
			ActiveScenarioName:        _engine.ActiveScenario?.Name,
			ScenarioTargetPopulation:  _engine.ActiveScenario?.Goal.TargetPopulation ?? 0,
			ScenarioTickLimit:         _engine.ActiveScenario?.TickLimit ?? 0,
			ScenarioBronzeTick:        _engine.ActiveScenario?.Medals.Bronze ?? 0,
			ScenarioSilverTick:        _engine.ActiveScenario?.Medals.Silver ?? 0,
			ScenarioGoldTick:          _engine.ActiveScenario?.Medals.Gold   ?? 0,
			ScenarioComplete:          _engine.ScenarioComplete,
			MedalEarned:               _engine.MedalEarned,
			ScenarioFailed:            _engine.ScenarioFailed,
			PersonalBestMedal:         GetPersonalBestMedal(_engine.ActiveScenario?.Id),
			PersonalBestTick:          GetPersonalBestTick(_engine.ActiveScenario?.Id),
			PolicyGreenCity:           _engine.PolicySystem.IsActive(PolicyType.GreenCity),
			PolicyIndustrialHub:       _engine.PolicySystem.IsActive(PolicyType.IndustrialHub),
			PolicyCommercialBoost:     _engine.PolicySystem.IsActive(PolicyType.CommercialBoost),
			PolicyOpenCity:            _engine.PolicySystem.IsActive(PolicyType.OpenCity),
			PolicyTotalCostPerTick:    _engine.PolicySystem.GetCostPerTick()
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
		_audio.SetAmbientLevel(Math.Clamp((float)state.Population / 5000f, 0f, 1f));

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

		// Low employment warning: fires when ratio drops below 0.40, resets above 0.55
		if (state.EmploymentWarning && !_employmentWarnedLow)
		{
			_employmentWarnedLow = true;
			_toastSystem.AddAlert("⚠ Low employment — add industrial zones to resume growth");
		}
		if (_engine.EmploymentSystem.EmploymentRatio >= 0.55f) _employmentWarnedLow = false;

		// Happiness warnings (fire before abandonment kicks in at 25%)
		if (state.Happiness < 0.40 && !_happinessWarnedLow)
		{
			_happinessWarnedLow = true;
			_toastSystem.AddAlert($"⚠️ Happiness at {(int)(state.Happiness * 100)}% — reduce industry or add services");
		}
		if (state.Happiness >= 0.45) _happinessWarnedLow = false; // reset when recovered

		if (state.Happiness < 0.30 && !_happinessWarnedCritical)
		{
			_happinessWarnedCritical = true;
			_toastSystem.AddAlert($"🚨 Happiness critical ({(int)(state.Happiness * 100)}%) — city may be abandoned soon!");
		}
		if (state.Happiness >= 0.35) _happinessWarnedCritical = false;

		// Milestone toast
		if (!string.IsNullOrEmpty(milestone) && milestone != _lastLoggedMilestone)
		{
			_toastSystem.AddMilestone($"🏆 {milestone} reached!");
			_audio.PlayMilestone();
		}

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

	// ── Guided tutorial ───────────────────────────────────────────────────

	private static readonly string[] TutorialStepMessages =
	{
		"",  // index 0 unused
		"Step 1: Place a Road\nClick 'Road' in the toolbar and build north of the highway stub.",
		"Step 2: Zone Residential\nClick '🏠 Residential' and place 2+ zones next to your road.",
		"Step 3: Add Power\nOpen the Utilities tab (press U), then place a Coal Plant away from homes.",
		"Step 4: Connect Power Lines\nUse 'Pwr Line' to link the plant to your zones.",
		"Step 5: Watch it grow!\nPress Space or click Resume — wait for your first cottage!"
	};

	private void AdvanceTutorial(int nextStep)
	{
		_tutorialStep = nextStep;
		if (nextStep >= 1 && nextStep <= 5)
			_tutorialPanel.ShowStep(nextStep, TutorialStepMessages[nextStep]);

		// Step 5: auto-unpause so the simulation runs and buildings can spawn
		if (nextStep == 5)
		{
			_standalonePaused = false;
			_toolbar.SetPaused(false);
		}
	}

	/// <summary>
	/// Called from _Process after the flash timer expires.  Figures out which step
	/// to move to based on what has already been completed.
	/// </summary>
	private void AdvanceTutorialDelayed()
	{
		if (!_tutorialStep1Done)          { AdvanceTutorial(1); return; }
		if (!_tutorialStep2Done)          { AdvanceTutorial(2); return; }
		if (!_tutorialStep3Done)          { AdvanceTutorial(3); return; }
		if (!_tutorialStep4Done)          { AdvanceTutorial(4); return; }
		if (!_tutorialStep5Done)          { AdvanceTutorial(5); return; }
		CompleteTutorial();
	}

	/// <summary>
	/// Inspects the current grid / engine state and advances the tutorial when
	/// a step's completion condition is satisfied.  Safe to call every frame / every
	/// tile-placement — guards against re-triggering already-completed steps.
	/// </summary>
	private void CheckTutorialProgress()
	{
		if (!_tutorialActive) return;

		// Step 1: road placed
		if (_tutorialStep == 1 && !_tutorialStep1Done)
		{
			var hasRoad = _grid.AllTiles().Any(t =>
				t.Zone == Loopolis.Core.Grid.ZoneType.Road ||
				t.Zone == Loopolis.Core.Grid.ZoneType.Avenue);
			if (hasRoad)
			{
				_tutorialStep1Done = true;
				_tutorialPanel.CompleteStep();
				_tutorialStepFlashTimer = 1.2f; // brief delay before advancing
				return;
			}
		}

		// Step 2: 2+ residential zone tiles placed
		if (_tutorialStep == 2 && !_tutorialStep2Done)
		{
			var resCount = _grid.AllTiles().Count(t => t.Zone == Loopolis.Core.Grid.ZoneType.Residential);
			if (resCount >= 2)
			{
				_tutorialStep2Done = true;
				_tutorialPanel.CompleteStep();
				_tutorialStepFlashTimer = 1.2f;
				return;
			}
		}

		// Step 3: coal/nuclear/power plant placed
		if (_tutorialStep == 3 && !_tutorialStep3Done)
		{
			var hasPlant = _grid.AllTiles().Any(t =>
				t.Zone == Loopolis.Core.Grid.ZoneType.CoalPlant ||
				t.Zone == Loopolis.Core.Grid.ZoneType.NuclearPlant ||
				t.Zone == Loopolis.Core.Grid.ZoneType.PowerPlant);
			if (hasPlant)
			{
				_tutorialStep3Done = true;
				_tutorialPanel.CompleteStep();
				_tutorialStepFlashTimer = 1.2f;
				return;
			}
		}

		// Step 4: at least 1 residential zone tile HasPower
		if (_tutorialStep == 4 && !_tutorialStep4Done)
		{
			// Re-propagate power network so the check reflects the current layout
			// even when paused in build mode.
			_engine.PowerNetwork.Propagate(_grid);
			var anyPowered = _grid.AllTiles().Any(t =>
				t.Zone == Loopolis.Core.Grid.ZoneType.Residential && t.HasPower);
			if (anyPowered)
			{
				_tutorialStep4Done = true;
				_tutorialPanel.CompleteStep();
				_tutorialStepFlashTimer = 1.2f;
				return;
			}
		}

		// Step 5: first building appears — checked after tick in _Process
		if (_tutorialStep == 5 && !_tutorialStep5Done)
		{
			var hasBuilding = _grid.Buildings.Count > 0;
			if (hasBuilding)
			{
				_tutorialStep5Done = true;
				CompleteTutorial();
			}
		}
	}

	private void CompleteTutorial()
	{
		_tutorialActive = false;
		_tutorialPanel.HideTutorial();
		_toastSystem.AddMilestone("Tutorial complete! Your city has started. Keep building!");
		Log("[tutorial] Completed — first building spawned!");
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

	// ── Leaderboard helpers ────────────────────────────────────────────────────

	/// <summary>
	/// Returns the path to leaderboard.json.
	/// Uses Godot's user data dir (writable on all platforms) with a fallback to godot/saves/.
	/// </summary>
	private static string GetLeaderboardPath()
	{
		try
		{
			var userDir = Godot.OS.GetUserDataDir();
			return System.IO.Path.Combine(userDir, "leaderboard.json");
		}
		catch
		{
			// Fallback: project-relative saves directory (editor / dev mode)
			var projectDir = Godot.ProjectSettings.GlobalizePath("res://");
			return System.IO.Path.Combine(projectDir, "saves", "leaderboard.json");
		}
	}

	/// <summary>Returns the personal best medal for the given scenario, or null if no entry exists.</summary>
	private static string? GetPersonalBestMedal(string? scenarioId)
	{
		if (string.IsNullOrEmpty(scenarioId)) return null;
		try
		{
			var entries = LeaderboardSystem.Load(GetLeaderboardPath());
			return entries.TryGetValue(scenarioId, out var entry) ? entry.Medal : null;
		}
		catch { return null; }
	}

	/// <summary>Returns the personal best tick for the given scenario, or 0 if no entry exists.</summary>
	private static int GetPersonalBestTick(string? scenarioId)
	{
		if (string.IsNullOrEmpty(scenarioId)) return 0;
		try
		{
			var entries = LeaderboardSystem.Load(GetLeaderboardPath());
			return entries.TryGetValue(scenarioId, out var entry) ? entry.Tick : 0;
		}
		catch { return 0; }
	}
}
