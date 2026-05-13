using Godot;
using System;

namespace LoopolisGodot;

/// <summary>
/// Full-width top bar showing key city stats: balance, population/milestone,
/// power, zone counts, happiness, and tick counter.
/// A hamburger menu (☰) on the left provides New Game / Main Menu actions.
/// Layer = 12 (above all other UI layers).
/// </summary>
public partial class TopBar : CanvasLayer
{
    // ── Signals ────────────────────────────────────────────────────────────
    [Signal] public delegate void NewGameRequestedEventHandler();
    [Signal] public delegate void MainMenuRequestedEventHandler();

    // ── Stat labels ────────────────────────────────────────────────────────
    private Label _balanceLabel   = null!;
    private Label _popLabel       = null!;
    private Label _powerLabel     = null!;
    private Label _zonesLabel     = null!;
    private Label _happinessLabel = null!;
    private Label _tickLabel      = null!;

    // ── Hamburger dropdown ─────────────────────────────────────────────────
    private PanelContainer _dropdownPanel = null!;

    public override void _Ready()
    {
        Layer = 12;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        panel.GrowVertical = Control.GrowDirection.End;
        panel.CustomMinimumSize = new Vector2(0, 48);
        panel.MouseFilter = Control.MouseFilterEnum.Stop;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.05f, 0.05f, 0.05f, 0.88f);
        panelStyle.ContentMarginLeft   = 8;
        panelStyle.ContentMarginRight  = 8;
        panelStyle.ContentMarginTop    = 4;
        panelStyle.ContentMarginBottom = 4;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 0);
        hbox.SizeFlagsVertical = Control.SizeFlags.Fill;
        panel.AddChild(hbox);

        // ── Hamburger button (left-most) ───────────────────────────────────
        var hamburgerBtn = new Button();
        hamburgerBtn.FocusMode = Control.FocusModeEnum.None;
        hamburgerBtn.Text = "☰";
        hamburgerBtn.CustomMinimumSize = new Vector2(44, 44);
        hamburgerBtn.AddThemeFontSizeOverride("font_size", 18);
        hamburgerBtn.TooltipText = "Menu";
        var hbNormal = new StyleBoxFlat();
        hbNormal.BgColor = new Color(0f, 0f, 0f, 0f);
        hamburgerBtn.AddThemeStyleboxOverride("normal",  hbNormal);
        hamburgerBtn.AddThemeStyleboxOverride("focus",   hbNormal);
        var hbHover = new StyleBoxFlat();
        hbHover.BgColor = new Color(1f, 1f, 1f, 0.08f);
        hamburgerBtn.AddThemeStyleboxOverride("hover",   hbHover);
        hamburgerBtn.AddThemeStyleboxOverride("pressed", hbHover);
        hamburgerBtn.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        hamburgerBtn.Pressed += ToggleDropdown;
        hbox.AddChild(hamburgerBtn);

        AddSep(hbox);

        _balanceLabel   = MakeLabel("💰 $0  +$0/tk");
        _popLabel       = MakeLabel("👥 0  →Town  [░░░░░░░░]  0%");
        _powerLabel     = MakeLabel("⚡ —");
        _zonesLabel     = MakeLabel("R:0  C:0  I:0");
        _happinessLabel = MakeLabel("😊 100%");
        _tickLabel      = MakeLabel("T:0");

        // Balance — left aligned
        _balanceLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(_balanceLabel);

        AddSep(hbox);

        // Population + milestone bar
        _popLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(_popLabel);

        AddSep(hbox);

        // Power
        _powerLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(_powerLabel);

        AddSep(hbox);

        // Zone counts
        _zonesLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(_zonesLabel);

        AddSep(hbox);

        // Happiness
        _happinessLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(_happinessLabel);

        AddSep(hbox);

        // Tick — right-most, muted
        _tickLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _tickLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
        hbox.AddChild(_tickLabel);

        // ── Dropdown panel (below hamburger, hidden by default) ────────────
        _dropdownPanel = new PanelContainer();
        _dropdownPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _dropdownPanel.Position = new Vector2(0, 48);
        _dropdownPanel.CustomMinimumSize = new Vector2(180, 0);
        _dropdownPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _dropdownPanel.Visible = false;
        var dropStyle = new StyleBoxFlat();
        dropStyle.BgColor = new Color(0.08f, 0.08f, 0.08f, 0.94f);
        dropStyle.BorderColor = new Color(0.3f, 0.3f, 0.35f);
        dropStyle.BorderWidthBottom = dropStyle.BorderWidthTop =
            dropStyle.BorderWidthLeft = dropStyle.BorderWidthRight = 1;
        dropStyle.CornerRadiusBottomLeft = dropStyle.CornerRadiusBottomRight =
            dropStyle.CornerRadiusTopLeft = dropStyle.CornerRadiusTopRight = 4;
        dropStyle.ContentMarginLeft   = 4;
        dropStyle.ContentMarginRight  = 4;
        dropStyle.ContentMarginTop    = 4;
        dropStyle.ContentMarginBottom = 4;
        _dropdownPanel.AddThemeStyleboxOverride("panel", dropStyle);
        AddChild(_dropdownPanel);

        var dropVbox = new VBoxContainer();
        dropVbox.AddThemeConstantOverride("separation", 2);
        _dropdownPanel.AddChild(dropVbox);

        dropVbox.AddChild(MakeDropdownButton("▶  Continue",  () => _dropdownPanel.Visible = false));
        dropVbox.AddChild(MakeDropdownButton("🆕  New Game",  () =>
        {
            _dropdownPanel.Visible = false;
            EmitSignal(SignalName.NewGameRequested);
        }));
        dropVbox.AddChild(MakeDropdownButton("🚪  Main Menu", () =>
        {
            _dropdownPanel.Visible = false;
            EmitSignal(SignalName.MainMenuRequested);
        }));
    }

    private void ToggleDropdown()
    {
        _dropdownPanel.Visible = !_dropdownPanel.Visible;
    }

    private static Button MakeDropdownButton(string text, Action onPressed)
    {
        var btn = new Button();
        btn.FocusMode = Control.FocusModeEnum.None;
        btn.Text = text;
        btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        btn.CustomMinimumSize = new Vector2(0, 36);
        btn.AddThemeFontSizeOverride("font_size", 13);
        btn.Alignment = HorizontalAlignment.Left;

        var normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = new Color(0f, 0f, 0f, 0f);
        normalStyle.ContentMarginLeft   = 12;
        normalStyle.ContentMarginRight  = 8;
        normalStyle.ContentMarginTop    = 4;
        normalStyle.ContentMarginBottom = 4;
        btn.AddThemeStyleboxOverride("normal", normalStyle);
        btn.AddThemeStyleboxOverride("focus",  normalStyle);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(1f, 1f, 1f, 0.10f);
        hoverStyle.ContentMarginLeft   = 12;
        hoverStyle.ContentMarginRight  = 8;
        hoverStyle.ContentMarginTop    = 4;
        hoverStyle.ContentMarginBottom = 4;
        btn.AddThemeStyleboxOverride("hover",   hoverStyle);
        btn.AddThemeStyleboxOverride("pressed", hoverStyle);

        btn.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        btn.Pressed += () => onPressed();
        return btn;
    }

    /// <summary>Update all labels from the latest SharedState.</summary>
    public void UpdateStats(SharedState state)
    {
        // Balance + net income
        var netSign = state.NetPerTick >= 0 ? "+" : "";
        _balanceLabel.Text = $"💰 ${state.Balance:N0}  {netSign}${state.NetPerTick:F1}/tk";
        _balanceLabel.AddThemeColorOverride("font_color",
            state.NetPerTick >= 0 ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.35f, 0.35f));

        // Population + milestone mini bar
        if (!string.IsNullOrEmpty(state.NextMilestoneName) && state.NextMilestoneTarget > 0)
        {
            var pct  = Math.Clamp((int)((double)state.Population / state.NextMilestoneTarget * 100), 0, 100);
            var bar  = MakeBar8(pct);
            _popLabel.Text = $"👥 {state.Population:N0}  →{state.NextMilestoneName}  [{bar}]  {pct}%";
            _popLabel.AddThemeColorOverride("font_color", GetMilestoneColor(state.NextMilestoneName));
        }
        else
        {
            _popLabel.Text = $"👥 {state.Population:N0}";
            _popLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        }

        // Power
        if (state.Power == null)
        {
            _powerLabel.Text = "⚡ —";
            _powerLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        }
        else if (state.Power.IsBrownout)
        {
            _powerLabel.Text = "⚡ BROWNOUT";
            _powerLabel.AddThemeColorOverride("font_color", new Color(1f, 0.5f, 0.1f));
        }
        else
        {
            var surplus = state.Power.SupplyMW - state.Power.DemandMW;
            _powerLabel.Text = surplus >= 0
                ? $"⚡ +{surplus}MW"
                : $"⚡ {state.Power.DemandMW}/{state.Power.SupplyMW}MW";
            _powerLabel.AddThemeColorOverride("font_color",
                surplus >= 0 ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.5f, 0.1f));
        }

        // Zone counts
        _zonesLabel.Text = $"R:{state.ResZones}  C:{state.ComZones}  I:{state.IndZones}";
        _zonesLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));

        // Happiness
        var happyPct = (int)(state.Happiness * 100);
        _happinessLabel.Text = $"😊 {happyPct}%";
        _happinessLabel.AddThemeColorOverride("font_color",
            happyPct >= 70 ? new Color(0.3f, 1f, 0.3f)  :
            happyPct >= 40 ? new Color(1f, 0.75f, 0.1f)  :
                             new Color(1f, 0.3f, 0.3f));

        // Tick
        _tickLabel.Text = $"T:{state.Tick}";
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Label MakeLabel(string text)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        lbl.AddThemeFontSizeOverride("font_size", 14);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        return lbl;
    }

    private static void AddSep(HBoxContainer parent)
    {
        var sep = new VSeparator();
        sep.AddThemeColorOverride("color", new Color(0.25f, 0.25f, 0.25f));
        parent.AddChild(sep);
    }

    private static string MakeBar8(int percent)
    {
        var filled = Math.Clamp(percent, 0, 100) * 8 / 100;
        return new string('█', filled) + new string('░', 8 - filled);
    }

    private static Color GetMilestoneColor(string name) => name switch
    {
        var n when n.Contains("Town")       => new Color(0.85f, 0.60f, 0.15f),
        var n when n.Contains("City")       => new Color(0.78f, 0.82f, 0.88f),
        var n when n.Contains("Metropolis") => new Color(1.0f,  0.85f, 0.10f),
        var n when n.Contains("Loopolis")   => new Color(0.20f, 1.0f,  0.90f),
        _                                   => new Color(0.70f, 0.85f, 1.0f),
    };
}
