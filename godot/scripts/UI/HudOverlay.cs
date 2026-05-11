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
    private Label _selectedLabel  = null!;
    private Label _pausedLabel    = null!;
    private Label _milestoneLabel = null!;
    private double _milestoneTimer = 0;
    private string? _lastShownMilestone;
    private const double MilestoneDuration = 3.0; // seconds

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

        if (state.CommercialIncomePerTick > 0)
            _taxCostLabel.Text = $"Tax: ${state.TaxPerTick:F1} | Shops: ${state.CommercialIncomePerTick:F1} | Costs: ${state.MaintenancePerTick:F1}/tick";
        else
            _taxCostLabel.Text = $"Tax: ${state.TaxPerTick:F1}/tick | Costs: ${state.MaintenancePerTick:F1}/tick";

        var happyPct = (int)(state.Happiness * 100);
        _happyLabel.Text = $"Happiness: {happyPct}%";

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
