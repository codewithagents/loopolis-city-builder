using Godot;
using Loopolis.Core.Simulation;

namespace LoopolisGodot;

/// <summary>
/// Persistent advisor message strip anchored to the bottom of the screen.
/// Shows the single most important hint from CityAdvisor.Advise() colored by priority.
///
/// Layout (CanvasLayer, layer = 9):
///   PanelContainer (bottom-center, semi-transparent dark background, 32px tall)
///     HBoxContainer
///       Label  — icon (colored by priority)
///       Label  — message text (colored by priority)
///
/// Toggle with 'B' key.
/// Updated each tick via UpdateFromEngine() in standalone mode.
/// </summary>
public partial class AdvisorBar : CanvasLayer
{
    // ── Colors per priority ────────────────────────────────────────────────
    private static readonly Color ColorGood     = new(0.667f, 0.667f, 0.667f); // #AAAAAA
    private static readonly Color ColorTip      = new(0.000f, 0.800f, 1.000f); // #00CCFF
    private static readonly Color ColorWarning  = new(1.000f, 0.800f, 0.000f); // #FFCC00
    private static readonly Color ColorCritical = new(1.000f, 0.267f, 0.267f); // #FF4444

    // ── Icons per priority ─────────────────────────────────────────────────
    private static readonly string IconGood     = "✓";
    private static readonly string IconTip      = "i";
    private static readonly string IconWarning  = "⚠";
    private static readonly string IconCritical = "⚠";

    // ── Background colors per priority (subtle tint) ───────────────────────
    private static readonly Color BgNormal   = new(0.04f, 0.04f, 0.04f, 0.82f);
    private static readonly Color BgWarning  = new(0.15f, 0.10f, 0.00f, 0.88f);
    private static readonly Color BgCritical = new(0.20f, 0.03f, 0.03f, 0.88f);

    private Label _iconLabel   = null!;
    private Label _textLabel   = null!;
    private StyleBoxFlat _bgStyle = null!;
    private PanelContainer _panel = null!;

    // Tracks whether the bar is visible (toggled with 'B')
    private bool _barVisible = true;

    // Last displayed text/priority — skip redraw when unchanged
    private string _lastText = "";
    private AdvisoryPriority _lastPriority = AdvisoryPriority.Good;

    public override void _Ready()
    {
        Layer = 9; // below TopBar (12), above the city grid

        // ── Panel container — anchored bottom-left, fixed 32px height ─────────
        _panel = new PanelContainer();
        _panel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _panel.GrowVertical   = Control.GrowDirection.Begin;
        _panel.GrowHorizontal = Control.GrowDirection.End;
        // Offset: sit just above the very bottom edge of the screen.
        // AnchorBottom=1, OffsetTop=-32 → 32 px tall strip at the bottom.
        _panel.OffsetTop    = -32f;
        _panel.OffsetBottom =   0f;
        _panel.OffsetLeft   =   0f;
        // Right edge matches screen width (AnchorRight=1, but we use BottomLeft preset
        // so we set anchors manually to span full width).
        _panel.AnchorRight  = 1f;
        _panel.MouseFilter  = Control.MouseFilterEnum.Ignore;

        _bgStyle = new StyleBoxFlat();
        _bgStyle.BgColor              = BgNormal;
        _bgStyle.ContentMarginLeft    = 10;
        _bgStyle.ContentMarginRight   = 10;
        _bgStyle.ContentMarginTop     = 4;
        _bgStyle.ContentMarginBottom  = 4;
        _panel.AddThemeStyleboxOverride("panel", _bgStyle);
        AddChild(_panel);

        // ── HBoxContainer inside the panel ────────────────────────────────────
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 6);
        hbox.Alignment    = BoxContainer.AlignmentMode.Center;
        hbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        hbox.MouseFilter  = Control.MouseFilterEnum.Ignore;
        _panel.AddChild(hbox);

        // Icon label
        _iconLabel = new Label();
        _iconLabel.Text                = IconGood;
        _iconLabel.VerticalAlignment   = VerticalAlignment.Center;
        _iconLabel.AddThemeColorOverride("font_color", ColorGood);
        _iconLabel.AddThemeFontSizeOverride("font_size", 14);
        _iconLabel.MouseFilter         = Control.MouseFilterEnum.Ignore;
        hbox.AddChild(_iconLabel);

        // Message text label
        _textLabel = new Label();
        _textLabel.Text                = "City advisor initializing…";
        _textLabel.VerticalAlignment   = VerticalAlignment.Center;
        _textLabel.ClipText            = true;
        _textLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _textLabel.AddThemeColorOverride("font_color", ColorGood);
        _textLabel.AddThemeFontSizeOverride("font_size", 13);
        _textLabel.MouseFilter         = Control.MouseFilterEnum.Ignore;
        hbox.AddChild(_textLabel);

        // 'B' hint on the far right
        var hintLabel = new Label();
        hintLabel.Text               = "[B]";
        hintLabel.VerticalAlignment  = VerticalAlignment.Center;
        hintLabel.AddThemeColorOverride("font_color", new Color(0.35f, 0.35f, 0.35f));
        hintLabel.AddThemeFontSizeOverride("font_size", 10);
        hintLabel.MouseFilter        = Control.MouseFilterEnum.Ignore;
        hbox.AddChild(hintLabel);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Update the advisor bar from the live SimulationEngine (standalone mode).
    /// Skips redraw when text and priority are unchanged.
    /// </summary>
    public void UpdateFromEngine(SimulationEngine engine)
    {
        if (!_barVisible) return;
        var advice = engine.CurrentAdvice;
        ApplyAdvice(advice.Text, advice.Priority);
    }

    /// <summary>
    /// Update the advisor bar from a raw text + priority string (viewer mode or pre-built message).
    /// Priority string matches AdvisoryPriority enum names (case-insensitive).
    /// </summary>
    public void UpdateFromState(string text, string priorityName)
    {
        if (!_barVisible) return;
        if (!System.Enum.TryParse<AdvisoryPriority>(priorityName, ignoreCase: true, out var priority))
            priority = AdvisoryPriority.Tip;
        ApplyAdvice(text, priority);
    }

    /// <summary>Toggle bar visibility on/off.</summary>
    public void Toggle()
    {
        _barVisible = !_barVisible;
        _panel.Visible = _barVisible;
    }

    public bool IsBarVisible => _barVisible;

    // ── Internal helpers ───────────────────────────────────────────────────

    private void ApplyAdvice(string text, AdvisoryPriority priority)
    {
        // Skip if nothing changed — avoids redundant theme calls every tick
        if (text == _lastText && priority == _lastPriority) return;
        _lastText     = text;
        _lastPriority = priority;

        var (icon, color, bg) = priority switch
        {
            AdvisoryPriority.Critical => (IconCritical, ColorCritical, BgCritical),
            AdvisoryPriority.Warning  => (IconWarning,  ColorWarning,  BgWarning),
            AdvisoryPriority.Tip      => (IconTip,      ColorTip,      BgNormal),
            _                         => (IconGood,      ColorGood,     BgNormal),
        };

        _iconLabel.Text = icon;
        _iconLabel.AddThemeColorOverride("font_color", color);

        _textLabel.Text = text;
        _textLabel.AddThemeColorOverride("font_color", color);

        _bgStyle.BgColor = bg;
        _panel.AddThemeStyleboxOverride("panel", _bgStyle);
    }
}
