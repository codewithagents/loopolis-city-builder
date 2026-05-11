using Godot;

namespace LoopolisGodot;

public partial class Camera : Camera2D
{
    private bool _dragging = false;
    private Vector2 _dragStart;

    public override void _Ready()
    {
        Zoom = new Vector2(3f, 3f); // 3x zoom — 16px tiles → 48px, city clearly visible
        // Center on the starter city: power plant at tile (10,10), zones around (10-11, 12-13)
        // World coords = tile * 16px. City center ≈ tile (10.5, 12) → world (168, 192)
        Position = new Vector2(168, 200);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Middle)
            {
                _dragging = mb.Pressed;
                _dragStart = mb.Position;
            }
            else if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                Zoom *= 1.1f;
                Zoom = Zoom.Clamp(new Vector2(0.5f, 0.5f), new Vector2(8f, 8f));
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                Zoom /= 1.1f;
                Zoom = Zoom.Clamp(new Vector2(0.5f, 0.5f), new Vector2(8f, 8f));
            }
        }

        if (@event is InputEventMouseMotion mm && _dragging)
        {
            Position -= mm.Relative / Zoom;
        }
    }
}
