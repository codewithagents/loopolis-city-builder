using Godot;
using System;
using System.Collections.Generic;
using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;

namespace LoopolisGodot;

// OverlayMode defined in OverlayMode.cs

public partial class TilemapRenderer : Node2D
{
	private CityGrid? _grid;
	public const int TileSize = 32;

	// ── Building spawn animation ─────────────────────────────────────────────
	// Maps anchor tile → progress [0..>1.0]; removed when progress > 1.0
	private readonly Dictionary<Vector2I, float> _buildingSpawn = new();
	private const float SpawnSpeed = 3.5f; // full travel in ~0.29 s

	/// <summary>
	/// Starts a bounce-in spawn animation for a building.
	/// Call this as soon as the building is placed (anchor tile + footprint size).
	/// </summary>
	public void AnimateBuildingSpawn(Vector2I anchorTile, int w, int h)
	{
		for (var dx = 0; dx < w; dx++)
		for (var dy = 0; dy < h; dy++)
			_buildingSpawn[new Vector2I(anchorTile.X + dx, anchorTile.Y + dy)] = 0f;
		QueueRedraw();
	}

	/// <summary>
	/// Simple ease-out-bounce curve.
	/// Returns a scale factor: 0 → grows fast → overshoots to 1.15 → settles at 1.0
	/// </summary>
	private static float SpawnBounceScale(float progress)
	{
		// progress in [0,1]
		if (progress < 0.7f)
			return (progress / 0.7f) * 1.15f;
		// overshoot settle: scale from 1.15 → 1.0 over remaining 0.3
		return 1.0f + (1.0f - progress) / 0.3f * 0.15f;
	}

	// ── Time accumulator for animated effects ───────────────────────────────
	private float _time = 0f;
	private bool _hasWaterTiles = false; // set by Refresh(); drives continuous shimmer redraws
	private bool _hasUpgradeReadyBuildings = false; // set each _Draw(); drives per-frame redraw for pulsing badge

	// ── Smoke particles ──────────────────────────────────────────────────────
	private record SmokeParticle(Vector2 WorldPos, float Age, float MaxAge, float BaseRadius);
	private readonly List<SmokeParticle> _smoke = new();
	private float _smokeSpawnTimer = 0f;
	private const float SmokeSpawnInterval = 1.0f; // spawn wave every 1 second

	/// <summary>World-space chimney positions for all powered factory/quarry/ind_park tiles.</summary>
	private readonly List<Vector2> _chimneyPositions = new();

	/// <summary>
	/// Recomputes chimney positions from the current grid.
	/// Called from Refresh() so smoke always spawns at the right tiles.
	/// </summary>
	private void RebuildChimneyPositions()
	{
		_chimneyPositions.Clear();
		if (_grid == null) return;
		foreach (var building in _grid.Buildings.Values)
		{
			// Only powered buildings emit smoke
			var anchorTile = _grid.GetTile(building.AnchorX, building.AnchorY);
			if (!anchorTile.HasPower) continue;

			switch (building.TypeId)
			{
				case "ind_factory_1x1":
					// Chimney at pixel (23, 4) relative to anchor (spec above)
					_chimneyPositions.Add(new Vector2(
						building.AnchorX * TileSize + 23f,
						building.AnchorY * TileSize + 4f));
					break;
				case "ind_quarry_2x2":
					// Crane/equipment in corner — place smoke above top-right area
					_chimneyPositions.Add(new Vector2(
						building.AnchorX * TileSize + TileSize * 1.5f,
						building.AnchorY * TileSize + TileSize * 0.3f));
					break;
				case "ind_park_4x2":
					// Two chimneys: unit 1 and unit 3
					_chimneyPositions.Add(new Vector2(
						building.AnchorX * TileSize + TileSize * 0.75f,
						building.AnchorY * TileSize + 4f));
					_chimneyPositions.Add(new Vector2(
						building.AnchorX * TileSize + TileSize * 2.75f,
						building.AnchorY * TileSize + 4f));
					break;
				case "ind_park_2x4":
					// Two chimneys: top unit and third unit
					_chimneyPositions.Add(new Vector2(
						building.AnchorX * TileSize + TileSize * 0.75f,
						building.AnchorY * TileSize + 4f));
					_chimneyPositions.Add(new Vector2(
						building.AnchorX * TileSize + TileSize * 0.75f,
						building.AnchorY * TileSize + TileSize * 2f + 4f));
					break;
				case "ind_complex_4x4":
					// Two chimneys at diagonally opposite corners: top-left unit and bottom-right unit
					_chimneyPositions.Add(new Vector2(
						building.AnchorX * TileSize + TileSize * 0.75f,
						building.AnchorY * TileSize + 4f));
					_chimneyPositions.Add(new Vector2(
						building.AnchorX * TileSize + TileSize * 2.75f,
						building.AnchorY * TileSize + TileSize * 2f + 4f));
					break;
			}
		}
	}

	// ── Camera zoom tracking ────────────────────────────────────────────────
	private float _currentZoom = 1.0f;

	/// <summary>
	/// Called by World.cs whenever the camera zoom changes.
	/// When zoom drops to ≤0.5×, _Draw() switches to simplified icon mode.
	/// </summary>
	public void SetCameraZoom(float zoom)
	{
		var wasIconMode = _currentZoom <= 0.5f;
		_currentZoom = zoom;
		var isIconMode = _currentZoom <= 0.5f;
		// Only force a redraw when the mode boundary is crossed
		if (wasIconMode != isIconMode)
			QueueRedraw();
	}

	// ── Overlay system ──────────────────────────────────────────────────────
	public OverlayMode ActiveOverlay = OverlayMode.None;

	// Neglect map: (x, y) → neglect value (0.0–0.20) for residential tiles
	// Populated each tick by World.cs from HappinessSystem.GetNeglect() in standalone mode.
	private float[,]? _neglectMap;

	/// <summary>
	/// Updates the per-tile neglect map used for the pulsing amber ring warning.
	/// Only called in standalone mode; viewer mode skips per-tile neglect.
	/// </summary>
	public void SetNeglectMap(float[,] neglectMap)
	{
		_neglectMap = neglectMap;
		// No QueueRedraw here — caller (World._Process) will call Refresh() which triggers it.
	}

	// Height and forest maps — parallel arrays updated with Refresh()
	// These are separate from CityGrid because Core doesn't have HeightLevel on Tile yet.
	// Viewer mode populates them from SharedTile.Height/HasForest.
	// Standalone mode derives them from TerrainType until Core adds HeightLevel.
	private int[,]? _heightMap;
	private bool[,]? _forestMap;

	private HashSet<(int, int)> _coverageHighlight = new();
	private Color _coverageColor = Colors.Transparent;

	private static readonly Color ColorEmpty         = new Color(0.15f, 0.15f, 0.15f);
	private static readonly Color ColorWater        = new Color(0.18f, 0.42f, 0.72f); // blue
	private static readonly Color ColorForest       = new Color(0.13f, 0.42f, 0.18f); // dark green
	private static readonly Color ColorHill         = new Color(0.831f, 0.663f, 0.416f); // warm tan #D4A96A
	private static readonly Color ColorHillHatch    = new Color(0.627f, 0.471f, 0.353f); // darker hatch lines #A0785A
	private static readonly Color ColorHillShadow   = new Color(0.627f, 0.471f, 0.353f, 0.8f); // bottom/right edge shadow

	// Height-based land colors
	private static readonly Color ColorDeepWater    = new Color(0.082f, 0.396f, 0.753f); // #1565C0
	private static readonly Color ColorShallowWater = new Color(0.098f, 0.463f, 0.824f); // #1976D2
	private static readonly Color ColorLowland      = new Color(0.180f, 0.490f, 0.196f); // #2E7D32
	private static readonly Color ColorMidland      = new Color(0.220f, 0.557f, 0.235f); // #388E3C
	private static readonly Color ColorHighland     = new Color(0.831f, 0.663f, 0.416f); // #D4A96A (matches existing hill)
	private static readonly Color ColorUpland       = new Color(0.553f, 0.431f, 0.388f); // #8D6E63
	private static readonly Color ColorPeak         = new Color(0.620f, 0.620f, 0.620f); // #9E9E9E

	// Forest overlay
	private static readonly Color ColorForestOverlay = new Color(0.062f, 0.380f, 0.090f, 0.40f); // dark green 40% alpha
	private static readonly Color ColorForestDot     = new Color(0.062f, 0.380f, 0.090f, 0.85f); // forest center dot

	// Cliff edge indicator
	private static readonly Color CliffEdgeColor    = new Color(0.3f, 0.2f, 0.1f, 0.9f); // dark brown

	// Plateau highlight
	private static readonly Color PlateauShimmer    = new Color(1f, 1f, 1f, 0.25f); // white 25% alpha
	private static readonly Color ColorResidential  = new Color(0.2f,  0.7f,  0.2f);
	// Unpowered cottage (res_house_1x1 without power) — muted grey-green signals "limited capacity"
	private static readonly Color ColorCottageUnpowered = new Color(0.6f, 0.7f, 0.6f);
	private static readonly Color ColorCommercial   = new Color(0.2f,  0.4f,  0.9f);
	private static readonly Color ColorIndustrial   = new Color(0.9f,  0.8f,  0.1f);
	// Terrain-specific industrial variants
	// Timber Mill (ind_mill_2x2): warm earthy green — suggests lumber/forest industry
	private static readonly Color ColorIndustrialMill   = new Color(0.35f, 0.55f, 0.20f);
	// Quarry/Coal Mine (ind_quarry_2x2): cool stone grey — suggests rock/mine extraction
	private static readonly Color ColorIndustrialQuarry = new Color(0.50f, 0.48f, 0.45f);
	// High-tier building fill colors
	private static readonly Color ColorHighrise      = new Color(0.62f, 0.68f, 0.78f);  // cool blue-grey
	private static readonly Color ColorOffice        = new Color(0.45f, 0.58f, 0.80f);  // stronger blue
	private static readonly Color ColorIndComplex    = new Color(0.50f, 0.48f, 0.44f);  // dark industrial
	private static readonly Color ColorRoad         = new Color(0.5f,  0.5f,  0.5f);
	private static readonly Color ColorAvenue       = new Color(0.62f, 0.62f, 0.62f);
	private static readonly Color ColorPowerPlant   = new Color(0.9f,  0.3f,  0.1f);
	private static readonly Color ColorPowerLine    = new Color(0.1f,  0.9f,  0.9f);
	private static readonly Color ColorFireStation  = new Color(1.0f,  0.4f,  0.1f);
	private static readonly Color ColorPoliceStation= new Color(0.2f,  0.4f,  1.0f);
	private static readonly Color ColorSchool       = new Color(0.7f,  0.3f,  0.9f);
	// M8 zone colors
	private static readonly Color ColorPoliceHQ     = new Color(0.102f, 0.137f, 0.494f); // #1a237e deep blue
	private static readonly Color ColorFireHQ       = new Color(0.718f, 0.110f, 0.110f); // #b71c1c deep red
	private static readonly Color ColorHospital     = new Color(0.647f, 0.839f, 0.647f); // #a5d6a7 soft green-white
	private static readonly Color ColorCoalPlant    = new Color(0.259f, 0.259f, 0.259f); // #424242 dark grey
	private static readonly Color ColorNuclearPlant = new Color(0.976f, 0.659f, 0.145f); // #f9a825 yellow-green
	// Park — vibrant grass green, distinct from forest terrain (darker) and Timber Mill (earthy brown-green)
	private static readonly Color ColorPark         = new Color(0.30f,  0.72f,  0.25f);
	private static readonly Color ColorParkOutline  = new Color(0.55f,  0.90f,  0.45f);
	// Unpowered zones get a dark overlay — show the mechanic visually
	private static readonly Color UnpoweredTint     = new Color(0f, 0f, 0f, 0.45f);
	// Brownout overlay — amber tint on BFS-powered tiles when capacity < demand
	private static readonly Color BrownoutTint      = new Color(1f, 0.55f, 0f, 0.22f);
	// Idle zone border — amber dashed outline on zoned tiles with no road access
	private static readonly Color IdleBorderColor   = new Color(1f, 0.561f, 0f, 0.85f); // #FF8F00

	private bool _isBrownout = false;

	// Road pulse — flash newly-placed road/avenue tiles bright white for 0.4 s
	private readonly Dictionary<Vector2I, float> _roadPulse = new();
	private const float RoadPulseDuration = 0.4f;

