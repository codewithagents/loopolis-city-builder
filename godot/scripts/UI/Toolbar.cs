using Godot;
using System.Collections.Generic;

namespace LoopolisGodot;

/// <summary>
/// Right-side vertical sidebar toolbar (160px wide, full height).
/// Tabs: Zones / Services / Utilities / Overlays.
/// Always-visible strip at bottom: Tax, Speed, Pause, Stats, New Game, Menu.
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
    [Signal] public delegate void StatsToggledEventHandler();
    [Signal] public delegate void OverlayChangedEventHandler(int mode);

    // ── State ──────────────────────────────────────────────────────────────────

    private string _selectedZone  = "";
    private string _taxLevel      = "normal";
    private float  _selectedSpeed = 2.0f;
    private int    _activeTab     = 0;   // 0=Zones 1=Services 2=Utilities 3=Overlays
    private int    _lastKnownPop  = 0;
    private int    _activeOverlayMode = 0;

    private readonly Dictionary<string, Button> _buttons      = new();
    private readonly Dictionary<string, Button> _taxButtons   = new();
    private readonly Dictionary<float,  Button> _speedButtons = new();
    private Button? _pauseButton;
    private Label?  _buildModeLabel;

    // 4 tab containers (one per tab)
    private readonly Container[] _tabContainers = new Container[4];
    private readonly Button[]    _tabHeaders    = new Button[4];

    // Overlay-mode buttons: keyed by OverlayMode int value
    private readonly Dictionary<int, Button> _overlayButtons = new();

    // ── Zone / tool definitions ────────────────────────────────────────────────

    // (Label, Zone, Color, Tooltip, MilestoneMin, Tab)
    // Tab 0 = Zones, Tab 1 = Services, Tab 2 = Utilities
    private static readonly (string Label, string Zone, Color Color, string Tooltip, int MilestoneMin, int Tab)[] ZoneButtons =
    {
        // Tab 0 — Zones
        ("Res",     "Residential", new Color(0.2f,  0.7f,  0.2f),  "Residential — Place: $50 · Maint: $0.50/tick",          0, 0),
        ("Com",     "Commercial",  new Color(0.2f,  0.4f,  0.9f),  "Commercial — Place: $100 · Maint: $0.50/tick",          0, 0),
        ("Ind",     "Industrial",  new Color(0.9f,  0.8f,  0.1f),  "Industrial — Place: $75 · Maint: $0.25/tick",           0, 0),
        ("Road",    "Road",        new Color(0.5f,  0.5f,  0.5f),  "Road — Place: $25 · Maint: $1.00/tick",                 0, 0),
        ("Avenue",  "Avenue",      new Color(0.62f, 0.62f, 0.62f), "Avenue — Place: $60 · Maint: $2.00/tick\nHigher capacity road — Requires Town (500 pop)", 500, 0),
        ("Erase",   "Erase",       new Color(0.6f,  0.15f, 0.15f), "Erase — no cost",                                       0, 0),

        // Tab 1 — Services
        ("Fire St", "FireStation",   new Color(1.0f,  0.4f,  0.1f),  "Fire Station — Place: $300 · Maint: $3.00/tick\nCoverage radius: 4 tiles",                  0,    1),
        ("Fire HQ", "FireHQ",        new Color(0.718f,0.110f,0.110f),"Fire HQ — Place: $2,000 · Maint: $25.00/tick\nCoverage radius: 10 tiles — Requires City (5,000 pop)", 5000, 1),
        ("Police",  "PoliceStation", new Color(0.2f,  0.4f,  1.0f),  "Police Station — Place: $300 · Maint: $3.00/tick\nCoverage radius: 4 tiles",                 0,    1),
        ("Pol HQ",  "PoliceHQ",      new Color(0.102f,0.137f,0.494f),"Police HQ — Place: $2,000 · Maint: $25.00/tick\nCoverage radius: 10 tiles — Requires City (5,000 pop)", 5000, 1),
        ("School",  "School",        new Color(0.7f,  0.3f,  0.9f),  "School — Place: $400 · Maint: $5.00/tick\nCoverage radius: 5 tiles",                        0,    1),
        ("Hospital","Hospital",      new Color(0.647f,0.839f,0.647f),"Hospital — Place: $3,000 · Maint: $35.00/tick\nCoverage radius: 8 tiles — Requires City (5,000 pop)", 5000, 1),

        // Tab 2 — Utilities
        ("Coal",    "CoalPlant",    new Color(0.259f,0.259f,0.259f),"Coal Plant — Place: $500 · Maint: $8.00/tick\n500 MW output — emits pollution",           0, 2),
        ("Nuclear", "NuclearPlant", new Color(0.976f,0.659f,0.145f),"Nuclear Plant — Place: $8,000 · Maint: $50.00/tick\n3,000 MW · clean — Requires Town (500 pop)", 500, 2),
        ("Pwr Line","PowerLine",    new Color(0.2f,  0.8f,  0.9f),  "Power Line — Place: $10 · Maint: $0.10/tick",          0, 2),
        ("Trans.",  "Transformer",  new Color(0.8f,  0.7f,  0.2f),  "Transformer — Place: $200 · Maint: $1.00/tick",        0, 2),
    };

    private static readonly (string Label, string Level, Color Color)[] TaxButtonDefs =
    {
        ("Low",  "low",    new Color(0.2f, 0.8f, 0.3f)),
        ("Norm", "normal", new Color(0.4f, 0.4f, 0.7f)),
        ("High", "high",   new Color(0.85f, 0.3f, 0.2f)),
    };

    private static readonly string[] TabLabels = { "Zn", "Sv", "Ut", "Ov" };
    private static readonly string[] TabTooltips = { "Zones", "Services", "Utilities", "Overlays" };

    // ── _Ready ─────────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Layer = 9;

        // Right-side panel: 160px wide, full height
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        panel.CustomMinimumSize = new Vector2(160, 0);
        panel.MouseFilter = Control.MouseFilterEnum.Stop;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
        panelStyle.ContentMarginLeft   = 4;
        panelStyle.ContentMarginRight  = 4;
        panelStyle.ContentMarginTop    = 52; // leave room for TopBar (48px + 4px gap)
        panelStyle.ContentMarginBottom = 4;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        var outerVBox = new VBoxContainer();
        outerVBox.AddThemeConstantOverride("separation", 4);
        panel.AddChild(outerVBox);

        // BUILD MODE label — shown when a tool is active
        _buildModeLabel = new Label();
        _buildModeLabel.Text = "⏸ BUILD MODE\n[Esc to resume]";
        _buildModeLabel.Visible = false;
        _buildModeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _buildModeLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.1f));
        _buildModeLabel.AddThemeFontSizeOverride("font_size", 11);
        outerVBox.AddChild(_buildModeLabel);

        // ── 4 tab buttons in 2×2 grid ────────────────────────────────────────

        var tabGrid = new GridContainer();
        tabGrid.Columns = 4;
        tabGrid.AddThemeConstantOverride("h_separation", 2);
        tabGrid.AddThemeConstantOverride("v_separation", 2);
        outerVBox.AddChild(tabGrid);

        for (var i = 0; i < 4; i++)
        {
            var capturedTab = i;
            var tabBtn = new Button();
            tabBtn.FocusMode = Control.FocusModeEnum.None;
            tabBtn.Text = TabLabels[i];
            tabBtn.TooltipText = TabTooltips[i];
            tabBtn.CustomMinimumSize = new Vector2(34, 28);
            tabBtn.AddThemeFontSizeOverride("font_size", 11);
            tabBtn.Pressed += () => SwitchTab(capturedTab);
            tabGrid.AddChild(tabBtn);
            _tabHeaders[i] = tabBtn;
        }

        // ── Tab content panels (one per tab) ────────────────────────────────

        for (var t = 0; t < 4; t++)
        {
            var grid = new GridContainer();
            grid.Columns = 2;
            grid.AddThemeConstantOverride("h_separation", 3);
            grid.AddThemeConstantOverride("v_separation", 3);
            grid.Visible = (t == _activeTab);
            outerVBox.AddChild(grid);
            _tabContainers[t] = grid;
        }

        // Populate zone/service/utility tabs
        foreach (var (label, zone, color, tooltip, milestoneMin, tab) in ZoneButtons)
        {
            var btn = MakeSidebarButton(label, color, tooltip);
            var capturedZone = zone;
            btn.Pressed += () =>
            {
                if (btn.Disabled) return;
                SelectZone(capturedZone, btn);
            };
            _tabContainers[tab].AddChild(btn);
            _buttons[zone] = btn;
        }

        // Populate overlays tab (tab 3)
        var overlayDefs = new (string Label, int Mode, Color Color)[]
        {
            ("None",      0, new Color(0.5f,  0.5f,  0.5f)),
            ("Happy",     1, new Color(0.3f,  1f,    0.3f)),
            ("Traffic",   2, new Color(1f,    0.4f,  0.2f)),
            ("Coverage",  3, new Color(0.3f,  0.6f,  1f)),
            ("Land Val",  4, new Color(1f,    0.85f, 0.2f)),
            ("Pollution", 5, new Color(0.7f,  0.3f,  0.3f)),
        };
        foreach (var (olabel, omode, ocolor) in overlayDefs)
        {
            var btn = MakeSidebarButton(olabel, ocolor, "");
            var capturedMode = omode;
            btn.Pressed += () => OnOverlayClicked(capturedMode);
            _tabContainers[3].AddChild(btn);
            _overlayButtons[omode] = btn;
        }

        // ── Separator ────────────────────────────────────────────────────────

        outerVBox.AddChild(MakeHSep());

        // ── Tax row ──────────────────────────────────────────────────────────

        var taxLabel = MakeSmallLabel("Tax:");
        outerVBox.AddChild(taxLabel);

        var taxRow = new HBoxContainer();
        taxRow.AddThemeConstantOverride("separation", 2);
        outerVBox.AddChild(taxRow);

        foreach (var (tlabel, level, color) in TaxButtonDefs)
        {
            var taxBtn = MakeSidebarButton(tlabel, color, "");
            taxBtn.CustomMinimumSize = new Vector2(44, 30);
            var capturedLevel = level;
            taxBtn.Pressed += () => SelectTaxLevel(capturedLevel);
            taxRow.AddChild(taxBtn);
            _taxButtons[level] = taxBtn;
        }

        HighlightTaxButton(_taxLevel);

        outerVBox.AddChild(MakeHSep());

        // ── Speed row ────────────────────────────────────────────────────────

        var speedLabel = MakeSmallLabel("Speed:");
        outerVBox.AddChild(speedLabel);

        var speedRow = new HBoxContainer();
        speedRow.AddThemeConstantOverride("separation", 2);
        outerVBox.AddChild(speedRow);

        var speedOptions = new (string Label, float Tps)[]
        {
            ("½×", 0.5f), ("1×", 1.0f), ("2×", 2.0f), ("4×", 4.0f),
        };
        foreach (var (slabel, tps) in speedOptions)
        {
            var btn = new Button();
            btn.FocusMode = Control.FocusModeEnum.None;
            btn.Text = slabel;
            btn.CustomMinimumSize = new Vector2(32, 28);
            btn.AddThemeFontSizeOverride("font_size", 11);
            var style = MakeSpeedButtonStyle(false);
            btn.AddThemeStyleboxOverride("normal", style);
            btn.AddThemeStyleboxOverride("focus",  style);
            var capturedTps = tps;
            btn.Pressed += () => SelectSpeed(capturedTps);
            speedRow.AddChild(btn);
            _speedButtons[tps] = btn;
        }

        HighlightSpeedButton(_selectedSpeed);

        outerVBox.AddChild(MakeHSep());

        // ── Bottom action buttons ─────────────────────────────────────────────

        _pauseButton = MakeActionButton("⏸ Pause");
        _pauseButton.Pressed += () => EmitSignal(SignalName.PauseToggled);
        outerVBox.AddChild(_pauseButton);

        var statsBtn = MakeActionButton("📊 Stats");
        statsBtn.Pressed += () => EmitSignal(SignalName.StatsToggled);
        outerVBox.AddChild(statsBtn);

        var newGameBtn = MakeActionButton("New Game");
        newGameBtn.Pressed += () => EmitSignal(SignalName.NewGameRequested);
        outerVBox.AddChild(newGameBtn);

        var menuBtn = MakeActionButton("Menu");
        menuBtn.Pressed += () => EmitSignal(SignalName.MainMenuRequested);
        outerVBox.AddChild(menuBtn);

        // Apply initial milestone locks and tab highlights
        UpdateMilestoneLocks(0);
        HighlightTabHeaders();
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    public string SelectedZone => _selectedZone;

    /// <summary>Deselects all zone/tool buttons and emits ZoneSelected("").</summary>
    public void DeselectAll()
    {
        _selectedZone = "";
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

    /// <summary>Shows or hides the BUILD MODE indicator.</summary>
    public void SetBuildMode(bool active)
    {
        if (_buildModeLabel != null)
            _buildModeLabel.Visible = active;
    }

    public void SetPaused(bool paused)
    {
        if (_pauseButton != null)
            _pauseButton.Text = paused ? "▶ Resume" : "⏸ Pause";
    }

    public void SetSpeed(float tps) => SelectSpeed(tps);

    public void SetTaxRate(string level)
    {
        _taxLevel = level;
        HighlightTaxButton(level);
    }

    /// <summary>Programmatically select a zone by name (e.g. from keyboard shortcuts).</summary>
    public void SelectZone(string zone)
    {
        if (!_buttons.TryGetValue(zone, out var btn)) return;
        SelectZone(zone, btn);
    }

    /// <summary>Switch to a specific tab index (0=Zones, 1=Services, 2=Utilities, 3=Overlays).</summary>
    public void SwitchToTab(int tabIndex) => SwitchTab(tabIndex);

    /// <summary>Switch to the tab that contains the given zone.</summary>
    public void SwitchToTabForZone(string zone)
    {
        foreach (var (_, z, _, _, _, tab) in ZoneButtons)
        {
            if (z == zone) { SwitchTab(tab); return; }
        }
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
        for (var i = 0; i < 4; i++)
            _tabContainers[i].Visible = (i == _activeTab);
        HighlightTabHeaders();
    }

    private void HighlightTabHeaders()
    {
        for (var i = 0; i < 4; i++)
        {
            var btn = _tabHeaders[i];
            if (i == _activeTab)
            {
                var style = new StyleBoxFlat();
                style.BgColor = new Color(0.25f, 0.35f, 0.55f);
                style.BorderColor = new Color(0.6f, 0.8f, 1f);
                style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 2;
                style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 3;
                style.ContentMarginLeft = style.ContentMarginRight = 4;
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
                style.ContentMarginLeft = style.ContentMarginRight = 4;
                style.ContentMarginTop  = style.ContentMarginBottom = 3;
                btn.AddThemeStyleboxOverride("normal",  style);
                btn.AddThemeStyleboxOverride("focus",   style);
                var hoverStyle = new StyleBoxFlat();
                hoverStyle.BgColor = new Color(0.18f, 0.22f, 0.32f);
                hoverStyle.BorderColor = new Color(0.5f, 0.6f, 0.8f);
                hoverStyle.BorderWidthBottom = hoverStyle.BorderWidthTop = hoverStyle.BorderWidthLeft = hoverStyle.BorderWidthRight = 1;
                hoverStyle.CornerRadiusTopLeft = hoverStyle.CornerRadiusTopRight = hoverStyle.CornerRadiusBottomLeft = hoverStyle.CornerRadiusBottomRight = 3;
                hoverStyle.ContentMarginLeft = hoverStyle.ContentMarginRight = 4;
                hoverStyle.ContentMarginTop  = hoverStyle.ContentMarginBottom = 3;
                btn.AddThemeStyleboxOverride("hover",   hoverStyle);
                btn.AddThemeStyleboxOverride("pressed", style);
                btn.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            }
        }
    }

    // ── Overlay selection ───────────────────────────────────────────────────────

    private void OnOverlayClicked(int mode)
    {
        // Toggle: clicking active overlay deactivates it
        _activeOverlayMode = (_activeOverlayMode == mode) ? 0 : mode;
        HighlightOverlayButtons();
        EmitSignal(SignalName.OverlayChanged, _activeOverlayMode);
    }

    private void HighlightOverlayButtons()
    {
        foreach (var (mode, btn) in _overlayButtons)
        {
            var selected = (mode == _activeOverlayMode);
            // Rebuild styling using the button's current border color as a hint
            // We don't need per-button color access here — just dim/bright
            if (selected)
            {
                var style = new StyleBoxFlat();
                style.BgColor     = new Color(0.25f, 0.35f, 0.25f);
                style.BorderColor = new Color(0.5f,  1f,    0.5f);
                style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 2;
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
                // Restore default dark style
                var style = new StyleBoxFlat();
                style.BgColor     = new Color(0.12f, 0.12f, 0.15f);
                style.BorderColor = new Color(0.35f, 0.35f, 0.45f);
                style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 1;
                style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 3;
                style.ContentMarginLeft = style.ContentMarginRight = 4;
                style.ContentMarginTop  = style.ContentMarginBottom = 4;
                btn.AddThemeStyleboxOverride("normal",  style);
                btn.AddThemeStyleboxOverride("hover",   MakeHoverStyle(style));
                btn.AddThemeStyleboxOverride("pressed", style);
                btn.AddThemeStyleboxOverride("focus",   style);
            }
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private void SelectZone(string zone, Button btn)
    {
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
                    style.ContentMarginTop  = style.ContentMarginBottom = 3;
                    btn.AddThemeStyleboxOverride("normal",  style);
                    btn.AddThemeStyleboxOverride("hover",   style);
                    btn.AddThemeStyleboxOverride("pressed", style);
                    btn.AddThemeStyleboxOverride("focus",   style);
                }
                else
                {
                    var style = new StyleBoxFlat();
                    style.BgColor     = color * 0.4f;
                    style.BorderColor = color;
                    style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 1;
                    style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 3;
                    style.ContentMarginLeft = style.ContentMarginRight = 4;
                    style.ContentMarginTop  = style.ContentMarginBottom = 3;
                    btn.AddThemeStyleboxOverride("normal",  style);
                    btn.AddThemeStyleboxOverride("hover",   MakeHoverStyle(style));
                    btn.AddThemeStyleboxOverride("pressed", style);
                    btn.AddThemeStyleboxOverride("focus",   style);
                }
                break;
            }
        }
    }

    // ── Style factories ─────────────────────────────────────────────────────────

    private static Button MakeSidebarButton(string label, Color bgColor, string tooltip)
    {
        var btn = new Button();
        btn.FocusMode = Control.FocusModeEnum.None;
        btn.Text = label;
        btn.TooltipText = tooltip;
        btn.CustomMinimumSize = new Vector2(70, 36);
        btn.AddThemeFontSizeOverride("font_size", 12);

        var baseStyle = new StyleBoxFlat();
        baseStyle.BgColor     = bgColor * 0.5f;
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

    private static Button MakeActionButton(string label)
    {
        var btn = new Button();
        btn.FocusMode = Control.FocusModeEnum.None;
        btn.Text = label;
        btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        btn.CustomMinimumSize = new Vector2(0, 32);
        btn.AddThemeFontSizeOverride("font_size", 13);
        return btn;
    }

    private static Label MakeSmallLabel(string text)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        lbl.AddThemeFontSizeOverride("font_size", 11);
        return lbl;
    }

    private static Control MakeHSep()
    {
        var sep = new HSeparator();
        sep.AddThemeColorOverride("color", new Color(0.25f, 0.25f, 0.25f));
        return sep;
    }

    private static StyleBoxFlat MakeSpeedButtonStyle(bool selected)
    {
        var s = new StyleBoxFlat();
        s.BgColor      = selected ? new Color(0.3f, 0.3f, 0.5f) : new Color(0.12f, 0.12f, 0.18f);
        s.BorderColor  = selected ? new Color(0.8f, 0.8f, 1f)   : new Color(0.3f,  0.3f,  0.4f);
        s.BorderWidthBottom = s.BorderWidthTop = s.BorderWidthLeft = s.BorderWidthRight = selected ? 2 : 1;
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight = s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 3;
        s.ContentMarginLeft = s.ContentMarginRight = 4;
        s.ContentMarginTop  = s.ContentMarginBottom = 3;
        return s;
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
        style.BgColor     = baseStyle.BgColor * 1.3f;
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
        foreach (var (label, z, color, _, _, _tab) in ZoneButtons)
        {
            if (z == zone)
            {
                var s = new StyleBoxFlat();
                s.BgColor     = color * 0.5f;
                s.BorderColor = color;
                s.BorderWidthBottom = s.BorderWidthTop = s.BorderWidthLeft = s.BorderWidthRight = 2;
                s.CornerRadiusTopLeft = s.CornerRadiusTopRight = s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 3;
                s.ContentMarginLeft = s.ContentMarginRight = 4;
                s.ContentMarginTop  = s.ContentMarginBottom = 4;
                return s;
            }
        }
        var fallback = new StyleBoxFlat();
        fallback.BgColor = new Color(0.2f, 0.2f, 0.2f);
        return fallback;
    }
}
