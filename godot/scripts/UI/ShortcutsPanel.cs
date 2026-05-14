using Godot;

namespace LoopolisGodot;

/// <summary>
/// Modal keyboard shortcuts reference panel.
/// Press '?' to show, press '?' or Escape to close.
/// Layer = 15 (above game UI, below scenario result panel).
/// Built entirely in code — no .tscn dependency.
/// </summary>
public partial class ShortcutsPanel : CanvasLayer
{
    private ColorRect  _backdrop = null!;
    private bool       _visible  = false;

    // Panel dimensions
    private const float CardW = 480f;
    private const float CardH = 560f;

    public new bool IsVisible => _visible;

    public override void _Ready()
    {
        Layer = 15;

        // ── Semi-transparent backdrop (blocks clicks on city while open) ──────
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0f, 0f, 0f, 0.65f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        _backdrop.Visible = false;
        AddChild(_backdrop);

        // ── Centered card container ───────────────────────────────────────────
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        var card = new PanelContainer();
        card.CustomMinimumSize = new Vector2(CardW, CardH);
        var cardStyle = new StyleBoxFlat();
        cardStyle.BgColor = new Color(0.08f, 0.08f, 0.08f, 0.92f);
        cardStyle.BorderColor = new Color(0.5f, 0.5f, 0.6f);
        cardStyle.BorderWidthLeft = cardStyle.BorderWidthRight =
            cardStyle.BorderWidthTop = cardStyle.BorderWidthBottom = 1;
        cardStyle.CornerRadiusTopLeft = cardStyle.CornerRadiusTopRight =
            cardStyle.CornerRadiusBottomLeft = cardStyle.CornerRadiusBottomRight = 8;
        cardStyle.ContentMarginLeft  = cardStyle.ContentMarginRight  = 28;
        cardStyle.ContentMarginTop   = cardStyle.ContentMarginBottom = 22;
        card.AddThemeStyleboxOverride("panel", cardStyle);
        center.AddChild(card);

        // ── Card content ─────────────────────────────────────────────────────
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 0);
        card.AddChild(outer);

        // Title row
        var titleLabel = new Label();
        titleLabel.Text = "Keyboard Shortcuts";
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.AddThemeColorOverride("font_color", Colors.White);
        titleLabel.AddThemeFontSizeOverride("font_size", 16);
        outer.AddChild(titleLabel);

