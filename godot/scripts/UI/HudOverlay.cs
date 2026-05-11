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
        vbox.AddChild(_powerLabel);
        vbox.AddChild(_eventLabel);
        vbox.AddChild(_selectedLabel);
        vbox.AddChild(_pausedLabel);

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
        _selectedLabel.Text = $"[Selected: {zoneName}]";
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