	/// <summary>
	/// Starts a 0.4-second white-flash pulse on the road tile at <paramref name="tile"/>.
	/// Call this immediately after a Road or Avenue tile is successfully placed.
	/// </summary>
	public void PulseRoad(Vector2I tile)
	{
		_roadPulse[tile] = RoadPulseDuration;
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		var dt = (float)delta;
		var needsRedraw = false;

		// ── Time accumulator (water shimmer + upgrade badge pulse) ──────────
		_time += dt;
		if (_hasWaterTiles && ActiveOverlay == OverlayMode.None)
			needsRedraw = true;

		// ── Upgrade-ready badge pulse ─────────────────────────────────────
		// When any building is at 80%+ capacity, the gold badge pulses — keep redrawing.
		if (_hasUpgradeReadyBuildings && ActiveOverlay == OverlayMode.None && !World.UpgradeToolActive && _currentZoom > 0.5f)
			needsRedraw = true;

		// ── Road pulse ───────────────────────────────────────────────────────
		if (_roadPulse.Count > 0)
		{
			var roadKeys = new Vector2I[_roadPulse.Count];
			_roadPulse.Keys.CopyTo(roadKeys, 0);
			foreach (var key in roadKeys)
			{
				var remaining = _roadPulse[key] - dt;
				if (remaining <= 0f)
					_roadPulse.Remove(key);
				else
					_roadPulse[key] = remaining;
			}
			needsRedraw = true;
		}

		// ── Spawn animation ──────────────────────────────────────────────────
		if (_buildingSpawn.Count > 0)
		{
			var spawnKeys = new Vector2I[_buildingSpawn.Count];
			_buildingSpawn.Keys.CopyTo(spawnKeys, 0);
			foreach (var key in spawnKeys)
			{
				var progress = _buildingSpawn[key] + dt * SpawnSpeed;
				if (progress > 1.0f)
					_buildingSpawn.Remove(key);
				else
					_buildingSpawn[key] = progress;
			}
			needsRedraw = true;
		}

		// ── Smoke particles ──────────────────────────────────────────────────
		if (_smoke.Count > 0 || _chimneyPositions.Count > 0)
		{
			// Age and move existing particles
			for (var i = _smoke.Count - 1; i >= 0; i--)
			{
				var p = _smoke[i];
				var newAge = p.Age + dt;
				if (newAge >= p.MaxAge)
				{
					_smoke.RemoveAt(i);
					continue;
				}
				// Drift upward and slightly right each frame
				var newPos = p.WorldPos + new Vector2(0.5f, -12f) * dt;
				_smoke[i] = p with { WorldPos = newPos, Age = newAge };
			}

			// Spawn new waves at chimney positions
			if (_chimneyPositions.Count > 0 && ActiveOverlay == OverlayMode.None)
			{
				_smokeSpawnTimer += dt;
				if (_smokeSpawnTimer >= SmokeSpawnInterval)
				{
					_smokeSpawnTimer = 0f;
					foreach (var pos in _chimneyPositions)
					{
						// Slight randomness so smoke looks organic
						var jitter = new Vector2(
							(float)(GD.Randf() - 0.5f) * 3f,
							(float)(GD.Randf() - 0.5f) * 2f);
						_smoke.Add(new SmokeParticle(pos + jitter, 0f, 1.5f, 3f));
					}
				}
			}

			needsRedraw = true;
		}

		if (needsRedraw)
			QueueRedraw();
	}

	// Fire tile — on-fire overlay for FireBreak event
	private static readonly Color FireOverlay = new Color(1f, 0.25f, 0f, 0.60f);   // vivid orange-red
	private static readonly Color FireBorder  = new Color(1f, 0.70f, 0f, 0.95f);   // bright amber border
	private int _fireTileX = -1;
	private int _fireTileY = -1;

	// Service fatigue — degraded service tiles
	private static readonly Color FatigueAmber = new Color(1.00f, 0.70f, 0.00f); // amber for 40–59% capacity
	private static readonly Color FatigueRed   = new Color(0.95f, 0.20f, 0.20f); // red for exactly at minimum (40%)
	private ServiceFatigueEntry[]? _degradedServices;

	/// <summary>
	/// Updates the list of degraded service tiles for the next redraw.
	/// Pass null or empty array to clear all fatigue indicators.
	/// </summary>
	public void SetDegradedServices(ServiceFatigueEntry[]? entries)
	{
		_degradedServices = entries;
		QueueRedraw();
	}

	// Upgrade tool highlight — gold border on buildings that can be upgraded
	private static readonly Color UpgradeBorderColor = new Color(0.85f, 0.70f, 0.15f, 0.90f);  // gold
	private static readonly Color UpgradeTintColor   = new Color(0.85f, 0.70f, 0.15f, 0.12f);  // subtle gold tint

	/// <summary>
	/// Building TypeIds that have a next upgrade tier available (for gold highlight in upgrade tool mode).
	/// Maintained in sync with World.GetUpgradeInfoForType and ManualUpgradeSystem in Core.
	/// </summary>
	private static readonly System.Collections.Generic.HashSet<string> UpgradeableTypes = new()
	{
		"res_house_1x1",
		"res_townhouse_2x2",
		"res_apartment_4x4",
		"com_shop_1x1",
		"com_strip_1x3",
		"com_strip_3x1",
		"com_shopping_3x3",
		"ind_factory_1x1",
		"ind_warehouse_2x2",
		"ind_mill_2x2",
		"ind_quarry_2x2",
	};

	/// <summary>
	/// Sets the tile currently on fire (shown with vivid orange-red overlay).
	/// Pass (-1, -1) to clear the fire tile.
	/// </summary>
	public void SetFireTile(int x, int y)
	{
		if (_fireTileX == x && _fireTileY == y) return;
		_fireTileX = x;
		_fireTileY = y;
		QueueRedraw();
	}

	// Rectangle paint preview
	private bool _hasRectPreview = false;
	private Vector2I _rectPreviewStart;
	private Vector2I _rectPreviewEnd;
	private Color _rectPreviewColor = Colors.Transparent;

	/// <summary>Set brownout state so the renderer can apply the amber tint on next redraw.</summary>
	public void SetBrownout(bool brownout)
	{
		if (_isBrownout == brownout) return;
		_isBrownout = brownout;
		QueueRedraw();
	}

	public void Refresh(CityGrid grid)
	{
		_grid = grid;
		// Read HeightLevel and HasForest directly from the Core tile data.
		var w = grid.Width;
		var h = grid.Height;
		_heightMap = new int[w, h];
		_forestMap = new bool[w, h];
		_hasWaterTiles = false;
		for (var x = 0; x < w; x++)
		for (var y = 0; y < h; y++)
		{
			_heightMap[x, y] = grid.GetHeightLevel(x, y);
			_forestMap[x, y] = grid.HasForestAt(x, y);
			if (_heightMap[x, y] <= 0) _hasWaterTiles = true;
		}
		RebuildChimneyPositions();
		QueueRedraw();
	}

	/// <summary>
	/// Refresh with explicit height and forest maps (viewer mode, populated from SharedTile.Height / HasForest).
	/// </summary>
	public void RefreshWithHeight(CityGrid grid, int[,] heightMap, bool[,] forestMap)
	{
		_grid      = grid;
		_heightMap = heightMap;
		_forestMap = forestMap;
		// Detect water tiles so shimmer animation keeps running
		_hasWaterTiles = false;
		for (var x = 0; x < heightMap.GetLength(0); x++)
		for (var y = 0; y < heightMap.GetLength(1); y++)
			if (heightMap[x, y] <= 0) { _hasWaterTiles = true; break; }
		RebuildChimneyPositions();
		QueueRedraw();
	}

	/// <summary>Returns the height level for the given tile coordinate, or 1 if no height map is loaded.</summary>
	public int GetTileHeight(int x, int y) => GetHeight(x, y);

	/// <summary>Returns whether the given tile has a forest overlay, or false if no forest map is loaded.</summary>
	public bool GetTileForest(int x, int y)
	{
		if (_forestMap == null) return false;
		if (x < 0 || x >= _forestMap.GetLength(0)) return false;
		if (y < 0 || y >= _forestMap.GetLength(1)) return false;
		return _forestMap[x, y];
	}

	public void SetCoverageHighlight(IEnumerable<(int, int)> tiles, Color color)
	{
		_coverageHighlight = new HashSet<(int, int)>(tiles);
		_coverageColor = color;
		QueueRedraw();
	}

	public void ClearCoverageHighlight()
	{
		_coverageHighlight.Clear();
		_coverageColor = Colors.Transparent;
		QueueRedraw();
	}

	/// <summary>Shows a semi-transparent rectangle preview for drag-to-place zone painting.</summary>
	public void SetRectPreview(Vector2I start, Vector2I end, Color color)
	{
		_hasRectPreview    = true;
		_rectPreviewStart  = start;
		_rectPreviewEnd    = end;
		_rectPreviewColor  = color;
		QueueRedraw();
	}

	/// <summary>Removes the rectangle paint preview overlay.</summary>
	public void ClearRectPreview()
	{
		_hasRectPreview = false;
		QueueRedraw();
	}

	// ── Height rendering helpers ────────────────────────────────────────────

	/// <summary>Returns the base land color for a given height level.</summary>
	private static Color HeightToColor(int height)
	{
		return height switch
		{
			<= 0 => ColorDeepWater,
			1    => ColorLowland,
			2    => ColorMidland,
			3    => ColorHighland,
			4    => ColorUpland,
			_    => ColorPeak,
		};
	}

	/// <summary>
	/// Returns a subtle brightness multiplier for a zoned tile based on height.
	/// Height 1 = no change, 2–3 = slightly brighter (+10% L), ≥4 = slightly darker, ≤0 = blue-tint error.
	/// </summary>
	private static Color ApplyHeightTintToZoneColor(Color baseColor, int height)
	{
		if (height <= 0)
		{
			// Water — should never be zoned, show blue-tinted error
			return baseColor.Lerp(new Color(0.2f, 0.4f, 0.9f), 0.35f);
		}
		if (height >= 4)
		{
			// Upland/peak — slightly darker (more dramatic terrain)
			return new Color(
				Mathf.Clamp(baseColor.R * 0.92f, 0f, 1f),
				Mathf.Clamp(baseColor.G * 0.92f, 0f, 1f),
				Mathf.Clamp(baseColor.B * 0.92f, 0f, 1f),
				baseColor.A);
		}
		if (height >= 2)
		{
			// Midland/highland — slightly brighter (+10% on each channel)
			return new Color(
				Mathf.Clamp(baseColor.R * 1.10f, 0f, 1f),
				Mathf.Clamp(baseColor.G * 1.10f, 0f, 1f),
				Mathf.Clamp(baseColor.B * 1.10f, 0f, 1f),
				baseColor.A);
		}
		// height == 1: standard, no modification
		return baseColor;
	}

	/// <summary>
	/// Returns the height for a neighbour tile, defaulting to 1 if out of bounds.
	/// </summary>
	private int GetHeight(int x, int y)
	{
		if (_heightMap == null) return 1;
		if (x < 0 || x >= _heightMap.GetLength(0)) return 1;
		if (y < 0 || y >= _heightMap.GetLength(1)) return 1;
		return _heightMap[x, y];
	}

