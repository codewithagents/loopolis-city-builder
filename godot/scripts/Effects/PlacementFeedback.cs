using Godot;

namespace LoopolisGodot;

/// <summary>
/// Floating score label that drifts upward and fades out after a zone is placed.
/// Shows two lines of text:
///   Primary   — white, larger (e.g. "~42 potential residents")
///   Secondary — amber/grey, smaller (e.g. "+$6/tick est." or "high pollution exposure")
///
/// Created via <see cref="Create"/> and added as a child of TilemapRenderer (world space)
/// so its Position maps directly to tile coordinates. Self-destructs after animation.
/// </summary>
public partial class PlacementFeedback : Node2D
{
    private const float Duration   = 1.2f;  // total life in seconds
    private const float RisePixels = 40f;   // vertical travel during life
    private const float RiseSpeed  = RisePixels / Duration;

    private const int PrimaryFontSize   = 12;
    private const int SecondaryFontSize = 10;

    private float   _elapsed   = 0f;
    private string  _primary   = "";
    private string  _secondary = "";
    private Vector2 _startPos;

    // ── Factory ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a PlacementFeedback ready to be AddChild'd.
    /// <paramref name="worldPosition"/> is the world-space anchor (centre-top of the tile).
    /// </summary>
    public static PlacementFeedback Create(Vector2 worldPosition, string primary, string secondary = "")
    {
        var node = new PlacementFeedback
        {
            _primary   = primary,
            _secondary = secondary,
            _startPos  = worldPosition,
            Position   = worldPosition,
            ZIndex     = 25,
        };
        return node;
    }

    // ── Godot lifecycle ───────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;

        if (_elapsed >= Duration)
        {
            QueueFree();
            return;
        }

        var t = Mathf.Clamp(_elapsed / Duration, 0f, 1f);

        // Drift upward
        Position = _startPos - new Vector2(0f, RisePixels * t);

        // Fade: stay opaque for first 30%, then linear fade to 0
        var alpha = t < 0.3f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.3f) / 0.7f);
        Modulate = new Color(1f, 1f, 1f, alpha);

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_elapsed >= Duration) return;

        var font = ThemeDB.FallbackFont;

        // ── Primary label (white, bold-ish via size) ──────────────────────────
        var primarySize = font.GetStringSize(_primary, HorizontalAlignment.Center, -1, PrimaryFontSize);
        var primaryOrigin = new Vector2(-primarySize.X * 0.5f, 0f);

        // Drop shadow
        DrawString(font, primaryOrigin + new Vector2(1f, 1f), _primary,
            HorizontalAlignment.Left, -1, PrimaryFontSize, new Color(0f, 0f, 0f, 0.65f));
        // White text
        DrawString(font, primaryOrigin, _primary,
            HorizontalAlignment.Left, -1, PrimaryFontSize, Colors.White);

        // ── Secondary label (amber/grey, one line below) ───────────────────────
        if (!string.IsNullOrEmpty(_secondary))
        {
            var secondarySize = font.GetStringSize(_secondary, HorizontalAlignment.Center, -1, SecondaryFontSize);
            var secondaryOrigin = new Vector2(-secondarySize.X * 0.5f, PrimaryFontSize + 3f);

            // Determine colour: pollution-related labels get amber, others get light grey
            var secondaryColor = _secondary.Contains("pollution")
                ? new Color(1.0f, 0.65f, 0.15f)   // amber warning
                : new Color(0.75f, 0.75f, 0.75f);  // light grey

            DrawString(font, secondaryOrigin + new Vector2(1f, 1f), _secondary,
                HorizontalAlignment.Left, -1, SecondaryFontSize, new Color(0f, 0f, 0f, 0.55f));
            DrawString(font, secondaryOrigin, _secondary,
                HorizontalAlignment.Left, -1, SecondaryFontSize, secondaryColor);
        }
    }
}
