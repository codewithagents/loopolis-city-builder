using Godot;
using System.Collections.Generic;

namespace LoopolisGodot;

/// <summary>
/// City Health warning strip — anchored to the top-right of the screen.
/// Shows a stacked list of contextual warnings derived from the current SharedState.
/// Each warning appears when its condition becomes true and disappears when resolved.
///
/// Also renders a happiness breakdown tooltip that appears on hover over the
/// happiness indicator row.
///
/// Layer = 10 (same as HudOverlay — sits beside it, never overlaps the map).
/// </summary>
public partial class CityHealthPanel : CanvasLayer
{
    // ── Warning definitions ─────────────────────────────────────────────────

    private enum WarningId
    {
        Bankruptcy,
        Abandonment,
        BudgetCrisis,
        Unemployment,
        Brownout,
        Unpowered,
        HappinessCritical,
        FireCoverage,
        PoliceCoverage,
        NoRoadAccess,
    }

    private static readonly Color WarnRed    = new(1f,  0.3f, 0.3f);
    private static readonly Color WarnOrange = new(1f,  0.65f, 0.1f);
    private static readonly Color WarnYellow = new(1f,  0.95f, 0.3f);
    private static readonly Color WarnCyan   = new(0.3f, 0.95f, 1f);

    // ── UI nodes ───────────────────────────────────────────────────────────

    private PanelContainer _warningPanel  = null!;
    private VBoxContainer  _warningVBox   = null!;
    private PanelContainer _tooltipPanel  = null!;
    private Label          _tooltipLabel  = null!;

    // Per-warning labels — only created once, hidden/shown as needed
    private readonly Dictionary<WarningId, Label> _warningLabels = new();

    // Track which warnings are currently active (for change detection)
    private readonly HashSet<WarningId> _activeWarnings = new();

    // Happiness bar row — hover area for the breakdown tooltip
    private Control _happinessHoverArea = null!;
    private bool    _tooltipVisible     = false;

    // Pulse animation
    private double _pulseTimer = 0;
    private const double PulsePeriod = 1.4; // seconds for one full pulse

