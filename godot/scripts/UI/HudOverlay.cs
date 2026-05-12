using System;
using System.Linq;
using Godot;

namespace LoopolisGodot;


/// <summary>
/// Stats HUD shown in the top-left corner as a semi-transparent dark panel.
/// Displays tick, population, balance, net income, happiness, selected tool, pause state, and milestone banners.
/// </summary>
public partial class HudOverlay : CanvasLayer
{
    private Label _tickLabel          = null!;
    private Label _popLabel           = null!;
    private Label _nextMilestoneLabel = null!;
    private Label _capacityNudge      = null!;
    private Label _balanceLabel   = null!;
    private Label _netLabel       = null!;
    private Label _taxCostLabel   = null!;
    private Label _happyLabel     = null!;
    private Label _jobsLabel      = null!;
    private Label _commerceLabel  = null!;
    private Label _industryLabel  = null!;
    private Label _schoolLabel    = null!;
    private Label _policeLabel    = null!;
    private Label _fireLabel      = null!;
    private Label _hospitalLabel  = null!;
    private Label _commuteLabel   = null!;
    private Label _powerLabel     = null!;
    private Label _eventLabel     = null!;
    private Label _selectedLabel  = null!;
    private Label _pausedLabel    = null!;
    private Label _milestoneLabel = null!;
    private double _milestoneTimer = 0;
    private double _balanceWarningTimer = 0;
    private string? _lastShownMilestone;
    private const double MilestoneDuration = 3.0; // seconds
    private const double BalanceWarningDuration = 0.8; // seconds

    // Tutorial banner — shown until a power plant is placed
    private Label _tutorialBanner = null!;
    private bool _tutorialDismissed = false;

    // Error banner — shown when the simulation throws an unhandled exception
    private PanelContainer _errorBannerPanel = null!;
    private Label _errorBannerLabel = null!;

    // Overlay legend panel — shown in bottom-right when an overlay is active
    private PanelContainer _overlayLegendPanel = null!;
    private Label _overlayLegendTitle = null!;
    private Label[] _overlayLegendRows = System.Array.Empty<Label>();
    private ColorRect[] _overlayLegendSwatches = System.Array.Empty<ColorRect>();

    public override void _Ready()
    {
        Layer = 10;

        // Root panel: dark semi-transparent background in top-left
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        panel.Position = new Vector2(8, 8);
        panel.AddThemeStyleboxOverride("panel", MakePanelStyle());
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);
        panel.AddChild(vbox);

        _tickLabel     = MakeLabel("Tick: 0");
        _popLabel      = MakeLabel("Pop: 0");

