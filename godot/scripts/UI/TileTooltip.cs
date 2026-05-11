using Godot;
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
    /// Populate and show the tooltip next to the given screen position.
    /// </summary>
    public void ShowFor(Tile tile, Vector2 screenPos)
    {
        // Clear old labels
        foreach (Node child in _vbox.GetChildren())
            child.QueueFree();

        // Build lines based on zone type
        switch (tile.Zone)
        {
            case ZoneType.Residential:
                AddLine("Residential", 15, new Color(0.3f, 1f, 0.4f));
                AddPowerRoadLines(tile);
                AddReadyLine(tile);
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

        // Position near cursor, nudging so it doesn't sit under the mouse
        const float offsetX = 16f;
        const float offsetY = -8f;
        _panel.Position = screenPos + new Vector2(offsetX, offsetY);

        _panel.Visible = true;
    }

    public new void Hide()
    {
        _panel.Visible = false;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

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
