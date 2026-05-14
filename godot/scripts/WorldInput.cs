using Godot;
using System;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace LoopolisGodot;

/// <summary>
/// Input handling partial — keyboard shortcuts, mouse click/drag zone painting, upgrade tool.
/// All fields referenced here are declared in World.cs.
/// </summary>
public partial class World : Node2D
{
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

	/// <summary>Toggles the Upgrade tool on/off.</summary>
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

	private void ToggleOverlay(OverlayMode mode)
	{
		_activeOverlay = (_activeOverlay == mode) ? OverlayMode.None : mode;
		_renderer.ActiveOverlay = _activeOverlay;
		_renderer.QueueRedraw();
		_hud.ShowOverlayLegend(_activeOverlay);
	}

	private void Log(string text) => _eventLog.AddEntry(text);

	// ── Unhandled input ────────────────────────────────────────────────────

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

			// 'V' toggles the city statistics panel
			if (key.Keycode == Key.V)
			{
				if (_statsPanel.IsVisible)
				{
					_statsPanel.Hide();
				}
				else
				{
					_statsPanel.Show();
					// Populate immediately so the panel is not blank on first open
					if (_viewerMode && _reader?.LastState != null)
						_statsPanel.UpdateFromState(_reader.LastState);
					else if (!_viewerMode && _engine != null && _population != null)
						_statsPanel.UpdateFromEngine(_engine, _population.Population,
							_budget?.Balance ?? 0.0,
							(float)_engine.HappinessSystem.AverageHappiness(_grid));
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

			// Escape: close stats panel, policy panel, shortcuts panel, upgrade tool, cancel tool
			if (key.Keycode == Key.Escape)
			{
				if (_statsPanel.IsVisible)
				{
					_statsPanel.Hide();
					GetViewport().SetInputAsHandled();
					return;
				}

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
				var placementCost = BudgetSystem.GetPlacementCost(selectedZone, terrain);
				if (_budget != null && !_budget.CanAfford(placementCost))
				{
					// Flash the balance label red briefly to signal insufficient funds
					_hud.FlashBalanceWarning();
					return;
				}
				if (System.Enum.TryParse<ZoneType>(selectedZone, out var zoneType))
				{
					_budget?.Charge(placementCost);

					// Score BEFORE placing (tile must still be Empty for the scorer to accept it)
					var placementScore = Loopolis.Core.Simulation.PlacementScorer.Score(_grid, tileX, tileY, zoneType);

					_grid.SetZone(tileX, tileY, zoneType);
					// Immediately re-propagate road network so tooltips reflect current state
					// even when the simulation is paused (e.g. build mode).
					_engine.RoadNetwork.Propagate(_grid);
					Log($"[T:{_standaloneTick}] Placed {selectedZone} at ({tileX},{tileY})");

					// Floating placement-score label — drifts upward and fades (standalone only)
					if (placementScore != null)
					{
						var worldPos = new Vector2(
							tileX * TilemapRenderer.TileSize + TilemapRenderer.TileSize * 0.5f,
							tileY * TilemapRenderer.TileSize);
						var feedback = PlacementFeedback.Create(worldPos, placementScore.PrimaryLabel, placementScore.SecondaryLabel);
						_renderer.AddChild(feedback);
					}

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
	/// In standalone mode: calls ManualUpgradeSystem directly.
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
	/// Returns (cost, targetName) for a given building typeId if it can be manually upgraded,
	/// or null if the type is at max tier.
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
}
