using Godot;
using System;

namespace LoopolisGodot;

/// <summary>
/// Small panel shown at the top-centre of the screen when a city event fires.
/// Gives the player the choice to intervene (spending money to resolve early) or
/// let the event play out naturally.
///
/// Layer 12 — above most UI panels (HintOverlay=8, Toolbar=9, HudOverlay=10, TileTooltip=11).
/// </summary>
public partial class EventResponsePanel : CanvasLayer
{
    // ── Layout constants ───────────────────────────────────────────────────────
    private const float PanelW       = 380f;
    private const float PanelH       = 130f;
    private const float TopOffset    = 44f;   // px below the top of the viewport
    private const float BorderW      = 5f;    // left accent stripe width

    // ── UI nodes (built in _Ready) ─────────────────────────────────────────────
    private PanelContainer _root      = null!;
    private Label          _titleLbl  = null!;
    private Label          _descLbl   = null!;
    private Button         _intervene = null!;
    private Button         _dismiss   = null!;

    // ── State ──────────────────────────────────────────────────────────────────
    private string _currentEventType = "";
    private int    _currentCost      = 0;

    /// <summary>
    /// Raised when the player clicks "Intervene".
    /// Caller (World.cs) must call engine.RespondToCurrentEvent() and show a toast.
    /// </summary>
    public event Action? InterveneRequested;

    // ── Godot lifecycle ────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Layer   = 12;
        Visible = false;
        BuildPanel();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Display the panel for the given event.
    /// <paramref name="canAfford"/> dims the Intervene button when the player lacks funds.
    /// </summary>
    public void ShowEvent(string eventType, int cost, bool canAfford)
    {
        _currentEventType = eventType;
        _currentCost      = cost;

        _titleLbl.Text = EventTitle(eventType);
        _descLbl.Text  = EventDesc(eventType);

        var btnText = $"Intervene (${cost:N0})";
        _intervene.Text     = btnText;
        _intervene.Disabled = !canAfford;
        _intervene.Modulate = canAfford ? Colors.White : new Color(1f, 1f, 1f, 0.45f);

        RepositionPanel();
        Visible = true;
    }

    /// <summary>Hide the panel without triggering any response action.</summary>
    public new void Hide() => Visible = false;

    /// <summary>Whether the panel is currently shown.</summary>
    public new bool IsVisible => Visible;

    // ── Internal helpers ───────────────────────────────────────────────────────

