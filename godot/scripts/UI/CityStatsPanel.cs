using Godot;
using System;
using System.Collections.Generic;
using Loopolis.Core.Simulation;

namespace LoopolisGodot;

/// <summary>
/// Modal City Statistics panel. Press V to toggle.
/// Layer = 11.
///
/// Shows population / happiness / balance trends, peak records, growth rate,
/// and a 10-point population sparkline drawn with DrawLine.
///
/// Works in both standalone mode (reads directly from SimulationEngine.Statistics)
/// and viewer/server mode (reads from SharedState fields populated by CityStatisticsSystem).
/// Built entirely in code — no .tscn dependency. Follows the PolicyPanel pattern.
/// </summary>
public partial class CityStatsPanel : CanvasLayer
{
    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color PanelBg      = new(0.06f, 0.07f, 0.10f, 0.95f);
    private static readonly Color PanelBorder  = new(0.35f, 0.50f, 0.75f, 1.00f);
    private static readonly Color HeaderColor  = new(0.55f, 0.80f, 1.00f);
    private static readonly Color WhiteText    = new(0.95f, 0.95f, 0.95f);
    private static readonly Color GreyText     = new(0.58f, 0.58f, 0.63f);
    private static readonly Color GreenColor   = new(0.35f, 0.90f, 0.40f);
    private static readonly Color RedColor     = new(0.95f, 0.35f, 0.30f);
    private static readonly Color AmberColor   = new(1.00f, 0.78f, 0.20f);
    private static readonly Color SparkBg      = new(0.08f, 0.10f, 0.14f);
    private static readonly Color SparkLine    = new(0.35f, 0.90f, 0.40f);

    // ── Layout constants ──────────────────────────────────────────────────────
    private const float CardW       = 340f;
    private const float SparkW      = 290f;
    private const float SparkH      = 56f;
    private const int   SparkPoints = 20;  // number of history points shown in sparkline

    // ── Nodes created in _Ready ───────────────────────────────────────────────
    private ColorRect  _backdrop  = null!;
    private bool       _visible   = false;

    // Dynamic labels updated in UpdateFromState / UpdateFromEngine
    private Label _popValueLabel      = null!;
    private Label _popTrendLabel      = null!;
    private Label _happyValueLabel    = null!;
    private Label _happyTrendLabel    = null!;
    private Label _balanceValueLabel  = null!;
    private Label _balanceTrendLabel  = null!;
    private Label _peakPopLabel       = null!;
    private Label _peakBalLabel       = null!;
    private Label _growthRateLabel    = null!;
    private SparklineControl _sparkline = null!;

    public new bool IsVisible => _visible;

    public override void _Ready()
    {
        Layer = 11;

        // ── Full-screen backdrop ──────────────────────────────────────────────
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0f, 0f, 0f, 0.55f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        _backdrop.Visible = false;
        AddChild(_backdrop);

        // ── Card positioned right-of-centre ──────────────────────────────────
        // Use AnchorRight so the card floats at a fixed offset from the right edge,
        // similar to TopBar positioning, without requiring a CenterContainer.
        var card = new PanelContainer();
        card.CustomMinimumSize = new Vector2(CardW, 0f);

        // Anchor to top-right so we can offset from the right edge
        card.AnchorRight  = 1f;
        card.AnchorBottom = 0f;
        // Offset: right edge 20 px from screen right, positioned below TopBar (~40 px)
        card.OffsetRight  = -20f;
        card.OffsetLeft   = -(CardW + 20f);
        card.OffsetTop    = 48f;
        card.OffsetBottom = 0f;  // auto-sized by content (PanelContainer)

        var cardStyle = new StyleBoxFlat();
        cardStyle.BgColor            = PanelBg;
        cardStyle.BorderColor        = PanelBorder;
        cardStyle.BorderWidthTop     = 2;
        cardStyle.BorderWidthBottom  = 1;
        cardStyle.BorderWidthLeft    = 1;
        cardStyle.BorderWidthRight   = 1;
        cardStyle.CornerRadiusTopLeft = cardStyle.CornerRadiusTopRight =
            cardStyle.CornerRadiusBottomLeft = cardStyle.CornerRadiusBottomRight = 8;
        cardStyle.ContentMarginLeft   = 18;
        cardStyle.ContentMarginRight  = 18;
        cardStyle.ContentMarginTop    = 14;
        cardStyle.ContentMarginBottom = 14;
        card.AddThemeStyleboxOverride("panel", cardStyle);
        _backdrop.AddChild(card);

        // ── Card content (VBox) ───────────────────────────────────────────────
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 0);
        card.AddChild(vbox);

