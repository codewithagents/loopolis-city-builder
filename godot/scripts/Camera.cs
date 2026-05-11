using Godot;

namespace LoopolisGodot;

public partial class Camera : Camera2D
{
    private bool _dragging = false;
    private Vector2 _dragStart;

    public override void _Ready()
    {
        // Center on seeded starter city at tile (15,14) → world pos (480, 448).
        // Zoom 1.5x so the starter city is clearly visible on startup.
        Zoom = new Vector2(1.5f, 1.5f);
        Position = new Vector2(480, 448);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Middle || mb.ButtonIndex == MouseButton.Right)
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
