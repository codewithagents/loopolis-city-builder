using Godot;
using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace LoopolisGodot;

/// <summary>
/// Floating tooltip shown near the cursor when hovering over a non-empty tile.
/// Sits on CanvasLayer 11 (above HUD at 10, below GameOverPanel at 15).
/// </summary>
public partial class TileTooltip : CanvasLayer
{
    private PanelContainer _panel = null!;
    private VBoxContainer  _vbox  = null!;

    public override void _Ready()
    {
        Layer = 11;

        _panel = new PanelContainer();
        _panel.Visible = false;
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.AddThemeStyleboxOverride("panel", MakePanelStyle());
        AddChild(_panel);

        _vbox = new VBoxContainer();
        _vbox.AddThemeConstantOverride("separation", 1);
        _panel.AddChild(_vbox);
    }

    /// <summary>
    /// Show a unified tooltip for a multi-tile building. Hides per-tile
    /// road/power connectivity lines because the building already exists and
    /// was road-connected when it grew.
    /// </summary>
    public void ShowForBuilding(Building building, int totalPop, Tile anchorTile, Vector2 screenPos, SharedState? state = null)
    {
        foreach (Node child in _vbox.GetChildren())
            child.QueueFree();

        // Pretty-print the TypeId, e.g. "res_townhouse_2x2" → "Townhouse 2×2"
        var displayName = PrettifyTypeId(building.TypeId, building.Width, building.Height);

        var zoneColor = building.Zone switch
        {
            ZoneType.Residential => new Color(0.3f, 1f, 0.4f),
            ZoneType.Commercial  => new Color(0.4f, 0.6f, 1f),
            ZoneType.Industrial  => new Color(1f, 0.9f, 0.2f),
            _                    => new Color(0.9f, 0.9f, 0.9f),
        };

        AddLine(displayName, 15, zoneColor);

        var zoneLabel = building.Zone switch
        {
            ZoneType.Residential => "Residential",
            ZoneType.Commercial  => "Commercial",
            ZoneType.Industrial  => "Industrial",
            _                    => building.Zone.ToString(),
        };
        AddLine(zoneLabel, 12, zoneColor * 0.85f);

        var capacity = building.MaxPopulation;
        if (building.Zone == ZoneType.Residential)
            AddLine($"Pop: {totalPop} / {capacity}", 13, new Color(0.8f, 0.8f, 0.8f));

        // Show power status (building-wide — use anchor tile's HasPower)
        var powerText  = anchorTile.HasPower ? "Power: ✓" : "Power: ✗";
        var powerColor = anchorTile.HasPower ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f);
        AddLine(powerText, 13, powerColor);

        // Growth / happiness notes for residential buildings
        if (building.Zone == ZoneType.Residential)
        {
            var demandFactor = anchorTile.HasDemandBoost ? 1.5f : 1.0f;
            var growthMultiplier = demandFactor * (float)anchorTile.Happiness;
            AddLine($"Growth: {growthMultiplier:F1}×", 13, new Color(0.8f, 0.8f, 0.8f));

            if (anchorTile.Happiness < 0.5f)
                AddLine("⚠ Pollution reducing growth", 12, new Color(1f, 0.4f, 0.2f));
            else if (anchorTile.HasDemandBoost)
                AddLine("✓ Commercial demand boost", 12, new Color(0.3f, 1f, 0.3f));
            else if (anchorTile.Happiness > 0.9f)
                AddLine("★ High happiness", 12, new Color(1f, 0.9f, 0.3f));

            // Growth checklist — show upgrade requirements for existing buildings
            AddGrowthChecklist(building.TypeId, totalPop, capacity, anchorTile, state);
        }

