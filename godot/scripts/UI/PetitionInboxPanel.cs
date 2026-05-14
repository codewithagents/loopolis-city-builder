using Godot;
using System;

namespace LoopolisGodot;

/// <summary>
/// Toggleable petition inbox panel. Press I to open/close.
/// Shows up to 5 active petitions with district name, text, and deadline countdown.
/// Layer = 11 (same as CityStatsPanel).
/// Built entirely in code — no .tscn dependency.
/// </summary>
public partial class PetitionInboxPanel : CanvasLayer
{
    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color PanelBg      = new(0.06f, 0.07f, 0.10f, 0.95f);
    private static readonly Color PanelBorder  = new(0.85f, 0.65f, 0.25f, 1.00f); // amber gold
    private static readonly Color HeaderColor  = new(1.00f, 0.80f, 0.35f);        // warm amber
    private static readonly Color WhiteText    = new(0.95f, 0.95f, 0.95f);
    private static readonly Color GreyText     = new(0.58f, 0.58f, 0.63f);
    private static readonly Color DistrictColor = new(1.00f, 0.88f, 0.50f);       // district name
    private static readonly Color UrgentColor  = new(1.00f, 0.40f, 0.30f);        // < 20 ticks remaining
    private static readonly Color WarningColor = new(1.00f, 0.70f, 0.20f);        // < 40 ticks remaining
    private static readonly Color OkColor      = new(0.55f, 0.80f, 0.55f);        // plenty of time
    private static readonly Color EmptyColor   = new(0.50f, 0.55f, 0.65f);        // no petitions text

    // ── Layout ────────────────────────────────────────────────────────────────
    private const float CardW       = 500f;
    private const int   MaxShown    = 5;

    // ── State ─────────────────────────────────────────────────────────────────
    private ColorRect  _backdrop   = null!;
    private VBoxContainer _listBox = null!;   // contains the petition rows
    private Label _emptyLabel      = null!;
    private Label _countLabel      = null!;   // "N active petitions"
    private bool  _visible         = false;
    private int   _currentTick     = 0;

    public new bool IsVisible => _visible;

    public override void _Ready()
    {
        Layer = 11;

        // ── Semi-transparent backdrop ──────────────────────────────────────────
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0f, 0f, 0f, 0.55f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        _backdrop.Visible = false;
        AddChild(_backdrop);

        // ── Card anchored to top-right, below TopBar ──────────────────────────
        var card = new PanelContainer();
        card.CustomMinimumSize = new Vector2(CardW, 0f);
        card.AnchorRight  = 1f;
        card.AnchorBottom = 0f;
        card.OffsetRight  = -20f;
        card.OffsetLeft   = -(CardW + 20f);
        card.OffsetTop    = 48f;
        card.OffsetBottom = 0f;

        var cardStyle = new StyleBoxFlat();
        cardStyle.BgColor           = PanelBg;
        cardStyle.BorderColor       = PanelBorder;
        cardStyle.BorderWidthTop    = 2;
        cardStyle.BorderWidthBottom = 1;
        cardStyle.BorderWidthLeft   = 1;
        cardStyle.BorderWidthRight  = 1;
        cardStyle.CornerRadiusTopLeft = cardStyle.CornerRadiusTopRight =
            cardStyle.CornerRadiusBottomLeft = cardStyle.CornerRadiusBottomRight = 8;
        cardStyle.ContentMarginLeft   = 18;
        cardStyle.ContentMarginRight  = 18;
        cardStyle.ContentMarginTop    = 14;
        cardStyle.ContentMarginBottom = 14;
        card.AddThemeStyleboxOverride("panel", cardStyle);
        _backdrop.AddChild(card);

        // ── Card content ──────────────────────────────────────────────────────
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 0);
        card.AddChild(vbox);

        // Title row: "Petition Inbox" + [X] close + key hint
        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(titleRow);

        var titleLabel = new Label();
        titleLabel.Text = "\U0001f4dc Petition Inbox";
        titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleLabel.AddThemeColorOverride("font_color", HeaderColor);
        titleLabel.AddThemeFontSizeOverride("font_size", 17);
        titleRow.AddChild(titleLabel);

