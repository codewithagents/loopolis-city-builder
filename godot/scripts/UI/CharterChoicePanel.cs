using Godot;
using System;

namespace LoopolisGodot;

/// <summary>
/// Full-screen modal panel shown exactly once when the Town milestone is reached.
/// Forces the player to pick one of three era charters that permanently modify city growth.
/// There is no cancel button — a charter must be chosen.
///
/// Dual-mode: in viewer mode it writes a select_charter IPC command; in standalone mode it
/// calls engine.Charters.SelectCharter() directly (same pattern as EventResponsePanel).
///
/// Layer 15 — above all other panels (GameOverPanel=13, ScenarioResult=14).
/// </summary>
public partial class CharterChoicePanel : CanvasLayer
{
    // ── Events ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Fired when the player clicks a charter card.
    /// Parameter: charter name string ("Merchant", "Industrial", "Civic").
    /// World.cs handles the actual IPC command or engine call.
    /// </summary>
    public event Action<string>? CharterSelected;

    // ── Charter data (mirrors CharterLibrary.AllTownCharters) ─────────────────
    private static readonly (string Key, string Title, string Body, string Effect, Color Border)[] CharterCards =
    {
        (
            Key:    "Merchant",
            Title:  "Merchant Charter",
            Body:   "Your city's identity is built on commerce. Markets thrive here.",
            Effect: "Commercial growth +30% · Land value +6%",
            Border: new Color(1.00f, 0.72f, 0.18f) // amber
        ),
        (
            Key:    "Industrial",
            Title:  "Industrial Charter",
            Body:   "The forge and the factory define your skyline. Workers flock here for steady jobs.",
            Effect: "Industrial growth +35% · +10 jobs per factory",
            Border: new Color(0.42f, 0.62f, 0.82f) // steel-blue
        ),
        (
            Key:    "Civic",
            Title:  "Civic Charter",
            Body:   "Your citizens demand well-run streets and good schools. This city is livable.",
            Effect: "Service radius +3 · Parks give 2x happiness",
            Border: new Color(0.32f, 0.82f, 0.42f) // green
        ),
    };

    // ── Layout constants ───────────────────────────────────────────────────────
    private const float CardW       = 220f;
    private const float CardH       = 220f;
    private const float CardSpacing = 20f;

    // ── State ─────────────────────────────────────────────────────────────────
    /// <summary>Set to true after the first card click so repeated events are dropped.</summary>
    private bool _charterChosen = false;

    // ── Godot lifecycle ────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Layer   = 15;
        Visible = false;
        BuildPanel();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public new void Show()
    {
        Visible = true;
    }

    public new void Hide()
    {
        Visible = false;
    }

    // ── UI construction ────────────────────────────────────────────────────────

