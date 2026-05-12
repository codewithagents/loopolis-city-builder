using Godot;

namespace LoopolisGodot;

/// <summary>
/// Expanding ring animation played when a road or power plant is placed.
/// Self-destructs after the animation completes (~0.6 seconds).
///
/// Usage: instantiate, set Color, call Start(tileWorldCenter).
/// The node must be added as a child of the TilemapRenderer (or any Node2D
/// in world-space) so its local position maps to tile coordinates.
/// </summary>
public partial class RippleEffect : Node2D
{
    private const float Duration    = 0.6f;
    private const float StartRadius = TilemapRenderer.TileSize * 0.5f;
    private const float EndRadius   = TilemapRenderer.TileSize * 4f;
    private const float StartAlpha  = 0.8f;
    private const float ArcWidth    = 2f;

    private float _elapsed = 0f;
    private bool  _running = false;

    /// <summary>Color of the ripple ring. Set before calling Start().</summary>
    public Color RippleColor { get; set; } = new Color(1f, 0.835f, 0.31f); // gold #FFD54F default

    /// <summary>Start the ripple animation centred at <paramref name="worldCenter"/>.</summary>
    public void Start(Vector2 worldCenter)
    {
        Position = worldCenter;
        _elapsed = 0f;
        _running = true;
    }

    public override void _Process(double delta)
    {
        if (!_running) return;

        _elapsed += (float)delta;
        if (_elapsed >= Duration)
        {
            QueueFree();
            return;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_running) return;

        var t = Mathf.Clamp(_elapsed / Duration, 0f, 1f);

        // Expand radius linearly
        var radius = Mathf.Lerp(StartRadius, EndRadius, t);

        // Fade out alpha
        var alpha = Mathf.Lerp(StartAlpha, 0f, t);

        var color = new Color(RippleColor.R, RippleColor.G, RippleColor.B, alpha);

        // DrawArc draws a circular arc. Full circle = 0 to TAU.
        DrawArc(Vector2.Zero, radius, 0f, Mathf.Tau, 48, color, ArcWidth, true);
    }
}
