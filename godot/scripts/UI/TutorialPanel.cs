using Godot;

namespace LoopolisGodot;

/// <summary>
/// Persistent tutorial instruction banner. Anchored to the bottom-center of the screen,
/// slightly above the midpoint. Shows the current step instruction and step counter.
/// Layer 11 — above Toolbar (9) and TopBar (12 is above this, which is fine since
/// TopBar is docked at top and tutorial panel is at bottom-center).
/// </summary>
public partial class TutorialPanel : CanvasLayer
{
    private PanelContainer _panel   = null!;
    private Label          _stepLabel    = null!;
    private Label          _instructionLabel = null!;
    private Label          _checkLabel  = null!;

    private float _flashTimer  = 0f;
    private bool  _flashing    = false;
    private bool  _isVisible   = false;

    // Amber accent colors
    private static readonly Color AmberBorder = new(0.90f, 0.70f, 0.15f);
    private static readonly Color DarkBg      = new(0.05f, 0.05f, 0.08f, 0.92f);

    public new bool IsVisible => _isVisible;

    public override void _Ready()
    {
        Layer = 11;

        // Root control — full viewport, no mouse blocking
        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(root);

        // Panel — 320×90, anchored bottom-center, shifted up to y=65% of viewport
        _panel = new PanelContainer();
        _panel.CustomMinimumSize = new Vector2(320, 88);
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Panel style: dark background with amber left border stripe
        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = DarkBg;
        panelStyle.BorderColor = AmberBorder;
        panelStyle.BorderWidthLeft   = 5;
        panelStyle.BorderWidthRight  = 1;
        panelStyle.BorderWidthTop    = 1;
        panelStyle.BorderWidthBottom = 1;
        panelStyle.CornerRadiusTopLeft    = 4;
        panelStyle.CornerRadiusTopRight   = 4;
        panelStyle.CornerRadiusBottomLeft = 4;
        panelStyle.CornerRadiusBottomRight = 4;
        panelStyle.ContentMarginLeft   = 14;
        panelStyle.ContentMarginRight  = 14;
        panelStyle.ContentMarginTop    = 10;
        panelStyle.ContentMarginBottom = 10;
        _panel.AddThemeStyleboxOverride("panel", panelStyle);

        // Anchor panel to bottom-center of viewport
        _panel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical   = Control.GrowDirection.Begin;
        // Position upward from bottom: 180px above the bottom edge
        _panel.Position = new Vector2(-160, -180);
        root.AddChild(_panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        _panel.AddChild(vbox);

        // Step counter (small, muted)
        _stepLabel = new Label();
        _stepLabel.Text = "Step 1 / 5";
        _stepLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _stepLabel.AddThemeColorOverride("font_color", new Color(0.60f, 0.58f, 0.40f));
        _stepLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(_stepLabel);

        // Main instruction (large, white)
        _instructionLabel = new Label();
        _instructionLabel.Text = "";
        _instructionLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _instructionLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _instructionLabel.AddThemeFontSizeOverride("font_size", 14);
        _instructionLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _instructionLabel.CustomMinimumSize = new Vector2(290, 0);
        vbox.AddChild(_instructionLabel);

        // Checkmark — shown briefly when step completes
        _checkLabel = new Label();
        _checkLabel.Text = "Step complete!";
        _checkLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _checkLabel.AddThemeColorOverride("font_color", new Color(0.40f, 0.95f, 0.40f));
        _checkLabel.AddThemeFontSizeOverride("font_size", 12);
        _checkLabel.Visible = false;
        vbox.AddChild(_checkLabel);

        // Hidden by default
        _panel.Visible = false;
        _isVisible = false;
    }

    public override void _Process(double delta)
    {
        if (!_flashing) return;

        _flashTimer -= (float)delta;
        if (_flashTimer <= 0f)
        {
            _flashing = false;
            _checkLabel.Visible = false;
        }
    }

    /// <summary>Shows the banner with the given instruction text and step number (1-5).</summary>
    public void ShowStep(int step, string instruction)
    {
        _stepLabel.Text = $"Step {step} / 5";
        _instructionLabel.Text = instruction;
        _checkLabel.Visible = false;
        _flashing = false;
        _panel.Visible = true;
        _isVisible = true;
    }

    /// <summary>Brief green checkmark flash before advancing to the next step.</summary>
    public void CompleteStep()
    {
        _checkLabel.Visible = true;
        _flashing = true;
        _flashTimer = 1.2f;
    }

    /// <summary>Fades out and hides the tutorial banner.</summary>
    public void HideTutorial()
    {
        _panel.Visible = false;
        _isVisible = false;
        _flashing = false;
        _checkLabel.Visible = false;
    }
}
