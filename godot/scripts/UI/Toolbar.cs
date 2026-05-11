using Godot;
using System.Collections.Generic;

namespace LoopolisGodot;

/// <summary>
/// Horizontal toolbar at the bottom of the screen.
/// Lets the player choose a zone/tool, pause/resume, and start a new game.
/// Writes command.json in viewer mode; calls back to World in standalone mode.
/// </summary>
public partial class Toolbar : CanvasLayer
{
    // Fired when the player selects a tool. World.cs subscribes.
    [Signal]
    public delegate void ZoneSelectedEventHandler(string zoneName);

    // Fired when the player clicks Pause/Resume.
    [Signal]
    public delegate void PauseToggledEventHandler();

    // Fired when the player clicks New Game.
    [Signal]
    public delegate void NewGameRequestedEventHandler();

    private string _selectedZone = "Road";
    private readonly Dictionary<string, Button> _buttons = new();
    private Button? _pauseButton;

    // Zone definitions: (label, zone name, background color)
    private static readonly (string Label, string Zone, Color Color)[] ZoneButtons =
    {
        ("R",       "Residential", new Color(0.2f,  0.7f,  0.2f)),
        ("C",       "Commercial",  new Color(0.2f,  0.4f,  0.9f)),
        ("I",       "Industrial",  new Color(0.9f,  0.8f,  0.1f)),
        ("Road",    "Road",        new Color(0.5f,  0.5f,  0.5f)),
        ("Line",    "PowerLine",   new Color(0.1f,  0.9f,  0.9f)),
        ("Plant",   "PowerPlant",  new Color(0.9f,  0.3f,  0.1f)),
        ("Fire",    "FireStation", new Color(1.0f,  0.4f,  0.1f)),
        ("Police",  "PoliceStation", new Color(0.2f, 0.4f, 1.0f)),
        ("School",  "School",      new Color(0.7f,  0.3f,  0.9f)),
        ("Erase",   "Erase",       new Color(0.6f,  0.15f, 0.15f)),
    };

    public override void _Ready()
    {
        Layer = 9;

        // Container anchored to the bottom of the screen
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        panel.GrowVertical = Control.GrowDirection.Begin;
        panel.AddThemeStyleboxOverride("panel", MakePanelStyle());
        AddChild(panel);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 4);
        panel.AddChild(hbox);

        // Zone / tool buttons
        foreach (var (label, zone, color) in ZoneButtons)
        {
            var btn = MakeZoneButton(label, color);
            btn.Pressed += () => SelectZone(zone, btn);
            hbox.AddChild(btn);
            _buttons[zone] = btn;
        }

        // Separator
        var sep = new VSeparator();
        hbox.AddChild(sep);

        // Pause / Resume button
        _pauseButton = new Button();
        _pauseButton.Text = "Pause";
        _pauseButton.CustomMinimumSize = new Vector2(60, 32);
        _pauseButton.Pressed += OnPauseToggled;
        hbox.AddChild(_pauseButton);

        // New Game button
        var newGameBtn = new Button();
        newGameBtn.Text = "New Game";
        newGameBtn.CustomMinimumSize = new Vector2(70, 32);
        newGameBtn.Pressed += () => EmitSignal(SignalName.NewGameRequested);
        hbox.AddChild(newGameBtn);

