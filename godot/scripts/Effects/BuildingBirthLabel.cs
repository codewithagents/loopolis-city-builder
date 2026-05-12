using Godot;

namespace LoopolisGodot;

/// <summary>
/// Floating label that rises and fades out above a newly-grown building.
/// Self-destructs after the animation completes (~1.5 seconds).
///
/// Usage: instantiate, call Start(tileWorldCenter, text).
/// Add as a child of TilemapRenderer (world-space) so Position maps to tile coords.
/// </summary>
public partial class BuildingBirthLabel : Node2D
{
    private const float Duration   = 1.5f;
    private const float RisePixels = TilemapRenderer.TileSize * 2f;

    private float   _elapsed   = 0f;
    private bool    _running   = false;
    private string  _text      = "";
    private Vector2 _startPos;

    public override void _Ready()
    {
        ZIndex = 20; // render above everything else
    }

    /// <summary>
    /// Start the animation at <paramref name="worldCenter"/> (centre of anchor tile).
    /// </summary>
    public void Start(Vector2 worldCenter, string text)
    {
        _text     = text;
        _startPos = worldCenter - new Vector2(0f, TilemapRenderer.TileSize * 0.5f); // start just above tile
        Position  = _startPos;
        _elapsed  = 0f;
        _running  = true;
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

        var t = Mathf.Clamp(_elapsed / Duration, 0f, 1f);

        // Move upward
        Position = _startPos - new Vector2(0f, RisePixels * t);

        // Fade out in the last 60% of the animation
        var alpha = t < 0.4f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.4f) / 0.6f);
        Modulate = new Color(1f, 1f, 1f, alpha);

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_running || _text.Length == 0) return;

        var font     = ThemeDB.FallbackFont;
        const int    fontSize   = 11;
        const float  shadowOff  = 1f;

        var textSize = font.GetStringSize(_text, HorizontalAlignment.Center, -1, fontSize);
        var origin   = new Vector2(-textSize.X * 0.5f, 0f);

        // Dark shadow for readability
        var shadowColor = new Color(0f, 0f, 0f, 0.7f);
        DrawString(font, origin + new Vector2(shadowOff, shadowOff), _text,
            HorizontalAlignment.Left, -1, fontSize, shadowColor);

        // White main text
        DrawString(font, origin, _text,
            HorizontalAlignment.Left, -1, fontSize, Colors.White);
    }
}
