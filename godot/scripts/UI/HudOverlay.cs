using System;
using System.Linq;
using Godot;

namespace LoopolisGodot;

/// <summary>
/// Detail Stats panel — shown/hidden via Toggle(). Defaults to hidden.
/// Position: bottom-left floating panel, grows upward.
/// The error banner is a separate element always rendered regardless of stats visibility.
/// Layer = 10.
/// </summary>
public partial class HudOverlay : CanvasLayer
{
    // ── Detail stats panel ─────────────────────────────────────────────────
    private PanelContainer _statsPanel = null!;

    private Label _balanceLabel   = null!;
    private Label _jobsLabel      = null!;
    private Label _schoolLabel    = null!;
    private Label _policeLabel    = null!;
    private Label _fireLabel      = null!;
    private Label _hospitalLabel  = null!;
    private Label _commuteLabel   = null!;
    private Label _eventLabel     = null!;
    private Label _selectedLabel  = null!;

    private double _balanceWarningTimer = 0;
    private const double BalanceWarningDuration = 0.8;

    // ── Milestone banner (floating, centered, timed) ───────────────────────
    private Label _milestoneLabel = null!;
    private double _milestoneTimer = 0;
    private string? _lastShownMilestone;
    private const double MilestoneDuration = 3.0;

    // ── Error banner — always visible, independent of stats panel ─────────
    private PanelContainer _errorBannerPanel = null!;
    private Label _errorBannerLabel = null!;

    // ── Overlay legend panel ───────────────────────────────────────────────
    private PanelContainer _overlayLegendPanel = null!;
    private Label _overlayLegendTitle = null!;
    private Label[] _overlayLegendRows = Array.Empty<Label>();
    private ColorRect[] _overlayLegendSwatches = Array.Empty<ColorRect>();

