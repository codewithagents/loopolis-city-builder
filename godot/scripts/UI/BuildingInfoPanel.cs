using Godot;
using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace LoopolisGodot;

/// <summary>
/// Floating info card that appears when the player clicks on a placed building.
/// Shows detailed stats: name, size, milestone, population, happiness, power,
/// service coverage, pollution, and upgrade hint.
///
/// Positioning: 40px right of the clicked tile; flips left if it would clip the screen edge.
/// Always clamps vertically to stay within viewport bounds.
///
/// Dismiss by clicking elsewhere, clicking the same building, or pressing Escape.
/// Layer = 12 (above tooltip at 11, below policy/shortcuts/gameover panels).
/// </summary>
public partial class BuildingInfoPanel : CanvasLayer
{
    // ── Panel size hint — layout is dynamic, but we need a width for flip-logic ──
    private const float PanelWidth   = 290f;
    private const float PanelHeight  = 210f;  // approximate; actual size depends on content
    private const float TileOffset   = 40f;   // px to the right of tile world position

    // ── Colours ──────────────────────────────────────────────────────────────────
    private static readonly Color ColHeader    = new(0.95f, 0.95f, 0.95f);
    private static readonly Color ColSubtitle  = new(0.65f, 0.65f, 0.70f);
    private static readonly Color ColGood      = new(0.30f, 1.00f, 0.40f);
    private static readonly Color ColWarn      = new(1.00f, 0.75f, 0.15f);
    private static readonly Color ColBad       = new(1.00f, 0.30f, 0.30f);
    private static readonly Color ColNeutral   = new(0.80f, 0.80f, 0.80f);
    private static readonly Color ColAmber     = new(1.00f, 0.85f, 0.20f);
    private static readonly Color ColSep       = new(0.40f, 0.40f, 0.40f, 0.60f);

    // ── UI nodes ─────────────────────────────────────────────────────────────────
    private PanelContainer _panel = null!;
    private VBoxContainer  _vbox  = null!;

    // ── State ─────────────────────────────────────────────────────────────────────
    /// <summary>The building ID currently shown, or null when the panel is hidden.</summary>
    public string? ShownBuildingId { get; private set; }

    // ── Friendly name lookup (same set as TileTooltip) ───────────────────────────
    private static string GetFriendlyName(string typeId) => typeId switch
    {
        "res_house_1x1"       => "Cottage",
        "res_townhouse_2x2"   => "Townhouse",
        "res_villa_2x3"       => "Forest Villa",
        "res_villa_3x2"       => "Forest Villa",
        "res_villa_hillside_3x3" => "Hillside Villa",
        "res_apartment_4x4"   => "Apartment Block",
        "res_highrise_6x6"    => "Highrise",
        "com_shop_1x1"        => "Corner Shop",
        "com_strip_1x3"       => "Strip Mall",
        "com_strip_3x1"       => "Strip Mall",
        "com_shopping_3x3"    => "Shopping Centre",
        "com_office_4x4"      => "Office Tower",
        "ind_factory_1x1"     => "Factory",
        "ind_warehouse_2x2"   => "Warehouse",
        "ind_mill_2x2"        => "Timber Mill",
        "ind_quarry_2x2"      => "Quarry",
        "ind_complex_4x4"     => "Industrial Complex",
        "ind_park_4x2"        => "Industrial Park",
        "ind_park_2x4"        => "Industrial Park",
        _                     => FormatTypeId(typeId),
    };

    private static Color ZoneColor(ZoneType zone) => zone switch
    {
        ZoneType.Residential => new Color(0.30f, 1.00f, 0.40f),
        ZoneType.Commercial  => new Color(0.40f, 0.60f, 1.00f),
        ZoneType.Industrial  => new Color(1.00f, 0.90f, 0.20f),
        _                    => new Color(0.80f, 0.80f, 0.80f),
    };

    private static string MilestoneLabel(GameState ms) => ms switch
    {
        GameState.Town       => "Town",
        GameState.City       => "City",
        GameState.Metropolis => "Metropolis",
        GameState.Loopolis   => "Loopolis",
        _                    => "Always",
    };

