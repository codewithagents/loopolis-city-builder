using Godot;
using System;
using System.IO;
using Loopolis.Core.Policies;
using Loopolis.Core.Simulation;

namespace LoopolisGodot;

/// <summary>
/// Modal overlay panel for toggling city policies.
/// Press 'O' to show/hide. Layer = 14.
///
/// Shows 4 policy rows — each with name, description, cost, unlock state, and a toggle button.
/// In standalone mode, calls engine.PolicySystem directly.
/// In server (viewer) mode, writes {"cmd":"set_policy","policy":"GreenCity","active":true} to command.json.
/// </summary>
public partial class PolicyPanel : CanvasLayer
{
    private ColorRect _backdrop = null!;
    private bool _visible = false;

    // Policy state cache (refreshed each Update call)
    private bool _isStandalone;
    private SimulationEngine? _engine;
    private SharedState? _viewerState;
    private string? _sessionId;
    private int _currentPop;
    private string _sharedDir = "";

    // Per-policy UI row data
    private readonly PolicyRowWidgets[] _rows = new PolicyRowWidgets[4];

    // Subtitle showing live cost
    private Label _subtitleLabel = null!;

    // Amber/gold brand colour used throughout the panel
    private static readonly Color AmberColor   = new(1.00f, 0.78f, 0.20f);
    private static readonly Color GreyText     = new(0.60f, 0.60f, 0.65f);
    private static readonly Color WhiteText    = new(0.95f, 0.95f, 0.95f);
    private static readonly Color ActiveBg     = new(0.10f, 0.30f, 0.10f);
    private static readonly Color ActiveBorder = new(0.30f, 0.80f, 0.30f);
    private static readonly Color InactiveBg   = new(0.15f, 0.15f, 0.22f);
    private static readonly Color InactiveBorder = new(0.40f, 0.40f, 0.60f);
    private static readonly Color LockedBg     = new(0.10f, 0.10f, 0.12f);
    private static readonly Color LockedBorder = new(0.30f, 0.30f, 0.35f);

    // Struct to hold references into each policy row so we can update them later
    private struct PolicyRowWidgets
    {
        public PolicyType Type;
        public Button ToggleBtn;
        public Label  LockLabel;
    }

    public new bool IsVisible => _visible;

    public override void _Ready()
    {
        Layer = 14;

        // Resolve shared dir for server-mode commands
        var projectDir = ProjectSettings.GlobalizePath("res://");
        _sharedDir = Path.Combine(projectDir, "shared");

        // ── Full-screen backdrop (captures clicks, dims city behind) ──────────
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0f, 0f, 0f, 0.60f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        _backdrop.Visible = false;
        AddChild(_backdrop);

        // ── Centered card ─────────────────────────────────────────────────────
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        var card = new PanelContainer();
        card.CustomMinimumSize = new Vector2(440f, 370f);
        var cardStyle = new StyleBoxFlat();
        cardStyle.BgColor       = new Color(0.08f, 0.08f, 0.12f, 0.93f);
        cardStyle.BorderColor   = AmberColor;
        cardStyle.BorderWidthTop    = 2;
        cardStyle.BorderWidthBottom = 1;
        cardStyle.BorderWidthLeft   = 1;
        cardStyle.BorderWidthRight  = 1;
        cardStyle.CornerRadiusTopLeft = cardStyle.CornerRadiusTopRight =
            cardStyle.CornerRadiusBottomLeft = cardStyle.CornerRadiusBottomRight = 8;
        cardStyle.ContentMarginLeft   = 22;
        cardStyle.ContentMarginRight  = 22;
        cardStyle.ContentMarginTop    = 18;
        cardStyle.ContentMarginBottom = 16;
        card.AddThemeStyleboxOverride("panel", cardStyle);
        center.AddChild(card);

        // ── Card content ──────────────────────────────────────────────────────
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 0);
        card.AddChild(vbox);

        // Title
        var title = new Label();
        title.Text = "City Policies";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", AmberColor);
        title.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(title);