        PositionAndShow(screenPos);
    }

    /// <summary>
    /// Populate and show the tooltip next to the given screen position.
    /// Only called for bare zoned tiles with no building (BuildingId == null).
    /// </summary>
    public void ShowFor(Tile tile, Vector2 screenPos, SharedState? state = null)
    {
        // Clear old labels
        foreach (Node child in _vbox.GetChildren())
            child.QueueFree();

        // Show terrain line at top if not flat
        var terrainName = tile.Terrain switch
        {
            TerrainType.Water  => "Water — cannot build",
            TerrainType.Forest => "Forest — +$75 clearing cost",
            TerrainType.Hill   => "Hill — +$50 build cost, +$0.25/tick",
            _                  => ""
        };
        if (!string.IsNullOrEmpty(terrainName))
            AddLine(terrainName, 12, new Color(0.7f, 0.85f, 0.7f));

        // Build lines based on zone type
        switch (tile.Zone)
        {
            case ZoneType.Residential:
                AddLine("Residential", 15, new Color(0.3f, 1f, 0.4f));
                AddPowerRoadLines(tile);
                AddReadyLine(tile);
                AddLine($"Pop: {tile.Population} / 50", 13, new Color(0.8f, 0.8f, 0.8f));
                AddGrowthLines(tile);
                AddBareTileChecklist(tile, state);
                break;

            case ZoneType.Commercial:
                AddLine("Commercial", 15, new Color(0.4f, 0.6f, 1f));
                AddPowerRoadLines(tile);
                AddReadyLine(tile);
                AddBareTileChecklist(tile, state);
                break;

            case ZoneType.Industrial:
                AddLine("Industrial", 15, new Color(1f, 0.9f, 0.2f));
                AddPowerRoadLines(tile);
                AddReadyLine(tile);
                AddBareTileChecklist(tile, state);
                break;

            case ZoneType.Road:
                AddLine("Road", 15, new Color(0.8f, 0.8f, 0.8f));
                if (tile.TrafficLoad > 0)
                {
                    var loadPct = (int)(tile.TrafficLoad / 8.0 * 100);
                    var loadColor = loadPct >= 100 ? new Color(1f, 0.2f, 0.2f)
                                  : loadPct >= 80  ? new Color(1f, 0.55f, 0.1f)
                                  : loadPct >= 60  ? new Color(1f, 0.95f, 0.2f)
                                                   : new Color(0.7f, 0.7f, 0.7f);
                    AddLine($"Traffic: {loadPct}%{(loadPct >= 100 ? " — OVERLOADED" : "")}", 12, loadColor);
                }
                break;

            case ZoneType.Avenue:
                AddLine("Avenue", 15, new Color(0.8f, 0.8f, 0.8f));
                AddLine("Higher capacity road", 12, new Color(0.65f, 0.65f, 0.65f));
                if (tile.TrafficLoad > 0)
                {
                    var aLoadPct = (int)(tile.TrafficLoad / 16.0 * 100);
                    var aLoadColor = aLoadPct >= 100 ? new Color(1f, 0.2f, 0.2f)
                                   : aLoadPct >= 80  ? new Color(1f, 0.55f, 0.1f)
                                   : aLoadPct >= 60  ? new Color(1f, 0.95f, 0.2f)
                                                     : new Color(0.7f, 0.7f, 0.7f);
                    AddLine($"Traffic: {aLoadPct}%{(aLoadPct >= 100 ? " — OVERLOADED" : "")}", 12, aLoadColor);
                }
                break;

            case ZoneType.PowerLine:
                AddLine("Power Line", 15, new Color(0.2f, 1f, 1f));
                break;

            case ZoneType.PowerPlant:   // legacy alias — same stats as CoalPlant
            case ZoneType.CoalPlant:
                AddLine("Coal Plant", 15, new Color(0.7f, 0.7f, 0.7f));
                AddLine("Output: 500 MW — emits pollution", 13, new Color(0.6f, 0.6f, 0.6f));
                AddLine("Maint: $8.00/tick", 12, new Color(0.65f, 0.65f, 0.65f));
                break;

            case ZoneType.NuclearPlant:
                AddLine("Nuclear Plant", 15, new Color(0.976f, 0.659f, 0.145f));
                AddLine("Output: 3,000 MW — clean energy", 13, new Color(0.9f, 0.85f, 0.5f));
                AddLine("Maint: $50.00/tick", 12, new Color(0.65f, 0.65f, 0.65f));
                break;

            case ZoneType.FireStation:
                AddLine("Fire Station", 15, new Color(1f, 0.5f, 0.2f));
                AddLine("Coverage radius: 4 tiles", 13, new Color(0.8f, 0.6f, 0.4f));
                AddLine("Maint: $3.00/tick", 12, new Color(0.65f, 0.65f, 0.65f));
                break;

            case ZoneType.FireHQ:
                AddLine("Fire HQ", 15, new Color(0.9f, 0.3f, 0.3f));
                AddLine("Coverage radius: 10 tiles", 13, new Color(0.9f, 0.5f, 0.4f));
                AddLine("Maint: $25.00/tick  |  Unlock: City (5,000 pop)", 12, new Color(0.65f, 0.65f, 0.65f));
                break;

            case ZoneType.PoliceStation:
                AddLine("Police Station", 15, new Color(0.4f, 0.6f, 1f));
                AddLine("Coverage radius: 4 tiles", 13, new Color(0.5f, 0.6f, 0.9f));
                AddLine("Maint: $3.00/tick", 12, new Color(0.65f, 0.65f, 0.65f));
                break;

            case ZoneType.PoliceHQ:
                AddLine("Police HQ", 15, new Color(0.4f, 0.5f, 0.9f));
                AddLine("Coverage radius: 10 tiles", 13, new Color(0.5f, 0.6f, 0.9f));
                AddLine("Maint: $25.00/tick  |  Unlock: City (5,000 pop)", 12, new Color(0.65f, 0.65f, 0.65f));
                break;

            case ZoneType.School:
                AddLine("School", 15, new Color(0.8f, 0.4f, 1f));
                AddLine("Coverage radius: 5 tiles", 13, new Color(0.75f, 0.5f, 0.9f));
                AddLine("Maint: $5.00/tick", 12, new Color(0.65f, 0.65f, 0.65f));
                break;

            case ZoneType.Hospital:
                AddLine("Hospital", 15, new Color(0.55f, 0.9f, 0.6f));
                AddLine("Coverage radius: 8 tiles — reduces event damage", 13, new Color(0.5f, 0.85f, 0.55f));
                AddLine("Maint: $35.00/tick  |  Unlock: City (5,000 pop)", 12, new Color(0.65f, 0.65f, 0.65f));
                break;

            default:
                _panel.Visible = false;
                return;
        }

        PositionAndShow(screenPos);
    }

    public new void Hide()
    {
        _panel.Visible = false;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void PositionAndShow(Vector2 screenPos)
    {
        const float offsetX = 16f;
        const float offsetY = -8f;
        _panel.Position = screenPos + new Vector2(offsetX, offsetY);
        _panel.Visible = true;
    }

    private void AddPowerRoadLines(Tile tile)
    {
        var powerText  = tile.HasPower       ? "Power: ✓" : "Power: ✗";
        var roadText   = tile.HasRoadAccess  ? "Road:  ✓" : "Road:  ✗";
        var powerColor = tile.HasPower       ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f);
        var roadColor  = tile.HasRoadAccess  ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f);
        AddLine(powerText, 13, powerColor);
        AddLine(roadText,  13, roadColor);
    }

    private void AddReadyLine(Tile tile)
    {
        if (tile.IsReadyToDevelop)
            AddLine("Ready to develop", 13, new Color(0.3f, 1f, 0.3f));
        else
            AddLine("Not ready — needs power + road", 13, new Color(1f, 0.8f, 0.2f));
    }

    private void AddGrowthLines(Tile tile)
    {
        var demandFactor = tile.HasDemandBoost ? 1.5f : 1.0f;
        var growthMultiplier = demandFactor * (float)tile.Happiness;
        AddLine($"Growth: {growthMultiplier:F1}×", 13, new Color(0.8f, 0.8f, 0.8f));

        if (tile.Happiness < 0.5f)
            AddLine("⚠ Pollution reducing growth", 12, new Color(1f, 0.4f, 0.2f));
        else if (tile.HasDemandBoost)
            AddLine("✓ Commercial demand boost", 12, new Color(0.3f, 1f, 0.3f));
        else if (tile.Happiness > 0.9f)
            AddLine("★ High happiness", 12, new Color(1f, 0.9f, 0.3f));
    }

    // ── Growth checklist ─────────────────────────────────────────────────────

    /// <summary>
    /// Adds a divider and growth checklist section for a bare (no-building) zoned tile.
    /// Shows "No building yet" plus road + power checks, and "Building will appear soon" if both met.
    /// </summary>
    private void AddBareTileChecklist(Tile tile, SharedState? state)
    {
        // Only show for zoned tiles that could develop buildings
        if (tile.Zone != ZoneType.Residential && tile.Zone != ZoneType.Commercial && tile.Zone != ZoneType.Industrial)
            return;

        AddSeparator();
        AddLine("No building yet", 12, new Color(0.9f, 0.7f, 0.2f));

        var roadOk  = tile.HasRoadAccess;
        var powerOk = tile.HasPower;

        var roadColor  = roadOk  ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f);
        var powerColor = powerOk ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f);

        AddLine((roadOk  ? "✅" : "✗ ") + " Road access",  12, roadColor);
        AddLine((powerOk ? "✅" : "✗ ") + " Power",         12, powerColor);

        if (roadOk && powerOk)
            AddLine("Building will appear soon", 12, new Color(0.3f, 1f, 0.3f));
    }

    /// <summary>
    /// Adds a divider and growth checklist section for an existing building.
    /// Determines the next likely upgrade tier and shows what conditions are met/unmet.
    /// </summary>
    private void AddGrowthChecklist(string typeId, int totalPop, int capacity, Tile anchorTile, SharedState? state)
    {
        // Find the next upgrade tier for this building type
        var nextTier = GetNextTier(typeId);
        if (nextTier == null) return;

        AddSeparator();
        AddLine($"Upgrade: {PrettifyTypeId(nextTier.TypeId, nextTier.Width, nextTier.Height)}", 12, new Color(0.8f, 0.8f, 1f));

        var cityPop = state?.Population ?? 0;

        // Collect checks as (label, passed)
        var checks = new System.Collections.Generic.List<(string Label, bool Passed)>();

        // Road and power are always relevant
        checks.Add(("Road access",  anchorTile.HasRoadAccess));
        checks.Add(("Power",        anchorTile.HasPower));

        // 80% population fill
        var fillRatio = capacity > 0 ? (double)totalPop / capacity : 0.0;
        checks.Add(($"80% capacity ({totalPop}/{capacity})", fillRatio >= 0.8));

        // Milestone requirements
        if (nextTier.MinMilestone == GameState.Town)
            checks.Add(("Town milestone (500 pop)", cityPop >= 500));
        else if (nextTier.MinMilestone == GameState.City)
            checks.Add(("City milestone (5,000 pop)", cityPop >= 5000));

        // Service coverage checks based on tier conditions
        foreach (var cond in nextTier.Conditions)
        {
            switch (cond.Type)
            {
                case BuildingConditionType.ServiceCoverage when cond.ServiceZone == ZoneType.School:
                    var schoolCov = state?.CoverageSummary?.SchoolCoveragePercent ?? 0.0;
                    checks.Add(("School coverage", schoolCov > 0.1));
                    break;
                case BuildingConditionType.ServiceCoverage when cond.ServiceZone == ZoneType.PoliceStation:
                    var policeCov = state?.CoverageSummary?.PoliceCoveragePercent ?? 0.0;
                    checks.Add(("Police coverage", policeCov > 0.1));
                    break;
                case BuildingConditionType.ServiceCoverage when cond.ServiceZone == ZoneType.FireStation:
                    var fireCov = state?.CoverageSummary?.FireCoveragePercent ?? 0.0;
                    checks.Add(("Fire station coverage", fireCov > 0.1));
                    break;
                case BuildingConditionType.ForestNearby:
                    // Can't evaluate per-tile forest terrain here without full grid — note it as informational
                    checks.Add(($"Forest nearby (radius {cond.Param})", false));
                    break;
                case BuildingConditionType.MinLandValue:
                    checks.Add(($"Land value ≥{cond.DoubleParam:F0}%", false));
                    break;
            }
        }

        var failedColor  = new Color(1f,    0.35f, 0.35f);
        var passedColor  = new Color(0.35f, 1f,    0.35f);
        var allPassed    = true;

        foreach (var (label, passed) in checks)
        {
            AddLine((passed ? "✅ " : "✗  ") + label, 12, passed ? passedColor : failedColor);
            if (!passed) allPassed = false;
        }

        if (allPassed)
            AddLine("Upgrade expected soon!", 12, new Color(0.3f, 1f, 0.4f));
    }

    /// <summary>Returns the next upgrade BuildingTypeDefinition for the given TypeId, or null if at max tier.</summary>
    private static Loopolis.Core.Buildings.BuildingTypeDefinition? GetNextTier(string typeId)
    {
        // Map current TypeId to the TypeId of the next upgrade
        return typeId switch
        {
            // Residential upgrade chain: house → townhouse → villa/apartment
            "res_house_1x1"      => Loopolis.Core.Buildings.BuildingCatalog.Find("res_townhouse_2x2"),
            "res_townhouse_2x2"  => Loopolis.Core.Buildings.BuildingCatalog.Find("res_apartment_4x4"),
            // Commercial upgrade chain
            "com_shop_1x1"       => Loopolis.Core.Buildings.BuildingCatalog.Find("com_strip_1x3"),
            "com_strip_1x3"      => Loopolis.Core.Buildings.BuildingCatalog.Find("com_shopping_3x3"),
            "com_strip_3x1"      => Loopolis.Core.Buildings.BuildingCatalog.Find("com_shopping_3x3"),
            // Industrial upgrade chain
            "ind_factory_1x1"    => Loopolis.Core.Buildings.BuildingCatalog.Find("ind_warehouse_2x2"),
            "ind_warehouse_2x2"  => Loopolis.Core.Buildings.BuildingCatalog.Find("ind_park_4x2"),
            _ => null  // max tier or unknown
        };
    }

    private void AddSeparator()
    {
        var sep = new HSeparator();
        sep.AddThemeColorOverride("color", new Color(0.4f, 0.4f, 0.4f, 0.6f));
        _vbox.AddChild(sep);
    }

    /// <summary>
    /// Converts a TypeId like "res_townhouse_2x2" to a display name like "Townhouse 2×2".
    /// Falls back to width×height from the Building record if the TypeId suffix doesn't encode size.
    /// </summary>
    private static string PrettifyTypeId(string typeId, int width, int height)
    {
        // Strip zone prefix (res_, com_, ind_)
        var withoutPrefix = typeId;
        foreach (var prefix in new[] { "res_", "com_", "ind_" })
        {
            if (typeId.StartsWith(prefix))
            {
                withoutPrefix = typeId[prefix.Length..];
                break;
            }
        }

        // Strip trailing _WxH dimension suffix if present (e.g. "_2x2", "_4x4")
        var parts = withoutPrefix.Split('_');
        var nameParts = parts;
        if (parts.Length > 1)
        {
            var last = parts[^1];
            if (last.Contains('x') && last.Length <= 5)
                nameParts = parts[..^1];
        }

        // Title-case each word
        var title = string.Join(" ", System.Array.ConvertAll(nameParts,
            p => p.Length == 0 ? p : char.ToUpper(p[0]) + p[1..]));

        return $"{title} {width}×{height}";
    }

    private void AddLine(string text, int fontSize, Color color)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        _vbox.AddChild(label);
    }

    private static StyleBoxFlat MakePanelStyle()
    {
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0f, 0f, 0f, 0.78f);
        style.ContentMarginLeft   = 7;
        style.ContentMarginRight  = 7;
        style.ContentMarginTop    = 5;
        style.ContentMarginBottom = 5;
        style.CornerRadiusTopLeft     = 3;
        style.CornerRadiusTopRight    = 3;
        style.CornerRadiusBottomLeft  = 3;
        style.CornerRadiusBottomRight = 3;
        return style;
    }
}