        // Highlight the default selection
        HighlightButton(_selectedZone);
    }

    /// <summary>Update the Pause/Resume button label to reflect game state.</summary>
    public void SetPaused(bool paused)
    {
        if (_pauseButton != null)
            _pauseButton.Text = paused ? "Resume" : "Pause";
    }

    public string SelectedZone => _selectedZone;

    // ── Private helpers ────────────────────────────────────────────────────

    private void SelectZone(string zone, Button btn)
    {
        _selectedZone = zone;
        HighlightButton(zone);
        EmitSignal(SignalName.ZoneSelected, zone);
    }

    private void HighlightButton(string zone)
    {
        foreach (var (z, btn) in _buttons)
        {
            if (z == zone)
            {
                // Bright white border to indicate selection
                var style = MakeButtonStyle(btn.GetThemeStylebox("normal") as StyleBoxFlat, selected: true);
                btn.AddThemeStyleboxOverride("normal",   style);
                btn.AddThemeStyleboxOverride("hover",    style);
                btn.AddThemeStyleboxOverride("pressed",  style);
                btn.AddThemeStyleboxOverride("focus",    style);
            }
            else
            {
                var style = GetBaseStyle(z);
                btn.AddThemeStyleboxOverride("normal",  style);
                btn.AddThemeStyleboxOverride("hover",   MakeHoverStyle(style));
                btn.AddThemeStyleboxOverride("pressed", style);
                btn.AddThemeStyleboxOverride("focus",   style);
            }
        }
    }

    private void OnPauseToggled()
    {
        EmitSignal(SignalName.PauseToggled);
    }

    private static Button MakeZoneButton(string label, Color bgColor)
    {
        var btn = new Button();
        btn.Text = label;
        btn.CustomMinimumSize = new Vector2(48, 32);

        var baseStyle = new StyleBoxFlat();
        baseStyle.BgColor = bgColor * 0.6f;  // slightly dark by default
        baseStyle.BorderColor = bgColor;
        baseStyle.BorderWidthBottom = 2;
        baseStyle.BorderWidthTop    = 2;
        baseStyle.BorderWidthLeft   = 2;
        baseStyle.BorderWidthRight  = 2;
        baseStyle.CornerRadiusTopLeft     = 3;
        baseStyle.CornerRadiusTopRight    = 3;
        baseStyle.CornerRadiusBottomLeft  = 3;
        baseStyle.CornerRadiusBottomRight = 3;
        baseStyle.ContentMarginLeft   = 4;
        baseStyle.ContentMarginRight  = 4;
        baseStyle.ContentMarginTop    = 4;
        baseStyle.ContentMarginBottom = 4;

        btn.AddThemeStyleboxOverride("normal",  baseStyle);
        btn.AddThemeStyleboxOverride("hover",   MakeHoverStyle(baseStyle));
        btn.AddThemeStyleboxOverride("pressed", baseStyle);
        btn.AddThemeStyleboxOverride("focus",   baseStyle);
        btn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));

        return btn;
    }

    private static StyleBoxFlat MakeButtonStyle(StyleBoxFlat? existing, bool selected)
    {
        var style = new StyleBoxFlat();
        if (existing != null)
        {
            style.BgColor     = existing.BgColor;
            style.BorderColor = existing.BorderColor;
            style.CornerRadiusTopLeft     = existing.CornerRadiusTopLeft;
            style.CornerRadiusTopRight    = existing.CornerRadiusTopRight;
            style.CornerRadiusBottomLeft  = existing.CornerRadiusBottomLeft;
            style.CornerRadiusBottomRight = existing.CornerRadiusBottomRight;
        }
        else
        {
            style.BgColor     = new Color(0.3f, 0.3f, 0.3f);
            style.BorderColor = new Color(1f, 1f, 1f);
        }

        if (selected)
        {
            style.BgColor = style.BgColor * 1.4f;  // brighter background
            style.BorderColor = new Color(1f, 1f, 1f);  // white border
            style.BorderWidthBottom = 3;
            style.BorderWidthTop    = 3;
            style.BorderWidthLeft   = 3;
            style.BorderWidthRight  = 3;
        }
        else
        {
            style.BorderWidthBottom = 2;
            style.BorderWidthTop    = 2;
            style.BorderWidthLeft   = 2;
            style.BorderWidthRight  = 2;
        }

        style.ContentMarginLeft   = 4;
        style.ContentMarginRight  = 4;
        style.ContentMarginTop    = 4;
        style.ContentMarginBottom = 4;
        return style;
    }

    private static StyleBoxFlat MakeHoverStyle(StyleBoxFlat baseStyle)
    {
        var style = new StyleBoxFlat();
        style.BgColor     = baseStyle.BgColor * 1.2f;
        style.BorderColor = baseStyle.BorderColor;
        style.BorderWidthBottom = baseStyle.BorderWidthBottom;
        style.BorderWidthTop    = baseStyle.BorderWidthTop;
        style.BorderWidthLeft   = baseStyle.BorderWidthLeft;
        style.BorderWidthRight  = baseStyle.BorderWidthRight;
        style.CornerRadiusTopLeft     = baseStyle.CornerRadiusTopLeft;
        style.CornerRadiusTopRight    = baseStyle.CornerRadiusTopRight;
        style.CornerRadiusBottomLeft  = baseStyle.CornerRadiusBottomLeft;
        style.CornerRadiusBottomRight = baseStyle.CornerRadiusBottomRight;
        style.ContentMarginLeft   = baseStyle.ContentMarginLeft;
        style.ContentMarginRight  = baseStyle.ContentMarginRight;
        style.ContentMarginTop    = baseStyle.ContentMarginTop;
        style.ContentMarginBottom = baseStyle.ContentMarginBottom;
        return style;
    }

    private StyleBoxFlat GetBaseStyle(string zone)
    {
        // Recreate the base style from zone colors
        foreach (var (label, z, color) in ZoneButtons)
        {
            if (z == zone)
            {
                var s = new StyleBoxFlat();
                s.BgColor     = color * 0.6f;
                s.BorderColor = color;
                s.BorderWidthBottom = 2;
                s.BorderWidthTop    = 2;
                s.BorderWidthLeft   = 2;
                s.BorderWidthRight  = 2;
                s.CornerRadiusTopLeft     = 3;
                s.CornerRadiusTopRight    = 3;
                s.CornerRadiusBottomLeft  = 3;
                s.CornerRadiusBottomRight = 3;
                s.ContentMarginLeft   = 4;
                s.ContentMarginRight  = 4;
                s.ContentMarginTop    = 4;
                s.ContentMarginBottom = 4;
                return s;
            }
        }
        var fallback = new StyleBoxFlat();
        fallback.BgColor = new Color(0.3f, 0.3f, 0.3f);
        return fallback;
    }

    private static StyleBoxFlat MakePanelStyle()
    {
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.05f, 0.05f, 0.05f, 0.85f);
        style.ContentMarginLeft   = 8;
        style.ContentMarginRight  = 8;
        style.ContentMarginTop    = 6;
        style.ContentMarginBottom = 6;
        return style;
    }
}