    // ── Manual upgrade table (mirrors ManualUpgradeSystem + TileTooltip) ─────────
    private static readonly System.Collections.Generic.Dictionary<string, (int Cost, string TargetName)> UpgradeInfo
        = new()
    {
        { "res_house_1x1",     (600,   "Townhouse")        },
        { "res_townhouse_2x2", (1_200, "Apartment Block")  },
        { "res_apartment_4x4", (3_500, "Highrise")         },
        { "com_shop_1x1",      (500,   "Strip Mall")       },
        { "com_strip_1x3",     (1_000, "Shopping Centre")  },
        { "com_strip_3x1",     (1_000, "Shopping Centre")  },
        { "com_shopping_3x3",  (2_200, "Office Tower")     },
        { "ind_factory_1x1",   (700,   "Warehouse")        },
        { "ind_warehouse_2x2", (2_000, "Industrial Park")  },
        { "ind_mill_2x2",      (1_500, "Industrial Park")  },
        { "ind_quarry_2x2",    (1_800, "Industrial Park")  },
    };

    // ── Godot lifecycle ───────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Layer = 12;  // above tooltip (11), below policy/shortcuts panels (14+)

        _panel = new PanelContainer();
        _panel.Visible = false;
        _panel.MouseFilter = Control.MouseFilterEnum.Stop; // block clicks from reaching world
        _panel.AddThemeStyleboxOverride("panel", MakePanelStyle());
        AddChild(_panel);

        _vbox = new VBoxContainer();
        _vbox.AddThemeConstantOverride("separation", 2);
        _panel.AddChild(_vbox);
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Show the panel for a building in standalone mode.
    /// If the same building is clicked again the panel is toggled closed.
    /// </summary>
    public void ShowForBuilding(
        Building building,
        CityGrid grid,
        SimulationEngine engine,
        Vector2 worldPos,       // world-space top-left corner of the anchor tile
        Vector2 screenSize)
    {
        if (ShownBuildingId == building.Id)
        {
            // Same building clicked — toggle off
            ClosePanel();
            return;
        }

        ShownBuildingId = building.Id;
        PopulatePanel(building, grid, engine, null);
        PositionPanel(worldPos, screenSize);
        _panel.Visible = true;
    }

    /// <summary>
    /// Show the panel for a building in viewer mode.
    /// Only basic info is shown (population, zone, power) from SharedState data.
    /// </summary>
    public void ShowForBuildingViewer(
        BuildingInfo buildingInfo,
        SharedState state,
        Vector2 worldPos,
        Vector2 screenSize)
    {
        if (ShownBuildingId == buildingInfo.Id)
        {
            ClosePanel();
            return;
        }

        ShownBuildingId = buildingInfo.Id;
        PopulatePanelViewer(buildingInfo, state);
        PositionPanel(worldPos, screenSize);
        _panel.Visible = true;
    }

    /// <summary>Close and reset the panel.</summary>
    public void ClosePanel()
    {
        ShownBuildingId = null;
        _panel.Visible = false;
    }

    /// <summary>True when the panel is currently visible.</summary>
    public new bool IsVisible => _panel?.Visible ?? false;

    // ── Panel content — standalone mode ──────────────────────────────────────────