    public override void _Ready()
    {
        Layer = 10;

        // ── Milestone banner (top-center, always rendered) ─────────────────
        _milestoneLabel = new Label();
        _milestoneLabel.Text = "";
        _milestoneLabel.Visible = false;
        _milestoneLabel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _milestoneLabel.Position = new Vector2(0, 56); // below TopBar
        _milestoneLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _milestoneLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.2f));
        _milestoneLabel.AddThemeFontSizeOverride("font_size", 22);
        AddChild(_milestoneLabel);

        // ── Error banner — top-center, ALWAYS rendered ─────────────────────
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

        // ── Overlay legend — bottom-right ──────────────────────────────────
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
        legendStyle.CornerRadiusTopLeft = legendStyle.CornerRadiusTopRight =
            legendStyle.CornerRadiusBottomLeft = legendStyle.CornerRadiusBottomRight = 4;
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

        _overlayLegendSwatches = new ColorRect[3];
        _overlayLegendRows     = new Label[4];
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

        // ── Detail stats panel — bottom-left, HIDDEN by default ────────────
        _statsPanel = new PanelContainer();
        _statsPanel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _statsPanel.GrowVertical = Control.GrowDirection.Begin;
        _statsPanel.Position = new Vector2(8, -8);
        _statsPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _statsPanel.AddThemeStyleboxOverride("panel", MakePanelStyle());
        _statsPanel.Visible = false; // hidden by default
        AddChild(_statsPanel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);
        _statsPanel.AddChild(vbox);

        // Header row: title + close button
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(headerRow);

        var titleLabel = new Label();
        titleLabel.Text = "📊 DETAIL STATS";
        titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 1f));
        titleLabel.AddThemeFontSizeOverride("font_size", 14);
        headerRow.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.FocusMode = Control.FocusModeEnum.None;
        closeBtn.Text = "×";
        closeBtn.CustomMinimumSize = new Vector2(22, 22);
        closeBtn.AddThemeFontSizeOverride("font_size", 14);
        var closeBtnStyle = new StyleBoxFlat();
        closeBtnStyle.BgColor = new Color(0f, 0f, 0f, 0f);
        closeBtn.AddThemeStyleboxOverride("normal",  closeBtnStyle);
        closeBtn.AddThemeStyleboxOverride("hover",   closeBtnStyle);
        closeBtn.AddThemeStyleboxOverride("pressed", closeBtnStyle);
        closeBtn.AddThemeStyleboxOverride("focus",   closeBtnStyle);
        closeBtn.Pressed += Toggle;
        headerRow.AddChild(closeBtn);

        vbox.AddChild(MakeHSep());

        _balanceLabel  = MakeLabel("Budget: Tax $0/tk | Costs $0/tk");
        _jobsLabel     = MakeLabel("Jobs: —");
        _schoolLabel   = MakeServiceLabel("🏫 School: — (none built)");
        _policeLabel   = MakeServiceLabel("🚔 Police: — (none built)");
        _fireLabel     = MakeServiceLabel("🔥 Fire: — (none built)");
        _hospitalLabel = MakeServiceLabel("🏥 Hospital: — (none built)");
        _commuteLabel  = MakeServiceLabel("🚗 Commute: no workers yet");
        _eventLabel    = MakeLabel("");
        _selectedLabel = MakeLabel("[No tool selected]");

        vbox.AddChild(_balanceLabel);
        vbox.AddChild(MakeHSep());
        vbox.AddChild(_jobsLabel);
        vbox.AddChild(MakeHSep());
        vbox.AddChild(_schoolLabel);
        vbox.AddChild(_policeLabel);
        vbox.AddChild(_fireLabel);
        vbox.AddChild(_hospitalLabel);
        vbox.AddChild(MakeHSep());
        vbox.AddChild(_commuteLabel);
        vbox.AddChild(MakeHSep());
        vbox.AddChild(_eventLabel);
        vbox.AddChild(_selectedLabel);

        _eventLabel.AddThemeColorOverride("font_color", new Color(1f, 0.5f, 0.2f));
        _eventLabel.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_milestoneTimer > 0)
        {
            _milestoneTimer -= delta;
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

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Toggle the stats panel visibility.</summary>
    public void Toggle()
    {
        _statsPanel.Visible = !_statsPanel.Visible;
    }

    /// <summary>Flash the balance label red briefly to signal insufficient funds.</summary>
    public void FlashBalanceWarning()
    {
        _balanceWarningTimer = BalanceWarningDuration;
    }

    /// <summary>Shows a full-width red error banner at the top. Always visible regardless of stats panel state.</summary>
    public void ShowErrorBanner(string message)
    {
        _errorBannerLabel.Text = $"SIMULATION ERROR: {message}    [Press F12 to dismiss]";
        _errorBannerPanel.Visible = true;
    }

    /// <summary>Hides the error banner.</summary>
    public void DismissErrorBanner()
    {
        _errorBannerPanel.Visible = false;
    }

    /// <summary>Shows or hides the overlay legend in the bottom-right corner.</summary>
    public void ShowOverlayLegend(OverlayMode mode)
    {
        if (mode == OverlayMode.None)
        {
            _overlayLegendPanel.Visible = false;
            return;
        }

        string title;
        (Color swatchColor, string rowText)[] rows;

        switch (mode)
        {
            case OverlayMode.Happiness:
                title = "HAPPINESS";
                rows = new[]
                {
                    (new Color(0.267f, 1f, 0.267f), "High  (>70%)"),
                    (new Color(1f, 1f, 0f),          "Medium (40–70%)"),
                    (new Color(1f, 0f, 0f),           "Low   (<40%)"),
                };
                break;
            case OverlayMode.Traffic:
                title = "TRAFFIC";
                rows = new[]
                {
                    (new Color(1f, 0f, 0f),          "Heavy (60+)"),
                    (new Color(1f, 1f, 0f),           "Moderate (10–60)"),
                    (new Color(0.5f, 0.5f, 0.5f),     "Light (<10)"),
                };
                break;
            case OverlayMode.Coverage:
                title = "COVERAGE";
                rows = new[]
                {
                    (new Color(0f, 0.6f, 1f),         "Fully covered"),
                    (new Color(1f, 0.55f, 0f),         "Road, no power"),
                    (new Color(0.15f, 0.15f, 0.15f),   "No road access"),
                };
                break;
            case OverlayMode.LandValue:
                title = "LAND VALUE";
                rows = new[]
                {
                    (new Color(1f, 0.843f, 0f),        "High value"),
                    (new Color(0.6f, 0.5f, 0.1f),      "Medium value"),
                    (new Color(0.239f, 0.125f, 0f),     "Low value"),
                };
                break;
            case OverlayMode.Pollution:
                title = "POLLUTION";
                rows = new[]
                {
                    (new Color(0.545f, 0f, 0f),         "Heavy"),
                    (new Color(1f, 0.5f, 0f),            "Moderate"),
                    (new Color(0.5f, 0.5f, 0.5f),        "None"),
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

    /// <summary>Update the detail stats panel content from the latest SharedState.</summary>
    public void UpdateStats(SharedState state)
    {
        // Budget row
        var taxModStr = state.TaxModifier > 0.001  ? " [↓Tax]" :
                        state.TaxModifier < -0.001 ? " [↑Tax]" : "";
        _balanceLabel.Text = $"Budget: Tax ${state.TaxPerTick:F1}/tk | Costs ${state.MaintenancePerTick:F1}/tk{taxModStr}";

        // Jobs row
        if (state.RequiredJobs > 0 && state.EmploymentRatio < 1.0)
        {
            var gapPct = (int)((1.0 - state.EmploymentRatio) * 100);
            _jobsLabel.Text = $"Jobs: {state.AvailableJobs}/{state.RequiredJobs} ⚠ {gapPct}% gap";
            _jobsLabel.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.1f));
        }
        else
        {
            _jobsLabel.Text = $"Jobs: {state.AvailableJobs} available";
            _jobsLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
        }

        // Service rows
        if (state.CoverageSummary != null)
        {
            var cov = state.CoverageSummary;

            UpdateServiceLabel(_schoolLabel,   "🏫 School",   cov.SchoolSeatsUsed,     cov.SchoolSeatsTotal,     "seats");
            UpdateServiceLabel(_policeLabel,   "🚔 Police",   cov.PoliceCapacityUsed,  cov.PoliceCapacityTotal,  "units");
            UpdateServiceLabel(_fireLabel,     "🔥 Fire",     cov.FireCapacityUsed,    cov.FireCapacityTotal,    "bldgs");
            UpdateServiceLabel(_hospitalLabel, "🏥 Hospital", cov.HospitalBedsUsed,    cov.HospitalBedsTotal,    "beds");
        }
        else
        {
            _schoolLabel.Text   = "🏫 School: — (none built)";
            _policeLabel.Text   = "🚔 Police: — (none built)";
            _fireLabel.Text     = "🔥 Fire: — (none built)";
            _hospitalLabel.Text = "🏥 Hospital: — (none built)";
            foreach (var l in new[] { _schoolLabel, _policeLabel, _fireLabel, _hospitalLabel })
                l.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        }

        // Commute row
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
                var jamStr = wf.OverloadedEdges > 0 ? $" | {wf.OverloadedEdges} jammed" : "";
                var unrStr = wf.UnroutedWorkers > 0 ? $" | {wf.UnroutedWorkers} unrouted" : "";
                _commuteLabel.Text = $"🚗 Commute: avg {wf.AverageCommuteDistance:F1} tiles{unrStr}{jamStr}";
                _commuteLabel.AddThemeColorOverride("font_color",
                    wf.OverloadedEdges > 0 ? new Color(1f, 0.5f, 0.15f) :
                    wf.UnroutedWorkers > 10 ? new Color(1f, 0.8f, 0.2f) :
                                              new Color(0.75f, 0.9f, 0.75f));
            }
        }
        else
        {
            _commuteLabel.Text = "🚗 Commute: no workers yet";
            _commuteLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        }

        // Active event row
        if (!string.IsNullOrEmpty(state.ActiveEventName))
        {
            var penaltyPct = (int)(Mathf.Abs((float)state.EventHappinessPenalty) * 100);
            _eventLabel.Text = $"! Active Event: {state.ActiveEventName} — -{penaltyPct}% happiness";
            _eventLabel.Visible = true;
        }
        else
        {
            _eventLabel.Visible = false;
        }

        // Milestone banner (floating)
        if (!string.IsNullOrEmpty(state.MilestoneReached) && state.MilestoneReached != _lastShownMilestone)
        {
            _lastShownMilestone = state.MilestoneReached;
            ShowMilestone(state.MilestoneReached);
        }
    }

    /// <summary>Called by World.cs when a tool is selected/deselected.</summary>
    public void SetSelectedZone(string zoneName)
    {
        if (string.IsNullOrEmpty(zoneName) || zoneName == "Empty")
            _selectedLabel.Text = "[No tool selected]";
        else
            _selectedLabel.Text = $"[Selected: {zoneName}]";
    }

    /// <summary>Kept for compatibility — sets the paused display text in the stats panel.</summary>
    public void SetBuildModePaused(bool buildMode)
    {
        // No-op: build mode is now shown in Toolbar's sidebar indicator.
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void UpdateServiceLabel(Label lbl, string prefix, int used, int total, string unit)
    {
        if (total == 0)
        {
            lbl.Text = $"{prefix}: — (none built)";
            lbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        }
        else
        {
            var ratio = (double)used / total;
            lbl.Text = $"{prefix}: {used}/{total} {unit}";
            lbl.AddThemeColorOverride("font_color",
                ratio >= 0.9 ? new Color(1f, 0.3f, 0.3f) :
                ratio >= 0.7 ? new Color(1f, 0.85f, 0.15f) :
                               new Color(0.3f, 1f, 0.3f));
        }
    }

    private void ShowMilestone(string milestone)
    {
        var (emoji, color, size, duration) = milestone switch
        {
            "Town"       => ("🥉", new Color(0.85f, 0.60f, 0.15f), 28, 3.0),
            "City"       => ("🥈", new Color(0.80f, 0.85f, 0.90f), 32, 3.5),
            "Metropolis" => ("🥇", new Color(1.0f,  0.85f, 0.10f), 38, 4.0),
            "Loopolis"   => ("🏆", new Color(0.20f, 1.0f,  0.90f), 44, 5.0),
            _            => ("★",  new Color(1f,    0.9f,  0.2f),  26, 3.0),
        };

        _milestoneLabel.Text = $"  {emoji}  {milestone.ToUpperInvariant()} REACHED!  {emoji}  ";
        _milestoneLabel.AddThemeFontSizeOverride("font_size", size);
        _milestoneLabel.AddThemeColorOverride("font_color", color);
        _milestoneLabel.Modulate = new Color(1f, 1f, 1f, 1f);
        _milestoneLabel.Visible = true;
        _milestoneTimer = duration;
    }

    private static Label MakeLabel(string text)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        lbl.AddThemeFontSizeOverride("font_size", 13);
        return lbl;
    }

    private static Label MakeServiceLabel(string text)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        lbl.AddThemeFontSizeOverride("font_size", 13);
        return lbl;
    }

    private static Control MakeHSep()
    {
        var sep = new HSeparator();
        sep.AddThemeColorOverride("color", new Color(0.25f, 0.25f, 0.25f, 0.7f));
        return sep;
    }

    private static StyleBoxFlat MakePanelStyle()
    {
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.05f, 0.05f, 0.05f, 0.88f);
        style.ContentMarginLeft   = 10;
        style.ContentMarginRight  = 10;
        style.ContentMarginTop    = 6;
        style.ContentMarginBottom = 6;
        style.CornerRadiusTopLeft     = 4;
        style.CornerRadiusTopRight    = 4;
        style.CornerRadiusBottomLeft  = 4;
        style.CornerRadiusBottomRight = 4;
        return style;
    }
}
