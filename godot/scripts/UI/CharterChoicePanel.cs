using Godot;
using System;

namespace LoopolisGodot;

/// <summary>
/// Full-screen modal panel shown when a charter milestone is reached (Town, City, or Metropolis era).
/// Forces the player to pick one of three era charters that permanently modify city growth.
/// There is no cancel button — a charter must be chosen.
///
/// Dual-mode: in viewer mode it writes a select_charter / select_city_charter / select_metropolis_charter
/// IPC command; in standalone mode it calls engine.Charters.SelectCharter() / SelectCityCharter() /
/// SelectMetropolisCharter() directly.
///
/// Three-era: call Show() for Town era cards, ShowCityCharters() for City era cards,
/// ShowMetropolisCharters() for Metropolis era cards.
/// World.cs checks IsForCityEra / IsForMetropolisEra after CharterSelected fires to route correctly.
///
/// Layer 15 — above all other panels (GameOverPanel=13, ScenarioResult=14).
/// </summary>
public partial class CharterChoicePanel : CanvasLayer
{
    // ── Events ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Fired when the player clicks a charter card.
    /// Parameter: charter name string ("Merchant", "Industrial", "Civic",
    ///            "InnovationHub", "GreenCanopy", "TradeCorridors",
    ///            "NexusCity", "GreenUtopia", "EmpireOfSteel").
    /// World.cs checks IsForCityEra / IsForMetropolisEra to decide which IPC command or engine call to make.
    /// </summary>
    public event Action<string>? CharterSelected;

    /// <summary>
    /// True when the panel is showing City era charter cards.
    /// False when showing Town or Metropolis era cards.
    /// Read by World.cs inside the CharterSelected handler to route correctly.
    /// </summary>
    public bool IsForCityEra { get; private set; }

    /// <summary>
    /// True when the panel is showing Metropolis era charter cards.
    /// False when showing Town or City era cards.
    /// Read by World.cs inside the CharterSelected handler to route correctly.
    /// </summary>
    public bool IsForMetropolisEra { get; private set; }

    /// <summary>
    /// True when the panel is showing Town era charter cards (default era).
    /// This is always the negation of the other two — no new backing state needed.
    /// </summary>
    public bool IsForTownEra => !IsForCityEra && !IsForMetropolisEra;

    // ── Town charter data (mirrors CharterLibrary.AllTownCharters) ─────────────
    private static readonly (string Key, string Title, string Body, string Effect, Color Border)[] TownCharterCards =
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

    // ── City charter data (mirrors CharterLibrary.AllCityCharters) ─────────────
    private static readonly (string Key, string Title, string Body, string Effect, Color Border)[] CityCharterCards =
    {
        (
            Key:    "InnovationHub",
            Title:  "Innovation Hub",
            Body:   "Your city embraces density. Smart zoning fills every building to capacity.",
            Effect: "Residential capacity +20% · Tax revenue +8%",
            Border: new Color(0.55f, 0.30f, 0.90f) // purple
        ),
        (
            Key:    "GreenCanopy",
            Title:  "Green Canopy",
            Body:   "Green infrastructure networks define your skyline. Parks and clean tech heal your city.",
            Effect: "Pollution impact ×0.5 · Park radius +2 tiles",
            Border: new Color(0.25f, 0.72f, 0.35f) // forest green
        ),
        (
            Key:    "TradeCorridors",
            Title:  "Trade Corridors",
            Body:   "Markets and trade routes flow through your city. The commercial sector never sleeps.",
            Effect: "Commercial growth +25% · Land value +8%",
            Border: new Color(0.85f, 0.65f, 0.10f) // deep gold
        ),
    };

