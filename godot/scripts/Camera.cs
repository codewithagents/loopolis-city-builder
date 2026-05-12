using Godot;
using System;

namespace LoopolisGodot;

public partial class Camera : Camera2D
{
    // ── Constants ──────────────────────────────────────────────────────────────

    private const float ZoomMin       = 0.10f;  // fully zoomed out (128×128 map fits on screen)
    private const float ZoomMax       = 3.00f;  // fully zoomed in for tile detail
    private const float ZoomStep      = 1.10f;  // multiplier per scroll notch
    private const float PanSpeedBase  = 400f;   // pixels per second at zoom=1 (keyboard pan)

    // ── State ──────────────────────────────────────────────────────────────────

    private bool    _dragging        = false;
    private int     _mapWidth        = 32;   // in tiles; updated by FitToMap()
    private int     _mapHeight       = 32;
    private int     _tileSize        = TilemapRenderer.TileSize;
    private bool    _fitPending      = false; // deferred fit until after first process frame

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by World.cs once it knows the grid size.
    /// Resets zoom and centers the camera to show the whole map.
    /// Can be called from _Ready (viewport size available) or later (viewer mode first tick).
    /// </summary>
    public void FitToMap(int gridWidth, int gridHeight)
    {
        _mapWidth    = gridWidth;
        _mapHeight   = gridHeight;
        _fitPending  = false;
        ApplyInitialZoom();
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        // World.cs will call FitToMap() immediately after setting up the simulation.
        // Schedule a default fit in case FitToMap is never called (e.g. unit test / editor).
        _fitPending = true;
    }

    public override void _Process(double delta)
    {
        // Apply deferred fit on the first process frame (viewport size is guaranteed valid here)
        if (_fitPending)
        {
            _fitPending = false;
            ApplyInitialZoom();
        }

        HandleKeyboardPan((float)delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            // ── Drag start/stop ──────────────────────────────────────────────
            if (mb.ButtonIndex == MouseButton.Middle || mb.ButtonIndex == MouseButton.Right)
            {
                _dragging = mb.Pressed;
                if (_dragging)
                    Input.MouseMode = Input.MouseModeEnum.Visible; // keep cursor visible
            }

            // ── Scroll-to-zoom (zoom toward cursor) ──────────────────────────
            else if (mb.ButtonIndex == MouseButton.WheelUp   && mb.Pressed)
                ZoomTowardCursor(mb.Position, ZoomStep);
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
                ZoomTowardCursor(mb.Position, 1f / ZoomStep);
        }

        // ── Middle/right drag pan ────────────────────────────────────────────
        if (@event is InputEventMouseMotion mm && _dragging)
        {
            // Move in the opposite direction of the drag (world moves with mouse)
            Position -= mm.Relative / Zoom;
            ClampPosition();
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes and applies the initial zoom so the full map fits in 80% of the viewport,
    /// then centers the camera on the map.
    /// </summary>
    private void ApplyInitialZoom()
    {
        var mapPixelW = _mapWidth  * _tileSize;
        var mapPixelH = _mapHeight * _tileSize;

        var viewport = GetViewport();
        if (viewport == null) return;

        var viewportSize = viewport.GetVisibleRect().Size;
        if (viewportSize.X <= 0 || viewportSize.Y <= 0) return;

        var zoomX = viewportSize.X * 0.80f / mapPixelW;
        var zoomY = viewportSize.Y * 0.80f / mapPixelH;
        var initial = Math.Min(zoomX, zoomY);
        initial = Mathf.Clamp(initial, ZoomMin, ZoomMax);

        Zoom     = new Vector2(initial, initial);
        Position = new Vector2(mapPixelW * 0.5f, mapPixelH * 0.5f);
    }

    /// <summary>
    /// Applies a zoom multiplier while keeping the world point under the cursor fixed.
    /// </summary>
    private void ZoomTowardCursor(Vector2 mouseScreenPos, float factor)
    {
        var oldZoom = Zoom.X;
        var newZoom = Mathf.Clamp(oldZoom * factor, ZoomMin, ZoomMax);
        if (Mathf.IsEqualApprox(newZoom, oldZoom)) return;

        // World position under the cursor before zoom
        var viewport = GetViewport();
        if (viewport == null) return;
        var viewportSize = viewport.GetVisibleRect().Size;
        var screenCenter = viewportSize * 0.5f;

        // offset from screen center in world units (at old zoom)
        var cursorOffset = (mouseScreenPos - screenCenter) / oldZoom;

        // After zoom, the same world point should be at the same screen position:
        // new_position = old_position + cursorOffset * (1 - newZoom/oldZoom)
        var zoomRatio = newZoom / oldZoom;
        Position = Position + cursorOffset * (1f - zoomRatio);

        Zoom = new Vector2(newZoom, newZoom);
        ClampPosition();
    }

    /// <summary>
    /// Keyboard pan: arrow keys only (WASD conflicts with the toolbar's Z/S/R/C/I shortcuts).
    /// Speed scales inversely with zoom so panning feels consistent regardless of zoom level.
    /// </summary>
    private void HandleKeyboardPan(float delta)
    {
        var panSpeed = PanSpeedBase / Zoom.X;
        var dir = Vector2.Zero;

        if (Input.IsKeyPressed(Key.Up))    dir.Y -= 1;
        if (Input.IsKeyPressed(Key.Down))  dir.Y += 1;
        if (Input.IsKeyPressed(Key.Left))  dir.X -= 1;
        if (Input.IsKeyPressed(Key.Right)) dir.X += 1;

        if (dir == Vector2.Zero) return;
        Position += dir.Normalized() * panSpeed * delta;
        ClampPosition();
    }

    /// <summary>
    /// Clamps the camera so the player cannot pan completely off the map.
    /// Allows a half-viewport margin on each side so the edge of the map
    /// can always be scrolled to screen-center.
    /// </summary>
    private void ClampPosition()
    {
        var mapPixelW = _mapWidth  * _tileSize;
        var mapPixelH = _mapHeight * _tileSize;
        Position = Position.Clamp(Vector2.Zero, new Vector2(mapPixelW, mapPixelH));
    }
}
