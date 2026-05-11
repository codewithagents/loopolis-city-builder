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

    // Fired when the player clicks Menu.
    [Signal]
    public delegate void MainMenuRequestedEventHandler();

    // Fired when the player changes the tax rate.
    [Signal]
    public delegate void TaxRateChangedEventHandler(string level);

    // Fired when the player changes the game speed.
    [Signal]
    public delegate void SpeedChangedEventHandler(float ticksPerSecond);

    private string _selectedZone = "Road";
    private string _taxLevel = "normal";
    private float _selectedSpeed = 2.0f;
    private readonly Dictionary<string, Button> _buttons = new();
    private Button? _pauseButton;
    private readonly Dictionary<string, Button> _taxButtons = new();
    private readonly Dictionary<float, Button> _speedButtons = new();

    // Zone definitions: (label, zone name, background color, tooltip)
    private static readonly (string Label, string Zone, Color Color, string Tooltip)[] ZoneButtons =
    {
        ("R",       "Residential",   new Color(0.2f,  0.7f,  0.2f), "Residential — Place: $50 · Maint: $0.50/tick"),
        ("C",       "Commercial",    new Color(0.2f,  0.4f,  0.9f), "Commercial — Place: $100 · Maint: $0.50/tick"),
        ("I",       "Industrial",    new Color(0.9f,  0.8f,  0.1f), "Industrial — Place: $75 · Maint: $0.25/tick"),
        ("Road",    "Road",          new Color(0.5f,  0.5f,  0.5f), "Road — Place: $25 · Maint: $1.00/tick"),
        ("Line",    "PowerLine",     new Color(0.1f,  0.9f,  0.9f), "Power Line — Place: $40 · Maint: $0.50/tick"),
        ("Plant",   "PowerPlant",    new Color(0.9f,  0.3f,  0.1f), "Power Plant — Place: $500 · Maint: $8.00/tick"),
        ("Fire",    "FireStation",   new Color(1.0f,  0.4f,  0.1f), "Fire Station — Place: $300 · Maint: $3.00/tick"),
        ("Police",  "PoliceStation", new Color(0.2f,  0.4f,  1.0f), "Police Station — Place: $300 · Maint: $3.00/tick"),
        ("School",  "School",        new Color(0.7f,  0.3f,  0.9f), "School — Place: $400 · Maint: $5.00/tick"),
        ("Erase",   "Erase",         new Color(0.6f,  0.15f, 0.15f), "Erase — no cost"),
    };

    // Tax rate button definitions: (label, level, background color)
    private static readonly (string Label, string Level, Color Color)[] TaxButtonDefs =
    {
        ("Tax: Low",  "low",    new Color(0.2f, 0.8f, 0.3f)),
        ("Tax: Norm", "normal", new Color(0.4f, 0.4f, 0.7f)),
        ("Tax: High", "high",   new Color(0.85f, 0.3f, 0.2f)),
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
        foreach (var (label, zone, color, tooltip) in ZoneButtons)
        {
            var btn = MakeZoneButton(label, color, tooltip);
            btn.Pressed += () => SelectZone(zone, btn);
            hbox.AddChild(btn);
            _buttons[zone] = btn;
        }

        // Separator
        var sep = new VSeparator();
        hbox.AddChild(sep);

        // Tax rate buttons
        foreach (var (label, level, color) in TaxButtonDefs)
        {
            var taxBtn = MakeZoneButton(label, color);
            taxBtn.CustomMinimumSize = new Vector2(72, 44);
            var capturedLevel = level;
            taxBtn.Pressed += () => SelectTaxLevel(capturedLevel);
            hbox.AddChild(taxBtn);
            _taxButtons[level] = taxBtn;
        }

        // Highlight the default tax level
        HighlightTaxButton(_taxLevel);

        // Separator
        var sep2 = new VSeparator();
        hbox.AddChild(sep2);

        // Pause / Resume button
        _pauseButton = new Button();
        _pauseButton.Text = "Pause";
        _pauseButton.CustomMinimumSize = new Vector2(80, 44);
        _pauseButton.AddThemeFontSizeOverride("font_size", 14);
        _pauseButton.Pressed += OnPauseToggled;
        hbox.AddChild(_pauseButton);

        // Speed label
        var speedLabel = new Label();
        speedLabel.Text = "Speed:";
        speedLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        speedLabel.AddThemeFontSizeOverride("font_size", 13);
        hbox.AddChild(speedLabel);

        // Speed buttons: ½×, 1×, 2×, 4×
        var speedOptions = new (string Label, float Tps)[]
        {
            ("½×", 0.5f),
            ("1×", 1.0f),
            ("2×", 2.0f),
            ("4×", 4.0f),
        };
        foreach (var (label, tps) in speedOptions)
        {
            var btn = new Button();
            btn.Text = label;
            btn.CustomMinimumSize = new Vector2(44, 44);
            btn.AddThemeFontSizeOverride("font_size", 13);
            btn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
            var style = MakeSpeedButtonStyle(false);
            btn.AddThemeStyleboxOverride("normal", style);
            btn.AddThemeStyleboxOverride("focus", style);
            var capturedTps = tps;
            btn.Pressed += () => SelectSpeed(capturedTps);
            hbox.AddChild(btn);
            _speedButtons[tps] = btn;
        }

        // Highlight the default speed (2×)
        HighlightSpeedButton(_selectedSpeed);

        // New Game button
        var newGameBtn = new Button();
        newGameBtn.Text = "New Game";
        newGameBtn.CustomMinimumSize = new Vector2(90, 44);
        newGameBtn.AddThemeFontSizeOverride("font_size", 14);
        newGameBtn.Pressed += () => EmitSignal(SignalName.NewGameRequested);
        hbox.AddChild(newGameBtn);

        // Menu button
        var menuBtn = new Button();
        menuBtn.Text = "Menu";
        menuBtn.CustomMinimumSize = new Vector2(70, 44);
        menuBtn.AddThemeFontSizeOverride("font_size", 14);
        menuBtn.Pressed += () => EmitSignal(SignalName.MainMenuRequested);
        hbox.AddChild(menuBtn);

        // Highlight the default selection
        HighlightButton(_selectedZone);
    }

    /// <summary>Update the Pause/Resume button label to reflect game state.</summary>
    public void SetPaused(bool paused)
    {
        if (_pauseButton != null)
            _pauseButton.Text = paused ? "Resume" : "Pause";
    }

    /// <summary>Programmatically set the game speed (e.g. from keyboard shortcuts in World.cs).</summary>
    public void SetSpeed(float tps) => SelectSpeed(tps);

    public string SelectedZone => _selectedZone;

    // ── Private helpers ────────────────────────────────────────────────────

    /// <summary>Programmatically select a zone by name (e.g. from keyboard shortcuts in World.cs).</summary>
    public void SelectZone(string zone)
    {
        if (!_buttons.TryGetValue(zone, out var btn)) return;
        SelectZone(zone, btn);
    }

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

    private void SelectSpeed(float tps)
    {
        _selectedSpeed = tps;
        HighlightSpeedButton(tps);
        EmitSignal(SignalName.SpeedChanged, tps);
    }

    private void HighlightSpeedButton(float tps)
    {
        foreach (var (speed, btn) in _speedButtons)
        {
            var selected = Mathf.IsEqualApprox(speed, tps);
            btn.AddThemeStyleboxOverride("normal", MakeSpeedButtonStyle(selected));
            btn.AddThemeStyleboxOverride("focus", MakeSpeedButtonStyle(selected));
        }
    }

    private static StyleBoxFlat MakeSpeedButtonStyle(bool selected)
    {
        var s = new StyleBoxFlat();
        s.BgColor = selected ? new Color(0.3f, 0.3f, 0.5f) : new Color(0.15f, 0.15f, 0.2f);
        s.BorderColor = selected ? new Color(0.8f, 0.8f, 1f) : new Color(0.3f, 0.3f, 0.4f);
        s.BorderWidthBottom = s.BorderWidthTop = s.BorderWidthLeft = s.BorderWidthRight = selected ? 2 : 1;
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight = s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 3;
        s.ContentMarginLeft = s.ContentMarginRight = 4;
        s.ContentMarginTop = s.ContentMarginBottom = 4;
        return s;
    }

    private void SelectTaxLevel(string level)
    {
        _taxLevel = level;
        HighlightTaxButton(level);
        EmitSignal(SignalName.TaxRateChanged, level);
    }

    private void HighlightTaxButton(string activeLevel)
    {
        foreach (var (level, btn) in _taxButtons)
        {
            var isActive = level == activeLevel;
            foreach (var (_, lvl, color) in TaxButtonDefs)
            {
                if (lvl != level) continue;
                if (isActive)
                {
                    var style = new StyleBoxFlat();
                    style.BgColor = color * 0.9f;
                    style.BorderColor = new Color(1f, 1f, 1f);
                    style.BorderWidthBottom = 3;
                    style.BorderWidthTop    = 3;
                    style.BorderWidthLeft   = 3;
                    style.BorderWidthRight  = 3;
                    style.CornerRadiusTopLeft     = 3;
                    style.CornerRadiusTopRight    = 3;
                    style.CornerRadiusBottomLeft  = 3;
                    style.CornerRadiusBottomRight = 3;
                    style.ContentMarginLeft   = 4;
                    style.ContentMarginRight  = 4;
                    style.ContentMarginTop    = 4;
                    style.ContentMarginBottom = 4;
                    btn.AddThemeStyleboxOverride("normal",  style);
                    btn.AddThemeStyleboxOverride("hover",   style);
                    btn.AddThemeStyleboxOverride("pressed", style);
                    btn.AddThemeStyleboxOverride("focus",   style);
                }
                else
                {
                    var style = new StyleBoxFlat();
                    style.BgColor = color * 0.5f;
                    style.BorderColor = color;
                    style.BorderWidthBottom = 2;
                    style.BorderWidthTop    = 2;
                    style.BorderWidthLeft   = 2;
                    style.BorderWidthRight  = 2;
                    style.CornerRadiusTopLeft     = 3;
                    style.CornerRadiusTopRight    = 3;
                    style.CornerRadiusBottomLeft  = 3;
                    style.CornerRadiusBottomRight = 3;
                    style.ContentMarginLeft   = 4;
                    style.ContentMarginRight  = 4;
                    style.ContentMarginTop    = 4;
                    style.ContentMarginBottom = 4;
                    btn.AddThemeStyleboxOverride("normal",  style);
                    btn.AddThemeStyleboxOverride("hover",   MakeHoverStyle(style));
                    btn.AddThemeStyleboxOverride("pressed", style);
                    btn.AddThemeStyleboxOverride("focus",   style);
                }
                break;
            }
        }
    }

    private static Button MakeZoneButton(string label, Color bgColor, string tooltip = "")
    {
        var btn = new Button();
        btn.Text = label;
        btn.TooltipText = tooltip;
        btn.CustomMinimumSize = new Vector2(64, 44);
        btn.AddThemeFontSizeOverride("font_size", 14);

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
        foreach (var (label, z, color, _) in ZoneButtons)
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
