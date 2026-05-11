using Godot;

namespace LoopolisGodot;

public partial class Camera : Camera2D
{
    private bool _dragging = false;
    private Vector2 _dragStart;

    public override void _Ready()
    {
        // 32px tiles at zoom=1: 32×32 grid = 1024px — fits 1440px window with margin.
        // Grid center = tile (16,16) → world (512, 512).
        Zoom = new Vector2(1f, 1f);
        Position = new Vector2(512, 512);
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