    private void PopulatePanel(Building building, CityGrid grid, SimulationEngine engine, SharedState? state)
    {
        ClearContent();

        var def         = BuildingCatalog.Find(building.TypeId);
        var anchorTile  = grid.GetTile(building.AnchorX, building.AnchorY);
        var zoneColor   = ZoneColor(building.Zone);

        // ── Title: friendly name ──────────────────────────────────────────────
        var friendlyName = GetFriendlyName(building.TypeId);
        AddLabel(friendlyName, 15, zoneColor);

        // ── Subtitle: size · milestone ────────────────────────────────────────
        var sizeStr      = $"{building.Width}×{building.Height}";
        var milestone    = def != null ? MilestoneLabel(def.MinMilestone) : "—";
        AddLabel($"{sizeStr}  ·  Requires: {milestone}", 12, ColSubtitle);

        AddSeparator();

        // ── Population (residential only) ─────────────────────────────────────
        if (building.Zone == ZoneType.Residential)
        {
            var totalPop  = 0;
            foreach (var (bx, by) in building.Tiles())
                if (grid.IsInBounds(bx, by))
                    totalPop += grid.GetTile(bx, by).Population;

            var capacity = building.MaxPopulation;
            var popColor = totalPop > capacity
                ? ColWarn                       // over-capacity — amber
                : totalPop >= capacity * 0.8
                    ? ColGood                   // 80%+ — ready to upgrade
                    : ColNeutral;
            AddRow("Population", $"{totalPop} / {capacity}", popColor);
        }

        // ── Happiness ─────────────────────────────────────────────────────────
        var happiness    = (float)anchorTile.Happiness;
        var happinessStr = $"{happiness:F2}  {HappinessEmoji(happiness)}";
        var happColor    = happiness >= 0.70f ? ColGood
                         : happiness >= 0.40f ? ColWarn
                                              : ColBad;
        AddRow("Happiness", happinessStr, happColor);

        // ── Power ─────────────────────────────────────────────────────────────
        var powerStr   = anchorTile.HasPower ? "Powered" : "No power";
        var powerColor = anchorTile.HasPower ? ColGood : ColBad;
        AddRow("Power", (anchorTile.HasPower ? "✓ " : "✗ ") + powerStr, powerColor);

        // ── Service coverage (use city-wide aggregate from engine) ────────────
        var sc = engine.LastServiceCoverage ?? ServiceCoverageResult.Empty;

        AddSeparator();
        AddLabel("Service coverage (city-wide)", 12, ColSubtitle);

        AddServiceRow("Fire",     sc.FireCoveragePercent);
        AddServiceRow("Police",   sc.PoliceCoveragePercent);
        AddServiceRow("School",   sc.SchoolCoveragePercent);
        AddServiceRow("Hospital", sc.HospitalCoveragePercent);

        // ── Pollution (industrial and nearby residential) ─────────────────────
        var pollution = (float)anchorTile.PollutionLevel;
        if (pollution > 0.01f)
        {
            AddSeparator();
            var pollPct   = (int)(pollution * 100);
            var pollColor = pollPct >= 60 ? ColBad
                          : pollPct >= 25 ? ColWarn
                                          : ColNeutral;
            AddRow("Pollution", $"{pollPct}%{(pollPct >= 25 ? " !" : "")}", pollColor);
        }

        // ── Jobs (industrial buildings) ────────────────────────────────────────
        if (building.Zone == ZoneType.Industrial)
        {
            var tileCount = building.Width * building.Height;
            var jobs = anchorTile.HasPower ? tileCount * 20 : tileCount * 2;
            AddSeparator();
            AddRow("Jobs provided", $"~{jobs}", ColNeutral);
        }

        // ── Upgrade hint ──────────────────────────────────────────────────────
        if (UpgradeInfo.TryGetValue(building.TypeId, out var upgradeEntry))
        {
            AddSeparator();
            var canAfford = engine.Budget.Balance >= upgradeEntry.Cost;
            var upgradeColor = canAfford ? ColAmber : ColBad;
            AddLabel($"Upgrade → {upgradeEntry.TargetName}", 13, upgradeColor);
            AddLabel($"Cost: ${upgradeEntry.Cost:N0}  (press G then click)", 12,
                canAfford ? new Color(0.80f, 0.70f, 0.35f) : ColBad);
        }
    }

    // ── Panel content — viewer mode ───────────────────────────────────────────────

    private void PopulatePanelViewer(BuildingInfo info, SharedState state)
    {
        ClearContent();

        if (!System.Enum.TryParse<ZoneType>(info.Zone, out var zone))
            zone = ZoneType.Empty;
        var zoneColor   = ZoneColor(zone);
        var friendlyName = GetFriendlyName(info.TypeId);

        // Title
        AddLabel(friendlyName, 15, zoneColor);
        AddLabel($"{info.Width}×{info.Height}  ·  {info.Zone}", 12, ColSubtitle);

        AddSeparator();

        // Population from building tiles in state.Tiles (sum tiles where BuildingId == info.Id)
        if (zone == ZoneType.Residential)
        {
            var totalPop = 0;
            foreach (var t in state.Tiles)
                if (t.BuildingId == info.Id)
                    totalPop += t.Population;
            var capacity = info.Width * info.Height * 50;
            var popColor = totalPop >= capacity * 0.8 ? ColGood : ColNeutral;
            AddRow("Population", $"{totalPop} / {capacity}", popColor);
        }

        // Power from anchor tile
        var anchorShared = state.GetTile(info.X, info.Y);
        if (anchorShared != null)
        {
            var hasPower   = anchorShared.HasPower;
            var powerColor = hasPower ? ColGood : ColBad;
            AddRow("Power", (hasPower ? "✓ Powered" : "✗ No power"), powerColor);

            var happiness    = anchorShared.Happiness;
            var happColor    = happiness >= 0.70f ? ColGood : happiness >= 0.40f ? ColWarn : ColBad;
            AddRow("Happiness", $"{happiness:F2}  {HappinessEmoji(happiness)}", happColor);

            if (anchorShared.PollutionLevel > 0.01f)
            {
                AddSeparator();
                var pollPct   = (int)(anchorShared.PollutionLevel * 100);
                var pollColor = pollPct >= 60 ? ColBad : pollPct >= 25 ? ColWarn : ColNeutral;
                AddRow("Pollution", $"{pollPct}%", pollColor);
            }
        }

        AddSeparator();
        AddLabel("Detailed stats: use standalone mode", 11, ColSubtitle);
    }