	/// <summary>
	/// Draws a height-based empty terrain tile with water depth, cliff edges, plateau highlight,
	/// and forest overlay.
	/// </summary>
	private void DrawHeightTile(int tileX, int tileY, float px, float py)
	{
		var height = GetHeight(tileX, tileY);
		var isForest = _forestMap != null
			&& tileX >= 0 && tileX < _forestMap.GetLength(0)
			&& tileY >= 0 && tileY < _forestMap.GetLength(1)
			&& _forestMap[tileX, tileY];

		// ── Base color ──────────────────────────────────────────────────────

		Color baseColor;
		if (height <= 0)
		{
			// Water: check if any cardinal neighbour is ≥ 1 → shallow coast, else deep
			var hasLandNeighbour =
				GetHeight(tileX - 1, tileY) >= 1 ||
				GetHeight(tileX + 1, tileY) >= 1 ||
				GetHeight(tileX, tileY - 1) >= 1 ||
				GetHeight(tileX, tileY + 1) >= 1;
			baseColor = hasLandNeighbour ? ColorShallowWater : ColorDeepWater;
		}
		else
		{
			baseColor = HeightToColor(height);
		}

		DrawRect(new Rect2(px, py, TileSize, TileSize), baseColor);

		// ── Water depth effect: darker small rect in center ────────────────
		if (height <= 0)
		{
			var depthColor = new Color(
				Mathf.Max(baseColor.R - 0.07f, 0f),
				Mathf.Max(baseColor.G - 0.07f, 0f),
				Mathf.Max(baseColor.B - 0.08f, 0f),
				0.65f);
			var depthSize = TileSize * 0.35f;
			var depthOffset = (TileSize - depthSize) * 0.5f;
			DrawRect(new Rect2(px + depthOffset, py + depthOffset, depthSize, depthSize), depthColor);

			// ── Water shimmer bands (only when no overlay active) ───────────
			if (ActiveOverlay == OverlayMode.None)
			{
				// Band 1: slow ripple offset per tile column
				var band1Y = py + 10 + (int)(Mathf.Sin(_time * 1.2f + tileX * 0.7f) * 3);
				DrawRect(new Rect2(px, band1Y, TileSize, 3), new Color(0.35f, 0.55f, 0.80f, 0.25f));

				// Band 2: slightly different frequency + phase for organic variation
				var band2Y = py + 20 + (int)(Mathf.Sin(_time * 0.9f + tileX * 0.5f + 1.5f) * 4);
				DrawRect(new Rect2(px, band2Y, TileSize, 2), new Color(0.45f, 0.65f, 0.90f, 0.18f));
			}

			// No further overlays for water tiles
			return;
		}

		// ── Forest overlay ─────────────────────────────────────────────────
		if (isForest)
		{
			DrawRect(new Rect2(px, py, TileSize, TileSize), ColorForestOverlay);
			// Small dark green dot in center
			var dotSize = 5f;
			DrawRect(new Rect2(
				px + (TileSize - dotSize) * 0.5f,
				py + (TileSize - dotSize) * 0.5f,
				dotSize, dotSize), ColorForestDot);
		}

		// ── Cliff edge indicator: 3px dark brown line on edges with height diff > 1 ──
		const float cliffWidth = 3f;
		// Left edge
		if (System.Math.Abs(height - GetHeight(tileX - 1, tileY)) > 1)
			DrawRect(new Rect2(px, py, cliffWidth, TileSize), CliffEdgeColor);
		// Right edge
		if (System.Math.Abs(height - GetHeight(tileX + 1, tileY)) > 1)
			DrawRect(new Rect2(px + TileSize - cliffWidth, py, cliffWidth, TileSize), CliffEdgeColor);
		// Top edge
		if (System.Math.Abs(height - GetHeight(tileX, tileY - 1)) > 1)
			DrawRect(new Rect2(px, py, TileSize, cliffWidth), CliffEdgeColor);
		// Bottom edge
		if (System.Math.Abs(height - GetHeight(tileX, tileY + 1)) > 1)
			DrawRect(new Rect2(px, py + TileSize - cliffWidth, TileSize, cliffWidth), CliffEdgeColor);

		// ── Plateau highlight: all 4 cardinal neighbours within ±1 height ──
		if (height >= 2)
		{
			var dLeft  = System.Math.Abs(height - GetHeight(tileX - 1, tileY));
			var dRight = System.Math.Abs(height - GetHeight(tileX + 1, tileY));
			var dUp    = System.Math.Abs(height - GetHeight(tileX, tileY - 1));
			var dDown  = System.Math.Abs(height - GetHeight(tileX, tileY + 1));
			if (dLeft <= 1 && dRight <= 1 && dUp <= 1 && dDown <= 1)
			{
				// Small white triangle in the top-left corner
				const float shimmerSize = 7f;
				var v1 = new Vector2(px + 1, py + 1);
				var v2 = new Vector2(px + 1 + shimmerSize, py + 1);
				var v3 = new Vector2(px + 1, py + 1 + shimmerSize);
				DrawTriangle(v1, v2, v3, PlateauShimmer);
			}
		}
	}

	/// <summary>
	/// Draws cliff edge indicators on an already-rendered zoned tile.
	/// Applies 3px dark brown lines only on edges where height difference > 1.
	/// </summary>
	private void DrawZonedCliffEdges(int tileX, int tileY, float px, float py)
	{
		if (_heightMap == null) return;
		var height = GetHeight(tileX, tileY);
		const float cliffWidth = 3f;
		if (System.Math.Abs(height - GetHeight(tileX - 1, tileY)) > 1)
			DrawRect(new Rect2(px, py, cliffWidth, TileSize), CliffEdgeColor);
		if (System.Math.Abs(height - GetHeight(tileX + 1, tileY)) > 1)
			DrawRect(new Rect2(px + TileSize - cliffWidth, py, cliffWidth, TileSize), CliffEdgeColor);
		if (System.Math.Abs(height - GetHeight(tileX, tileY - 1)) > 1)
			DrawRect(new Rect2(px, py, TileSize, cliffWidth), CliffEdgeColor);
		if (System.Math.Abs(height - GetHeight(tileX, tileY + 1)) > 1)
			DrawRect(new Rect2(px, py + TileSize - cliffWidth, TileSize, cliffWidth), CliffEdgeColor);
	}