    // ── Metropolis charter data (mirrors CharterLibrary.AllMetropolisCharters) ─────
    private static readonly (string Key, string Title, string Body, string Effect, Color Border)[] MetropolisCharterCards =
    {
        (
            Key:    "NexusCity",
            Title:  "Nexus City",
            Body:   "Every district is connected. A web of services and infrastructure that no other city can match.",
            Effect: "Service radius +5 · Residential capacity +30% · Tax revenue +8%",
            Border: new Color(0.20f, 0.70f, 1.00f) // electric blue
        ),
        (
            Key:    "GreenUtopia",
            Title:  "Green Utopia",
            Body:   "Industrial age is over. Your city runs on clean energy, parks, and clear skies.",
            Effect: "Pollution impact ×0.1 · Park happiness ×3.0 · Park radius +3 tiles",
            Border: new Color(0.10f, 0.90f, 0.45f) // vivid green
        ),
        (
            Key:    "EmpireOfSteel",
            Title:  "Empire of Steel",
            Body:   "Your factories define the continent's economy. What your city makes, the world buys.",
            Effect: "Industrial growth ×1.60 · +25 jobs per factory tile · Commercial growth ×1.30",
            Border: new Color(0.80f, 0.25f, 0.15f) // forge red
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
        // Build with Town era data by default — ShowCityCharters() rebuilds for City era
        BuildPanel(
            title:    "Your Town Has a Character",
            subtitle: "Choose a charter that defines your city forever.",
            cards:    TownCharterCards);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Show the Town era charter panel (resets and rebuilds).</summary>
    public new void Show()
    {
        _charterChosen     = false;
        IsForCityEra       = false;
        IsForMetropolisEra = false;
        RebuildPanelContent(
            title:    "Your Town Has a Character",
            subtitle: "Choose a charter that defines your city forever.",
            cards:    TownCharterCards);
        Visible = true;
    }

    /// <summary>Show the City era charter panel (resets and rebuilds with City cards).</summary>
    public void ShowCityCharters()
    {
        _charterChosen     = false;
        IsForCityEra       = true;
        IsForMetropolisEra = false;
        RebuildPanelContent(
            title:    "Your City Has an Identity",
            subtitle: "Choose a charter that shapes your city's future.",
            cards:    CityCharterCards);
        Visible = true;
    }

    /// <summary>Show the Metropolis era charter panel (resets and rebuilds).</summary>
    public void ShowMetropolisCharters()
    {
        _charterChosen     = false;
        IsForCityEra       = false;
        IsForMetropolisEra = true;
        RebuildPanelContent(
            title:    "Your Metropolis Has a Legacy",
            subtitle: "Choose the charter that will define your city for history.",
            cards:    MetropolisCharterCards);
        Visible = true;
    }

    public new void Hide()
    {
        Visible = false;
    }

    // ── UI construction ────────────────────────────────────────────────────────

    private void BuildPanel(
        string title,
        string subtitle,
        (string Key, string Title, string Body, string Effect, Color Border)[] cards)
    {
        // Full-screen dark overlay (blocks map interaction behind the panel)
        var overlay = new ColorRect();
        overlay.Color = new Color(0f, 0f, 0f, 0.65f);
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(overlay);

        AddChild(BuildCentredVbox(title, subtitle, cards));
    }

    /// <summary>
    /// Clears all existing CanvasLayer children and rebuilds them for the given era.
    /// Called by Show() and ShowCityCharters() to switch between Town and City cards.
    /// </summary>
    private void RebuildPanelContent(
        string title,
        string subtitle,
        (string Key, string Title, string Body, string Effect, Color Border)[] cards)
    {
        // Remove all existing children
        foreach (var child in GetChildren())
            child.QueueFree();

        // Rebuild overlay + card content
        var overlay = new ColorRect();
        overlay.Color = new Color(0f, 0f, 0f, 0.65f);
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(overlay);

        AddChild(BuildCentredVbox(title, subtitle, cards));
    }

    private VBoxContainer BuildCentredVbox(
        string title,
        string subtitle,
        (string Key, string Title, string Body, string Effect, Color Border)[] cards)
    {
        var outerVbox = new VBoxContainer();
        outerVbox.SetAnchorsPreset(Control.LayoutPreset.Center);
        outerVbox.GrowHorizontal = Control.GrowDirection.Both;
        outerVbox.GrowVertical   = Control.GrowDirection.Both;
        outerVbox.AddThemeConstantOverride("separation", 16);

        // Title
        var titleLbl = new Label();
        titleLbl.Text = title;
        titleLbl.HorizontalAlignment = HorizontalAlignment.Center;
        titleLbl.AddThemeColorOverride("font_color", new Color(1.00f, 0.85f, 0.30f));
        titleLbl.AddThemeFontSizeOverride("font_size", 26);
        outerVbox.AddChild(titleLbl);

        // Subtitle
        var subtitleLbl = new Label();
        subtitleLbl.Text = subtitle;
        subtitleLbl.HorizontalAlignment = HorizontalAlignment.Center;
        subtitleLbl.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.70f));
        subtitleLbl.AddThemeFontSizeOverride("font_size", 14);
        outerVbox.AddChild(subtitleLbl);

        // Three cards side-by-side
        var cardRow = new HBoxContainer();
        cardRow.AddThemeConstantOverride("separation", (int)CardSpacing);
        outerVbox.AddChild(cardRow);

        foreach (var card in cards)
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

        return outerVbox;
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
        // Panel hides itself — World.cs will also free it when ActiveCharter / CityActiveCharter populates
        Hide();
    }
}