    public override void _Ready()
    {
        Layer = 10;

        BuildWarningPanel();
        BuildTooltipPanel();
        BuildHappinessHoverArea();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called each tick (standalone) or each poll (viewer mode) with the
    /// latest simulation state. Recomputes all warnings and updates visibility.
    /// </summary>
    public void UpdateWarnings(SharedState state)
    {
        var newActive = ComputeActiveWarnings(state);

        foreach (var id in _warningLabels.Keys)
        {
            var shouldShow = newActive.Contains(id);
            _warningLabels[id].Visible = shouldShow;
        }

        _activeWarnings.Clear();
        foreach (var id in newActive) _activeWarnings.Add(id);

        // Show/hide the entire panel
        _warningPanel.Visible = _activeWarnings.Count > 0;

        // Update tooltip content
        UpdateHappinessTooltip(state);
    }

    // ── Warning computation ────────────────────────────────────────────────

    private HashSet<WarningId> ComputeActiveWarnings(SharedState state)
    {
        var result = new HashSet<WarningId>();

        // Emergency warnings (PauseReason)
        if (state.PauseReason == "BankruptcyWarning")
            result.Add(WarningId.Bankruptcy);

        if (state.PauseReason == "AbandonmentWarning")
            result.Add(WarningId.Abandonment);

        // Budget crisis: in the red and burning at least $20/tick
        if (state.Balance < 0 && state.NetPerTick < -20)
        {
            result.Add(WarningId.BudgetCrisis);
            UpdateLabel(WarningId.BudgetCrisis,
                $"💸 Budget crisis — spending ${System.Math.Abs((int)state.NetPerTick)}/tick more than income");
        }

        // Unemployment: >50% gap AND large enough population to matter
        if (state.Employment != null &&
            state.Employment.UnemploymentRate > 0.5 &&
            state.Population > 100)
        {
            result.Add(WarningId.Unemployment);
            var pct = (int)(state.Employment.UnemploymentRate * 100);
            UpdateLabel(WarningId.Unemployment,
                $"⚠ Jobs shortage ({pct}% gap) — build more Industrial zones");
        }

        // Brownout: power supply below demand
        if (state.Power != null && state.Power.IsBrownout)
        {
            result.Add(WarningId.Brownout);
            UpdateLabel(WarningId.Brownout,
                $"⚡ Power brownout — add more plants ({state.Power.SupplyMW}/{state.Power.DemandMW} MW)");
        }

        // Unpowered zones
        if (state.CoverageSummary != null && state.CoverageSummary.UnpoweredZonedTilesCount > 0)
        {
            result.Add(WarningId.Unpowered);
            UpdateLabel(WarningId.Unpowered,
                $"⚡ {state.CoverageSummary.UnpoweredZonedTilesCount} zones unpowered");
        }

        // Happiness critical (<35%)
        if (state.Happiness < 0.35)
        {
            result.Add(WarningId.HappinessCritical);
            var pct = (int)(state.Happiness * 100);
            UpdateLabel(WarningId.HappinessCritical,
                $"😟 Happiness critical ({pct}%) — check services & events");
        }

        // Fire coverage low (below 30% and meaningful population)
        if (state.CoverageSummary != null &&
            state.CoverageSummary.FireCoveragePercent < 0.3 &&
            state.Population > 200)
        {
            result.Add(WarningId.FireCoverage);
            var pct = (int)(state.CoverageSummary.FireCoveragePercent * 100);
            UpdateLabel(WarningId.FireCoverage,
                $"🔥 Fire coverage low ({pct}%) — add Fire Stations");
        }

        // Police coverage low (below 30% and meaningful population)
        if (state.CoverageSummary != null &&
            state.CoverageSummary.PoliceCoveragePercent < 0.3 &&
            state.Population > 200)
        {
            result.Add(WarningId.PoliceCoverage);
            var pct = (int)(state.CoverageSummary.PoliceCoveragePercent * 100);
            UpdateLabel(WarningId.PoliceCoverage,
                $"👮 Police coverage low ({pct}%) — add Police Stations");
        }

        // Road-less zones: zoned tiles that cost maintenance but can never develop
        var noRoadCount = 0;
        foreach (var tile in state.Tiles)
        {
            if (!tile.HasRoadAccess &&
                (tile.Zone == "Residential" || tile.Zone == "Commercial" || tile.Zone == "Industrial"))
                noRoadCount++;
        }
        if (noRoadCount > 3)
        {
            result.Add(WarningId.NoRoadAccess);
            var wasteCost = (int)(noRoadCount * 0.25);
            UpdateLabel(WarningId.NoRoadAccess,
                $"⚠ {noRoadCount} zones have no road access — wasting ${wasteCost}/tick");
        }

        return result;
    }

    private void UpdateLabel(WarningId id, string text)
    {
        if (_warningLabels.TryGetValue(id, out var lbl))
            lbl.Text = text;
    }

    // ── Pulse animation ────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_activeWarnings.Count == 0) return;

        _pulseTimer += delta;
        if (_pulseTimer > PulsePeriod) _pulseTimer -= PulsePeriod;

        // Gentle sine pulse: alpha oscillates between 0.75 and 1.0
        var t = (float)(_pulseTimer / PulsePeriod);
        var alpha = 0.75f + 0.25f * Mathf.Sin(t * Mathf.Tau);

        // Only pulse emergency warnings; informational ones stay solid
        if (_warningLabels.TryGetValue(WarningId.Bankruptcy, out var bankLabel) && bankLabel.Visible)
            bankLabel.Modulate = new Color(1f, 1f, 1f, alpha);
        if (_warningLabels.TryGetValue(WarningId.Abandonment, out var abandLabel) && abandLabel.Visible)
            abandLabel.Modulate = new Color(1f, 1f, 1f, alpha);
    }

    // ── Build warning panel ────────────────────────────────────────────────

    private void BuildWarningPanel()
    {
        _warningPanel = new PanelContainer();
        _warningPanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _warningPanel.GrowHorizontal = Control.GrowDirection.Begin;
        _warningPanel.OffsetRight    = -8f;
        _warningPanel.OffsetTop      = 8f;
        _warningPanel.AddThemeStyleboxOverride("panel", MakeWarningPanelStyle());
        _warningPanel.Visible = false;
        AddChild(_warningPanel);

        _warningVBox = new VBoxContainer();
        _warningVBox.AddThemeConstantOverride("separation", 3);
        _warningPanel.AddChild(_warningVBox);

        // Create one label per warning ID, all hidden initially.
        // Order determines display order (top = most critical).
        AddWarningLabel(WarningId.Bankruptcy,      "🚨 Bankruptcy imminent!",           WarnRed);
        AddWarningLabel(WarningId.Abandonment,     "🚨 Abandonment imminent — happiness collapsed!", WarnRed);
        AddWarningLabel(WarningId.BudgetCrisis,    "💸 Budget crisis",                  WarnOrange);
        AddWarningLabel(WarningId.Unemployment,    "⚠ Jobs shortage",                  WarnOrange);
        AddWarningLabel(WarningId.Brownout,        "⚡ Power brownout — add more plants", WarnOrange);
        AddWarningLabel(WarningId.Unpowered,       "⚡ Zones unpowered",               WarnYellow);
        AddWarningLabel(WarningId.HappinessCritical, "😟 Happiness critical",           WarnOrange);
        AddWarningLabel(WarningId.FireCoverage,    "🔥 Fire coverage low",             WarnYellow);
        AddWarningLabel(WarningId.PoliceCoverage,  "👮 Police coverage low",           WarnCyan);
        AddWarningLabel(WarningId.NoRoadAccess,    "⚠ Zones with no road access",     WarnOrange);
    }

