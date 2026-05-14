using Godot;
using System;
using Loopolis.Core.Grid;

namespace LoopolisGodot;

/// <summary>
/// Minimap — bottom-right corner panel showing the full city at a glance.
/// Renders one rectangle per tile at a scaled-down resolution and overlays
/// a viewport rectangle showing what the Camera2D currently sees.
///
/// Layer = 10 (same as HudOverlay, below TopBar at 12).
///
/// Public API:
///   SetCamera(Camera camera)         — call from World._Ready() once camera is resolved
///   UpdateFromGrid(CityGrid grid)    — call each tick in standalone mode
///   UpdateFromState(SharedState state, CityGrid grid) — call each tick in viewer mode
///
/// Toggle visibility: press M key or click the header toggle button.
/// </summary>
public partial class Minimap : CanvasLayer
{
    // ── Layout constants ───────────────────────────────────────────────────────
    private const int PanelMargin  = 8;    // px from viewport edge
    private const int HeaderHeight = 20;   // px for the title bar
    private const int MapDisplaySize = 128; // inner drawing area (square)
    private const int PanelWidth  = MapDisplaySize + 8;  // 136px
    private const int PanelHeight = HeaderHeight + MapDisplaySize + 8; // 156px

    // ── State ─────────────────────────────────────────────────────────────────
    private Camera? _camera;
    private CityGrid? _grid;
    private int _gridWidth  = 64;
    private int _gridHeight = 64;
    private float _tileDisplaySize = 2f; // recalculated when grid size known

    // ── Nodes ─────────────────────────────────────────────────────────────────
    private PanelContainer _panel     = null!;
    private MapDrawControl _mapDraw   = null!;
    private bool _visible = true;

    // ── Minimap tile colors (minimap-specific palette) ─────────────────────────
    // These are intentionally simplified from TilemapRenderer's full palette —
    // at 2px/tile visual nuance is lost; use clear, distinct hues.
    private static readonly Color ColWater      = new Color(0.15f, 0.25f, 0.40f); // dark blue
    private static readonly Color ColRoad       = new Color(0.35f, 0.35f, 0.38f);
    private static readonly Color ColAvenue     = new Color(0.50f, 0.50f, 0.55f);
    private static readonly Color ColRes        = new Color(0.85f, 0.65f, 0.30f); // warm amber
    private static readonly Color ColCom        = new Color(0.30f, 0.55f, 0.90f); // blue
    private static readonly Color ColInd        = new Color(0.70f, 0.65f, 0.15f); // yellow-olive
    private static readonly Color ColMill       = new Color(0.35f, 0.55f, 0.20f); // earthy green
    private static readonly Color ColQuarry     = new Color(0.50f, 0.48f, 0.45f); // stone grey
    private static readonly Color ColPark       = new Color(0.30f, 0.72f, 0.25f); // green
    private static readonly Color ColService    = new Color(0.70f, 0.30f, 0.30f); // service red
    private static readonly Color ColPower      = new Color(0.90f, 0.85f, 0.20f); // yellow
    private static readonly Color ColForest     = new Color(0.20f, 0.40f, 0.18f); // dark forest
    private static readonly Color ColElevated   = new Color(0.55f, 0.50f, 0.45f); // stone
    private static readonly Color ColFlat       = new Color(0.22f, 0.28f, 0.18f); // dark grass

