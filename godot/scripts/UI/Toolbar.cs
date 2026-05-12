using Godot;
using System.Collections.Generic;

namespace LoopolisGodot;

/// <summary>
/// Horizontal toolbar at the bottom of the screen.
/// Buttons are grouped into three tabs: Zones (Z), Utilities (U), Services (S).
/// Always-visible strip: Erase, Tax, Pause, Speed, New Game, Menu.
///
/// Tab keyboard shortcuts: Z = Zones, U = Utilities, S = Services.
/// Zone shortcuts R/C/I/Road always work regardless of active tab.
/// </summary>
public partial class Toolbar : CanvasLayer
{
    // ── Signals ────────────────────────────────────────────────────────────────

    [Signal] public delegate void ZoneSelectedEventHandler(string zoneName);
    [Signal] public delegate void PauseToggledEventHandler();
    [Signal] public delegate void NewGameRequestedEventHandler();
    [Signal] public delegate void MainMenuRequestedEventHandler();
    [Signal] public delegate void TaxRateChangedEventHandler(string level);
    [Signal] public delegate void SpeedChangedEventHandler(float ticksPerSecond);

    // ── State ──────────────────────────────────────────────────────────────────

    private string _selectedZone = "";   // empty = no tool selected
    private string _taxLevel     = "normal";
    private float  _selectedSpeed = 2.0f;
    private int    _activeTab    = 0;   // 0 = Zones, 1 = Utilities, 2 = Services
    private int    _lastKnownPop = 0;

    private readonly Dictionary<string, Button> _buttons    = new();
    private readonly Dictionary<string, Button> _taxButtons = new();
    private readonly Dictionary<float,  Button> _speedButtons = new();
    private Button? _pauseButton;
    private Label?  _buildModeLabel;

    // Tab containers: each holds the buttons for one tab
    private readonly Container[] _tabContainers = new Container[3];
    // Tab header buttons
    private readonly Button[] _tabHeaders = new Button[3];

    // ── Zone / tool definitions ────────────────────────────────────────────────

    // Tab index: 0 = Zones, 1 = Utilities, 2 = Services
    // milestoneMin = 0 always available; 500 = Town; 5000 = City
    private static readonly (string Label, string Zone, Color Color, string Tooltip, int MilestoneMin, int Tab)[] ZoneButtons =
    {
        // Tab 0 — Zones
        ("R",       "Residential", new Color(0.2f,  0.7f,  0.2f),  "Residential — Place: $50 · Maint: $0.50/tick",          0, 0),
        ("C",       "Commercial",  new Color(0.2f,  0.4f,  0.9f),  "Commercial — Place: $100 · Maint: $0.50/tick",          0, 0),
        ("I",       "Industrial",  new Color(0.9f,  0.8f,  0.1f),  "Industrial — Place: $75 · Maint: $0.25/tick",           0, 0),
        ("Road",    "Road",        new Color(0.5f,  0.5f,  0.5f),  "Road — Place: $25 · Maint: $1.00/tick",                 0, 0),
        ("Avenue",  "Avenue",      new Color(0.62f, 0.62f, 0.62f), "Avenue — Place: $60 · Maint: $2.00/tick\nHigher capacity road — Requires Town (500 pop)", 500, 0),

        // Tab 1 — Utilities
        ("Coal",    "CoalPlant",    new Color(0.259f,0.259f,0.259f),"Coal Plant — Place: $500 · Maint: $8.00/tick\n500 MW output — emits pollution",           0, 1),
        ("Nuclear", "NuclearPlant", new Color(0.976f,0.659f,0.145f),"Nuclear Plant — Place: $8,000 · Maint: $50.00/tick\n3,000 MW · clean — Requires Town (500 pop)", 500, 1),

        // Tab 2 — Services
        ("Fire",      "FireStation",   new Color(1.0f,  0.4f,  0.1f),  "Fire Station — Place: $300 · Maint: $3.00/tick\nCoverage radius: 4 tiles",                  0,    2),
        ("Fire HQ",   "FireHQ",        new Color(0.718f,0.110f,0.110f), "Fire HQ — Place: $2,000 · Maint: $25.00/tick\nCoverage radius: 10 tiles — Requires City (5,000 pop)", 5000, 2),
        ("Police",    "PoliceStation", new Color(0.2f,  0.4f,  1.0f),  "Police Station — Place: $300 · Maint: $3.00/tick\nCoverage radius: 4 tiles",                 0,    2),
        ("Police HQ", "PoliceHQ",      new Color(0.102f,0.137f,0.494f),"Police HQ — Place: $2,000 · Maint: $25.00/tick\nCoverage radius: 10 tiles — Requires City (5,000 pop)", 5000, 2),
        ("School",    "School",        new Color(0.7f,  0.3f,  0.9f),  "School — Place: $400 · Maint: $5.00/tick\nCoverage radius: 5 tiles",                        0,    2),
        ("Hospital",  "Hospital",      new Color(0.647f,0.839f,0.647f),"Hospital — Place: $3,000 · Maint: $35.00/tick\nCoverage radius: 8 tiles — Requires City (5,000 pop)", 5000, 2),
    };