        // Close hint
        var closeHint = new Label();
        closeHint.Text = "Press ? or Esc to close";
        closeHint.HorizontalAlignment = HorizontalAlignment.Center;
        closeHint.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.60f));
        closeHint.AddThemeFontSizeOverride("font_size", 11);
        outer.AddChild(closeHint);

        outer.AddChild(MakeSpacer(10));
        outer.AddChild(MakeHRule());
        outer.AddChild(MakeSpacer(10));

        // ── Two-column layout ─────────────────────────────────────────────────
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        outer.AddChild(columns);

        // Left column
        var leftCol = new VBoxContainer();
        leftCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftCol.AddThemeConstantOverride("separation", 4);
        columns.AddChild(leftCol);

        AddSection(leftCol, "Toolbar Tabs");
        AddRow(leftCol, "Z",       "Zones tab");
        AddRow(leftCol, "S",       "Services tab");
        AddRow(leftCol, "U",       "Utilities tab");
        AddRow(leftCol, "X",       "Overlays tab");

        leftCol.AddChild(MakeSpacer(8));
        AddSection(leftCol, "Zones & Tools");
        AddRow(leftCol, "R",       "Residential zone");
        AddRow(leftCol, "C",       "Commercial zone");
        AddRow(leftCol, "I",       "Industrial zone");
        AddRow(leftCol, "P",       "Park zone");
        AddRow(leftCol, "W",       "Road");
        AddRow(leftCol, "A",       "Avenue");
        AddRow(leftCol, "E",       "Erase mode");
        AddRow(leftCol, "G",       "Upgrade tool");
        AddRow(leftCol, "Esc",     "Deselect / cancel");

        leftCol.AddChild(MakeSpacer(8));
        AddSection(leftCol, "Camera");
        AddRow(leftCol, "Arrows",  "Pan camera");
        AddRow(leftCol, "Scroll",  "Zoom in/out");
        AddRow(leftCol, "Mid drag","Pan camera");

        // Right column
        var rightCol = new VBoxContainer();
        rightCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightCol.AddThemeConstantOverride("separation", 4);
        columns.AddChild(rightCol);

        AddSection(rightCol, "Overlays & UI");
        AddRow(rightCol, "X",      "Open Overlays tab");
        AddRow(rightCol, "F1",     "Happiness overlay");
        AddRow(rightCol, "F2",     "Traffic overlay");
        AddRow(rightCol, "F3",     "Coverage overlay");
        AddRow(rightCol, "F4",     "Land Value overlay");
        AddRow(rightCol, "F5",     "Pollution overlay");
        AddRow(rightCol, "H",      "Toggle HUD stats");
        AddRow(rightCol, "M",      "Toggle minimap");
        AddRow(rightCol, "O",      "City Policies");
        AddRow(rightCol, "?",      "This help panel");

        rightCol.AddChild(MakeSpacer(8));
        AddSection(rightCol, "Game");
        AddRow(rightCol, "Space",  "Pause / resume");
        AddRow(rightCol, "Ctrl+S", "Save game");
        AddRow(rightCol, "Ctrl+L", "Load game");
        AddRow(rightCol, "Esc",    "Main Menu / close");
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

    // ── Builder helpers ───────────────────────────────────────────────────────

    /// <summary>Adds a bold section-header label and a thin grey separator above it.</summary>
    private static void AddSection(VBoxContainer col, string title)
    {
        var lbl = new Label();
        lbl.Text = title.ToUpperInvariant();
        lbl.AddThemeColorOverride("font_color", new Color(0.70f, 0.70f, 0.80f));
        lbl.AddThemeFontSizeOverride("font_size", 11);
        col.AddChild(lbl);
        col.AddChild(MakeHRule());
    }

    /// <summary>
    /// Adds a single shortcut row: keyboard-key badge on the left, description on the right.
    /// </summary>
    private static void AddRow(VBoxContainer col, string keyLabel, string description)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        col.AddChild(row);

        // Key badge — styled like a physical keyboard key
        var badge = new PanelContainer();
        badge.CustomMinimumSize = new Vector2(44, 20);
        var badgeStyle = new StyleBoxFlat();
        badgeStyle.BgColor = new Color(0.25f, 0.25f, 0.35f);
        badgeStyle.BorderColor = new Color(0.5f, 0.5f, 0.6f);
        badgeStyle.BorderWidthLeft  = badgeStyle.BorderWidthRight  =
            badgeStyle.BorderWidthTop = badgeStyle.BorderWidthBottom = 1;
        badgeStyle.CornerRadiusTopLeft  = badgeStyle.CornerRadiusTopRight  =
            badgeStyle.CornerRadiusBottomLeft = badgeStyle.CornerRadiusBottomRight = 3;
        badgeStyle.ContentMarginLeft  = badgeStyle.ContentMarginRight  = 4;
        badgeStyle.ContentMarginTop   = badgeStyle.ContentMarginBottom = 1;
        badge.AddThemeStyleboxOverride("panel", badgeStyle);
        row.AddChild(badge);

        var keyLbl = new Label();
        keyLbl.Text = keyLabel;
        keyLbl.HorizontalAlignment = HorizontalAlignment.Center;
        keyLbl.VerticalAlignment   = VerticalAlignment.Center;
        keyLbl.AddThemeColorOverride("font_color", new Color(0.90f, 0.90f, 1.00f));
        keyLbl.AddThemeFontSizeOverride("font_size", 11);
        keyLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        badge.AddChild(keyLbl);

        // Description text
        var descLbl = new Label();
        descLbl.Text = description;
        descLbl.VerticalAlignment = VerticalAlignment.Center;
        descLbl.AddThemeColorOverride("font_color", new Color(0.90f, 0.90f, 0.90f));
        descLbl.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(descLbl);
    }

    private static Control MakeSpacer(int height)
    {
        var s = new Control();
        s.CustomMinimumSize = new Vector2(0, height);
        return s;
    }

    private static HSeparator MakeHRule()
    {
        var sep = new HSeparator();
        sep.AddThemeColorOverride("color", new Color(0.28f, 0.28f, 0.35f, 0.80f));
        return sep;
    }
}