	/// <summary>
	/// Draws a filled triangle using three DrawLine calls approximated by a polygon.
	/// Godot 4's Node2D _Draw() exposes DrawPolygon for filled shapes.
	/// </summary>
	private void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Color color)
	{
		DrawPolygon(new[] { a, b, c }, new[] { color, color, color });
	}

	/// <summary>
	/// Draws a dashed rectangular border around the tile at (px, py) with the given size.
	/// Each edge is walked in pixel steps; a dash is drawn for <paramref name="dashLen"/> pixels,
	/// then skipped for <paramref name="gapLen"/> pixels, cycling continuously.
	/// The phase carries across edges so the pattern looks continuous around the perimeter.
	/// </summary>
	private void DrawDashedBorder(float px, float py, int size, Color color, float dashLen, float gapLen, float width)
	{
		float period = dashLen + gapLen;
		float halfW  = width * 0.5f;
		float inset  = halfW; // keep the line fully inside the tile rect

		// The four corners of the inset border, going clockwise: TL → TR → BR → BL → TL
		var corners = new (float x, float y)[]
		{
			(px + inset,            py + inset),
			(px + size - inset,     py + inset),
			(px + size - inset,     py + size - inset),
			(px + inset,            py + size - inset),
		};

		float phase = 0f; // position within the current dash+gap cycle

		for (int i = 0; i < corners.Length; i++)
		{
			var (ax, ay) = corners[i];
			var (bx, by) = corners[(i + 1) % corners.Length];

			float edgeLen = Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
			float dx = (bx - ax) / edgeLen;
			float dy = (by - ay) / edgeLen;

			float t = 0f;
			while (t < edgeLen)
			{
				// Position within the dash/gap cycle
				float cyclePos = phase % period;
				float remaining = period - cyclePos;

				if (cyclePos < dashLen)
				{
					// We are in a dash segment
					float dashRemaining = dashLen - cyclePos;
					float drawLen = Mathf.Min(dashRemaining, edgeLen - t);

					var start = new Vector2(ax + dx * t,       ay + dy * t);
					var end   = new Vector2(ax + dx * (t + drawLen), ay + dy * (t + drawLen));
					DrawLine(start, end, color, width, false);

					phase += drawLen;
					t     += drawLen;
				}
				else
				{
					// We are in a gap segment
					float gapRemaining = period - cyclePos;
					float skipLen = Mathf.Min(gapRemaining, edgeLen - t);

					phase += skipLen;
					t     += skipLen;
				}
			}
		}
	}

	/// <summary>
	/// Draws 1–5 small filled circles in the centre of a road tile based on traffic congestion.
	/// Uses absolute worker-count tiers (TrafficLoad = workers passing through node per tick):
	///   0        → no dots
	///   1–10     → 1 dot  (light,  white)
	///   11–30    → 2 dots (moderate, white)
	///   31–60    → 3 dots (busy,   yellow)
	///   61–100   → 4 dots (heavy,  orange)
	///   101+     → 5 dots (jammed, red)
	/// </summary>
	private void DrawTrafficDots(int load, float px, float py)
	{
		int dots;
		Color dotColor;
		if      (load <= 0)   { return; }
		else if (load <= 10)  { dots = 1; dotColor = Colors.White; }
		else if (load <= 30)  { dots = 2; dotColor = Colors.White; }
		else if (load <= 60)  { dots = 3; dotColor = new Color(1f, 0.95f, 0.2f); }
		else if (load <= 100) { dots = 4; dotColor = new Color(1f, 0.55f, 0.1f); }
		else                  { dots = 5; dotColor = new Color(1f, 0.2f,  0.2f); }

		if (dots == 0) return;

		const float radius = 2.5f;
		const float spacing = 6f;
		// Centre all dots horizontally in the tile
		float totalWidth = (dots - 1) * spacing;
		float startX = px + TileSize * 0.5f - totalWidth * 0.5f;
		float centreY = py + TileSize * 0.5f;

		for (var i = 0; i < dots; i++)
			DrawCircle(new Vector2(startX + i * spacing, centreY), radius, dotColor);
	}

	/// <summary>
	/// Draws the Hill terrain tile: warm tan base + diagonal hatch lines suggesting elevation
	/// + a 2px darker shadow on the bottom and right edges for a subtle raised appearance.
	/// </summary>
	private void DrawHillTile(float px, float py)
	{
		// Base fill — full tile, seamless
		DrawRect(new Rect2(px, py, TileSize, TileSize), ColorHill);

		// Three diagonal hatch lines (top-left → bottom-right), evenly spaced
		const float hatchWidth = 1.2f;
		const int   steps      = 3;
		for (int i = 0; i < steps; i++)
		{
			float offset = TileSize * (i + 1) / (steps + 1);
			// Start on the top edge, end on the left edge when offset < TileSize, else wrap
			var a = new Vector2(px + offset, py);
			var b = new Vector2(px, py + offset);
			DrawLine(a, b, ColorHillHatch, hatchWidth, false);

			// Mirror: start on right edge, end on bottom edge
			var c = new Vector2(px + TileSize - offset, py + TileSize);
			var d = new Vector2(px + TileSize, py + TileSize - offset);
			DrawLine(c, d, ColorHillHatch, hatchWidth, false);
		}

		// 2px shadow on bottom edge (suggests a drop in elevation below)
		DrawRect(new Rect2(px, py + TileSize - 3, TileSize, 2), ColorHillShadow);
		// 2px shadow on right edge
		DrawRect(new Rect2(px + TileSize - 3, py, 2, TileSize), ColorHillShadow);
	}

	// ── Service / utility zone art ──────────────────────────────────────────

	/// <summary>
	/// Draws procedural facade detail on top of the flat color rectangle for service and
	/// utility zone tiles (FireStation, PoliceStation, School, Hospital, FireHQ, PoliceHQ,
	/// CoalPlant, NuclearPlant, PowerLine).
	///
	/// All coordinates are in local tile-space (0,0) = top-left of the tile.
	/// <paramref name="px"/> / <paramref name="py"/> are the world-pixel origin of the tile.
	/// </summary>
	private void DrawServiceZoneArt(ZoneType zone, float px, float py)
	{
		float ts = TileSize; // 32

		switch (zone)
		{
			case ZoneType.FireStation:
			case ZoneType.FireHQ:
			{
				// White garage door (bottom 40%)
				DrawRect(new Rect2(px + 4, py + 18, ts - 8, 12), new Color(0.95f, 0.95f, 0.95f, 0.90f));
				// Horizontal door panel lines
				DrawLine(new Vector2(px + 4, py + 22), new Vector2(px + ts - 4, py + 22),
					new Color(0.60f, 0.60f, 0.60f, 0.6f), 1f);
				DrawLine(new Vector2(px + 4, py + 26), new Vector2(px + ts - 4, py + 26),
					new Color(0.60f, 0.60f, 0.60f, 0.6f), 1f);
				// Yellow signal band at top
				DrawRect(new Rect2(px, py, ts, 5), new Color(1.0f, 0.82f, 0.0f, 0.85f));
				// Dark badge strip just below yellow band
				DrawRect(new Rect2(px + 8, py + 5, ts - 16, 5), new Color(0.15f, 0.10f, 0.10f, 0.80f));
				// Small red cross in badge (fire cross variant — two white rects)
				DrawRect(new Rect2(px + 13, py + 6, 6, 3), new Color(1.0f, 1.0f, 1.0f, 0.90f));
				DrawRect(new Rect2(px + 15, py + 5, 2, 5), new Color(1.0f, 1.0f, 1.0f, 0.90f));
				break;
			}

			case ZoneType.PoliceStation:
			case ZoneType.PoliceHQ:
			{
				// White accent band at top
				DrawRect(new Rect2(px, py, ts, 5), new Color(0.95f, 0.95f, 1.0f, 0.80f));
				// Dark blue sign band below
				DrawRect(new Rect2(px + 3, py + 5, ts - 6, 5), new Color(0.08f, 0.12f, 0.36f, 0.90f));
				// Shield/badge shape: gold pentagon outline (5 filled triangles + rect approximation)
				var shCx = px + ts * 0.5f;
				var shTop = py + 12f;
				// Shield body: dark rect with rounded top suggestion
				DrawRect(new Rect2(shCx - 5f, shTop, 10f, 9f), new Color(0.85f, 0.72f, 0.12f, 0.90f)); // gold
				// Shield chevron point at bottom
				DrawTriangle(
					new Vector2(shCx - 5f, shTop + 9f),
					new Vector2(shCx + 5f, shTop + 9f),
					new Vector2(shCx,      shTop + 13f),
					new Color(0.85f, 0.72f, 0.12f, 0.90f));
				// White star in center of shield
				DrawRect(new Rect2(shCx - 2f, shTop + 3f, 4f, 4f), new Color(1.0f, 1.0f, 1.0f, 0.95f));
				break;
			}

			case ZoneType.School:
			{
				// Warm brick top band
				DrawRect(new Rect2(px, py, ts, 5), new Color(0.76f, 0.54f, 0.22f, 0.80f));
				// Clock face: white circle at top-center
				DrawCircle(new Vector2(px + ts * 0.5f, py + 10f), 5f, new Color(0.96f, 0.96f, 0.92f, 0.95f));
				// Clock outline
				DrawLine(
					new Vector2(px + ts * 0.5f, py + 6f),
					new Vector2(px + ts * 0.5f, py + 10f),
					new Color(0.30f, 0.25f, 0.20f, 0.90f), 1f); // 12-o-clock hand
				DrawLine(
					new Vector2(px + ts * 0.5f, py + 10f),
					new Vector2(px + ts * 0.5f + 3.5f, py + 10f),
					new Color(0.30f, 0.25f, 0.20f, 0.90f), 1f); // 3-o-clock hand
				// Two windows (symmetric)
				DrawRect(new Rect2(px + 4, py + 17, 7, 6), new Color(0.75f, 0.88f, 1.0f, 0.85f));
				DrawRect(new Rect2(px + 21, py + 17, 7, 6), new Color(0.75f, 0.88f, 1.0f, 0.85f));
				// Flagpole in top-left corner
				DrawLine(new Vector2(px + 3f, py + 2f), new Vector2(px + 3f, py + 14f),
					new Color(0.65f, 0.65f, 0.65f, 0.80f), 1f);
				// Small flag rect
				DrawRect(new Rect2(px + 3f, py + 2f, 6f, 4f), new Color(0.85f, 0.12f, 0.12f, 0.85f));
				break;
			}

			case ZoneType.Hospital:
			{
				// Clean white top parapet
				DrawRect(new Rect2(px, py, ts, 4), new Color(0.96f, 0.96f, 0.96f, 0.85f));
				// Red cross centered on facade — vertical bar
				DrawRect(new Rect2(px + 13f, py + 7f, 6f, 18f), new Color(0.85f, 0.10f, 0.10f, 0.90f));
				// Red cross — horizontal bar
				DrawRect(new Rect2(px + 7f, py + 13f, 18f, 6f), new Color(0.85f, 0.10f, 0.10f, 0.90f));
				// Green canopy strip at bottom
				DrawRect(new Rect2(px, py + 26f, ts, 6f), new Color(0.18f, 0.62f, 0.28f, 0.80f));
				break;
			}

			case ZoneType.CoalPlant:
			case ZoneType.PowerPlant:
			{
				// Building mass: darker rect offset from tile edges
				DrawRect(new Rect2(px + 4, py + 14, 16, 16), new Color(0.20f, 0.20f, 0.20f, 0.80f));
				// Two chimneys
				DrawRect(new Rect2(px + 20, py + 4, 4, 14), new Color(0.18f, 0.18f, 0.18f, 0.90f));
				DrawRect(new Rect2(px + 26, py + 7, 4, 11), new Color(0.18f, 0.18f, 0.18f, 0.90f));
				// Chimney caps (slightly wider)
				DrawRect(new Rect2(px + 19, py + 3, 6, 2), new Color(0.14f, 0.14f, 0.14f, 0.90f));
				DrawRect(new Rect2(px + 25, py + 6, 6, 2), new Color(0.14f, 0.14f, 0.14f, 0.90f));
				// Control window: orange glow
				DrawRect(new Rect2(px + 6, py + 18, 9, 7), new Color(1.0f, 0.60f, 0.10f, 0.75f));
				break;
			}

			case ZoneType.NuclearPlant:
			{
				// Cooling tower silhouette: wide base tapering inward then out at top
				// Approximated as three stacked rects of varying widths (trapezoid suggestion)
				var towerX = px + 4f;
				var towerY = py + 8f;
				DrawRect(new Rect2(towerX,       towerY + 14f, 24f, 8f), new Color(0.88f, 0.88f, 0.86f, 0.85f)); // wide base
				DrawRect(new Rect2(towerX + 2f,  towerY + 8f,  20f, 8f), new Color(0.88f, 0.88f, 0.86f, 0.85f)); // mid section
				DrawRect(new Rect2(towerX + 4f,  towerY,       16f, 8f), new Color(0.88f, 0.88f, 0.86f, 0.85f)); // narrow top
				// Steam vent opening at very top (dark opening)
				DrawRect(new Rect2(towerX + 7f, towerY,        10f, 4f), new Color(0.30f, 0.30f, 0.30f, 0.70f));
				// Warning stripe at bottom of tower
				DrawRect(new Rect2(towerX, py + 26f, 24f, 4f), new Color(0.92f, 0.72f, 0.0f, 0.60f));
				DrawLine(new Vector2(towerX + 6f, py + 26f), new Vector2(towerX + 6f, py + 30f),
					new Color(0.18f, 0.18f, 0.18f, 0.60f), 1f);
				DrawLine(new Vector2(towerX + 12f, py + 26f), new Vector2(towerX + 12f, py + 30f),
					new Color(0.18f, 0.18f, 0.18f, 0.60f), 1f);
				DrawLine(new Vector2(towerX + 18f, py + 26f), new Vector2(towerX + 18f, py + 30f),
					new Color(0.18f, 0.18f, 0.18f, 0.60f), 1f);
				break;
			}

			case ZoneType.PowerLine:
			{
				// Pylon X-frame: two diagonal crossed lines suggesting a transmission tower
				var lineColor = new Color(0.06f, 0.55f, 0.55f, 0.85f); // slightly darker than the cyan base
				// Cross brace from top-left to bottom-right
				DrawLine(new Vector2(px + 6f,  py + 4f),  new Vector2(px + 26f, py + 28f), lineColor, 1.5f);
				// Cross brace from top-right to bottom-left
				DrawLine(new Vector2(px + 26f, py + 4f),  new Vector2(px + 6f,  py + 28f), lineColor, 1.5f);
				// Vertical center post
				DrawLine(new Vector2(px + 16f, py + 2f),  new Vector2(px + 16f, py + 30f), lineColor, 2f);
				// Top cross-arm (horizontal)
				DrawLine(new Vector2(px + 6f,  py + 8f),  new Vector2(px + 26f, py + 8f),  lineColor, 1.5f);
				// Insulator dots at arm ends and center
				DrawCircle(new Vector2(px + 6f,  py + 8f),  2f, new Color(0.88f, 0.88f, 0.88f, 0.90f));
				DrawCircle(new Vector2(px + 26f, py + 8f),  2f, new Color(0.88f, 0.88f, 0.88f, 0.90f));
				DrawCircle(new Vector2(px + 16f, py + 8f),  2f, new Color(0.88f, 0.88f, 0.88f, 0.90f));
				break;
			}
		}
	}

	// ── Overlay color helpers ───────────────────────────────────────────────

	private static Color HappinessOverlayColor(float h)
	{
		if (h < 0.5f)
			return new Color(1f, 0f, 0f).Lerp(new Color(1f, 1f, 0f), h * 2f) with { A = 0.75f };
		return new Color(1f, 1f, 0f).Lerp(new Color(0.267f, 1f, 0.267f), (h - 0.5f) * 2f) with { A = 0.75f };
	}

	private static Color TrafficOverlayColor(int load)
	{
		if (load <= 0) return Colors.Transparent;
		if (load < 60) return new Color(1f, 1f, 0.267f, 0.133f).Lerp(new Color(1f, 1f, 0f), load / 60f) with { A = 0.6f };
		return new Color(1f, 1f, 0f).Lerp(new Color(1f, 0f, 0f), System.Math.Min((load - 60f) / 40f, 1f)) with { A = 0.8f };
	}

	private static Color CoverageOverlayColor(bool hasRoad, bool hasPower, bool hasService)
	{
		if (!hasRoad)  return new Color(0.15f, 0.15f, 0.15f, 0.70f); // dark grey — no road
		if (!hasPower) return new Color(1f, 0.55f, 0f, 0.70f);        // amber — road but no power
		if (!hasService) return new Color(0f, 0.3f, 0.7f, 0.65f);     // dim blue — needs services
		return new Color(0f, 0.6f, 1f, 0.75f);                        // bright blue — fully covered
	}

	private static Color LandValueOverlayColor(float lv)
	{
		return new Color(0.239f, 0.125f, 0f).Lerp(new Color(1f, 0.843f, 0f), lv) with { A = 0.75f };
	}

	private static Color PollutionOverlayColor(float p)
	{
		if (p < 0.01f) return Colors.Transparent;
		return Colors.Orange.Lerp(new Color(0.545f, 0f, 0f), p) with { A = 0.7f };
	}

	private Color GetOverlayColor(Loopolis.Core.Grid.Tile tile)
	{
		return ActiveOverlay switch
		{
			OverlayMode.Happiness  => HappinessOverlayColor((float)tile.Happiness),
			OverlayMode.Traffic    => TrafficOverlayColor(tile.TrafficLoad),
			OverlayMode.Coverage   => CoverageOverlayColor(
										 tile.HasRoadAccess,
										 tile.HasPower,
										 tile.Happiness > 0.70f),
			OverlayMode.LandValue  => LandValueOverlayColor((float)tile.LandValue),
			OverlayMode.Pollution  => PollutionOverlayColor((float)tile.PollutionLevel),
			_                      => Colors.Transparent,
		};
	}

	private bool IsSameZone(ZoneType zone, int x, int y)
	{
		if (_grid == null) return false;
		if (!_grid.IsInBounds(x, y)) return false;
		return _grid.GetTile(x, y).Zone == zone;
	}

	// ── Procedural building detail drawing ───────────────────────────────────

	/// <summary>
	/// Draws procedural geometric detail for a single building.
	/// <paramref name="ox"/> / <paramref name="oy"/> are the world-pixel origin of the anchor tile.
	/// <paramref name="scale"/> is applied around the tile centre for the spawn animation (normally 1.0).
	/// Delegates all per-type pixel geometry to <see cref="BuildingDrawer.Draw"/>.
	/// </summary>
	private void DrawBuildingDetail(Building building, float ox, float oy, float scale)
	{
		BuildingDrawer.Draw(
			building.TypeId,
			building.Width,
			building.Height,
			ox,
			oy,
			scale,
			TileSize,
			(rect, color) => DrawRect(rect, color),
			(a, b, c, color) => DrawTriangle(a, b, c, color),
			(a, b, color, width) => DrawLine(a, b, color, width),
			(center, radius, color) => DrawCircle(center, radius, color));
	}

	/// <summary>
	/// Runs the building detail pass — draws procedural building geometry for all buildings
	/// in the grid. Only runs when <see cref="ActiveOverlay"/> is <see cref="OverlayMode.None"/>.
	/// Spawn animation: tiles in <see cref="_buildingSpawn"/> get a scale transform.
	/// </summary>
	private void DrawAllBuildingDetails()
	{
		if (_grid == null) return;
		if (ActiveOverlay != OverlayMode.None) return;

		foreach (var building in _grid.Buildings.Values)
		{
			float ox = building.AnchorX * TileSize;
			float oy = building.AnchorY * TileSize;

			// Determine spawn animation scale.
			// Use the anchor tile's progress; if absent, scale = 1.0 (fully shown).
			var anchorKey = new Vector2I(building.AnchorX, building.AnchorY);
			var scale = _buildingSpawn.TryGetValue(anchorKey, out var progress)
				? SpawnBounceScale(Mathf.Clamp(progress, 0f, 1f))
				: 1.0f;

			DrawBuildingDetail(building, ox, oy, scale);
		}
	}

	/// <summary>
	/// Draws fading smoke particles at their current world positions.
	/// </summary>
	private void DrawSmokeParticles()
	{
		foreach (var p in _smoke)
		{
			var alpha = 1.0f - (p.Age / p.MaxAge);
			var radius = p.BaseRadius + p.Age * 4f;
			var color = new Color(0.55f, 0.52f, 0.50f, alpha * 0.75f);
			DrawCircle(p.WorldPos, radius, color);
		}
	}

	// ── Icon mode helpers ────────────────────────────────────────────────────

	/// <summary>
	/// Returns a short ASCII label for a zone type, used in icon mode (zoom ≤ 0.5×).
	/// Returns empty string for zones that don't need a label (road, power line, empty).
	/// </summary>
	private static string GetZoneLabel(ZoneType zone) => zone switch
	{
		ZoneType.Residential  => "R",
		ZoneType.Commercial   => "C",
		ZoneType.Industrial   => "I",
		ZoneType.Park         => "P",
		ZoneType.FireStation  => "F",
		ZoneType.FireHQ       => "F",
		ZoneType.PoliceStation=> "P",
		ZoneType.PoliceHQ     => "P",
		ZoneType.School       => "S",
		ZoneType.Hospital     => "H",
		ZoneType.CoalPlant    => "E",
		ZoneType.PowerPlant   => "E",
		ZoneType.NuclearPlant => "E",
		_                     => "",
	};

	/// <summary>
	/// Returns the flat background color for a zone type, used in icon mode (zoom ≤ 0.5×).
	/// Matches the primary zone palette so the map is still readable at a glance.
	/// </summary>
	private static Color GetZoneIconColor(ZoneType zone) => zone switch
	{
		ZoneType.Residential   => new Color(0.30f, 0.65f, 0.30f),
		ZoneType.Commercial    => new Color(0.30f, 0.45f, 0.75f),
		ZoneType.Industrial    => new Color(0.70f, 0.52f, 0.25f),
		ZoneType.Park          => new Color(0.30f, 0.72f, 0.25f),
		ZoneType.Road          => new Color(0.45f, 0.45f, 0.45f),
		ZoneType.Avenue        => new Color(0.52f, 0.52f, 0.62f),
		ZoneType.PowerLine     => new Color(0.10f, 0.80f, 0.80f),
		ZoneType.PowerPlant    => new Color(0.26f, 0.26f, 0.26f),
		ZoneType.CoalPlant     => new Color(0.26f, 0.26f, 0.26f),
		ZoneType.NuclearPlant  => new Color(0.97f, 0.66f, 0.14f),
		ZoneType.FireStation   => new Color(1.0f, 0.40f, 0.10f),
		ZoneType.FireHQ        => new Color(0.72f, 0.11f, 0.11f),
		ZoneType.PoliceStation => new Color(0.20f, 0.40f, 1.0f),
		ZoneType.PoliceHQ      => new Color(0.10f, 0.14f, 0.49f),
		ZoneType.School        => new Color(0.70f, 0.30f, 0.90f),
		ZoneType.Hospital      => new Color(0.65f, 0.84f, 0.65f),
		_                      => new Color(0.15f, 0.15f, 0.15f),
	};

	/// <summary>
	/// Draws the simplified icon-mode view of the entire grid.
	/// Called by _Draw() when _currentZoom ≤ 0.5f.
	/// Each tile gets a flat zone-color fill; non-empty zones get a 1-letter ASCII label.
	/// Roads are drawn as solid grey rects (no detail). Overlays still apply on top.
	/// </summary>
	private void DrawIconMode()
	{
		if (_grid == null) return;

		var font     = ThemeDB.FallbackFont;
		const int fontSize = 8;
		const float halfTile = TileSize * 0.5f;

		foreach (var tile in _grid.AllTiles())
		{
			float px = tile.X * TileSize;
			float py = tile.Y * TileSize;
			var tileRect = new Rect2(px, py, TileSize, TileSize);

			if (tile.Zone == ZoneType.Empty)
			{
				// Empty terrain: use height-based color (same as detailed mode)
				var height = GetHeight(tile.X, tile.Y);
				var baseColor = height switch
				{
					<= 0 => ColorDeepWater,
					1    => ColorLowland,
					2    => ColorMidland,
					3    => ColorHighland,
					4    => ColorUpland,
					_    => ColorPeak,
				};
				DrawRect(tileRect, baseColor);
				continue;
			}

			// Flat zone fill
			var zoneColor = GetZoneIconColor(tile.Zone);
			DrawRect(tileRect, zoneColor);

			// Unpowered overlay (skip for roads/power lines/parks)
			if (!tile.HasPower
				&& tile.Zone is not ZoneType.Road
				&& tile.Zone is not ZoneType.Avenue
				&& tile.Zone is not ZoneType.PowerLine
				&& tile.Zone is not ZoneType.Park)
			{
				DrawRect(tileRect, UnpoweredTint);
			}

			// Single-letter label centered on tile (skip road/avenue/power line — no label needed)
			var label = GetZoneLabel(tile.Zone);
			if (label.Length > 0)
			{
				// Center a small 8px label on the tile
				var labelPos = new Vector2(px + halfTile - 3f, py + halfTile + 3f);
				DrawString(font, labelPos, label, HorizontalAlignment.Left, -1, fontSize,
					new Color(1f, 1f, 1f, 0.85f));
			}
		}

		// Overlay pass (same logic as detailed mode — still useful when zoomed out)
		if (ActiveOverlay != OverlayMode.None)
		{
			foreach (var t in _grid.AllTiles())
			{
				if (t.Zone == ZoneType.Empty) continue;
				if (ActiveOverlay == OverlayMode.Traffic &&
					t.Zone != ZoneType.Road && t.Zone != ZoneType.Avenue) continue;
				if (ActiveOverlay == OverlayMode.Coverage &&
					t.Zone != ZoneType.Residential && t.Zone != ZoneType.Commercial && t.Zone != ZoneType.Industrial)
					continue;
				if (ActiveOverlay == OverlayMode.Happiness && t.Zone == ZoneType.Park) continue;
				if (ActiveOverlay == OverlayMode.Pollution  && t.Zone == ZoneType.Park) continue;
				if (ActiveOverlay == OverlayMode.LandValue &&
					(t.Zone == ZoneType.Road || t.Zone == ZoneType.Avenue ||
					 t.Zone == ZoneType.PowerLine || t.Zone == ZoneType.PowerPlant ||
					 t.Zone == ZoneType.CoalPlant || t.Zone == ZoneType.NuclearPlant))
					continue;

				var overlayColor = GetOverlayColor(t);
				if (overlayColor.A > 0.01f)
					DrawRect(new Rect2(t.X * TileSize, t.Y * TileSize, TileSize, TileSize), overlayColor);
			}
		}

		// Brownout overlay (same as detailed mode)
		if (_isBrownout)
		{
			foreach (var tile in _grid.AllTiles())
			{
				if (!tile.HasPower) continue;
				if (tile.Zone is ZoneType.Empty or ZoneType.Road or ZoneType.PowerLine
					or ZoneType.PowerPlant or ZoneType.CoalPlant or ZoneType.NuclearPlant
					or ZoneType.Park)
					continue;
				DrawRect(new Rect2(tile.X * TileSize, tile.Y * TileSize, TileSize, TileSize), BrownoutTint);
			}
		}

		// Coverage highlight (same as detailed mode)
		if (_coverageHighlight.Count > 0)
		{
			var overlayColor = new Color(_coverageColor.R, _coverageColor.G, _coverageColor.B, 0.3f);
			foreach (var (cx, cy) in _coverageHighlight)
				DrawRect(new Rect2(cx * TileSize, cy * TileSize, TileSize, TileSize), overlayColor);
		}

		// Fire tile (same as detailed mode — still important to see at any zoom)
		if (_fireTileX >= 0 && _fireTileY >= 0)
		{
			var pulse = (float)(0.60 + 0.25 * Math.Sin(Time.GetTicksMsec() / 250.0));
			var fireColor = new Color(FireOverlay.R, FireOverlay.G, FireOverlay.B, pulse);
			DrawRect(new Rect2(_fireTileX * TileSize, _fireTileY * TileSize, TileSize, TileSize), fireColor);
			QueueRedraw();
		}

		// Rectangle paint preview (same as detailed mode)
		if (_hasRectPreview)
		{
			var minX = Mathf.Min(_rectPreviewStart.X, _rectPreviewEnd.X);
			var maxX = Mathf.Max(_rectPreviewStart.X, _rectPreviewEnd.X);
			var minY = Mathf.Min(_rectPreviewStart.Y, _rectPreviewEnd.Y);
			var maxY = Mathf.Max(_rectPreviewStart.Y, _rectPreviewEnd.Y);
			var rx = minX * TileSize;
			var ry = minY * TileSize;
			var rw = (maxX - minX + 1) * TileSize;
			var rh = (maxY - minY + 1) * TileSize;
			DrawRect(new Rect2(rx, ry, rw, rh), _rectPreviewColor);
			var borderC = new Color(_rectPreviewColor.R, _rectPreviewColor.G, _rectPreviewColor.B, 0.9f);
			const int previewBorderW = 2;
			DrawRect(new Rect2(rx,            ry,                        rw, previewBorderW), borderC);
			DrawRect(new Rect2(rx,            ry + rh - previewBorderW,  rw, previewBorderW), borderC);
			DrawRect(new Rect2(rx,            ry,                        previewBorderW, rh), borderC);
			DrawRect(new Rect2(rx + rw - previewBorderW, ry,             previewBorderW, rh), borderC);
		}
	}

	public override void _Draw()
	{
		if (_grid == null) return;

		// ── Icon mode: simplified rendering at low zoom ──────────────────────
		if (_currentZoom <= 0.5f)
		{
			DrawIconMode();
			return;
		}

		foreach (var tile in _grid.AllTiles())
		{
			float px = tile.X * TileSize;
			float py = tile.Y * TileSize;

			Color color;
			switch (tile.Zone)
			{
				case ZoneType.Residential:
				case ZoneType.Commercial:
				case ZoneType.Industrial:
				{
					// Scale brightness with population fill level
					// Special case: unpowered res_house_1x1 gets a muted grey-green tint
					// to distinguish "functional but limited capacity" from powered full-capacity.
					var isCottageUnpowered = !tile.HasPower
						&& tile.Zone == ZoneType.Residential
						&& tile.BuildingId != null
						&& _grid.Buildings.TryGetValue(tile.BuildingId, out var _thisBldg)
						&& _thisBldg.TypeId == "res_house_1x1";

					// Resolve the building TypeId for zone-specific palette entries.
					string? _bldgTypeId = null;
					if (tile.BuildingId != null
						&& _grid.Buildings.TryGetValue(tile.BuildingId, out var _bldgForColor))
						_bldgTypeId = _bldgForColor.TypeId;

					var baseColor = tile.Zone switch
					{
						ZoneType.Residential when isCottageUnpowered              => ColorCottageUnpowered,
						ZoneType.Residential when _bldgTypeId == "res_highrise_6x6" => ColorHighrise,
						ZoneType.Residential                                        => ColorResidential,
						ZoneType.Commercial  when _bldgTypeId == "com_office_4x4"   => ColorOffice,
						ZoneType.Commercial                                         => ColorCommercial,
						ZoneType.Industrial  when _bldgTypeId == "ind_mill_2x2"     => ColorIndustrialMill,
						ZoneType.Industrial  when _bldgTypeId == "ind_quarry_2x2"   => ColorIndustrialQuarry,
						ZoneType.Industrial  when _bldgTypeId == "ind_complex_4x4"  => ColorIndComplex,
						_                                                           => ColorIndustrial,
					};
					// Apply subtle height-based brightness modifier before fill lerp
					var heightTinted = ApplyHeightTintToZoneColor(baseColor, GetHeight(tile.X, tile.Y));
					var fillFraction = Mathf.Clamp(tile.Population / 50f, 0f, 1f);
					var emptyColor = heightTinted * 0.35f;
					color = emptyColor.Lerp(heightTinted, fillFraction);

					// Fill full tile — no gap between same-zone neighbours
					var fullRect = new Rect2(px, py, TileSize, TileSize);
					DrawRect(fullRect, color);

					// Draw dark border only on edges that face a different zone (cluster boundary)
					var borderColor = color * 0.45f;
					const int borderW = 2;

					bool adjLeft  = IsSameZone(tile.Zone, tile.X - 1, tile.Y);
					bool adjRight = IsSameZone(tile.Zone, tile.X + 1, tile.Y);
					bool adjUp    = IsSameZone(tile.Zone, tile.X,     tile.Y - 1);
					bool adjDown  = IsSameZone(tile.Zone, tile.X,     tile.Y + 1);

					if (!adjLeft)  DrawRect(new Rect2(px,                      py,      borderW, TileSize), borderColor);
					if (!adjRight) DrawRect(new Rect2(px + TileSize - borderW, py,      borderW, TileSize), borderColor);
					if (!adjUp)    DrawRect(new Rect2(px,  py,                 TileSize, borderW),           borderColor);
					if (!adjDown)  DrawRect(new Rect2(px,  py + TileSize - borderW, TileSize, borderW),      borderColor);

					// Density-based inner building rectangle
					if (fillFraction > 0.25f)
					{
						var buildingScale = Mathf.Lerp(0.4f, 0.75f, (fillFraction - 0.25f) / 0.75f);
						var margin = (int)(TileSize * (1f - buildingScale) / 2f);
						var buildingRect = new Rect2(
							px + margin, py + margin,
							TileSize - margin * 2, TileSize - margin * 2
						);
						var buildingColor = color * 1.25f;
						buildingColor.A = 1f;
						DrawRect(buildingRect, buildingColor);
					}

					// Dark overlay on zones that are zoned but not powered.
					// Exception: unpowered res_house_1x1 uses a dedicated fill color (ColorCottageUnpowered)
					// so we skip the generic dark overlay — it would make the muted tint unreadable.
					if (!tile.HasPower && !isCottageUnpowered)
						DrawRect(fullRect, UnpoweredTint);

					// Dashed amber border on zones with no road access — these cost
					// maintenance every tick but can never develop a building.
					if (!tile.HasRoadAccess)
						DrawDashedBorder(px, py, TileSize, IdleBorderColor, dashLen: 4f, gapLen: 4f, width: 2f);

					// Red semi-transparent overlay for pollution
					if (tile.PollutionLevel > 0.05f)
					{
						var pollutionColor = new Color(1f, 0f, 0f, (float)tile.PollutionLevel * 0.55f);
						DrawRect(fullRect, pollutionColor);
					}

					// Yellow dot in corner for residential tiles with demand boost
					if (tile.HasDemandBoost && tile.Zone == ZoneType.Residential)
					{
						var dotRect = new Rect2(px + TileSize - 7, py + TileSize - 7, 5, 5);
						DrawRect(dotRect, new Color(1f, 0.9f, 0.1f, 0.8f));
					}

					// Cliff edges: draw dark brown lines on edges bordering tiles with height diff > 1
					DrawZonedCliffEdges(tile.X, tile.Y, px, py);

					continue;
				}
				case ZoneType.Road:
				case ZoneType.Avenue:
				{
					var roadColor = tile.Zone == ZoneType.Avenue ? ColorAvenue : ColorRoad;
					var roadFull = new Rect2(px, py, TileSize, TileSize);
					DrawRect(roadFull, roadColor);

					// Darker border only on non-road/avenue edges
					bool rLeft  = IsSameZone(ZoneType.Road, tile.X - 1, tile.Y) || IsSameZone(ZoneType.Avenue, tile.X - 1, tile.Y);
					bool rRight = IsSameZone(ZoneType.Road, tile.X + 1, tile.Y) || IsSameZone(ZoneType.Avenue, tile.X + 1, tile.Y);
					bool rUp    = IsSameZone(ZoneType.Road, tile.X,     tile.Y - 1) || IsSameZone(ZoneType.Avenue, tile.X, tile.Y - 1);
					bool rDown  = IsSameZone(ZoneType.Road, tile.X,     tile.Y + 1) || IsSameZone(ZoneType.Avenue, tile.X, tile.Y + 1);
					var roadEdge = roadColor * 0.55f;

					if (!rLeft)  DrawRect(new Rect2(px,                  py, 2, TileSize), roadEdge);
					if (!rRight) DrawRect(new Rect2(px + TileSize - 2,   py, 2, TileSize), roadEdge);
					if (!rUp)    DrawRect(new Rect2(px, py,               TileSize, 2),    roadEdge);
					if (!rDown)  DrawRect(new Rect2(px, py + TileSize - 2, TileSize, 2),   roadEdge);

					// Avenue: draw a white center stripe to distinguish from Road
					if (tile.Zone == ZoneType.Avenue)
					{
						var stripeColor = new Color(1f, 1f, 1f, 0.25f);
						// Determine if mostly horizontal or vertical road
						bool hasHorizNeighbour = rLeft || rRight;
						bool hasVertNeighbour  = rUp   || rDown;
						if (hasHorizNeighbour && !hasVertNeighbour)
							DrawRect(new Rect2(px, py + TileSize / 2 - 1, TileSize, 2), stripeColor);
						else if (hasVertNeighbour && !hasHorizNeighbour)
							DrawRect(new Rect2(px + TileSize / 2 - 1, py, 2, TileSize), stripeColor);
						else
						{
							// Intersection or isolated — draw both stripes
							DrawRect(new Rect2(px, py + TileSize / 2 - 1, TileSize, 2), stripeColor);
							DrawRect(new Rect2(px + TileSize / 2 - 1, py, 2, TileSize), stripeColor);
						}
					}

					// ── Lane markings ────────────────────────────────────────────
					// Road: dashed white center line along the tile's primary axis.
					// Avenue: double yellow center lines (highway-style).
					{
						bool hasHoriz = rLeft || rRight;
						bool hasVert  = rUp   || rDown;
						// Prefer the primary axis; for isolated/intersection tiles draw both.
						bool drawH = hasHoriz || (!hasHoriz && !hasVert);
						bool drawV = hasVert  || (!hasHoriz && !hasVert);

						if (tile.Zone == ZoneType.Road)
						{
							// 3 short white dashes: 6px dash, 4px gap, centered on the tile axis.
							var dashColor = new Color(1f, 1f, 1f, 0.45f);
							const float dashLen = 6f;
							const float dashGap = 4f;
							const float dashPeriod = dashLen + dashGap;
							// Start offset so dashes are centered (3 dashes + 2 gaps = 26px, offset = (32-26)/2 = 3)
							const float dashStart = 3f;
							if (drawH && !drawV)
							{
								float cy2 = py + TileSize * 0.5f;
								for (var d = 0; d < 3; d++)
								{
									float x0 = px + dashStart + d * dashPeriod;
									DrawLine(new Vector2(x0, cy2), new Vector2(x0 + dashLen, cy2), dashColor, 1.5f);
								}
							}
							else if (drawV && !drawH)
							{
								float cx2 = px + TileSize * 0.5f;
								for (var d = 0; d < 3; d++)
								{
									float y0 = py + dashStart + d * dashPeriod;
									DrawLine(new Vector2(cx2, y0), new Vector2(cx2, y0 + dashLen), dashColor, 1.5f);
								}
							}
							// Intersections: skip lane markings (they would overlap confusingly)
						}
						else if (tile.Zone == ZoneType.Avenue)
						{
							// Double yellow center lines, 3px apart, 2px wide each.
							var yellowLine = new Color(1.0f, 0.88f, 0.10f, 0.55f);
							const float lineOffset = 1.5f; // half-separation from center
							if (drawH && !drawV)
							{
								float cy2 = py + TileSize * 0.5f;
								DrawLine(new Vector2(px, cy2 - lineOffset), new Vector2(px + TileSize, cy2 - lineOffset), yellowLine, 1.5f);
								DrawLine(new Vector2(px, cy2 + lineOffset), new Vector2(px + TileSize, cy2 + lineOffset), yellowLine, 1.5f);
							}
							else if (drawV && !drawH)
							{
								float cx2 = px + TileSize * 0.5f;
								DrawLine(new Vector2(cx2 - lineOffset, py), new Vector2(cx2 - lineOffset, py + TileSize), yellowLine, 1.5f);
								DrawLine(new Vector2(cx2 + lineOffset, py), new Vector2(cx2 + lineOffset, py + TileSize), yellowLine, 1.5f);
							}
							// Intersections: skip lane markings
						}
					}

					// Border connection tile: yellow downward triangle + yellow border outline
					if (tile.IsBorderConnection)
					{
						var borderYellow = new Color(1f, 0.85f, 0f, 1f);

						// Thin yellow 2px border around the tile edges
						DrawRect(new Rect2(px,                  py, TileSize, 2),           borderYellow); // top
						DrawRect(new Rect2(px,                  py + TileSize - 2, TileSize, 2), borderYellow); // bottom
						DrawRect(new Rect2(px,                  py, 2, TileSize),           borderYellow); // left
						DrawRect(new Rect2(px + TileSize - 2,   py, 2, TileSize),           borderYellow); // right

						// Downward-pointing triangle centered on tile, ~40% of tile size
						const float triSize = TileSize * 0.40f;
						var cx = px + TileSize * 0.5f;
						var cy = py + TileSize * 0.5f;
						var triTop    = cy - triSize * 0.5f;
						var triBottom = cy + triSize * 0.5f;
						var triLeft   = cx - triSize * 0.5f;
						var triRight  = cx + triSize * 0.5f;
						// Triangle: top-left, top-right, bottom-center (points downward)
						DrawTriangle(
							new Vector2(triLeft,  triTop),
							new Vector2(triRight, triTop),
							new Vector2(cx,       triBottom),
							borderYellow);
					}

					// Traffic load dots: show congestion level on road/avenue tiles
					DrawTrafficDots(tile.TrafficLoad, px, py);

					// Road pulse: white flash overlay that fades out over 0.4 s
					var tilePos = new Vector2I(tile.X, tile.Y);
					if (_roadPulse.TryGetValue(tilePos, out var pulseRemaining))
					{
						var pulseRatio = pulseRemaining / RoadPulseDuration;
						var pulseColor = new Color(1f, 1f, 1f, pulseRatio * 0.5f);
						DrawRect(new Rect2(px, py, TileSize, TileSize), pulseColor);
					}

					continue;
				}
				case ZoneType.PowerPlant:   // legacy alias — renders same as CoalPlant
				case ZoneType.CoalPlant:
					color = ColorCoalPlant;
					break;
				case ZoneType.NuclearPlant:
					color = ColorNuclearPlant;
					break;
				case ZoneType.PowerLine:
					color = ColorPowerLine;
					break;
				case ZoneType.FireStation:
					color = ColorFireStation;
					break;
				case ZoneType.FireHQ:
					color = ColorFireHQ;
					break;
				case ZoneType.PoliceStation:
					color = ColorPoliceStation;
					break;
				case ZoneType.PoliceHQ:
					color = ColorPoliceHQ;
					break;
				case ZoneType.School:
					color = ColorSchool;
					break;
				case ZoneType.Hospital:
					color = ColorHospital;
					break;
				case ZoneType.Park:
				{
					// Vibrant grass green fill — no building density rect, no unpowered tint.
					// Parks never develop buildings and never need power or road access.
					var parkFull = new Rect2(px, py, TileSize, TileSize);
					DrawRect(parkFull, ColorPark);

					// Outline only on edges that face a different zone (cluster boundary)
					bool pLeft  = IsSameZone(ZoneType.Park, tile.X - 1, tile.Y);
					bool pRight = IsSameZone(ZoneType.Park, tile.X + 1, tile.Y);
					bool pUp    = IsSameZone(ZoneType.Park, tile.X,     tile.Y - 1);
					bool pDown  = IsSameZone(ZoneType.Park, tile.X,     tile.Y + 1);
					const int parkBorderW = 2;
					if (!pLeft)  DrawRect(new Rect2(px,                          py, parkBorderW, TileSize), ColorParkOutline);
					if (!pRight) DrawRect(new Rect2(px + TileSize - parkBorderW, py, parkBorderW, TileSize), ColorParkOutline);
					if (!pUp)    DrawRect(new Rect2(px, py,                          TileSize, parkBorderW), ColorParkOutline);
					if (!pDown)  DrawRect(new Rect2(px, py + TileSize - parkBorderW, TileSize, parkBorderW), ColorParkOutline);

					// Small tree dot in center to distinguish parks from empty grass terrain
					var treeDotSize = 6f;
					DrawRect(new Rect2(
						px + (TileSize - treeDotSize) * 0.5f,
						py + (TileSize - treeDotSize) * 0.5f,
						treeDotSize, treeDotSize), ColorParkOutline);

					DrawZonedCliffEdges(tile.X, tile.Y, px, py);
					continue;
				}
				default:
				{
					// Empty tile: height-based gradient rendering with cliff edges, plateau highlight, forest overlay
					DrawHeightTile(tile.X, tile.Y, px, py);

					if (ActiveOverlay == OverlayMode.None)
					{
						var emptyHeight = GetHeight(tile.X, tile.Y);

						// ── Tree sprites on forest tiles ────────────────────────────
						var isForestTile = _forestMap != null
							&& tile.X >= 0 && tile.X < _forestMap.GetLength(0)
							&& tile.Y >= 0 && tile.Y < _forestMap.GetLength(1)
							&& _forestMap[tile.X, tile.Y];

						if (isForestTile && emptyHeight > 0)
						{
							// 3 deterministic tree positions within the tile
							var tx = (int)px;
							var ty = (int)py;
							var positions = new[]
							{
								new Vector2(tx + 8,  ty + 20),  // bottom-left tree
								new Vector2(tx + 24, ty + 22),  // bottom-right tree
								new Vector2(tx + 16, ty + 10),  // top-center tree (back)
							};

							foreach (var pos in positions)
							{
								// Trunk: thin vertical line
								DrawLine(pos, pos + new Vector2(0, 6), new Color(0.32f, 0.20f, 0.10f), 1f);
								// Foliage: filled circle
								DrawCircle(pos, 5f, new Color(0.22f, 0.52f, 0.18f));
								// Highlight arc on foliage
								DrawArc(pos, 5f, -2.5f, 0.5f, 6, new Color(0.38f, 0.68f, 0.28f, 0.7f), 1f);
							}
						}

						// ── Rocky hatch on elevated tiles with no zone ──────────────
						if (emptyHeight >= 2 && !isForestTile)
						{
							for (var i = 0; i < 4; i++)
							{
								var ox2 = px + 4 + i * 8;
								DrawLine(new Vector2(ox2, py + 18), new Vector2(ox2 + 6, py + 26),
									new Color(0.60f, 0.57f, 0.52f, 0.5f), 1f);
							}
						}
					}

					continue;
				}
			}

			// Service buildings and utility tiles
			var rect = new Rect2(px, py, TileSize, TileSize);
			DrawRect(rect, color);

			// Procedural art overlay for service and utility zones
			if (ActiveOverlay == OverlayMode.None && _currentZoom > 0.5f)
				DrawServiceZoneArt(tile.Zone, px, py);
		}

		// ── Procedural building detail pass ─────────────────────────────────
		// Drawn after all base fills so geometry sits on top of the zone background.
		// Skipped when any overlay is active (details would be obscured anyway).
		DrawAllBuildingDetails();

		// Brownout overlay: amber tint on all BFS-powered tiles when capacity < demand.
		// This is a different/weaker signal than the existing unpowered dark tint.
		if (_isBrownout && _grid != null)
		{
			foreach (var tile in _grid.AllTiles())
			{
				if (!tile.HasPower) continue;
				// Only overlay zoned tiles — roads/plants/terrain/parks don't get the tint
				if (tile.Zone is ZoneType.Empty or ZoneType.Road or ZoneType.PowerLine
					or ZoneType.PowerPlant or ZoneType.CoalPlant or ZoneType.NuclearPlant
					or ZoneType.Park)
					continue;
				var rect = new Rect2(tile.X * TileSize, tile.Y * TileSize, TileSize, TileSize);
				DrawRect(rect, BrownoutTint);
			}
		}

		// Coverage radius overlay: draw semi-transparent color over highlighted tiles
		if (_coverageHighlight.Count > 0)
		{
			var overlayColor = new Color(_coverageColor.R, _coverageColor.G, _coverageColor.B, 0.3f);
			foreach (var (cx, cy) in _coverageHighlight)
			{
				var rect = new Rect2(cx * TileSize, cy * TileSize, TileSize, TileSize);
				DrawRect(rect, overlayColor);
			}
		}

		// Multi-tile building outlines: draw a bright border around footprints larger than 1x1
		if (_grid != null)
		{
			foreach (var building in _grid.Buildings.Values)
			{
				if (building.TileCount <= 1) continue; // skip 1x1 buildings

				var borderColor = building.Zone switch
				{
					// High-rise gets a brighter cyan-blue outline to stand out from apartment
					ZoneType.Residential when building.TypeId == "res_highrise_6x6" => new Color(0.4f, 0.8f, 1.0f, 0.90f), // cyan-blue
					ZoneType.Residential => new Color(0.0f, 1.0f, 0.3f, 0.85f),   // bright green
					// Office tower gets a crisper steel-blue outline
					ZoneType.Commercial  when building.TypeId == "com_office_4x4"  => new Color(0.6f, 0.8f, 1.0f, 0.90f), // steel blue
					ZoneType.Commercial  => new Color(0.3f, 0.7f, 1.0f, 0.85f),   // bright blue
					// Terrain-specific industrial outlines match the fill palette
					ZoneType.Industrial when building.TypeId == "ind_mill_2x2"     => new Color(0.55f, 0.85f, 0.30f, 0.90f), // leafy green
					ZoneType.Industrial when building.TypeId == "ind_quarry_2x2"   => new Color(0.78f, 0.76f, 0.72f, 0.90f), // stone white
					ZoneType.Industrial when building.TypeId == "ind_complex_4x4"  => new Color(0.80f, 0.76f, 0.68f, 0.90f), // warm grey
					ZoneType.Industrial  => new Color(1.0f, 0.9f, 0.0f, 0.85f),   // bright yellow (default)
					_                    => new Color(1.0f, 1.0f, 1.0f, 0.85f),
				};

				const int outlineW = 3;
				float bx = building.AnchorX * TileSize;
				float by = building.AnchorY * TileSize;
				float bw = building.Width  * TileSize;
				float bh = building.Height * TileSize;

				// Draw four border edges (top, bottom, left, right)
				DrawRect(new Rect2(bx,                by,                bw, outlineW), borderColor);      // top
				DrawRect(new Rect2(bx,                by + bh - outlineW, bw, outlineW), borderColor);     // bottom
				DrawRect(new Rect2(bx,                by,                outlineW, bh), borderColor);      // left
				DrawRect(new Rect2(bx + bw - outlineW, by,                outlineW, bh), borderColor);    // right
			}
		}

		// ── Smoke particles ──────────────────────────────────────────────────
		// Drawn above building detail and outlines, below UI overlays.
		if (ActiveOverlay == OverlayMode.None)
			DrawSmokeParticles();

		// ── Overlay mode pass ────────────────────────────────────────────────
		// Drawn after all base tiles so the overlay tint covers everything uniformly.
		if (ActiveOverlay != OverlayMode.None && _grid != null)
		{
			foreach (var t in _grid.AllTiles())
			{
				if (t.Zone == ZoneType.Empty) continue; // skip empty terrain for all overlays
				// Traffic overlay only meaningful for road tiles; skip zone tiles
				if (ActiveOverlay == OverlayMode.Traffic &&
					t.Zone != ZoneType.Road && t.Zone != ZoneType.Avenue) continue;
				// Coverage overlay only meaningful for zoned (R/C/I) tiles; skip parks
				if (ActiveOverlay == OverlayMode.Coverage &&
					t.Zone != ZoneType.Residential && t.Zone != ZoneType.Commercial && t.Zone != ZoneType.Industrial)
					continue;
				// Happiness overlay: skip parks (they boost others but have no happiness of their own)
				if (ActiveOverlay == OverlayMode.Happiness && t.Zone == ZoneType.Park) continue;
				// Pollution overlay: parks are always clean — skip them entirely (no tint)
				if (ActiveOverlay == OverlayMode.Pollution && t.Zone == ZoneType.Park) continue;
				// Land value: skip water, roads, utilities
				if (ActiveOverlay == OverlayMode.LandValue &&
					(t.Zone == ZoneType.Road || t.Zone == ZoneType.Avenue ||
					 t.Zone == ZoneType.PowerLine || t.Zone == ZoneType.PowerPlant ||
					 t.Zone == ZoneType.CoalPlant || t.Zone == ZoneType.NuclearPlant))
					continue;

				var overlayColor = GetOverlayColor(t);
				if (overlayColor.A > 0.01f)
				{
					var overlayRect = new Rect2(t.X * TileSize, t.Y * TileSize, TileSize, TileSize);
					DrawRect(overlayRect, overlayColor);
				}
			}
		}

		// ── Neglect warning pulse (standalone mode only) ────────────────────
		// Pulsing or solid amber ring on residential tiles with service neglect.
		if (_neglectMap != null && _grid != null)
		{
			var timeSec = Time.GetTicksMsec() / 1000.0f;
			foreach (var t in _grid.AllTiles())
			{
				if (t.Zone != ZoneType.Residential) continue;
				if (t.BuildingId == null) continue; // only buildings show neglect warnings

				var nx = t.X;
				var ny = t.Y;
				if (nx < 0 || nx >= _neglectMap.GetLength(0)) continue;
				if (ny < 0 || ny >= _neglectMap.GetLength(1)) continue;
				var neglect = _neglectMap[nx, ny];
				if (neglect < 0.10f) continue;

				float alpha;
				if (neglect > 0.16f)
				{
					// High neglect: solid amber ring, always visible
					alpha = 0.90f;
				}
				else
				{
					// Mid neglect: pulsing amber ring (oscillates between 0.25 and 0.85)
					alpha = 0.55f + 0.30f * Mathf.Sin(timeSec * Mathf.Pi * 2f);
				}

				var ringColor = new Color(1f, 0.55f, 0f, alpha);
				float px = t.X * TileSize;
				float py = t.Y * TileSize;
				const float ringW = 2.5f;
				DrawRect(new Rect2(px,                        py,              TileSize, ringW), ringColor); // top
				DrawRect(new Rect2(px,                        py + TileSize - ringW, TileSize, ringW), ringColor); // bottom
				DrawRect(new Rect2(px,                        py,              ringW,    TileSize), ringColor); // left
				DrawRect(new Rect2(px + TileSize - ringW,     py,              ringW,    TileSize), ringColor); // right
			}

			// Queue another redraw so the pulse animation continues
			QueueRedraw();
		}

		// ── Growth blocker badges for bare zone tiles (visible at normal zoom) ─
		// Small geometric corner badges on undeveloped zone tiles so players can
		// tell at a glance why a zone isn't growing without having to hover.
		// Badges are only drawn in OverlayMode.None, only on tiles without a building,
		// and are suppressed when zoomed out below 0.5× (sub-pixel size, visual noise).
		if (_grid != null && ActiveOverlay == OverlayMode.None)
		{
			// Check camera zoom — skip badges below 0.5× zoom
			var camZoom = GetViewport().GetCamera2D()?.Zoom.X ?? 1f;
			if (camZoom >= 0.5f)
			{
				foreach (var t in _grid.AllTiles())
				{
					if (t.Zone != ZoneType.Residential && t.Zone != ZoneType.Commercial && t.Zone != ZoneType.Industrial)
						continue;
					if (t.BuildingId != null) continue; // building exists — badge not needed

					float px = t.X * TileSize;
					float py = t.Y * TileSize;

					if (!t.HasRoadAccess)
					{
						// No road: small grey road-hint square at bottom-left corner
						// 6×6 px dark rect with 1 px white border — subtle, not alarming
						const float badgeSize = 6f;
						const float margin = 2f;
						var bx = px + margin;
						var by = py + TileSize - margin - badgeSize;
						DrawRect(new Rect2(bx, by, badgeSize, badgeSize),
							new Color(0.18f, 0.18f, 0.18f, 0.75f));
						// White 1 px border
						DrawRect(new Rect2(bx,                        by,                        badgeSize, 1f),
							new Color(1f, 1f, 1f, 0.55f));
						DrawRect(new Rect2(bx,                        by + badgeSize - 1f,       badgeSize, 1f),
							new Color(1f, 1f, 1f, 0.55f));
						DrawRect(new Rect2(bx,                        by,                        1f, badgeSize),
							new Color(1f, 1f, 1f, 0.55f));
						DrawRect(new Rect2(bx + badgeSize - 1f,       by,                        1f, badgeSize),
							new Color(1f, 1f, 1f, 0.55f));
					}
					else if (!t.HasPower)
					{
						// No power: tiny 3-segment zig-zag line at bottom-right corner
						// Draws a small lightning-bolt silhouette in muted grey
						const float margin = 2f;
						var bx = px + TileSize - margin - 6f;
						var by = py + TileSize - margin - 6f;
						var zigColor = new Color(0.5f, 0.5f, 0.5f, 0.65f);
						// Zig-zag: top-right → middle-left → middle-right → bottom-left
						DrawLine(new Vector2(bx + 5f, by),       new Vector2(bx + 2f, by + 3f), zigColor, 1.2f);
						DrawLine(new Vector2(bx + 2f, by + 3f),  new Vector2(bx + 4f, by + 3f), zigColor, 1.2f);
						DrawLine(new Vector2(bx + 4f, by + 3f),  new Vector2(bx + 1f, by + 6f), zigColor, 1.2f);
					}
					// Tiles with both road and power (eligible to grow) get no badge —
					// the absence of any badge signals readiness.
				}
			}
		}

		// ── Upgrade-ready badge pass ─────────────────────────────────────────
		// Pulsing golden diamond badge on tiles whose building is at ≥80% population capacity.
		// Suppressed when: upgrade tool is active (full gold highlight covers it), any overlay
		// is active, or zoom ≤ 0.5× (icon mode, badges would be sub-pixel noise).
		// Resets _hasUpgradeReadyBuildings so _Process can gate per-frame redraws.
		_hasUpgradeReadyBuildings = false;
		if (_grid != null && ActiveOverlay == OverlayMode.None && !World.UpgradeToolActive && _currentZoom > 0.5f)
		{
			// Slow pulse: 0.7–1.0 opacity, 1.2-second cycle
			var badgeTime = Time.GetTicksMsec() / 1000.0f;
			var badgePulse = 0.7f + 0.3f * Mathf.Sin(badgeTime * Mathf.Pi * 2f / 1.2f);
			var badgeColor = new Color(1.0f, 0.85f, 0.1f, badgePulse);

			foreach (var building in _grid.Buildings.Values)
			{
				if (!UpgradeableTypes.Contains(building.TypeId)) continue;

				// Iterate every tile in the building — show a badge on each qualifying tile
				foreach (var (tx, ty) in building.Tiles())
				{
					if (!_grid.IsInBounds(tx, ty)) continue;
					var t = _grid.GetTile(tx, ty);
					if (t.Population <= 0) continue;

					// 80% of per-tile effective capacity
					var capacity = BuildingGrowthSystem.GetEffectiveCapacity(building.TypeId, t.HasPower);
					var threshold = (int)(capacity * 0.8f);
					if (t.Population < threshold) continue;

					_hasUpgradeReadyBuildings = true;

					// Draw a small 6×6 golden diamond in the top-right corner of the tile
					float bpx = tx * TileSize;
					float bpy = ty * TileSize;
					const float diamondHalf = 4f;
					const float margin = 4f;
					// Centre the diamond in the top-right area: (TileSize - margin - 1, margin + 1)
					var cx = bpx + TileSize - margin - 1f;
					var cy = bpy + margin + 1f;
					DrawPolygon(
						new[]
						{
							new Vector2(cx,               cy - diamondHalf), // top
							new Vector2(cx + diamondHalf, cy),               // right
							new Vector2(cx,               cy + diamondHalf), // bottom
							new Vector2(cx - diamondHalf, cy),               // left
						},
						new[] { badgeColor, badgeColor, badgeColor, badgeColor });
				}
			}
		}

		// ── Happiness-distress indicator ─────────────────────────────────────
		// Small red downward triangle on tiles with severe happiness deficit (< 0.28).
		// Signals that the distress decay system is actively working on this tile.
		// Suppressed in overlay modes (the happiness overlay covers this info already).
		if (_grid != null && ActiveOverlay == OverlayMode.None && _currentZoom > 0.5f)
		{
			var distressColor = new Color(0.9f, 0.2f, 0.2f, 0.8f);
			foreach (var t in _grid.AllTiles())
			{
				if (t.Zone != ZoneType.Residential && t.Zone != ZoneType.Commercial && t.Zone != ZoneType.Industrial)
					continue;
				if (t.BuildingId == null) continue; // only developed tiles
				if ((float)t.Happiness >= 0.28f) continue;

				float bpx = t.X * TileSize;
				float bpy = t.Y * TileSize;
				// Downward-pointing 5×5 triangle in the bottom-left corner
				const float triHalf = 3f;
				const float triMarg = 3f;
				var tcx = bpx + triMarg + triHalf;
				var tcy = bpy + TileSize - triMarg - 1f;
				// Triangle: top-left, top-right, bottom-center (points downward)
				DrawTriangle(
					new Vector2(tcx - triHalf, tcy - triHalf * 1.2f),
					new Vector2(tcx + triHalf, tcy - triHalf * 1.2f),
					new Vector2(tcx,           tcy),
					distressColor);
			}
		}

		// Rectangle paint preview: semi-transparent fill + solid border
		if (_hasRectPreview)
		{
			var minX = Mathf.Min(_rectPreviewStart.X, _rectPreviewEnd.X);
			var maxX = Mathf.Max(_rectPreviewStart.X, _rectPreviewEnd.X);
			var minY = Mathf.Min(_rectPreviewStart.Y, _rectPreviewEnd.Y);
			var maxY = Mathf.Max(_rectPreviewStart.Y, _rectPreviewEnd.Y);

			var rx = minX * TileSize;
			var ry = minY * TileSize;
			var rw = (maxX - minX + 1) * TileSize;
			var rh = (maxY - minY + 1) * TileSize;

			// Semi-transparent fill
			DrawRect(new Rect2(rx, ry, rw, rh), _rectPreviewColor);

			// Solid border (same color, full opacity)
			var borderC = new Color(_rectPreviewColor.R, _rectPreviewColor.G, _rectPreviewColor.B, 0.9f);
			const int previewBorderW = 2;
			DrawRect(new Rect2(rx,            ry,            rw, previewBorderW), borderC); // top
			DrawRect(new Rect2(rx,            ry + rh - previewBorderW, rw, previewBorderW), borderC); // bottom
			DrawRect(new Rect2(rx,            ry,            previewBorderW, rh), borderC); // left
			DrawRect(new Rect2(rx + rw - previewBorderW, ry, previewBorderW, rh), borderC); // right

			// Size label in the top-left corner of the preview (e.g. "3×4")
			var w = maxX - minX + 1;
			var h = maxY - minY + 1;
			if (w > 1 || h > 1)
			{
				var labelPos = new Vector2(rx + previewBorderW + 2, ry + previewBorderW + 1);
				DrawString(ThemeDB.FallbackFont, labelPos, $"{w}\xd7{h}", HorizontalAlignment.Left, -1, 11,
					new Color(1f, 1f, 1f, 0.9f));
			}
		}

		// ── Upgrade tool highlight ───────────────────────────────────────────
		// Gold border + subtle tint on buildings that can be upgraded (only when upgrade tool is active).
		if (World.UpgradeToolActive && _grid != null && ActiveOverlay == OverlayMode.None)
		{
			foreach (var building in _grid.Buildings.Values)
			{
				if (!UpgradeableTypes.Contains(building.TypeId)) continue;

				float bx = building.AnchorX * TileSize;
				float by = building.AnchorY * TileSize;
				float bw = building.Width  * TileSize;
				float bh = building.Height * TileSize;

				// Subtle gold tint over the footprint
				DrawRect(new Rect2(bx, by, bw, bh), UpgradeTintColor);

				// Gold border around the footprint
				const int upgradeBorderW = 2;
				DrawRect(new Rect2(bx,                      by,                      bw, upgradeBorderW), UpgradeBorderColor); // top
				DrawRect(new Rect2(bx,                      by + bh - upgradeBorderW, bw, upgradeBorderW), UpgradeBorderColor); // bottom
				DrawRect(new Rect2(bx,                      by,                      upgradeBorderW, bh), UpgradeBorderColor); // left
				DrawRect(new Rect2(bx + bw - upgradeBorderW, by,                      upgradeBorderW, bh), UpgradeBorderColor); // right
			}

			// Queue another redraw so the tool remains live (no continuous animation needed, but
			// if World state changes we want the overlay to update)
		}

		// ── Service fatigue indicator ────────────────────────────────────────
		// Small 8×8px square in the top-right corner of each degraded service tile.
		// Amber (40–59% capacity) or red (at minimum 40%) — only drawn at normal zoom.
		if (_degradedServices != null && _degradedServices.Length > 0 && _currentZoom > 0.5f)
		{
			foreach (var entry in _degradedServices)
			{
				// Capacity < 0.60 → show indicator. At minimum (≤0.40) → red, otherwise amber.
				if (entry.Capacity >= 0.60) continue;
				var indicatorColor = entry.Capacity <= 0.40
					? FatigueRed
					: FatigueAmber;
				// 8×8px square in the top-right corner (2px margin from edge)
				const float indSize = 8f;
				const float indMargin = 2f;
				var ix = entry.X * TileSize + TileSize - indMargin - indSize;
				var iy = entry.Y * TileSize + indMargin;
				DrawRect(new Rect2(ix, iy, indSize, indSize), indicatorColor);
				// 1px dark border for readability
				var borderColor = new Color(0f, 0f, 0f, 0.65f);
				DrawRect(new Rect2(ix, iy, indSize, 1f), borderColor);                          // top
				DrawRect(new Rect2(ix, iy + indSize - 1f, indSize, 1f), borderColor);           // bottom
				DrawRect(new Rect2(ix, iy, 1f, indSize), borderColor);                          // left
				DrawRect(new Rect2(ix + indSize - 1f, iy, 1f, indSize), borderColor);           // right
			}
		}

		// ── Fire tile overlay ────────────────────────────────────────────────
		// Vivid orange-red pulse over the burning tile during a FireBreak event.
		if (_fireTileX >= 0 && _fireTileY >= 0)
		{
			// Pulsing intensity: oscillate between 40% and 85% opacity
			var pulse = (float)(0.60 + 0.25 * Math.Sin(Time.GetTicksMsec() / 250.0));
			var fireColor = new Color(FireOverlay.R, FireOverlay.G, FireOverlay.B, pulse);
			var fireRect  = new Rect2(_fireTileX * TileSize, _fireTileY * TileSize, TileSize, TileSize);
			DrawRect(fireRect, fireColor);

			// Bright amber border
			const int fireBorderW = 3;
			var fb = FireBorder;
			DrawRect(new Rect2(fireRect.Position, new Vector2(fireRect.Size.X, fireBorderW)), fb);
			DrawRect(new Rect2(fireRect.Position.X, fireRect.End.Y - fireBorderW, fireRect.Size.X, fireBorderW), fb);
			DrawRect(new Rect2(fireRect.Position, new Vector2(fireBorderW, fireRect.Size.Y)), fb);
			DrawRect(new Rect2(fireRect.End.X - fireBorderW, fireRect.Position.Y, fireBorderW, fireRect.Size.Y), fb);

			// Fire emoji drawn above the tile
			var emojiPos = new Vector2(_fireTileX * TileSize + 4, _fireTileY * TileSize - 2);
			DrawString(ThemeDB.FallbackFont, emojiPos, "fire", HorizontalAlignment.Left, -1, 18,
				new Color(1f, 0.8f, 0f, 0.9f));

			QueueRedraw(); // keep pulsing each frame
		}
	}
}