    private static readonly (string Label, string Level, Color Color)[] TaxButtonDefs =
    {
        ("Tax: Low",  "low",    new Color(0.2f, 0.8f, 0.3f)),
        ("Tax: Norm", "normal", new Color(0.4f, 0.4f, 0.7f)),
        ("Tax: High", "high",   new Color(0.85f, 0.3f, 0.2f)),
    };

    private static readonly string[] TabLabels = { "Zones [Z]", "Utilities [U]", "Services [S]" };

    // ── _Ready ─────────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Layer = 9;

        // Outer panel anchored to the bottom
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        panel.GrowVertical = Control.GrowDirection.Begin;
        panel.AddThemeStyleboxOverride("panel", MakePanelStyle());
        AddChild(panel);

        // Outer vbox: [tab header row] + [content row]
        var outerVBox = new VBoxContainer();
        outerVBox.AddThemeConstantOverride("separation", 2);
        panel.AddChild(outerVBox);

        // ── Tab header row ──────────────────────────────────────────────────

        var tabRow = new HBoxContainer();
        tabRow.AddThemeConstantOverride("separation", 2);
        outerVBox.AddChild(tabRow);

        for (var i = 0; i < 3; i++)
        {
            var capturedTab = i;
            var tabBtn = new Button();
            tabBtn.Text = TabLabels[i];
            tabBtn.CustomMinimumSize = new Vector2(100, 28);
            tabBtn.AddThemeFontSizeOverride("font_size", 12);
            tabBtn.Pressed += () => SwitchTab(capturedTab);
            tabRow.AddChild(tabBtn);
            _tabHeaders[i] = tabBtn;
        }