        // Petition count badge
        _countLabel = new Label();
        _countLabel.Text = "";
        _countLabel.VerticalAlignment = VerticalAlignment.Center;
        _countLabel.AddThemeColorOverride("font_color", GreyText);
        _countLabel.AddThemeFontSizeOverride("font_size", 12);
        titleRow.AddChild(_countLabel);

        // Close [X] button
        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.FocusMode = Control.FocusModeEnum.None;
        closeBtn.CustomMinimumSize = new Vector2(28, 28);
        closeBtn.AddThemeFontSizeOverride("font_size", 14);
        closeBtn.AddThemeColorOverride("font_color", GreyText);
        var closeBtnStyle = new StyleBoxFlat();
        closeBtnStyle.BgColor = new Color(0f, 0f, 0f, 0f);
        closeBtn.AddThemeStyleboxOverride("normal",  closeBtnStyle);
        closeBtn.AddThemeStyleboxOverride("hover",   closeBtnStyle);
        closeBtn.AddThemeStyleboxOverride("pressed", closeBtnStyle);
        closeBtn.AddThemeStyleboxOverride("focus",   closeBtnStyle);
        closeBtn.Pressed += Hide;
        titleRow.AddChild(closeBtn);

        vbox.AddChild(MakeSpacer(4));
        vbox.AddChild(MakeHRule(PanelBorder));
        vbox.AddChild(MakeSpacer(10));

        // "No active petitions" label (shown when inbox is empty)
        _emptyLabel = new Label();
        _emptyLabel.Text = "No active petitions  \U0001f3d9";
        _emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyLabel.AddThemeColorOverride("font_color", EmptyColor);
        _emptyLabel.AddThemeFontSizeOverride("font_size", 14);
        _emptyLabel.Visible = true;
        vbox.AddChild(_emptyLabel);

        // Petition rows container (hidden when empty)
        _listBox = new VBoxContainer();
        _listBox.AddThemeConstantOverride("separation", 8);
        _listBox.Visible = false;
        vbox.AddChild(_listBox);

        vbox.AddChild(MakeSpacer(10));

        // Footer hint
        var hintRow = new HBoxContainer();
        vbox.AddChild(hintRow);
        var hintSpacer = new Control();
        hintSpacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hintRow.AddChild(hintSpacer);
        var hintLbl = new Label();
        hintLbl.Text = "Press I to close";
        hintLbl.AddThemeColorOverride("font_color", GreyText);
        hintLbl.AddThemeFontSizeOverride("font_size", 10);
        hintRow.AddChild(hintLbl);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

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

    public void ToggleVisible()
    {
        if (_visible) Hide();
        else Show();
    }

    /// <summary>
    /// Refresh the panel with current petition state. Call each frame while visible,
    /// or on every tick update. Works in both standalone and viewer mode.
    /// </summary>
    /// <param name="petitions">Active petition entries (may be null when none).</param>
    /// <param name="currentTick">Current simulation tick (for deadline countdown).</param>
    public void UpdatePetitions(PetitionEntry[]? petitions, int currentTick)
    {
        _currentTick = currentTick;

        var count = petitions?.Length ?? 0;
        var shown = Math.Min(count, MaxShown);

        // Update count badge
        _countLabel.Text = count > 0 ? $"({count})" : "";

        // Toggle empty state vs list
        _emptyLabel.Visible = count == 0;
        _listBox.Visible    = count > 0;

        if (count == 0) return;

        // Rebuild petition rows (clear + re-add; panels are lightweight)
        foreach (var child in _listBox.GetChildren())
            child.QueueFree();

        for (var i = 0; i < shown; i++)
            _listBox.AddChild(BuildPetitionRow(petitions![i], currentTick));

        // "and N more" label when there are more than MaxShown
        if (count > MaxShown)
        {
            var moreLabel = new Label();
            moreLabel.Text = $"  … and {count - MaxShown} more";
            moreLabel.AddThemeColorOverride("font_color", GreyText);
            moreLabel.AddThemeFontSizeOverride("font_size", 12);
            _listBox.AddChild(moreLabel);
        }
    }