        // Title row
        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 0);
        vbox.AddChild(titleRow);

        var titleLabel = new Label();
        titleLabel.Text = "City Statistics";
        titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleLabel.AddThemeColorOverride("font_color", HeaderColor);
        titleLabel.AddThemeFontSizeOverride("font_size", 17);
        titleRow.AddChild(titleLabel);

        var closeHint = new Label();
        closeHint.Text = "V";
        closeHint.VerticalAlignment = VerticalAlignment.Center;
        closeHint.AddThemeColorOverride("font_color", GreyText);
        closeHint.AddThemeFontSizeOverride("font_size", 11);
        titleRow.AddChild(closeHint);

        vbox.AddChild(MakeSpacer(6));
        vbox.AddChild(MakeHRule(PanelBorder));
        vbox.AddChild(MakeSpacer(8));

        // ── Section: Trends ───────────────────────────────────────────────────
        AddSectionHeader(vbox, "Trends");
        vbox.AddChild(MakeSpacer(4));

        (_popValueLabel,   _popTrendLabel)     = AddTrendRow(vbox, "Population");
        vbox.AddChild(MakeSpacer(2));
        (_happyValueLabel, _happyTrendLabel)   = AddTrendRow(vbox, "Happiness");
        vbox.AddChild(MakeSpacer(2));
        (_balanceValueLabel, _balanceTrendLabel) = AddTrendRow(vbox, "Balance");

        vbox.AddChild(MakeSpacer(10));
        vbox.AddChild(MakeHRule(new Color(0.20f, 0.22f, 0.28f, 0.80f)));
        vbox.AddChild(MakeSpacer(8));

        // ── Section: Peak Records ─────────────────────────────────────────────
        AddSectionHeader(vbox, "Peak Records");
        vbox.AddChild(MakeSpacer(4));

        _peakPopLabel = AddStatRow(vbox, "Peak Population:");
        vbox.AddChild(MakeSpacer(2));
        _peakBalLabel = AddStatRow(vbox, "Peak Balance:");

        vbox.AddChild(MakeSpacer(10));
        vbox.AddChild(MakeHRule(new Color(0.20f, 0.22f, 0.28f, 0.80f)));
        vbox.AddChild(MakeSpacer(8));

        // ── Section: Growth Rate ──────────────────────────────────────────────
        AddSectionHeader(vbox, "Growth");
        vbox.AddChild(MakeSpacer(4));

        _growthRateLabel = AddStatRow(vbox, "Pop Growth:");

        vbox.AddChild(MakeSpacer(10));
        vbox.AddChild(MakeHRule(new Color(0.20f, 0.22f, 0.28f, 0.80f)));
        vbox.AddChild(MakeSpacer(8));

        // ── Section: Sparkline ────────────────────────────────────────────────
        AddSectionHeader(vbox, "Population History");
        vbox.AddChild(MakeSpacer(6));

        _sparkline = new SparklineControl(SparkW, SparkH, SparkBg, SparkLine);
        vbox.AddChild(_sparkline);

        vbox.AddChild(MakeSpacer(10));

        // ── Close hint ────────────────────────────────────────────────────────
        var hintRow = new HBoxContainer();
        hintRow.AddThemeConstantOverride("separation", 0);
        vbox.AddChild(hintRow);

        var hintSpacer = new Control();
        hintSpacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hintRow.AddChild(hintSpacer);

        var hintLbl = new Label();
        hintLbl.Text = "Press V to close";
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
    /// Refresh the panel from standalone simulation engine data.
    /// Call each frame while the panel is visible (or on every tick) in standalone mode.
    /// </summary>
    public void UpdateFromEngine(SimulationEngine engine, int population, double balance, float happiness)
    {
        var stats = engine.Statistics;

        SetTrendRow(_popValueLabel,     _popTrendLabel,
            $"{population:N0}", stats.PopulationTrend());

        SetTrendRow(_happyValueLabel,   _happyTrendLabel,
            $"{happiness:P0}", stats.HappinessTrend());

        SetTrendRow(_balanceValueLabel, _balanceTrendLabel,
            $"${balance:N0}", stats.BalanceTrend());

        _peakPopLabel.Text = $"{stats.PeakPopulation:N0}";
        _peakBalLabel.Text = $"${stats.PeakBalance:N0}";

        SetGrowthRate(stats.PopulationGrowthRate);

        // Build sparkline from rolling history (last SparkPoints entries)
        var snapshots = new List<float>();
        foreach (var snap in stats.History)
            snapshots.Add(snap.Population);
        _sparkline.SetData(snapshots, SparkPoints);
    }

    /// <summary>
    /// Refresh the panel from server state (viewer mode).
    /// Call each frame while the panel is visible in viewer mode.
    /// </summary>
    public void UpdateFromState(SharedState state)
    {
        SetTrendRow(_popValueLabel,     _popTrendLabel,
            $"{state.Population:N0}", state.PopulationTrend);

        SetTrendRow(_happyValueLabel,   _happyTrendLabel,
            $"{state.Happiness:P0}", state.HappinessTrend);

        SetTrendRow(_balanceValueLabel, _balanceTrendLabel,
            $"${state.Balance:N0}", state.BalanceTrend);

        _peakPopLabel.Text = $"{state.PeakPopulation:N0}";
        _peakBalLabel.Text = $"${state.PeakBalance:N0}";

        SetGrowthRate(state.PopulationGrowthRate);

        // Build sparkline from StatsHistory list
        if (state.StatsHistory != null && state.StatsHistory.Count > 0)
        {
            var pops = new List<float>(state.StatsHistory.Count);
            foreach (var snap in state.StatsHistory)
                pops.Add(snap.Population);
            _sparkline.SetData(pops, SparkPoints);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private void SetTrendRow(Label valueLabel, Label trendLabel, string value, string trend)
    {
        valueLabel.Text = value;
        trendLabel.Text = trend;
        trendLabel.AddThemeColorOverride("font_color", trend switch
        {
            "↑" => GreenColor,
            "↓" => RedColor,
            _   => GreyText,
        });
    }

    private void SetGrowthRate(float rate)
    {
        // rate is fraction/tick; multiply by 100 for percent
        var pct = rate * 100f;
        _growthRateLabel.Text = pct switch
        {
            > 0.05f  => $"+{pct:F1}%/tick",
            < -0.05f => $"{pct:F1}%/tick",
            _        => "~0%/tick",
        };
        _growthRateLabel.AddThemeColorOverride("font_color", pct > 0.05f ? GreenColor
            : pct < -0.05f ? RedColor
            : GreyText);
    }

    // ── Row builders ──────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a trend row: "Label  value  arrow" and returns references to the value and arrow labels.
    /// </summary>
    private (Label valueLabel, Label trendLabel) AddTrendRow(VBoxContainer parent, string name)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        parent.AddChild(row);

        var nameLabel = new Label();
        nameLabel.Text = name;
        nameLabel.CustomMinimumSize = new Vector2(100f, 0f);
        nameLabel.AddThemeColorOverride("font_color", GreyText);
        nameLabel.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(nameLabel);

        var spacer = new Control();
        spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(spacer);

        var valueLabel = new Label();
        valueLabel.Text = "-";
        valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        valueLabel.CustomMinimumSize = new Vector2(80f, 0f);
        valueLabel.AddThemeColorOverride("font_color", WhiteText);
        valueLabel.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(valueLabel);

        var trendLabel = new Label();
        trendLabel.Text = "→";
        trendLabel.HorizontalAlignment = HorizontalAlignment.Center;
        trendLabel.CustomMinimumSize = new Vector2(22f, 0f);
        trendLabel.AddThemeColorOverride("font_color", GreyText);
        trendLabel.AddThemeFontSizeOverride("font_size", 15);
        row.AddChild(trendLabel);

        return (valueLabel, trendLabel);
    }

    /// <summary>
    /// Adds a stat row: "label  value" and returns the value label.
    /// </summary>
    private Label AddStatRow(VBoxContainer parent, string name)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        parent.AddChild(row);

        var nameLabel = new Label();
        nameLabel.Text = name;
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameLabel.AddThemeColorOverride("font_color", GreyText);
        nameLabel.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(nameLabel);

        var valueLabel = new Label();
        valueLabel.Text = "-";
        valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        valueLabel.AddThemeColorOverride("font_color", AmberColor);
        valueLabel.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(valueLabel);

        return valueLabel;
    }

    private static void AddSectionHeader(VBoxContainer parent, string title)
    {
        var lbl = new Label();
        lbl.Text = title.ToUpperInvariant();
        lbl.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.75f));
        lbl.AddThemeFontSizeOverride("font_size", 10);
        parent.AddChild(lbl);
    }

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