        // Subtitle (live cost — updated in Update())
        _subtitleLabel = new Label();
        _subtitleLabel.Text = "Active policies cost $0/tick";
        _subtitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _subtitleLabel.AddThemeColorOverride("font_color", GreyText);
        _subtitleLabel.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_subtitleLabel);

        vbox.AddChild(MakeSpacer(6));
        vbox.AddChild(MakeHRule());
        vbox.AddChild(MakeSpacer(8));

        // ── Policy rows ───────────────────────────────────────────────────────
        var allPolicies = PolicyCatalog.All;
        for (var i = 0; i < allPolicies.Length; i++)
        {
            var def = allPolicies[i];
            BuildPolicyRow(vbox, def, i);
            if (i < allPolicies.Length - 1)
            {
                vbox.AddChild(MakeSpacer(4));
                vbox.AddChild(MakeThinSep());
                vbox.AddChild(MakeSpacer(4));
            }
        }

        vbox.AddChild(MakeSpacer(10));
        vbox.AddChild(MakeHRule());
        vbox.AddChild(MakeSpacer(6));

        // ── Bottom hint ───────────────────────────────────────────────────────
        var hintRow = new HBoxContainer();
        hintRow.AddThemeConstantOverride("separation", 0);
        vbox.AddChild(hintRow);

        // Spacer pushes hint to right
        var hintSpacer = new Control();
        hintSpacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hintRow.AddChild(hintSpacer);

        var hintLabel = new Label();
        hintLabel.Text = "Press O to close";
        hintLabel.AddThemeColorOverride("font_color", GreyText);
        hintLabel.AddThemeFontSizeOverride("font_size", 10);
        hintRow.AddChild(hintLabel);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public new void Show()
    {
        _visible = true;
        _backdrop.Visible = true;
    }

    public new void Hide()
    {
        _visible = false;
        _backdrop.Visible = false;
    }

    /// <summary>
    /// Refreshes displayed policy state. Call every frame while visible, and once on Show().
    /// </summary>
    /// <param name="standalone">True when running own simulation (World has _engine).</param>
    /// <param name="engine">Simulation engine (standalone mode — may be null in server mode).</param>
    /// <param name="viewerState">Latest SharedState from server (server mode — may be null in standalone).</param>
    /// <param name="sessionId">Session ID used to write command files (server mode).</param>
    public void Update(bool standalone, SimulationEngine? engine, SharedState? viewerState, string? sessionId)
    {
        _isStandalone = standalone;
        _engine       = engine;
        _viewerState  = viewerState;
        _sessionId    = sessionId;
        _currentPop   = standalone
            ? (engine?.Population?.Population ?? 0)
            : (viewerState?.Population ?? 0);

        // Resolve policy active state
        var totalCost = 0;
        var allPolicies = PolicyCatalog.All;
        for (var i = 0; i < allPolicies.Length; i++)
        {
            var def = allPolicies[i];
            var active = GetPolicyActive(def.Type, standalone, engine, viewerState);
            var locked = IsPolicyLocked(def, _currentPop);
            RefreshRowWidgets(ref _rows[i], def, active, locked);
            if (active) totalCost += def.CostPerTick;
        }

        _subtitleLabel.Text = totalCost > 0
            ? $"Active policies cost ${totalCost}/tick"
            : "No active policies";
    }

    // ── Row builder ───────────────────────────────────────────────────────────

    private void BuildPolicyRow(VBoxContainer parent, PolicyDefinition def, int index)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        parent.AddChild(row);

        // ── Left: name + description ──────────────────────────────────────────
        var leftVbox = new VBoxContainer();
        leftVbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftVbox.AddThemeConstantOverride("separation", 2);
        row.AddChild(leftVbox);

        var nameLabel = new Label();
        nameLabel.Text = def.Name;
        nameLabel.AddThemeColorOverride("font_color", WhiteText);
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        leftVbox.AddChild(nameLabel);

        var descLabel = new Label();
        descLabel.Text = def.Description;
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        descLabel.AddThemeColorOverride("font_color", GreyText);
        descLabel.AddThemeFontSizeOverride("font_size", 11);
        descLabel.CustomMinimumSize = new Vector2(220f, 0f);
        leftVbox.AddChild(descLabel);

        // ── Right: cost + lock + toggle ───────────────────────────────────────
        var rightVbox = new VBoxContainer();
        rightVbox.AddThemeConstantOverride("separation", 4);
        rightVbox.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        row.AddChild(rightVbox);

        // Cost label
        var costLabel = new Label();
        costLabel.Text = $"${def.CostPerTick}/tick";
        costLabel.HorizontalAlignment = HorizontalAlignment.Right;
        costLabel.AddThemeColorOverride("font_color", AmberColor);
        costLabel.AddThemeFontSizeOverride("font_size", 12);
        rightVbox.AddChild(costLabel);

        // Lock indicator (hidden when unlocked)
        var lockLabel = new Label();
        lockLabel.HorizontalAlignment = HorizontalAlignment.Right;
        lockLabel.AddThemeColorOverride("font_color", GreyText);
        lockLabel.AddThemeFontSizeOverride("font_size", 10);
        lockLabel.Text = GetLockText(def);
        lockLabel.Visible = false; // hidden until we know milestone state in Update()
        rightVbox.AddChild(lockLabel);

        // Toggle button
        var toggleBtn = new Button();
        toggleBtn.FocusMode = Control.FocusModeEnum.None;
        toggleBtn.Text = "Enable";
        toggleBtn.CustomMinimumSize = new Vector2(88f, 32f);
        toggleBtn.AddThemeFontSizeOverride("font_size", 13);

        var capturedType = def.Type;
        toggleBtn.Pressed += () => OnTogglePressed(capturedType);
        rightVbox.AddChild(toggleBtn);

        _rows[index] = new PolicyRowWidgets
        {
            Type      = def.Type,
            ToggleBtn = toggleBtn,
            LockLabel = lockLabel,
        };
    }

    // ── Row refresh ───────────────────────────────────────────────────────────

    private static void RefreshRowWidgets(ref PolicyRowWidgets row, PolicyDefinition def,
        bool active, bool locked)
    {
        row.LockLabel.Visible = locked;
        row.LockLabel.Text    = GetLockText(def);

        row.ToggleBtn.Disabled = locked;

        if (locked)
        {
            row.ToggleBtn.Text = "Locked";
            ApplyButtonStyle(row.ToggleBtn, LockedBg, LockedBorder);
            row.ToggleBtn.AddThemeColorOverride("font_color", GreyText);
        }
        else if (active)
        {
            row.ToggleBtn.Text = "Active";
            ApplyButtonStyle(row.ToggleBtn, ActiveBg, ActiveBorder);
            row.ToggleBtn.AddThemeColorOverride("font_color", new Color(0.6f, 1f, 0.6f));
        }
        else
        {
            row.ToggleBtn.Text = "Enable";
            ApplyButtonStyle(row.ToggleBtn, InactiveBg, InactiveBorder);
            row.ToggleBtn.AddThemeColorOverride("font_color", WhiteText);
        }
    }

    private static void ApplyButtonStyle(Button btn, Color bg, Color border)
    {
        var style = new StyleBoxFlat();
        style.BgColor       = bg;
        style.BorderColor   = border;
        style.BorderWidthLeft = style.BorderWidthRight =
            style.BorderWidthTop = style.BorderWidthBottom = 2;
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight =
            style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 4;
        style.ContentMarginLeft = style.ContentMarginRight = 8;
        style.ContentMarginTop  = style.ContentMarginBottom = 4;
        btn.AddThemeStyleboxOverride("normal",  style);
        btn.AddThemeStyleboxOverride("focus",   style);

        var hover = new StyleBoxFlat();
        hover.BgColor       = bg + new Color(0.05f, 0.05f, 0.05f, 0f);
        hover.BorderColor   = border + new Color(0.15f, 0.15f, 0.15f, 0f);
        hover.BorderWidthLeft = hover.BorderWidthRight =
            hover.BorderWidthTop = hover.BorderWidthBottom = 2;
        hover.CornerRadiusTopLeft = hover.CornerRadiusTopRight =
            hover.CornerRadiusBottomLeft = hover.CornerRadiusBottomRight = 4;
        hover.ContentMarginLeft = hover.ContentMarginRight = 8;
        hover.ContentMarginTop  = hover.ContentMarginBottom = 4;
        btn.AddThemeStyleboxOverride("hover",   hover);
        btn.AddThemeStyleboxOverride("pressed", style);
    }

    // ── Toggle handler ────────────────────────────────────────────────────────

    private void OnTogglePressed(PolicyType type)
    {
        if (_isStandalone && _engine != null)
        {
            // Direct call in standalone mode
            if (_engine.PolicySystem.IsActive(type))
                _engine.PolicySystem.DeactivatePolicy(type);
            else
                _engine.PolicySystem.ActivatePolicy(type);
        }
        else if (!_isStandalone && _sessionId != null)
        {
            // Write command file for the Runner to pick up
            var currentlyActive = _viewerState != null && GetPolicyActive(type, false, null, _viewerState);
            var policyName = type.ToString();
            var isActive   = (!currentlyActive).ToString().ToLowerInvariant(); // toggle
            var json = $"{{\"cmd\":\"set_policy\",\"policy\":\"{policyName}\",\"active\":{isActive}}}";
            try
            {
                var commandPath = Path.Combine(_sharedDir, $"command-{_sessionId}.json");
                File.WriteAllText(commandPath, json);
            }
            catch { /* Runner may not be listening */ }
        }

        // Re-run update immediately so the button reflects the new state without waiting for next poll
        Update(_isStandalone, _engine, _viewerState, _sessionId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool GetPolicyActive(PolicyType type, bool standalone,
        SimulationEngine? engine, SharedState? viewerState)
    {
        if (standalone && engine != null)
            return engine.PolicySystem.IsActive(type);

        if (viewerState != null)
        {
            return type switch
            {
                PolicyType.GreenCity       => viewerState.PolicyGreenCity,
                PolicyType.IndustrialHub   => viewerState.PolicyIndustrialHub,
                PolicyType.CommercialBoost => viewerState.PolicyCommercialBoost,
                PolicyType.OpenCity        => viewerState.PolicyOpenCity,
                _                          => false,
            };
        }
        return false;
    }

    /// <summary>
    /// Returns true if the player has not yet reached the milestone required to use this policy.
    /// GameState.Active and GameState.Town map to milestones 0 and 500 respectively.
    /// </summary>
    private static bool IsPolicyLocked(PolicyDefinition def, int currentPop)
    {
        return def.UnlockAt switch
        {
            GameState.Town => currentPop < 500,
            GameState.City => currentPop < 5_000,
            _              => false,   // Active (and anything else) = always available
        };
    }

    private static string GetLockText(PolicyDefinition def) => def.UnlockAt switch
    {
        GameState.Town => "Requires Town (500 pop)",
        GameState.City => "Requires City (5,000 pop)",
        _              => "",
    };

    private static Control MakeSpacer(int h)
    {
        var s = new Control();
        s.CustomMinimumSize = new Vector2(0, h);
        return s;
    }

    private static HSeparator MakeHRule()
    {
        var sep = new HSeparator();
        sep.AddThemeColorOverride("color", new Color(0.35f, 0.30f, 0.15f, 0.80f)); // amber-tinted
        return sep;
    }

    private static HSeparator MakeThinSep()
    {
        var sep = new HSeparator();
        sep.AddThemeColorOverride("color", new Color(0.22f, 0.22f, 0.28f, 0.60f));
        return sep;
    }
}