    // ── Row builder ────────────────────────────────────────────────────────────

    private static PanelContainer BuildPetitionRow(PetitionEntry petition, int currentTick)
    {
        var ticksLeft = petition.DeadlineTick - currentTick;

        var rowPanel = new PanelContainer();
        var rowStyle = new StyleBoxFlat();
        rowStyle.BgColor = new Color(0.10f, 0.10f, 0.14f, 0.90f);
        rowStyle.BorderWidthLeft  = 3;
        rowStyle.BorderColor      = CategoryBorderColor(petition.Category);
        rowStyle.CornerRadiusTopLeft = rowStyle.CornerRadiusTopRight =
            rowStyle.CornerRadiusBottomLeft = rowStyle.CornerRadiusBottomRight = 5;
        rowStyle.ContentMarginLeft   = 10;
        rowStyle.ContentMarginRight  = 10;
        rowStyle.ContentMarginTop    = 7;
        rowStyle.ContentMarginBottom = 7;
        rowPanel.AddThemeStyleboxOverride("panel", rowStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);
        rowPanel.AddChild(vbox);

        // Row 1: district name (bold, amber) + deadline badge (right-aligned)
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(headerRow);

        var districtLabel = new Label();
        districtLabel.Text = petition.DistrictName;
        districtLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        districtLabel.AddThemeColorOverride("font_color", DistrictColor);
        districtLabel.AddThemeFontSizeOverride("font_size", 13);
        headerRow.AddChild(districtLabel);

        var categoryLabel = new Label();
        categoryLabel.Text = $"[{petition.Category}]";
        categoryLabel.VerticalAlignment = VerticalAlignment.Center;
        categoryLabel.AddThemeColorOverride("font_color", CategoryBorderColor(petition.Category));
        categoryLabel.AddThemeFontSizeOverride("font_size", 11);
        headerRow.AddChild(categoryLabel);

        var deadlineLabel = new Label();
        deadlineLabel.Text = ticksLeft > 0 ? $"expires in {ticksLeft}t" : "OVERDUE";
        deadlineLabel.VerticalAlignment = VerticalAlignment.Center;
        deadlineLabel.AddThemeFontSizeOverride("font_size", 11);
        deadlineLabel.AddThemeColorOverride("font_color", ticksLeft switch
        {
            <= 0    => new Color(1f, 0.3f, 0.3f),
            <= 20   => UrgentColor,
            <= 40   => WarningColor,
            _       => OkColor,
        });
        headerRow.AddChild(deadlineLabel);

        // Row 2: petition text (wrapped, smaller, grey-white)
        var textLabel = new Label();
        textLabel.Text = petition.Text;
        textLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        textLabel.AddThemeColorOverride("font_color", WhiteText);
        textLabel.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(textLabel);

        return rowPanel;
    }

    /// <summary>Returns the left-border accent color for a petition category.</summary>
    private static Color CategoryBorderColor(string category) => category switch
    {
        "Happiness"    => new Color(1.00f, 0.80f, 0.30f),
        "Power"        => new Color(0.30f, 0.80f, 1.00f),
        "Employment"   => new Color(0.60f, 0.85f, 0.40f),
        "Services"     => new Color(0.80f, 0.50f, 1.00f),
        "Pollution"    => new Color(0.50f, 0.75f, 0.30f),
        "Overcrowding" => new Color(1.00f, 0.50f, 0.25f),
        _              => new Color(0.55f, 0.55f, 0.65f),
    };

    // ── Layout helpers ──────────────────────────────────────────────────────────

    private static Control MakeSpacer(int h)
    {
        var s = new Control();
        s.CustomMinimumSize = new Vector2(0, h);
        return s;
    }

    private static HSeparator MakeHRule(Color color)
    {
        var sep = new HSeparator();
        sep.AddThemeColorOverride("color", color);
        return sep;
    }
}