    private void BuildPanel()
    {
        // Full-screen dark overlay (blocks map interaction behind the panel)
        var overlay = new ColorRect();
        overlay.Color = new Color(0f, 0f, 0f, 0.65f);
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(overlay);

        // Centred container card
        var outerVbox = new VBoxContainer();
        outerVbox.SetAnchorsPreset(Control.LayoutPreset.Center);
        outerVbox.GrowHorizontal = Control.GrowDirection.Both;
        outerVbox.GrowVertical   = Control.GrowDirection.Both;
        outerVbox.AddThemeConstantOverride("separation", 16);
        AddChild(outerVbox);

        // Title
        var titleLbl = new Label();
        titleLbl.Text = "Your Town Has a Character";
        titleLbl.HorizontalAlignment = HorizontalAlignment.Center;
        titleLbl.AddThemeColorOverride("font_color", new Color(1.00f, 0.85f, 0.30f));
        titleLbl.AddThemeFontSizeOverride("font_size", 26);
        outerVbox.AddChild(titleLbl);

        // Subtitle
        var subtitleLbl = new Label();
        subtitleLbl.Text = "Choose a charter that defines your city forever.";
        subtitleLbl.HorizontalAlignment = HorizontalAlignment.Center;
        subtitleLbl.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.70f));
        subtitleLbl.AddThemeFontSizeOverride("font_size", 14);
        outerVbox.AddChild(subtitleLbl);

        // Three cards side-by-side
        var cardRow = new HBoxContainer();
        cardRow.AddThemeConstantOverride("separation", (int)CardSpacing);
        outerVbox.AddChild(cardRow);

        foreach (var card in CharterCards)
        {
            cardRow.AddChild(BuildCard(card.Key, card.Title, card.Body, card.Effect, card.Border));
        }

        // Footer hint
        var footerLbl = new Label();
        footerLbl.Text = "This choice is permanent and cannot be changed later.";
        footerLbl.HorizontalAlignment = HorizontalAlignment.Center;
        footerLbl.AddThemeColorOverride("font_color", new Color(0.50f, 0.52f, 0.58f));
        footerLbl.AddThemeFontSizeOverride("font_size", 12);
        outerVbox.AddChild(footerLbl);
    }

    private Control BuildCard(string key, string title, string body, string effect, Color borderColor)
    {
        // Wrapper button (captures hover + click)
        var btn = new Button();
        btn.CustomMinimumSize = new Vector2(CardW, CardH);
        btn.FocusMode         = Control.FocusModeEnum.None;

        // Normal style — dark background with coloured border
        var normalStyle = MakeCardStyle(borderColor, new Color(0.08f, 0.09f, 0.12f, 0.96f));
        btn.AddThemeStyleboxOverride("normal",  normalStyle);
        btn.AddThemeStyleboxOverride("focus",   normalStyle);

        // Hover style — slightly lighter with brighter border
        var hoverStyle  = MakeCardStyle(
            new Color(borderColor.R * 1.3f, borderColor.G * 1.3f, borderColor.B * 1.3f, 1f).Clamp(),
            new Color(0.14f, 0.15f, 0.20f, 0.98f));
        btn.AddThemeStyleboxOverride("hover",   hoverStyle);
        btn.AddThemeStyleboxOverride("pressed", hoverStyle);

        // Content vbox inside the button
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        btn.AddChild(vbox);

        // Charter name (coloured, bold-size)
        var titleLbl = new Label();
        titleLbl.Text = title;
        titleLbl.AutowrapMode = TextServer.AutowrapMode.Word;
        titleLbl.HorizontalAlignment = HorizontalAlignment.Center;
        titleLbl.AddThemeColorOverride("font_color", borderColor);
        titleLbl.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(titleLbl);

        // Divider line
        var divider = new HSeparator();
        var divStyle = new StyleBoxFlat();
        divStyle.BgColor = new Color(borderColor.R, borderColor.G, borderColor.B, 0.35f);
        divider.AddThemeStyleboxOverride("separator", divStyle);
        vbox.AddChild(divider);

        // Body description (white, normal size)
        var bodyLbl = new Label();
        bodyLbl.Text = body;
        bodyLbl.AutowrapMode = TextServer.AutowrapMode.Word;
        bodyLbl.HorizontalAlignment = HorizontalAlignment.Center;
        bodyLbl.AddThemeColorOverride("font_color", new Color(0.88f, 0.88f, 0.88f));
        bodyLbl.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(bodyLbl);

        // Spacer pushes effect line to bottom
        var spacer = new Control();
        spacer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(spacer);

        // Effect line (gold, prominent)
        var effectLbl = new Label();
        effectLbl.Text = effect;
        effectLbl.AutowrapMode = TextServer.AutowrapMode.Word;
        effectLbl.HorizontalAlignment = HorizontalAlignment.Center;
        effectLbl.AddThemeColorOverride("font_color", new Color(1.00f, 0.82f, 0.30f));
        effectLbl.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(effectLbl);

        var capturedKey = key; // capture for closure
        btn.Pressed += () => OnCardClicked(capturedKey, title);

        return btn;
    }

    private static StyleBoxFlat MakeCardStyle(Color borderColor, Color bgColor)
    {
        var style = new StyleBoxFlat();
        style.BgColor                = bgColor;
        style.BorderColor            = borderColor;
        style.BorderWidthLeft        = 3;
        style.BorderWidthRight       = 3;
        style.BorderWidthTop         = 3;
        style.BorderWidthBottom      = 3;
        style.CornerRadiusTopLeft    = 6;
        style.CornerRadiusTopRight   = 6;
        style.CornerRadiusBottomLeft = 6;
        style.CornerRadiusBottomRight = 6;
        style.ContentMarginLeft      = 14f;
        style.ContentMarginRight     = 14f;
        style.ContentMarginTop       = 14f;
        style.ContentMarginBottom    = 14f;
        return style;
    }

    private void OnCardClicked(string charterKey, string charterTitle)
    {
        // Guard against a double-click in the same frame firing CharterSelected twice
        // (the panel hides on first click but QueueFree hasn't run yet, so buttons are still active).
        if (_charterChosen) return;
        _charterChosen = true;

        CharterSelected?.Invoke(charterKey);
        // Panel hides itself — World.cs will also free it when ActiveCharter populates
        Hide();
    }
}
