using Godot;

namespace LoopolisGodot;

/// <summary>
/// Bottom-right onboarding hint overlay. Shows one contextual hint at a time,
/// cycling through a 4-step sequence. Each hint is dismissed once the player
/// performs the relevant action. Fades in/out smoothly.
/// Layer = 8 (below toolbar at 9).
/// </summary>
public partial class HintOverlay : CanvasLayer
{
    private static readonly string[] Hints =
    {
        "💡 Connect a Power Plant with Power Lines to your zones — no power, no growth",
        "💡 Add Residential zones connected by Roads — watch them grow when powered",
        "💡 Build Fire/Police/School stations — neglected cities lose happiness and stall",
        "💡 Milestones: 500 pop = Town · 5 000 = City · 25 000 = Metropolis · 100 000 = Loopolis",
    };

    private Label          _hintLabel = null!;
    private PanelContainer _panel     = null!;

    private int    _currentHint = 0;   // 0–3 = active hint index; 4 = all done
    private double _fadeTimer   = 0;   // counts DOWN; 0 = no active fade
    private bool   _fadingOut   = false;
    private double _hint3Timer  = 0;   // counts UP while hint 3 is visible
    private bool   _gameOver    = false;

    private const double FadeInDuration  = 0.3;
    private const double FadeOutDuration = 0.5;
    private const double Hint3DisplayTime = 30.0;

    public override void _Ready()
    {
        Layer = 8;

        _panel = new PanelContainer();
        _panel.AddThemeStyleboxOverride("panel", MakePanelStyle());

        // Anchor to bottom-right; grow leftward/upward from that corner
        _panel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _panel.GrowHorizontal = Control.GrowDirection.Begin;
        _panel.GrowVertical   = Control.GrowDirection.Begin;
        _panel.OffsetRight    = -16f;
        _panel.OffsetBottom   = -70f;

        _hintLabel = new Label();
        _hintLabel.Text = Hints[0];
        _hintLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
        _hintLabel.AddThemeFontSizeOverride("font_size", 14);
        _hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hintLabel.CustomMinimumSize = new Vector2(320, 0);

        _panel.AddChild(_hintLabel);
        AddChild(_panel);

        // Start fully invisible and fade in
        _panel.Modulate = new Color(1f, 1f, 1f, 0f);
        _fadeTimer  = FadeInDuration;
        _fadingOut  = false;
    }

    public override void _Process(double delta)
    {
        if (_gameOver || _currentHint >= 4) return;

        // Handle active fade animation
        if (_fadeTimer > 0)
        {
            _fadeTimer -= delta;
            if (_fadeTimer < 0) _fadeTimer = 0;

            if (_fadingOut)
            {
                // Fading out: alpha goes 1 → 0
                var alpha = (float)(_fadeTimer / FadeOutDuration);
                _panel.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(alpha, 0f, 1f));

                if (_fadeTimer <= 0)
                {
                    // Fade-out complete — advance to next hint
                    _currentHint++;
                    if (_currentHint < 4)
                    {
                        _hintLabel.Text = Hints[_currentHint];
                        _hint3Timer     = 0;
                        _fadingOut      = false;
                        _fadeTimer      = FadeInDuration;
                        _panel.Visible  = true;
                    }
                    else
                    {
                        _panel.Visible = false;
                    }
                }
            }
            else
            {
                // Fading in: alpha goes 0 → 1
                var alpha = (float)(1.0 - (_fadeTimer / FadeInDuration));
                _panel.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(alpha, 0f, 1f));
            }
        }
        else if (!_fadingOut)
        {
            // Fully visible — keep alpha at 1
            _panel.Modulate = new Color(1f, 1f, 1f, 1f);
        }

        // Track hint 3 display time (auto-dismiss after 30 seconds)
        if (_currentHint == 3 && !_fadingOut)
        {
            _hint3Timer += delta;
        }
    }

    /// <summary>
    /// Called each tick (or poll) with the current game state.
    /// Advances the hint sequence when dismiss conditions are met.
    /// </summary>
    public void UpdateHints(SharedState state)
    {
        if (_gameOver || _currentHint >= 4 || _fadingOut) return;

        var shouldDismiss = _currentHint switch
        {
            // Hint 0: "Connect a Power Plant…"
            // Dismissed once population grows (player has power + zones working) OR tick > 50
            0 => state.Population > 0 || state.Tick > 50,

            // Hint 1: "Add Residential zones…"
            // Dismissed once a meaningful population has settled
            1 => state.Population > 20,

            // Hint 2: "Build Fire/Police/School…"
            // Dismissed once the player is feeling happiness pressure OR tick > 200
            2 => state.Happiness < 0.6 || state.Tick > 200,

            // Hint 3: "Milestones…"
            // Dismissed after 30 seconds of display
            3 => _hint3Timer >= Hint3DisplayTime,

            _ => false,
        };

        if (shouldDismiss)
            StartFadeOut();
    }

    /// <summary>Call when the game ends (bankruptcy). Immediately hides the panel.</summary>
    public void SetGameOver()
    {
        _gameOver      = true;
        _panel.Visible = false;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void StartFadeOut()
    {
        _fadingOut = true;
        _fadeTimer = FadeOutDuration;
    }

    private static StyleBoxFlat MakePanelStyle()
    {
        var style = new StyleBoxFlat();
        style.BgColor                 = new Color(0f, 0f, 0f, 0.65f);
        style.ContentMarginLeft       = 10;
        style.ContentMarginRight      = 10;
        style.ContentMarginTop        = 6;
        style.ContentMarginBottom     = 6;
        style.CornerRadiusTopLeft     = 4;
        style.CornerRadiusTopRight    = 4;
        style.CornerRadiusBottomLeft  = 4;
        style.CornerRadiusBottomRight = 4;
        return style;
    }
}