// ── Sparkline subcontrol ──────────────────────────────────────────────────────

/// <summary>
/// A simple Control that draws a mini population sparkline using DrawLine.
/// Data is set via SetData() and QueueRedraw() is called automatically.
/// </summary>
public partial class SparklineControl : Control
{
    private readonly float  _w;
    private readonly float  _h;
    private readonly Color  _bgColor;
    private readonly Color  _lineColor;
    private float[]         _points = System.Array.Empty<float>();

    public SparklineControl(float width, float height, Color bg, Color line)
    {
        _w         = width;
        _h         = height;
        _bgColor   = bg;
        _lineColor = line;
        CustomMinimumSize = new Vector2(_w, _h);
    }

    /// <summary>
    /// Supply the full rolling history list; only the last <paramref name="maxPoints"/> entries are rendered.
    /// </summary>
    public void SetData(List<float> history, int maxPoints)
    {
        var count = Math.Min(history.Count, maxPoints);
        var start = history.Count - count;
        _points = new float[count];
        for (var i = 0; i < count; i++)
            _points[i] = history[start + i];
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Background
        DrawRect(new Rect2(0, 0, _w, _h), _bgColor);

        if (_points.Length < 2) return;

        // Find min/max for normalisation
        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in _points) { if (v < min) min = v; if (v > max) max = v; }

        // Avoid division by zero when all values are identical
        var range = max - min;
        if (range < 1f) range = 1f;

        var n = _points.Length;
        var prev = Vector2.Zero;

        for (var i = 0; i < n; i++)
        {
            var x = _w * i / (n - 1);
            var y = _h - (_points[i] - min) / range * (_h - 6f) - 3f; // 3 px vertical padding
            var pt = new Vector2(x, y);

            if (i > 0)
                DrawLine(prev, pt, _lineColor, 1.5f, true);

            prev = pt;
        }

        // Final dot
        DrawCircle(prev, 2.5f, _lineColor);
    }
}