    private void AddWarningLabel(WarningId id, string defaultText, Color color)
    {
        var lbl = new Label();
        lbl.Text = defaultText;
        lbl.Visible = false;
        lbl.AddThemeColorOverride("font_color", color);
        lbl.AddThemeFontSizeOverride("font_size", 15);
        _warningVBox.AddChild(lbl);
        _warningLabels[id] = lbl;
    }

    // ── Happiness tooltip ──────────────────────────────────────────────────

    private void BuildTooltipPanel()
    {
        _tooltipPanel = new PanelContainer();
        _tooltipPanel.AddThemeStyleboxOverride("panel", MakeTooltipPanelStyle());
        _tooltipPanel.Visible = false;
        // Position is set dynamically in UpdateTooltipPosition
        AddChild(_tooltipPanel);

        _tooltipLabel = new Label();
        _tooltipLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
        _tooltipLabel.AddThemeFontSizeOverride("font_size", 14);
        _tooltipPanel.AddChild(_tooltipLabel);
    }

    private void UpdateHappinessTooltip(SharedState state)
    {
        if (state.HappinessBreakdown == null)
        {
            _tooltipLabel.Text = "";
            return;
        }

        var bd = state.HappinessBreakdown;
        var lines = new System.Text.StringBuilder();
        lines.AppendLine("Happiness breakdown:");
        lines.AppendLine($"  Service coverage:    {FormatPct(bd.ServiceCoverage)}");
        lines.AppendLine($"  Tax modifier:        {FormatPct(bd.TaxModifier)}");
        lines.AppendLine($"  Unemployment penalty:{FormatPct(bd.UnemploymentPenalty)}");
        lines.AppendLine($"  Event penalty:       {FormatPct(bd.EventPenalty)}");
        lines.Append(    $"  Neglect decay:       {FormatPct(bd.NeglectDecay)}");
        _tooltipLabel.Text = lines.ToString();
    }

    private static string FormatPct(double value)
    {
        var pct = (int)(value * 100);
        return pct >= 0 ? $"+{pct}%" : $"{pct}%";
    }

    // ── Happiness hover area ───────────────────────────────────────────────

    private void BuildHappinessHoverArea()
    {
        // Invisible Control node that follows the happiness label region in the HUD.
        // It uses mouse signals to show/hide the breakdown tooltip.
        // We anchor it below the warning panel in the top-left (where happiness label lives).
        _happinessHoverArea = new Control();
        _happinessHoverArea.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _happinessHoverArea.Position = new Vector2(8, 148); // approximate y of happiness row in HUD
        _happinessHoverArea.CustomMinimumSize = new Vector2(200, 22);
        _happinessHoverArea.Size = new Vector2(200, 22);
        _happinessHoverArea.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_happinessHoverArea);

        _happinessHoverArea.MouseEntered += () =>
        {
            _tooltipVisible = true;
            _tooltipPanel.Visible = true;
            PositionTooltip();
        };
        _happinessHoverArea.MouseExited += () =>
        {
            _tooltipVisible = false;
            _tooltipPanel.Visible = false;
        };
    }

    private void PositionTooltip()
    {
        // Place the tooltip to the right of the hover area (beside the HUD panel)
        _tooltipPanel.Position = new Vector2(220, 120);
    }

    // ── Style helpers ──────────────────────────────────────────────────────

    private static StyleBoxFlat MakeWarningPanelStyle()
    {
        var s = new StyleBoxFlat();
        s.BgColor = new Color(0.05f, 0.02f, 0.02f, 0.82f);
        s.BorderColor = new Color(0.8f, 0.3f, 0.1f, 0.7f);
        s.BorderWidthTop    = 1;
        s.BorderWidthBottom = 1;
        s.BorderWidthLeft   = 1;
        s.BorderWidthRight  = 1;
        s.ContentMarginLeft   = 10;
        s.ContentMarginRight  = 10;
        s.ContentMarginTop    = 6;
        s.ContentMarginBottom = 6;
        s.CornerRadiusTopLeft     = 4;
        s.CornerRadiusTopRight    = 4;
        s.CornerRadiusBottomLeft  = 4;
        s.CornerRadiusBottomRight = 4;
        return s;
    }

    private static StyleBoxFlat MakeTooltipPanelStyle()
    {
        var s = new StyleBoxFlat();
        s.BgColor = new Color(0f, 0f, 0f, 0.88f);
        s.BorderColor = new Color(0.5f, 0.5f, 0.7f, 0.9f);
        s.BorderWidthTop    = 1;
        s.BorderWidthBottom = 1;
        s.BorderWidthLeft   = 1;
        s.BorderWidthRight  = 1;
        s.ContentMarginLeft   = 10;
        s.ContentMarginRight  = 10;
        s.ContentMarginTop    = 7;
        s.ContentMarginBottom = 7;
        s.CornerRadiusTopLeft     = 4;
        s.CornerRadiusTopRight    = 4;
        s.CornerRadiusBottomLeft  = 4;
        s.CornerRadiusBottomRight = 4;
        return s;
    }
}