    private void RepositionPanel()
    {
        if (_root == null) return;
        var vpSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1152f, 648f);
        _root.SetPosition(new Vector2((vpSize.X - PanelW) * 0.5f, TopOffset));
    }

    private void BuildPanel()
    {
        // Root container
        _root = new PanelContainer();
        _root.CustomMinimumSize = new Vector2(PanelW, PanelH);
        _root.SetPosition(new Vector2(400f, TopOffset));

        // Dark amber-tinted background
        var style = new StyleBoxFlat();
        style.BgColor            = new Color(0.10f, 0.06f, 0.04f, 0.94f);
        style.BorderColor        = new Color(0.85f, 0.55f, 0.10f, 1f);
        style.BorderWidthLeft    = (int)BorderW;
        style.BorderWidthTop     = 0;
        style.BorderWidthRight   = 0;
        style.BorderWidthBottom  = 0;
        style.CornerRadiusTopLeft     = 4;
        style.CornerRadiusTopRight    = 4;
        style.CornerRadiusBottomLeft  = 4;
        style.CornerRadiusBottomRight = 4;
        style.ContentMarginLeft   = 12f;
        style.ContentMarginRight  = 12f;
        style.ContentMarginTop    = 8f;
        style.ContentMarginBottom = 8f;
        _root.AddThemeStyleboxOverride("panel", style);

        // Vertical layout: title + desc + buttons
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        _root.AddChild(vbox);

        // Title label (amber, bold)
        _titleLbl = new Label();
        _titleLbl.Text            = "";
        _titleLbl.AutowrapMode    = TextServer.AutowrapMode.Off;
        _titleLbl.AddThemeColorOverride("font_color", new Color(1f, 0.72f, 0.18f));
        _titleLbl.AddThemeFontSizeOverride("font_size", 15);
        vbox.AddChild(_titleLbl);

        // Description label (grey, small)
        _descLbl = new Label();
        _descLbl.Text         = "";
        _descLbl.AutowrapMode = TextServer.AutowrapMode.Word;
        _descLbl.AddThemeColorOverride("font_color", new Color(0.78f, 0.74f, 0.68f));
        _descLbl.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_descLbl);

        // Spacer
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0f, 4f);
        vbox.AddChild(spacer);

        // Button row
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(hbox);

        // Intervene button (orange)
        _intervene = new Button();
        _intervene.Text         = "Intervene";
        _intervene.CustomMinimumSize = new Vector2(160f, 30f);
        var btnStyle = new StyleBoxFlat();
        btnStyle.BgColor            = new Color(0.80f, 0.40f, 0.06f, 1f);
        btnStyle.BorderColor        = new Color(1f, 0.60f, 0.15f, 1f);
        btnStyle.BorderWidthBottom  = 2;
        btnStyle.CornerRadiusTopLeft = btnStyle.CornerRadiusTopRight =
        btnStyle.CornerRadiusBottomLeft = btnStyle.CornerRadiusBottomRight = 4;
        btnStyle.ContentMarginLeft = btnStyle.ContentMarginRight = 8f;
        btnStyle.ContentMarginTop  = btnStyle.ContentMarginBottom = 4f;
        _intervene.AddThemeStyleboxOverride("normal", btnStyle);

        var btnHoverStyle = (StyleBoxFlat)btnStyle.Duplicate();
        btnHoverStyle.BgColor = new Color(0.92f, 0.48f, 0.10f, 1f);
        _intervene.AddThemeStyleboxOverride("hover", btnHoverStyle);

        _intervene.AddThemeColorOverride("font_color", Colors.White);
        _intervene.AddThemeFontSizeOverride("font_size", 13);
        _intervene.Pressed += OnIntervenPressed;
        hbox.AddChild(_intervene);

        // Dismiss button (grey)
        _dismiss = new Button();
        _dismiss.Text = "Let it play out";
        _dismiss.CustomMinimumSize = new Vector2(140f, 30f);
        var dismissStyle = new StyleBoxFlat();
        dismissStyle.BgColor  = new Color(0.22f, 0.20f, 0.18f, 1f);
        dismissStyle.BorderColor = new Color(0.40f, 0.38f, 0.34f, 1f);
        dismissStyle.BorderWidthBottom = 2;
        dismissStyle.CornerRadiusTopLeft = dismissStyle.CornerRadiusTopRight =
        dismissStyle.CornerRadiusBottomLeft = dismissStyle.CornerRadiusBottomRight = 4;
        dismissStyle.ContentMarginLeft = dismissStyle.ContentMarginRight = 8f;
        dismissStyle.ContentMarginTop  = dismissStyle.ContentMarginBottom = 4f;
        _dismiss.AddThemeStyleboxOverride("normal", dismissStyle);

        var dismissHoverStyle = (StyleBoxFlat)dismissStyle.Duplicate();
        dismissHoverStyle.BgColor = new Color(0.30f, 0.27f, 0.24f, 1f);
        _dismiss.AddThemeStyleboxOverride("hover", dismissHoverStyle);

        _dismiss.AddThemeColorOverride("font_color", new Color(0.80f, 0.78f, 0.74f));
        _dismiss.AddThemeFontSizeOverride("font_size", 13);
        _dismiss.Pressed += () => Hide();
        hbox.AddChild(_dismiss);

        AddChild(_root);
    }

    private void OnIntervenPressed()
    {
        Hide();
        InterveneRequested?.Invoke();
    }

    // ── Display text maps ──────────────────────────────────────────────────────

    private static string EventTitle(string eventType) => eventType switch
    {
        "FireBreak"    => "Fire Emergency!",
        "CrimeWave"    => "Crime Wave!",
        "PowerOutage"  => "Power Failure!",
        "DemandSlump"  => "Demand Slump!",
        _              => eventType,
    };

    private static string EventDesc(string eventType) => eventType switch
    {
        "FireBreak"    => "A building is at risk of demolition.",
        "CrimeWave"    => "Happiness dropping in affected areas.",
        "PowerOutage"  => "Some areas losing power.",
        "DemandSlump"  => "Commercial growth stalled.",
        _              => "An event is affecting your city.",
    };
}