    // ── Positioning ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Position the panel near a tile's world-space coordinate.
    /// worldPos should be the top-left pixel of the anchor tile (AnchorX * TileSize, AnchorY * TileSize).
    /// The camera transform maps world coordinates to screen coordinates.
    /// </summary>
    private void PositionPanel(Vector2 worldPos, Vector2 screenSize)
    {
        // worldPos is already in screen space (callers pass the transformed position)
        var x = worldPos.X + TileOffset;
        var y = worldPos.Y;

        // Flip left if right edge would clip
        if (x + PanelWidth > screenSize.X - 10f)
            x = worldPos.X - PanelWidth - TileOffset;

        // Clamp vertically
        y = Mathf.Clamp(y, 10f, screenSize.Y - PanelHeight - 10f);

        _panel.Position = new Vector2(x, y);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private void ClearContent()
    {
        foreach (Node child in _vbox.GetChildren())
            child.QueueFree();
    }

    private void AddLabel(string text, int fontSize, Color color)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        _vbox.AddChild(label);
    }

    /// <summary>Adds a two-column row: left label key, right label value.</summary>
    private void AddRow(string key, string value, Color valueColor)
    {
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 6);

        var keyLabel = new Label();
        keyLabel.Text = key;
        keyLabel.AddThemeFontSizeOverride("font_size", 13);
        keyLabel.AddThemeColorOverride("font_color", ColSubtitle);
        keyLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(keyLabel);

        var valLabel = new Label();
        valLabel.Text = value;
        valLabel.AddThemeFontSizeOverride("font_size", 13);
        valLabel.AddThemeColorOverride("font_color", valueColor);
        valLabel.HorizontalAlignment = HorizontalAlignment.Right;
        hbox.AddChild(valLabel);

        _vbox.AddChild(hbox);
    }

    private void AddServiceRow(string name, float coveragePercent)
    {
        var pct   = (int)(coveragePercent * 100);
        var color = pct >= 60 ? ColGood : pct >= 20 ? ColWarn : ColBad;
        var check = pct >= 20 ? "✓" : "✗";
        AddRow(name, $"{check} {pct}%", color);
    }

    private void AddSeparator()
    {
        var sep = new HSeparator();
        sep.AddThemeColorOverride("color", ColSep);
        _vbox.AddChild(sep);
    }

    private static string HappinessEmoji(float h) =>
        h >= 0.80f ? "😊" : h >= 0.55f ? "😐" : h >= 0.35f ? "😟" : "😠";

    /// <summary>
    /// Fallback name formatter for unknown building TypeIds.
    /// Converts "res_townhouse_2x2" → "Townhouse 2×2".
    /// </summary>
    private static string FormatTypeId(string typeId)
    {
        var withoutPrefix = typeId;
        foreach (var prefix in new[] { "res_", "com_", "ind_" })
        {
            if (typeId.StartsWith(prefix))
            {
                withoutPrefix = typeId[prefix.Length..];
                break;
            }
        }
        var parts = withoutPrefix.Split('_');
        if (parts.Length > 1)
        {
            var last = parts[^1];
            if (last.Contains('x') && last.Length <= 5)
                parts = parts[..^1];
        }
        return string.Join(" ",
            System.Array.ConvertAll(parts,
                p => p.Length == 0 ? p : char.ToUpper(p[0]) + p[1..]));
    }

    private static StyleBoxFlat MakePanelStyle()
    {
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.92f);
        style.ContentMarginLeft   = 10;
        style.ContentMarginRight  = 10;
        style.ContentMarginTop    = 8;
        style.ContentMarginBottom = 8;
        style.CornerRadiusTopLeft     = 5;
        style.CornerRadiusTopRight    = 5;
        style.CornerRadiusBottomLeft  = 5;
        style.CornerRadiusBottomRight = 5;
        // Subtle border
        style.BorderColor      = new Color(0.35f, 0.35f, 0.45f, 0.70f);
        style.BorderWidthLeft  = 1;
        style.BorderWidthRight = 1;
        style.BorderWidthTop   = 1;
        style.BorderWidthBottom = 1;
        return style;
    }
}