        _nextMilestoneLabel = new Label();
        _nextMilestoneLabel.Text = "";
        _nextMilestoneLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f));
        _nextMilestoneLabel.AddThemeFontSizeOverride("font_size", 14);

        _capacityNudge = new Label();
        _capacityNudge.Text = "⚡ At capacity — build more zones!";
        _capacityNudge.Visible = false;
        _capacityNudge.AddThemeColorOverride("font_color", new Color(1f, 0.75f, 0.1f));
        _capacityNudge.AddThemeFontSizeOverride("font_size", 13);

        _balanceLabel  = MakeLabel("Balance: $0");
        _netLabel      = MakeLabel("Net: $0/tick");
        _taxCostLabel  = MakeLabel("Tax: $0/tick | Costs: $0/tick");
        _happyLabel    = MakeLabel("Happiness: 0%");

        _jobsLabel = MakeLabel("Jobs: ✓ 0 available");

        _commerceLabel = MakeLabel("Commerce: 0 zones | $0/tick | Demand: ░░░░░░░░░░ 0%");
        _industryLabel = MakeLabel("Industry: 0 zones | 0 jobs | Utilization: ░░░░░░░░░░ 0%");

        _schoolLabel   = MakeServiceLabel();
        _policeLabel   = MakeServiceLabel();
        _fireLabel     = MakeServiceLabel();
        _hospitalLabel = MakeServiceLabel();
        _commuteLabel  = MakeServiceLabel();

        _powerLabel = new Label();
        _powerLabel.Text = "";
        _powerLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        _powerLabel.AddThemeFontSizeOverride("font_size", 16);

        _eventLabel = new Label();
        _eventLabel.Text = "";
        _eventLabel.Visible = false;
        _eventLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        _eventLabel.AddThemeFontSizeOverride("font_size", 14);

        _selectedLabel = MakeLabel("[Selected: Road]");
        _pausedLabel   = MakeLabel("[Paused]");

        vbox.AddChild(_tickLabel);
        vbox.AddChild(_popLabel);
        vbox.AddChild(_nextMilestoneLabel);
        vbox.AddChild(_capacityNudge);
        vbox.AddChild(_balanceLabel);
        vbox.AddChild(_netLabel);
        vbox.AddChild(_taxCostLabel);
        vbox.AddChild(_happyLabel);
        vbox.AddChild(_jobsLabel);
        vbox.AddChild(_commerceLabel);
        vbox.AddChild(_industryLabel);
        vbox.AddChild(_schoolLabel);
        vbox.AddChild(_policeLabel);
        vbox.AddChild(_fireLabel);
        vbox.AddChild(_hospitalLabel);
        vbox.AddChild(_commuteLabel);
        vbox.AddChild(_powerLabel);
        vbox.AddChild(_eventLabel);
        vbox.AddChild(_selectedLabel);
        vbox.AddChild(_pausedLabel);

        _pausedLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.1f));
        _pausedLabel.Visible = false;

        // Milestone banner — centered near the top
        _milestoneLabel = new Label();
        _milestoneLabel.Text = "";
        _milestoneLabel.Visible = false;
        _milestoneLabel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _milestoneLabel.Position = new Vector2(0, 8);
        _milestoneLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _milestoneLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.2f));
        _milestoneLabel.AddThemeFontSizeOverride("font_size", 22);
        AddChild(_milestoneLabel);

        // Tutorial banner — top-center, semi-transparent dark background
        var tutorialPanel = new PanelContainer();
        tutorialPanel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        tutorialPanel.Position = new Vector2(0, 40);
        tutorialPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        var tutorialStyle = new StyleBoxFlat();
        tutorialStyle.BgColor = new Color(0f, 0f, 0f, 0.60f);
        tutorialStyle.ContentMarginLeft   = 12;
        tutorialStyle.ContentMarginRight  = 12;
        tutorialStyle.ContentMarginTop    = 5;
        tutorialStyle.ContentMarginBottom = 5;
        tutorialStyle.CornerRadiusTopLeft     = 4;
        tutorialStyle.CornerRadiusTopRight    = 4;
        tutorialStyle.CornerRadiusBottomLeft  = 4;
        tutorialStyle.CornerRadiusBottomRight = 4;
        tutorialPanel.AddThemeStyleboxOverride("panel", tutorialStyle);

        _tutorialBanner = new Label();
        _tutorialBanner.Text = "💡 Zone homes along the road to attract residents. Build a power plant to unlock larger buildings.";
        _tutorialBanner.HorizontalAlignment = HorizontalAlignment.Center;
        _tutorialBanner.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 1f));
        _tutorialBanner.AddThemeFontSizeOverride("font_size", 16);
        _tutorialBanner.MouseFilter = Control.MouseFilterEnum.Ignore;
        tutorialPanel.AddChild(_tutorialBanner);
        tutorialPanel.Visible = false;
        AddChild(tutorialPanel);

        // Keep a reference to the panel so we can show/hide it via the label's parent
        _tutorialBanner.SetMeta("panel", tutorialPanel);

        // Error banner — full-width red panel at the very top, hidden by default
        _errorBannerPanel = new PanelContainer();
        _errorBannerPanel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _errorBannerPanel.Position = new Vector2(0, 0);
        _errorBannerPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        var errorStyle = new StyleBoxFlat();
        errorStyle.BgColor = new Color(0.65f, 0.04f, 0.04f, 0.92f);
        errorStyle.ContentMarginLeft   = 16;
        errorStyle.ContentMarginRight  = 16;
        errorStyle.ContentMarginTop    = 6;
        errorStyle.ContentMarginBottom = 6;
        _errorBannerPanel.AddThemeStyleboxOverride("panel", errorStyle);

        _errorBannerLabel = new Label();
        _errorBannerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _errorBannerLabel.AddThemeColorOverride("font_color", Colors.White);
        _errorBannerLabel.AddThemeFontSizeOverride("font_size", 16);
        _errorBannerLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _errorBannerPanel.AddChild(_errorBannerLabel);
        _errorBannerPanel.Visible = false;
        AddChild(_errorBannerPanel);

        // ── Overlay legend panel — bottom-right, hidden until an overlay is active ──
        _overlayLegendPanel = new PanelContainer();
        _overlayLegendPanel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _overlayLegendPanel.GrowHorizontal = Control.GrowDirection.Begin;
        _overlayLegendPanel.GrowVertical   = Control.GrowDirection.Begin;
        _overlayLegendPanel.Position       = new Vector2(-152, -100);
        _overlayLegendPanel.MouseFilter    = Control.MouseFilterEnum.Ignore;
        var legendStyle = new StyleBoxFlat();
        legendStyle.BgColor            = new Color(0f, 0f, 0f, 0.72f);
        legendStyle.ContentMarginLeft   = 8;
        legendStyle.ContentMarginRight  = 8;
        legendStyle.ContentMarginTop    = 6;
        legendStyle.ContentMarginBottom = 6;
        legendStyle.CornerRadiusTopLeft     = 4;
        legendStyle.CornerRadiusTopRight    = 4;
        legendStyle.CornerRadiusBottomLeft  = 4;
        legendStyle.CornerRadiusBottomRight = 4;
        _overlayLegendPanel.AddThemeStyleboxOverride("panel", legendStyle);

        var legendVbox = new VBoxContainer();
        legendVbox.AddThemeConstantOverride("separation", 3);
        legendVbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        _overlayLegendPanel.AddChild(legendVbox);

        _overlayLegendTitle = new Label();
        _overlayLegendTitle.Text = "OVERLAY";
        _overlayLegendTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _overlayLegendTitle.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.9f));
        _overlayLegendTitle.AddThemeFontSizeOverride("font_size", 11);
        _overlayLegendTitle.MouseFilter = Control.MouseFilterEnum.Ignore;
        legendVbox.AddChild(_overlayLegendTitle);

        // Three swatch+label rows (high / medium / low or covered / partial / none etc.)
        _overlayLegendSwatches = new ColorRect[3];
        _overlayLegendRows     = new Label[4]; // 3 legend rows + 1 dismiss hint
        for (int i = 0; i < 3; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            row.MouseFilter = Control.MouseFilterEnum.Ignore;
            legendVbox.AddChild(row);

            var swatch = new ColorRect();
            swatch.CustomMinimumSize = new Vector2(12, 12);
            swatch.MouseFilter = Control.MouseFilterEnum.Ignore;
            row.AddChild(swatch);
            _overlayLegendSwatches[i] = swatch;

            var rowLabel = new Label();
            rowLabel.Text = "";
            rowLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
            rowLabel.AddThemeFontSizeOverride("font_size", 11);
            rowLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            row.AddChild(rowLabel);
            _overlayLegendRows[i] = rowLabel;
        }

        // Dismiss hint label
        var dismissLabel = new Label();
        dismissLabel.Text = "[press again to hide]";
        dismissLabel.HorizontalAlignment = HorizontalAlignment.Center;
        dismissLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        dismissLabel.AddThemeFontSizeOverride("font_size", 10);
        dismissLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        legendVbox.AddChild(dismissLabel);
        _overlayLegendRows[3] = dismissLabel;

        _overlayLegendPanel.Visible = false;
        AddChild(_overlayLegendPanel);

        // ── Hotkey hints row — bottom of screen, always visible ─────────────
        var hintsPanel = new PanelContainer();
        hintsPanel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        hintsPanel.GrowVertical = Control.GrowDirection.Begin;
        hintsPanel.Position = new Vector2(0, -28);
        hintsPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        var hintsStyle = new StyleBoxFlat();
        hintsStyle.BgColor = new Color(0f, 0f, 0f, 0.45f);
        hintsStyle.ContentMarginLeft   = 8;
        hintsStyle.ContentMarginRight  = 8;
        hintsStyle.ContentMarginTop    = 2;
        hintsStyle.ContentMarginBottom = 2;
        hintsPanel.AddThemeStyleboxOverride("panel", hintsStyle);

        var hintsLabel = new Label();
        hintsLabel.Text = "[F1] Happiness  [F2] Traffic  [F3] Coverage  [F4] Land Value  [F5] Pollution";
        hintsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        hintsLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f, 0.85f));
        hintsLabel.AddThemeFontSizeOverride("font_size", 10);
        hintsLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        hintsPanel.AddChild(hintsLabel);
        AddChild(hintsPanel);
    }

    public override void _Process(double delta)
    {
        if (_milestoneTimer > 0)
        {
            _milestoneTimer -= delta;
            // Fade out in last 0.5 seconds
            var alpha = _milestoneTimer < 0.5 ? (float)(_milestoneTimer / 0.5) : 1f;
            _milestoneLabel.Modulate = new Color(1f, 1f, 1f, alpha);
            if (_milestoneTimer <= 0)
                _milestoneLabel.Visible = false;
        }

        if (_balanceWarningTimer > 0)
        {
            _balanceWarningTimer -= delta;
            _balanceLabel.AddThemeColorOverride("font_color", new Color(1f, 0.25f, 0.25f));
            if (_balanceWarningTimer <= 0)
                _balanceLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        }
    }

    /// <summary>Flash the balance label red briefly to signal insufficient funds.</summary>
    public void FlashBalanceWarning()
    {
        _balanceWarningTimer = BalanceWarningDuration;
    }

    /// <summary>
    /// Shows a full-width red error banner at the top of the screen.
    /// The simulation should be paused before calling this.
    /// Press F12 to dismiss.
    /// </summary>
    public void ShowErrorBanner(string message)
    {
        _errorBannerLabel.Text = $"SIMULATION ERROR: {message}    [Press F12 to dismiss]";
        _errorBannerPanel.Visible = true;
    }

    /// <summary>Hides the error banner (called when the player presses F12).</summary>
    public void DismissErrorBanner()
    {
        _errorBannerPanel.Visible = false;
    }

    /// <summary>
    /// Shows or hides the overlay legend panel in the bottom-right corner.
    /// Called by World.cs when the active overlay changes.
    /// </summary>
    public void ShowOverlayLegend(OverlayMode mode)
    {
        if (mode == OverlayMode.None)
        {
            _overlayLegendPanel.Visible = false;
            return;
        }

        // Configure title and three color swatch rows based on the active overlay
        string title;
        (Color swatchColor, string rowText)[] rows;

        switch (mode)
        {
            case OverlayMode.Happiness:
                title = "HAPPINESS [F1]";
                rows = new[]
                {
                    (new Color(0.267f, 1f, 0.267f), "High  (>70%)"),
                    (new Color(1f, 1f, 0f),          "Medium (40–70%)"),
                    (new Color(1f, 0f, 0f),           "Low   (<40%)"),
                };
                break;
            case OverlayMode.Traffic:
                title = "TRAFFIC [F2]";
                rows = new[]
                {
                    (new Color(1f, 0f, 0f),           "Heavy (60+)"),
                    (new Color(1f, 1f, 0f),            "Moderate (10–60)"),
                    (new Color(0.5f, 0.5f, 0.5f),      "Light (<10)"),
                };
                break;
            case OverlayMode.Coverage:
                title = "COVERAGE [F3]";
                rows = new[]
                {
                    (new Color(0f, 0.6f, 1f),          "Fully covered"),
                    (new Color(1f, 0.55f, 0f),          "Road, no power"),
                    (new Color(0.15f, 0.15f, 0.15f),    "No road access"),
                };
                break;
            case OverlayMode.LandValue:
                title = "LAND VALUE [F4]";
                rows = new[]
                {
                    (new Color(1f, 0.843f, 0f),         "High value"),
                    (new Color(0.6f, 0.5f, 0.1f),       "Medium value"),
                    (new Color(0.239f, 0.125f, 0f),      "Low value"),
                };
                break;
            case OverlayMode.Pollution:
                title = "POLLUTION [F5]";
                rows = new[]
                {
                    (new Color(0.545f, 0f, 0f),          "Heavy"),
                    (new Color(1f, 0.5f, 0f),             "Moderate"),
                    (new Color(0.5f, 0.5f, 0.5f),         "None"),
                };
                break;
            default:
                _overlayLegendPanel.Visible = false;
                return;
        }

        _overlayLegendTitle.Text = title;
        for (int i = 0; i < 3; i++)
        {
            _overlayLegendSwatches[i].Color = rows[i].swatchColor;
            _overlayLegendRows[i].Text = rows[i].rowText;
        }
        _overlayLegendPanel.Visible = true;
    }

    /// <summary>Called by SharedStateReader (viewer mode) and World (standalone mode) each tick.</summary>
    public void UpdateStats(SharedState state)
    {
        _tickLabel.Text    = $"Tick: {state.Tick:N0}";
        if (state.MaxCapacity > 0)
            _popLabel.Text = $"Pop: {state.Population:N0} / {state.MaxCapacity:N0}";
        else
            _popLabel.Text = $"Pop: {state.Population:N0}";

        if (!string.IsNullOrEmpty(state.NextMilestoneName) && state.NextMilestoneTarget > 0)
        {
            _nextMilestoneLabel.Text = $"Next: {state.NextMilestoneName} ({state.Population:N0} / {state.NextMilestoneTarget:N0})";
            _nextMilestoneLabel.Visible = true;
        }
        else
        {
            _nextMilestoneLabel.Visible = false;
        }

        var nearCapacity = state.MaxCapacity > 0 && state.Population >= state.MaxCapacity - 5;
        _capacityNudge.Visible = nearCapacity;

        _balanceLabel.Text = $"Balance: ${state.Balance:N0}";

        var netSign = state.NetPerTick >= 0 ? "+" : "";
        _netLabel.Text = $"Net: {netSign}${state.NetPerTick:F1}/tick";
        _netLabel.AddThemeColorOverride("font_color",
            state.NetPerTick >= 0 ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f));

        var taxModStr = state.TaxModifier > 0.001  ? " [↓Tax +happy]" :
                        state.TaxModifier < -0.001 ? " [↑Tax -happy]" : "";
        if (state.CommercialIncomePerTick > 0)
            _taxCostLabel.Text = $"Tax: ${state.TaxPerTick:F1} | Shops: ${state.CommercialIncomePerTick:F1} | Costs: ${state.MaintenancePerTick:F1}/tick{taxModStr}";
        else
            _taxCostLabel.Text = $"Tax: ${state.TaxPerTick:F1}/tick | Costs: ${state.MaintenancePerTick:F1}/tick{taxModStr}";

        _taxCostLabel.AddThemeColorOverride("font_color",
            state.TaxModifier > 0.001  ? new Color(0.3f, 1f, 0.3f) :
            state.TaxModifier < -0.001 ? new Color(1f, 0.3f, 0.3f) :
                                         new Color(0.9f, 0.9f, 0.9f));

        var happyPct = (int)(state.Happiness * 100);
        _happyLabel.Text = $"Happiness: {happyPct}%";

        if (state.EmploymentRatio < 1.0 && state.RequiredJobs > 0)
        {
            var unemploymentPct = (int)((1.0 - state.EmploymentRatio) * 100);
            _jobsLabel.Text = $"Jobs: {state.AvailableJobs}/{state.RequiredJobs} (⚠️ {unemploymentPct}% gap)";
            _jobsLabel.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.1f));
        }
        else
        {
            _jobsLabel.Text = $"Jobs: ✓ {state.AvailableJobs} available";
            _jobsLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
        }

        // Commerce row
        {
            var commerceTiles   = state.Tiles.Count(t => t.Zone == "Commercial");
            var commerceBoost   = state.Tiles.Count(t => t.Zone == "Commercial" && t.HasDemandBoost);
            var demandPct       = commerceTiles > 0 ? (int)((double)commerceBoost / commerceTiles * 100) : 0;
            var demandBar       = MakeBar(demandPct);
            var demandColor     = demandPct >= 70 ? new Color(0.3f, 1f, 0.3f)
                                : demandPct >= 40 ? new Color(1f, 0.85f, 0.1f)
                                                  : new Color(1f, 0.35f, 0.35f);
            if (commerceTiles > 0)
            {
                _commerceLabel.Text = $"Commerce: {commerceTiles} zones | ${state.CommercialIncomePerTick:F1}/tick | Demand: {demandBar} {demandPct}%";
                _commerceLabel.AddThemeColorOverride("font_color", demandColor);
                _commerceLabel.Visible = true;
            }
            else
            {
                _commerceLabel.Visible = false;
            }
        }

        // Industry row
        {
            var industrialTiles = state.Tiles.Count(t => t.Zone == "Industrial");
            // max theoretical jobs = tiles * 20 (50 pop-units * 0.4 jobs/unit)
            var maxJobs         = industrialTiles * 20;
            var jobs            = state.Employment?.Jobs ?? state.AvailableJobs;
            var utilPct         = maxJobs > 0 ? (int)((double)jobs / maxJobs * 100) : 0;
            utilPct             = Math.Min(utilPct, 100);
            var utilBar         = MakeBar(utilPct);
            var utilColor       = utilPct >= 60 ? new Color(0.3f, 1f, 0.3f)
                                : utilPct >= 30 ? new Color(1f, 0.85f, 0.1f)
                                               : new Color(1f, 0.35f, 0.35f);
            if (industrialTiles > 0)
            {
                _industryLabel.Text = $"Industry: {industrialTiles} zones | {jobs:N0} jobs | Utilization: {utilBar} {utilPct}%";
                _industryLabel.AddThemeColorOverride("font_color", utilColor);
                _industryLabel.Visible = true;
            }
            else
            {
                _industryLabel.Visible = false;
            }
        }

        // Service capacity rows (G4)
        if (state.CoverageSummary != null)
        {
            var cov = state.CoverageSummary;

            // School
            if (cov.SchoolSeatsTotal == 0)
            {
                _schoolLabel.Text = "🏫 School: — (none built)";
                _schoolLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            }
            else
            {
                var pct    = (int)(cov.SchoolCoveragePercent * 100);
                var bar    = MakeBar(pct);
                _schoolLabel.Text = $"🏫 School:   {cov.SchoolSeatsUsed}/{cov.SchoolSeatsTotal} seats  {bar}  {pct}%";
                _schoolLabel.AddThemeColorOverride("font_color", CapacityColor(cov.SchoolSeatsUsed, cov.SchoolSeatsTotal));
            }
            _schoolLabel.Visible = true;

            // Police
            if (cov.PoliceCapacityTotal == 0)
            {
                _policeLabel.Text = "👮 Police: — (none built)";
                _policeLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            }
            else
            {
                var pct    = (int)(cov.PoliceCoveragePercent * 100);
                var bar    = MakeBar(pct);
                _policeLabel.Text = $"👮 Police:   {cov.PoliceCapacityUsed}/{cov.PoliceCapacityTotal}        {bar}  {pct}%";
                _policeLabel.AddThemeColorOverride("font_color", CapacityColor(cov.PoliceCapacityUsed, cov.PoliceCapacityTotal));
            }
            _policeLabel.Visible = true;

            // Fire
            if (cov.FireCapacityTotal == 0)
            {
                _fireLabel.Text = "🚒 Fire: — (none built)";
                _fireLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            }
            else
            {
                var pct    = (int)(cov.FireCoveragePercent * 100);
                var bar    = MakeBar(pct);
                _fireLabel.Text = $"🚒 Fire:     {cov.FireCapacityUsed}/{cov.FireCapacityTotal}        {bar}  {pct}%";
                _fireLabel.AddThemeColorOverride("font_color", CapacityColor(cov.FireCapacityUsed, cov.FireCapacityTotal));
            }
            _fireLabel.Visible = true;

            // Hospital
            if (cov.HospitalBedsTotal == 0)
            {
                _hospitalLabel.Text = "🏥 Hospital: — (none built)";
                _hospitalLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            }
            else
            {
                var pct    = (int)(cov.HospitalCoveragePercent * 100);
                var bar    = MakeBar(pct);
                _hospitalLabel.Text = $"🏥 Hospital: {cov.HospitalBedsUsed}/{cov.HospitalBedsTotal} beds   {bar}  {pct}%";
                _hospitalLabel.AddThemeColorOverride("font_color", CapacityColor(cov.HospitalBedsUsed, cov.HospitalBedsTotal));
            }
            _hospitalLabel.Visible = true;
        }
        else
        {
            _schoolLabel.Visible   = false;
            _policeLabel.Visible   = false;
            _fireLabel.Visible     = false;
            _hospitalLabel.Visible = false;
        }

        // Commute row (G4)
        if (state.WorkerFlow != null)
        {
            var wf = state.WorkerFlow;
            if (wf.WorkersRouted == 0 && wf.UnroutedWorkers == 0)
            {
                _commuteLabel.Text = "🚗 Commute: no workers yet";
                _commuteLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            }
            else
            {
                var jamStr     = wf.OverloadedEdges > 0  ? $" | {wf.OverloadedEdges} jammed roads" : "";
                var unrStr     = wf.UnroutedWorkers > 0  ? $" | {wf.UnroutedWorkers} unrouted" : "";
                _commuteLabel.Text = $"🚗 Commute: avg {wf.AverageCommuteDistance:F1} tiles{unrStr}{jamStr}";
                var commuteColor = wf.OverloadedEdges > 0  ? new Color(1f, 0.5f, 0.15f)
                                 : wf.UnroutedWorkers > 10 ? new Color(1f, 0.8f, 0.2f)
                                                           : new Color(0.75f, 0.9f, 0.75f);
                _commuteLabel.AddThemeColorOverride("font_color", commuteColor);
            }
            _commuteLabel.Visible = true;
        }
        else
        {
            _commuteLabel.Text = "🚗 Commute: no workers yet";
            _commuteLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            _commuteLabel.Visible = true;
        }

        // Power row
        if (state.Power != null)
        {
            var pwr = state.Power;
            var pct = pwr.DemandMW > 0 ? (int)(pwr.CapacityRatio * 100) : 100;
            if (pwr.IsBrownout)
            {
                _powerLabel.Text = $"⚡ {pwr.SupplyMW:N0} / {pwr.DemandMW:N0} MW  ({pct}%) ⚠ BROWNOUT";
                _powerLabel.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.1f)); // amber
            }
            else if (pwr.CapacityRatio >= 1.5)
            {
                _powerLabel.Text = $"⚡ {pwr.SupplyMW:N0} / {pwr.DemandMW:N0} MW  ({pct}%)";
                _powerLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.4f)); // green surplus
            }
            else
            {
                _powerLabel.Text = $"⚡ {pwr.SupplyMW:N0} / {pwr.DemandMW:N0} MW  ({pct}%)";
                _powerLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            }
            _powerLabel.Visible = true;
        }
        else
        {
            _powerLabel.Visible = false;
        }

        if (!string.IsNullOrEmpty(state.ActiveEventName))
        {
            var penaltyPct = (int)(Mathf.Abs((float)state.EventHappinessPenalty) * 100);
            _eventLabel.Text = $"! {state.ActiveEventName} — happiness -{penaltyPct}%";
            _eventLabel.Visible = true;
        }
        else
        {
            _eventLabel.Visible = false;
        }

        _pausedLabel.Visible = state.Paused;

        if (!string.IsNullOrEmpty(state.MilestoneReached) && state.MilestoneReached != _lastShownMilestone)
        {
            _lastShownMilestone = state.MilestoneReached;
            ShowMilestone(state.MilestoneReached);
        }

        // Tutorial banner: show when tick > 5 and no power plant exists yet; hide once placed
        UpdateTutorialBanner(state);
    }

    /// <summary>
    /// Shows the tutorial banner when tick > 5 and no power plant is on the map.
    /// Permanently dismisses it once a power plant is detected.
    /// </summary>
    private void UpdateTutorialBanner(SharedState state)
    {
        if (_tutorialDismissed) return;

        var panel = (Control)_tutorialBanner.GetMeta("panel").As<GodotObject>();

        // Check if a power plant exists on the map
        var hasPowerPlant = false;
        foreach (var t in state.Tiles)
        {
            if (t.Zone == "CoalPlant" || t.Zone == "NuclearPlant" || t.Zone == "PowerPlant")
            {
                hasPowerPlant = true;
                break;
            }
        }

        if (hasPowerPlant)
        {
            _tutorialDismissed = true;
            panel.Visible = false;
            return;
        }

        // Only show after tick 5 so the very first few frames don't clutter the screen
        panel.Visible = state.Tick > 5;
    }

    /// <summary>Called by World.cs (standalone mode) to update the next milestone display directly.</summary>
    public void UpdateNextMilestone(string? name, int target, int currentPop)
    {
        if (!string.IsNullOrEmpty(name) && target > 0)
        {
            _nextMilestoneLabel.Text = $"Next: {name} ({currentPop:N0} / {target:N0})";
            _nextMilestoneLabel.Visible = true;
        }
        else
        {
            _nextMilestoneLabel.Visible = false;
        }
    }

    /// <summary>Called by Toolbar when the player changes the selected zone.</summary>
    public void SetSelectedZone(string zoneName)
    {
        if (string.IsNullOrEmpty(zoneName) || zoneName == "Empty")
            _selectedLabel.Text = "[No tool selected]";
        else
            _selectedLabel.Text = $"[Selected: {zoneName}]";
    }

    /// <summary>
    /// Updates the paused label text to reflect build-mode vs manual pause.
    /// Call from World.cs whenever build-mode state changes.
    /// </summary>
    public void SetBuildModePaused(bool buildMode)
    {
        _pausedLabel.Text = buildMode ? "⏸ PAUSED — Build mode (Esc or Resume to continue)" : "[Paused]";
        _pausedLabel.AddThemeColorOverride("font_color",
            buildMode ? new Color(1f, 0.85f, 0.1f) : new Color(0.9f, 0.9f, 0.9f));
    }

    private void ShowMilestone(string milestone)
    {
        _milestoneLabel.Text = $"  {milestone}  ";
        _milestoneLabel.Modulate = new Color(1f, 1f, 1f, 1f);
        _milestoneLabel.Visible = true;
        _milestoneTimer = MilestoneDuration;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Builds a 10-character unicode block bar for a 0–100% value.</summary>
    private static string MakeBar(int percent)
    {
        var filled = Math.Clamp(percent, 0, 100) / 10;
        return new string('█', filled) + new string('░', 10 - filled);
    }

    /// <summary>Creates a hidden, monospace-friendly service-capacity label.</summary>
    private static Label MakeServiceLabel()
    {
        var label = new Label();
        label.Text = "";
        label.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        label.AddThemeFontSizeOverride("font_size", 14);
        label.Visible = false;
        return label;
    }

    /// <summary>
    /// Returns green/yellow/red based on how full a service capacity is.
    /// ≥90% → red (near capacity), ≥70% → yellow, else green.
    /// </summary>
    private static Color CapacityColor(int used, int total)
    {
        if (total == 0) return new Color(0.6f, 0.6f, 0.6f);
        var ratio = (double)used / total;
        return ratio >= 0.9 ? new Color(1f, 0.3f, 0.3f)
             : ratio >= 0.7 ? new Color(1f, 0.85f, 0.15f)
                            : new Color(0.3f, 1f, 0.3f);
    }

    private static Label MakeLabel(string text)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        label.AddThemeFontSizeOverride("font_size", 16);
        return label;
    }

    private static StyleBoxFlat MakePanelStyle()
    {
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0f, 0f, 0f, 0.65f);
        style.ContentMarginLeft   = 8;
        style.ContentMarginRight  = 8;
        style.ContentMarginTop    = 6;
        style.ContentMarginBottom = 6;
        style.CornerRadiusTopLeft     = 4;
        style.CornerRadiusTopRight    = 4;
        style.CornerRadiusBottomLeft  = 4;
        style.CornerRadiusBottomRight = 4;
        return style;
    }
}