    public override void _Ready()
    {
        Layer = 10;

        // ── Outer panel (PanelContainer anchored bottom-right) ─────────────────
        _panel = new PanelContainer();
        _panel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _panel.GrowHorizontal = Control.GrowDirection.Begin;
        _panel.GrowVertical   = Control.GrowDirection.Begin;
        _panel.CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);
        _panel.MouseFilter = Control.MouseFilterEnum.Stop;
        _panel.OffsetLeft   = -(PanelWidth  + PanelMargin);
        _panel.OffsetTop    = -(PanelHeight + PanelMargin);
        _panel.OffsetRight  = -PanelMargin;
        _panel.OffsetBottom = -PanelMargin;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.90f);
        panelStyle.BorderColor = new Color(0.25f, 0.25f, 0.30f);
        panelStyle.BorderWidthBottom = panelStyle.BorderWidthTop =
            panelStyle.BorderWidthLeft = panelStyle.BorderWidthRight = 1;
        panelStyle.CornerRadiusTopLeft = panelStyle.CornerRadiusTopRight =
            panelStyle.CornerRadiusBottomLeft = panelStyle.CornerRadiusBottomRight = 3;
        panelStyle.ContentMarginLeft   = 4;
        panelStyle.ContentMarginRight  = 4;
        panelStyle.ContentMarginTop    = 2;
        panelStyle.ContentMarginBottom = 4;
        _panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(_panel);

        // ── VBox: header + map drawing area ────────────────────────────────────
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);
        _panel.AddChild(vbox);

        // ── Header row: title label + toggle button ─────────────────────────
        var headerRow = new HBoxContainer();
        headerRow.CustomMinimumSize = new Vector2(0, HeaderHeight);
        headerRow.AddThemeConstantOverride("separation", 0);
        vbox.AddChild(headerRow);

        var titleLabel = new Label();
        titleLabel.Text = "Map";
        titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleLabel.VerticalAlignment   = VerticalAlignment.Center;
        titleLabel.AddThemeFontSizeOverride("font_size", 11);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        headerRow.AddChild(titleLabel);

        var toggleBtn = new Button();
        toggleBtn.FocusMode   = Control.FocusModeEnum.None;
        toggleBtn.Text        = "M";
        toggleBtn.TooltipText = "Toggle minimap (M)";
        toggleBtn.CustomMinimumSize = new Vector2(20, 20);
        toggleBtn.AddThemeFontSizeOverride("font_size", 10);
        var btnNormal = new StyleBoxFlat();
        btnNormal.BgColor = new Color(0f, 0f, 0f, 0f);
        toggleBtn.AddThemeStyleboxOverride("normal",  btnNormal);
        toggleBtn.AddThemeStyleboxOverride("focus",   btnNormal);
        var btnHover = new StyleBoxFlat();
        btnHover.BgColor = new Color(1f, 1f, 1f, 0.10f);
        toggleBtn.AddThemeStyleboxOverride("hover",   btnHover);
        toggleBtn.AddThemeStyleboxOverride("pressed", btnHover);
        toggleBtn.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        toggleBtn.Pressed += ToggleMapVisibility;
        headerRow.AddChild(toggleBtn);

        // ── Map drawing control ───────────────────────────────────────────────
        _mapDraw = new MapDrawControl(this);
        _mapDraw.CustomMinimumSize = new Vector2(MapDisplaySize, MapDisplaySize);
        _mapDraw.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _mapDraw.SizeFlagsVertical   = Control.SizeFlags.ExpandFill;
        // Capture mouse so clicks navigate the camera
        _mapDraw.MouseFilter = Control.MouseFilterEnum.Stop;
        _mapDraw.GuiInput += OnMapGuiInput;
        vbox.AddChild(_mapDraw);
    }

    public override void _Process(double delta)
    {
        // Redraw every frame to keep the camera viewport rect fresh
        if (_visible && _grid != null)
            _mapDraw.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.M)
            {
                ToggleMapVisibility();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Bind the Camera2D used for the viewport rectangle overlay.</summary>
    public void SetCamera(Camera camera)
    {
        _camera = camera;
    }

    /// <summary>Update tile data from a CityGrid (standalone mode).</summary>
    public void UpdateFromGrid(CityGrid grid)
    {
        _grid        = grid;
        _gridWidth   = grid.Width;
        _gridHeight  = grid.Height;
        RecalcTileDisplaySize();
        _mapDraw.QueueRedraw();
    }

    /// <summary>Update tile data from a SharedState + rebuilt CityGrid (viewer mode).</summary>
    public void UpdateFromState(SharedState state, CityGrid grid)
    {
        _grid        = grid;
        _gridWidth   = grid.Width  > 0 ? grid.Width  : (state.GridWidth  > 0 ? state.GridWidth  : 32);
        _gridHeight  = grid.Height > 0 ? grid.Height : (state.GridHeight > 0 ? state.GridHeight : 32);
        RecalcTileDisplaySize();
        _mapDraw.QueueRedraw();
    }

    // ── Internal ───────────────────────────────────────────────────────────────

    private void ToggleMapVisibility()
    {
        _visible = !_visible;
        _mapDraw.Visible = _visible;
    }

    private void RecalcTileDisplaySize()
    {
        // Fit the larger dimension into MapDisplaySize px, keep aspect uniform
        var maxDim = Math.Max(_gridWidth, _gridHeight);
        if (maxDim <= 0) maxDim = 64;
        _tileDisplaySize = (float)MapDisplaySize / maxDim;
    }

    /// <summary>
    /// Handle clicks on the map drawing area — pan the camera to the clicked tile.
    /// </summary>
    private void OnMapGuiInput(InputEvent @event)
    {
        if (_camera == null || _grid == null) return;
        if (@event is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex != MouseButton.Left || !mb.Pressed) return;

        // mb.Position is relative to _mapDraw's top-left corner
        var clickPos = mb.Position;
        var tileX = (int)(clickPos.X / _tileDisplaySize);
        var tileY = (int)(clickPos.Y / _tileDisplaySize);

        // Clamp to grid bounds
        tileX = Math.Clamp(tileX, 0, _gridWidth  - 1);
        tileY = Math.Clamp(tileY, 0, _gridHeight - 1);

        // Navigate camera to world position of that tile (Camera clamps itself)
        _camera.Position = new Vector2(
            (tileX + 0.5f) * TilemapRenderer.TileSize,
            (tileY + 0.5f) * TilemapRenderer.TileSize);
    }

    /// <summary>Maps a ZoneType (and optional building typeId) to its minimap color.</summary>
    internal Color GetTileColor(Tile tile)
    {
        // Terrain-first: water / elevated / forest with no zone
        if (tile.Zone == ZoneType.Empty)
        {
            if (tile.Terrain == TerrainType.Water) return ColWater;
            var h = tile.HeightLevel;
            if (h >= 2) return ColElevated;
            if (tile.HasForest) return ColForest;
            return ColFlat;
        }

        return tile.Zone switch
        {
            ZoneType.Road           => ColRoad,
            ZoneType.Avenue         => ColAvenue,
            ZoneType.Residential    => ColRes,
            ZoneType.Commercial     => ColCom,
            ZoneType.Industrial     => ColInd,
            // Services
            ZoneType.FireStation    => ColService,
            ZoneType.FireHQ         => ColService,
            ZoneType.PoliceStation  => ColService,
            ZoneType.PoliceHQ       => ColService,
            ZoneType.School         => ColService,
            ZoneType.Hospital       => ColService,
            // Power
            ZoneType.PowerPlant     => ColPower,
            ZoneType.CoalPlant      => ColPower,
            ZoneType.NuclearPlant   => ColPower,
            ZoneType.PowerLine      => ColPower,
            ZoneType.Park           => ColPark,
            _                       => ColFlat,
        };
    }

    internal Camera? GetCamera()  => _camera;
    internal int GridWidth()       => _gridWidth;
    internal int GridHeight()      => _gridHeight;
    internal float TileDisplaySize() => _tileDisplaySize;
    internal CityGrid? GetGrid()   => _grid;

    // ── Inner drawing control ──────────────────────────────────────────────────

    /// <summary>
    /// Custom Control that draws the minimap tiles and camera viewport rectangle.
    /// Kept as a nested class to avoid polluting the Godot node namespace.
    /// </summary>
    private partial class MapDrawControl : Control
    {
        private readonly Minimap _owner;

        public MapDrawControl(Minimap owner)
        {
            _owner = owner;
        }

        public override void _Draw()
        {
            var grid = _owner.GetGrid();
            if (grid == null) return;

            var gw  = _owner.GridWidth();
            var gh  = _owner.GridHeight();
            var ts  = _owner.TileDisplaySize();

            // ── Draw tiles ─────────────────────────────────────────────────────
            for (var y = 0; y < gh; y++)
            {
                for (var x = 0; x < gw; x++)
                {
                    var tile  = grid.GetTile(x, y);
                    var color = _owner.GetTileColor(tile);
                    var rect  = new Rect2(x * ts, y * ts, Math.Max(ts, 1f), Math.Max(ts, 1f));
                    DrawRect(rect, color);
                }
            }

            // ── Draw camera viewport rectangle ─────────────────────────────────
            var camera = _owner.GetCamera();
            if (camera == null) return;

            var viewport = camera.GetViewport();
            if (viewport == null) return;

            var viewSize = viewport.GetVisibleRect().Size;
            var camZoom  = camera.Zoom.X;
            if (camZoom <= 0f) return;

            // Viewport dimensions in world pixels
            var vpWorldW = viewSize.X / camZoom;
            var vpWorldH = viewSize.Y / camZoom;

            // Camera position is the center of the view in world pixels
            var camPos   = camera.Position;
            var worldLeft  = camPos.X - vpWorldW * 0.5f;
            var worldTop   = camPos.Y - vpWorldH * 0.5f;

            // Convert world pixels → tile coordinates → minimap pixels
            var mmLeft  = (worldLeft  / TilemapRenderer.TileSize) * ts;
            var mmTop   = (worldTop   / TilemapRenderer.TileSize) * ts;
            var mmW     = (vpWorldW   / TilemapRenderer.TileSize) * ts;
            var mmH     = (vpWorldH   / TilemapRenderer.TileSize) * ts;

            // Clamp the rectangle so it never extends outside the minimap area
            var mapPxW = gw * ts;
            var mapPxH = gh * ts;
            var clampedLeft = Math.Max(0f, mmLeft);
            var clampedTop  = Math.Max(0f, mmTop);
            var clampedRight  = Math.Min(mapPxW, mmLeft + mmW);
            var clampedBottom = Math.Min(mapPxH, mmTop  + mmH);

            if (clampedRight > clampedLeft && clampedBottom > clampedTop)
            {
                var vpRect = new Rect2(clampedLeft, clampedTop,
                    clampedRight - clampedLeft, clampedBottom - clampedTop);

                // Semi-transparent fill
                DrawRect(vpRect, new Color(1f, 1f, 1f, 0.07f));
                // Solid border
                DrawRect(vpRect, new Color(1f, 1f, 1f, 0.75f), filled: false, width: 1f);
            }
        }
    }
}
