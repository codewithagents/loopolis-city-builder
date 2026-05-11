using Godot;
using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;

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
    public void ShowForBuilding(Building building, int totalPop, Tile anchorTile, Vector2 screenPos)
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
        }

        PositionAndShow(screenPos);
    }

    /// <summary>
    /// Populate and show the tooltip next to the given screen position.
    /// Only called for bare zoned tiles with no building (BuildingId == null).
    /// </summary>
    public void ShowFor(Tile tile, Vector2 screenPos)
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
                break;

            case ZoneType.Commercial:
                AddLine("Commercial", 15, new Color(0.4f, 0.6f, 1f));
                AddPowerRoadLines(tile);
                AddReadyLine(tile);
                break;

            case ZoneType.Industrial:
                AddLine("Industrial", 15, new Color(1f, 0.9f, 0.2f));
                AddPowerRoadLines(tile);
                AddReadyLine(tile);
                break;

            case ZoneType.Road:
                AddLine("Road", 15, new Color(0.8f, 0.8f, 0.8f));
                break;

            case ZoneType.PowerLine:
                AddLine("Power Line", 15, new Color(0.2f, 1f, 1f));
                break;

            case ZoneType.PowerPlant:
                AddLine("Power Plant — source", 15, new Color(1f, 0.5f, 0.2f));
                break;

            case ZoneType.FireStation:
                AddLine("Fire Station — coverage radius 4", 15, new Color(1f, 0.5f, 0.2f));
                break;

            case ZoneType.PoliceStation:
                AddLine("Police Station — coverage radius 4", 15, new Color(0.4f, 0.6f, 1f));
                break;

            case ZoneType.School:
                AddLine("School — coverage radius 5", 15, new Color(0.8f, 0.4f, 1f));
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