        // Build mode indicator: shown in the tab row when a tool is active
        _buildModeLabel = new Label();
        _buildModeLabel.Text = "⏸ BUILD MODE  [Esc to resume]";
        _buildModeLabel.Visible = false;
        _buildModeLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.1f));
        _buildModeLabel.AddThemeFontSizeOverride("font_size", 13);
        tabRow.AddChild(_buildModeLabel);

        // ── Content row: [tabbed zone area] | [always-visible tools] ───────

        var contentRow = new HBoxContainer();
        contentRow.AddThemeConstantOverride("separation", 4);
        outerVBox.AddChild(contentRow);

        // Build one HBoxContainer per tab; only the active one is visible
        for (var t = 0; t < 3; t++)
        {
            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", 4);
            hbox.Visible = (t == _activeTab);
            contentRow.AddChild(hbox);
            _tabContainers[t] = hbox;
        }

        // Populate each tab with its zone buttons
        foreach (var (label, zone, color, tooltip, milestoneMin, tab) in ZoneButtons)
        {
            var btn = MakeZoneButton(label, color, tooltip);
            var capturedZone = zone;
            btn.Pressed += () =>
            {
                if (btn.Disabled) return;
                SelectZone(capturedZone, btn);
            };
            _tabContainers[tab].AddChild(btn);
            _buttons[zone] = btn;
        }

        // Apply initial milestone locks
        UpdateMilestoneLocks(0);

        // Erase button — always visible, in the tools strip
        var eraseSep = new VSeparator();
        contentRow.AddChild(eraseSep);

        var eraseBtn = MakeZoneButton("Erase", new Color(0.6f, 0.15f, 0.15f), "Erase — no cost");
        eraseBtn.Pressed += () => SelectZone("Erase", eraseBtn);
        contentRow.AddChild(eraseBtn);
        _buttons["Erase"] = eraseBtn;

        // Separator before tax
        var sep = new VSeparator();
        contentRow.AddChild(sep);

        // Tax rate buttons
        foreach (var (tlabel, level, color) in TaxButtonDefs)
        {
            var taxBtn = MakeZoneButton(tlabel, color);
            taxBtn.CustomMinimumSize = new Vector2(72, 44);
            var capturedLevel = level;
            taxBtn.Pressed += () => SelectTaxLevel(capturedLevel);
            contentRow.AddChild(taxBtn);
            _taxButtons[level] = taxBtn;
        }

        // Highlight default tax level
        HighlightTaxButton(_taxLevel);

        var sep2 = new VSeparator();
        contentRow.AddChild(sep2);

        // Pause button
        _pauseButton = new Button();
        _pauseButton.Text = "Pause";
        _pauseButton.CustomMinimumSize = new Vector2(80, 44);
        _pauseButton.AddThemeFontSizeOverride("font_size", 14);
        _pauseButton.Pressed += OnPauseToggled;
        contentRow.AddChild(_pauseButton);

        // Speed label
        var speedLabel = new Label();
        speedLabel.Text = "Speed:";
        speedLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        speedLabel.AddThemeFontSizeOverride("font_size", 13);
        contentRow.AddChild(speedLabel);

        // Speed buttons
        var speedOptions = new (string Label, float Tps)[]
        {
            ("½×", 0.5f),
            ("1×", 1.0f),
            ("2×", 2.0f),
            ("4×", 4.0f),
        };
        foreach (var (slabel, tps) in speedOptions)
        {
            var btn = new Button();
            btn.Text = slabel;
            btn.CustomMinimumSize = new Vector2(44, 44);
            btn.AddThemeFontSizeOverride("font_size", 13);
            btn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
            var style = MakeSpeedButtonStyle(false);
            btn.AddThemeStyleboxOverride("normal", style);
            btn.AddThemeStyleboxOverride("focus",  style);
            var capturedTps = tps;
            btn.Pressed += () => SelectSpeed(capturedTps);
            contentRow.AddChild(btn);
            _speedButtons[tps] = btn;
        }

        HighlightSpeedButton(_selectedSpeed);

        // New Game button
        var newGameBtn = new Button();
        newGameBtn.Text = "New Game";
        newGameBtn.CustomMinimumSize = new Vector2(90, 44);
        newGameBtn.AddThemeFontSizeOverride("font_size", 14);
        newGameBtn.Pressed += () => EmitSignal(SignalName.NewGameRequested);
        contentRow.AddChild(newGameBtn);

        // Menu button
        var menuBtn = new Button();
        menuBtn.Text = "Menu";
        menuBtn.CustomMinimumSize = new Vector2(70, 44);
        menuBtn.AddThemeFontSizeOverride("font_size", 14);
        menuBtn.Pressed += () => EmitSignal(SignalName.MainMenuRequested);
        contentRow.AddChild(menuBtn);

        // Apply initial highlights (no tool selected on startup)
        HighlightTabHeaders();
        if (!string.IsNullOrEmpty(_selectedZone))
            HighlightButton(_selectedZone);
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    public string SelectedZone => _selectedZone;

    /// <summary>
    /// Deselects all zone/tool buttons and emits ZoneSelected("") so World.cs
    /// knows no tool is active. Safe to call when no tool is selected.
    /// </summary>
    public void DeselectAll()
    {
        _selectedZone = "";
        // Clear all button highlights
        foreach (var (zone, btn) in _buttons)
        {
            var style = GetBaseStyle(zone);
            btn.AddThemeStyleboxOverride("normal",  style);
            btn.AddThemeStyleboxOverride("hover",   MakeHoverStyle(style));
            btn.AddThemeStyleboxOverride("pressed", style);
            btn.AddThemeStyleboxOverride("focus",   style);
        }
        EmitSignal(SignalName.ZoneSelected, "");
    }

    /// <summary>
    /// Shows or hides the BUILD MODE label in the toolbar tab row.
    /// Called by World.cs whenever build-mode pause state changes.
    /// </summary>
    public void SetBuildMode(bool active)
    {
        if (_buildModeLabel != null)
            _buildModeLabel.Visible = active;
    }

    public void SetPaused(bool paused)
    {
        if (_pauseButton != null)
            _pauseButton.Text = paused ? "Resume" : "Pause";
    }

    public void SetSpeed(float tps) => SelectSpeed(tps);

    public void SetTaxRate(string level)
    {
        _taxLevel = level;
        HighlightTaxButton(level);
    }

    /// <summary>Programmatically select a zone by name (e.g. from keyboard shortcuts in World.cs).</summary>
    public void SelectZone(string zone)
    {
        if (!_buttons.TryGetValue(zone, out var btn)) return;
        SelectZone(zone, btn);
    }

    /// <summary>Programmatically switch to a specific tab index (0=Zones, 1=Utilities, 2=Services).</summary>
    public void SwitchToTab(int tabIndex) => SwitchTab(tabIndex);

    /// <summary>Switch to the tab that contains the given zone (used when a keyboard shortcut selects
    /// a zone on a non-active tab, so the player can see the highlighted button).</summary>
    public void SwitchToTabForZone(string zone)
    {
        foreach (var (_, z, _, _, _, tab) in ZoneButtons)
        {
            if (z == zone) { SwitchTab(tab); return; }
        }
        // Erase lives in always-visible strip — no tab switch needed
    }

    public void UpdateMilestoneLocks(int population)
    {
        if (population == _lastKnownPop) return;
        _lastKnownPop = population;

        foreach (var (label, zone, color, tooltip, milestoneMin, tab) in ZoneButtons)
        {
            if (!_buttons.TryGetValue(zone, out var btn)) continue;
            if (milestoneMin <= 0) continue;

            var unlocked = population >= milestoneMin;
            btn.Disabled = !unlocked;

            var lockLabel = milestoneMin >= 5000 ? "Requires City (5,000 pop)" : "Requires Town (500 pop)";
            if (!unlocked)
            {
                btn.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.7f);
                btn.TooltipText = tooltip + $"\n🔒 {lockLabel}";
            }
            else
            {
                btn.Modulate = new Color(1f, 1f, 1f, 1f);
                btn.TooltipText = tooltip;
            }
        }
    }

    // ── Tab switching ───────────────────────────────────────────────────────────

    private void SwitchTab(int tabIndex)
    {
        _activeTab = tabIndex;
        for (var i = 0; i < 3; i++)
            _tabContainers[i].Visible = (i == _activeTab);
        HighlightTabHeaders();
    }

    private void HighlightTabHeaders()
    {
        for (var i = 0; i < 3; i++)
        {
            var btn = _tabHeaders[i];
            if (i == _activeTab)
            {
                var style = new StyleBoxFlat();
                style.BgColor = new Color(0.25f, 0.35f, 0.55f);
                style.BorderColor = new Color(0.6f, 0.8f, 1f);
                style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 2;
                style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 3;
                style.ContentMarginLeft = style.ContentMarginRight = 6;
                style.ContentMarginTop  = style.ContentMarginBottom = 3;
                btn.AddThemeStyleboxOverride("normal",  style);
                btn.AddThemeStyleboxOverride("hover",   style);
                btn.AddThemeStyleboxOverride("pressed", style);
                btn.AddThemeStyleboxOverride("focus",   style);
                btn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
            }
            else
            {
                var style = new StyleBoxFlat();
                style.BgColor = new Color(0.1f, 0.1f, 0.15f);
                style.BorderColor = new Color(0.35f, 0.35f, 0.45f);
                style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 1;
                style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 3;
                style.ContentMarginLeft = style.ContentMarginRight = 6;
                style.ContentMarginTop  = style.ContentMarginBottom = 3;
                btn.AddThemeStyleboxOverride("normal",  style);
                btn.AddThemeStyleboxOverride("focus",   style);
                var hoverStyle = new StyleBoxFlat();
                hoverStyle.BgColor = new Color(0.18f, 0.22f, 0.32f);
                hoverStyle.BorderColor = new Color(0.5f, 0.6f, 0.8f);
                hoverStyle.BorderWidthBottom = hoverStyle.BorderWidthTop = hoverStyle.BorderWidthLeft = hoverStyle.BorderWidthRight = 1;
                hoverStyle.CornerRadiusTopLeft = hoverStyle.CornerRadiusTopRight = hoverStyle.CornerRadiusBottomLeft = hoverStyle.CornerRadiusBottomRight = 3;
                hoverStyle.ContentMarginLeft = hoverStyle.ContentMarginRight = 6;
                hoverStyle.ContentMarginTop  = hoverStyle.ContentMarginBottom = 3;
                btn.AddThemeStyleboxOverride("hover",   hoverStyle);
                btn.AddThemeStyleboxOverride("pressed", style);
                btn.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            }
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private void SelectZone(string zone, Button btn)
    {
        // Clicking the same tool again toggles it off
        if (_selectedZone == zone)
        {
            DeselectAll();
            return;
        }
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
                var style = MakeButtonStyle(GetBaseStyle(z), selected: true);
                btn.AddThemeStyleboxOverride("normal",  style);
                btn.AddThemeStyleboxOverride("hover",   style);
                btn.AddThemeStyleboxOverride("pressed", style);
                btn.AddThemeStyleboxOverride("focus",   style);
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
            btn.AddThemeStyleboxOverride("focus",  MakeSpeedButtonStyle(selected));
        }
    }

    private static StyleBoxFlat MakeSpeedButtonStyle(bool selected)
    {
        var s = new StyleBoxFlat();
        s.BgColor      = selected ? new Color(0.3f, 0.3f, 0.5f) : new Color(0.15f, 0.15f, 0.2f);
        s.BorderColor  = selected ? new Color(0.8f, 0.8f, 1f)   : new Color(0.3f,  0.3f, 0.4f);
        s.BorderWidthBottom = s.BorderWidthTop = s.BorderWidthLeft = s.BorderWidthRight = selected ? 2 : 1;
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight = s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 3;
        s.ContentMarginLeft = s.ContentMarginRight = 4;
        s.ContentMarginTop  = s.ContentMarginBottom = 4;
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
                    style.BgColor     = color * 0.9f;
                    style.BorderColor = new Color(1f, 1f, 1f);
                    style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 3;
                    style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 3;
                    style.ContentMarginLeft = style.ContentMarginRight = 4;
                    style.ContentMarginTop  = style.ContentMarginBottom = 4;
                    btn.AddThemeStyleboxOverride("normal",  style);
                    btn.AddThemeStyleboxOverride("hover",   style);
                    btn.AddThemeStyleboxOverride("pressed", style);
                    btn.AddThemeStyleboxOverride("focus",   style);
                }
                else
                {
                    var style = new StyleBoxFlat();
                    style.BgColor     = color * 0.5f;
                    style.BorderColor = color;
                    style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 2;
                    style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 3;
                    style.ContentMarginLeft = style.ContentMarginRight = 4;
                    style.ContentMarginTop  = style.ContentMarginBottom = 4;
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
        btn.Text        = label;
        btn.TooltipText = tooltip;
        btn.CustomMinimumSize = new Vector2(64, 44);
        btn.AddThemeFontSizeOverride("font_size", 14);

        var baseStyle = new StyleBoxFlat();
        baseStyle.BgColor     = bgColor * 0.6f;
        baseStyle.BorderColor = bgColor;
        baseStyle.BorderWidthBottom = baseStyle.BorderWidthTop = baseStyle.BorderWidthLeft = baseStyle.BorderWidthRight = 2;
        baseStyle.CornerRadiusTopLeft = baseStyle.CornerRadiusTopRight = baseStyle.CornerRadiusBottomLeft = baseStyle.CornerRadiusBottomRight = 3;
        baseStyle.ContentMarginLeft = baseStyle.ContentMarginRight = 4;
        baseStyle.ContentMarginTop  = baseStyle.ContentMarginBottom = 4;

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
            style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = existing.CornerRadiusTopLeft;
        }
        else
        {
            style.BgColor     = new Color(0.3f, 0.3f, 0.3f);
            style.BorderColor = new Color(1f, 1f, 1f);
        }

        if (selected)
        {
            style.BgColor     = style.BgColor * 1.4f;
            style.BorderColor = new Color(1f, 1f, 1f);
            style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 3;
        }
        else
        {
            style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 2;
        }

        style.ContentMarginLeft = style.ContentMarginRight = 4;
        style.ContentMarginTop  = style.ContentMarginBottom = 4;
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
        // Check zone buttons
        foreach (var (label, z, color, _, _, _tab) in ZoneButtons)
        {
            if (z == zone)
            {
                var s = new StyleBoxFlat();
                s.BgColor     = color * 0.6f;
                s.BorderColor = color;
                s.BorderWidthBottom = s.BorderWidthTop = s.BorderWidthLeft = s.BorderWidthRight = 2;
                s.CornerRadiusTopLeft = s.CornerRadiusTopRight = s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 3;
                s.ContentMarginLeft = s.ContentMarginRight = 4;
                s.ContentMarginTop  = s.ContentMarginBottom = 4;
                return s;
            }
        }
        // Erase (not in ZoneButtons)
        if (zone == "Erase")
        {
            var s = new StyleBoxFlat();
            s.BgColor     = new Color(0.6f, 0.15f, 0.15f) * 0.6f;
            s.BorderColor = new Color(0.6f, 0.15f, 0.15f);
            s.BorderWidthBottom = s.BorderWidthTop = s.BorderWidthLeft = s.BorderWidthRight = 2;
            s.CornerRadiusTopLeft = s.CornerRadiusTopRight = s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 3;
            s.ContentMarginLeft = s.ContentMarginRight = 4;
            s.ContentMarginTop  = s.ContentMarginBottom = 4;
            return s;
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
