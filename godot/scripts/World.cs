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

	// City statistics panel (V key)
	private CityStatsPanel _statsPanel = null!;

	// Petition inbox panel (I key)
	private PetitionInboxPanel _petitionPanel = null!;

	// Event response panel
	private EventResponsePanel _eventResponsePanel = null!;

	// Charter choice panel — shown once when Town milestone fires
	private CharterChoicePanel? _charterPanel;

	// Building info panel — click any building to inspect it
	private BuildingInfoPanel _buildingInfoPanel = null!;

	// Tutorial hint progression (passive hints — separate from the guided tutorial)
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

	// Static instance reference — needed for Toolbar to call TogglePolicies() without a direct reference
	private static World? _instance;

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
		// Name must be set explicitly so SharedStateReader can find it via "/root/World/TopBar"
		_topBar = new TopBar();
		_topBar.Name = "TopBar";
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
		// Named explicitly so SharedStateReader can find it via "/root/World/ScenarioResultPanel"
		_scenarioResultPanel = new ScenarioResultPanel();
		_scenarioResultPanel.Name = "ScenarioResultPanel";
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

		// City statistics panel (press 'V' to toggle)
		_statsPanel = new CityStatsPanel();
		AddChild(_statsPanel);

		// Petition inbox panel (press 'I' to toggle)
		_petitionPanel = new PetitionInboxPanel();
		AddChild(_petitionPanel);

		// Event response panel (layer 12 — shown when a crisis event fires)
		_eventResponsePanel = new EventResponsePanel();
		_eventResponsePanel.InterveneRequested += OnEventInterveneRequested;
		AddChild(_eventResponsePanel);

		// Charter choice panel (layer 15 — shown once at Town milestone; created on demand)
		// Instantiated lazily in _Process when TownCharterPending first becomes true.

		// Building info panel (layer 12 — floats near the clicked building)
		_buildingInfoPanel = new BuildingInfoPanel();
		_buildingInfoPanel.RenovateRequested += OnRenovateServiceRequested;
		AddChild(_buildingInfoPanel);

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

		// If the user explicitly launched a new game from MainMenu (PendingScenarioId is set),
		// always run standalone — never attach to a lingering server from a previous session.
		var forceStandalone = PendingScenarioId != null;

		// Clean up stale state files (older than 5 seconds) left by previous sessions
		if (Directory.Exists(_sharedDir) && !forceStandalone)
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

		// Check for a live state file (written within the last 2 seconds) to enter viewer mode.
		// Skipped when user starts a fresh game from the MainMenu.
		var liveStateFile = forceStandalone ? null : FindLiveStateFile(_sharedDir);
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
			_reader.PetitionsThisTick += state            => FirePetitionToasts(state);
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
		// Sync initial zoom so icon mode threshold is correct from the first frame
		_renderer.SetCameraZoom(_camera.Zoom.X);
		_lastCameraZoom = _camera.Zoom.X;
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

	// Last camera zoom value tracked for icon-mode switching
	private float _lastCameraZoom = 1.0f;

	public override void _Process(double delta)
	{
		// Tooltip always updates (both modes)
		UpdateTooltip();

		// Coverage radius overlay: update whenever mouse moves to a new tile
		UpdateCoverageHighlight();

		// Sync camera zoom to renderer so icon mode can activate at ≤0.5×
		var cameraZoom = _camera.Zoom.X;
		if (!Mathf.IsEqualApprox(cameraZoom, _lastCameraZoom))
		{
			_lastCameraZoom = cameraZoom;
			_renderer.SetCameraZoom(cameraZoom);
		}

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

			// Keep stats panel live while open (server mode)
			if (_statsPanel.IsVisible && _reader?.LastState != null)
				_statsPanel.UpdateFromState(_reader.LastState);

			// Keep petition panel live while open (server mode)
			if (_petitionPanel.IsVisible && _reader?.LastState != null)
				_petitionPanel.UpdatePetitions(_reader.LastState.ActivePetitions, _reader.LastState.Tick);

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

				// Charter choice panel (viewer mode)
				if (viewerStateCopy.TownCharterPending && _charterPanel == null)
				{
					_charterPanel = new CharterChoicePanel();
					_charterPanel.CharterSelected += OnCharterSelected;
					AddChild(_charterPanel);
					_charterPanel.Show();
				}
				else if (!viewerStateCopy.TownCharterPending && _charterPanel != null)
				{
					// Charter chosen (possibly from another client or the server auto-selected)
					_charterPanel.QueueFree();
					_charterPanel = null;
				}

				// Service fatigue advisory toast (throttled to once per 200 ticks)
				FireServiceFatigueToastIfNeeded(viewerStateCopy);
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
				_engine!.Tick();
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[Loopolis] Tick crash at tick {_engine!.TickCount}: {ex.GetType().Name}: {ex.Message}");
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
					activeScenarioId: _engine.ActiveScenario!.Id);
				// Save personal best to leaderboard
				try
				{
					LeaderboardSystem.Save(
						_engine.ActiveScenario!.Id,
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

			// Charter choice panel (standalone mode) — show once when Town milestone reached
			if (_engine.Charters.TownCharterPending && _charterPanel == null)
			{
				_charterPanel = new CharterChoicePanel();
				_charterPanel.CharterSelected += OnCharterSelected;
				AddChild(_charterPanel);
				_charterPanel.Show();
			}
			else if (!_engine.Charters.TownCharterPending && _charterPanel != null)
			{
				_charterPanel.QueueFree();
				_charterPanel = null;
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

	// ── Toolbar signal handlers (pause / new game / tax / speed) ──────────

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
			PolicyTotalCostPerTick:    _engine.PolicySystem.GetCostPerTick(),
			TownCharterPending:        _engine.Charters.TownCharterPending,
			ActiveCharter:             _engine.Charters.ActiveCharter == Loopolis.Core.Charters.CharterType.None
										   ? null
										   : _engine.Charters.ActiveCharter.ToString(),
			ActiveCharterDescription:  _engine.Charters.ActiveCharter == Loopolis.Core.Charters.CharterType.None
										   ? null
										   : Loopolis.Core.Charters.CharterLibrary.Find(_engine.Charters.ActiveCharter)?.Effect
		);
		_lastState = state;
		_hud.UpdateStats(state);
		_topBar.UpdateStats(state);

		// Refresh stats panel (standalone mode) — only if open to avoid overhead
		if (_statsPanel.IsVisible)
			_statsPanel.UpdateFromEngine(_engine, _population!.Population, snapshot.Balance, (float)happiness);
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
