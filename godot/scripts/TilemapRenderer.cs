using Godot;
using System.Collections.Generic;
using Loopolis.Core.Grid;

namespace LoopolisGodot;

public partial class TilemapRenderer : Node2D
{
    private CityGrid? _grid;
    public const int TileSize = 32;

    private HashSet<(int, int)> _coverageHighlight = new();
    private Color _coverageColor = Colors.Transparent;

    private static readonly Color ColorEmpty         = new Color(0.15f, 0.15f, 0.15f);
    private static readonly Color ColorWater        = new Color(0.18f, 0.42f, 0.72f); // blue
    private static readonly Color ColorForest       = new Color(0.13f, 0.42f, 0.18f); // dark green
    private static readonly Color ColorHill         = new Color(0.52f, 0.46f, 0.34f); // sandy brown
    private static readonly Color ColorResidential  = new Color(0.2f,  0.7f,  0.2f);
    private static readonly Color ColorCommercial   = new Color(0.2f,  0.4f,  0.9f);
    private static readonly Color ColorIndustrial   = new Color(0.9f,  0.8f,  0.1f);
    private static readonly Color ColorRoad         = new Color(0.5f,  0.5f,  0.5f);
    private static readonly Color ColorPowerPlant   = new Color(0.9f,  0.3f,  0.1f);
    private static readonly Color ColorPowerLine    = new Color(0.1f,  0.9f,  0.9f);
    private static readonly Color ColorFireStation  = new Color(1.0f,  0.4f,  0.1f);
    private static readonly Color ColorPoliceStation= new Color(0.2f,  0.4f,  1.0f);
    private static readonly Color ColorSchool       = new Color(0.7f,  0.3f,  0.9f);
    // Unpowered zones get a dark overlay — show the mechanic visually
    private static readonly Color UnpoweredTint     = new Color(0f, 0f, 0f, 0.45f);

    public void Refresh(CityGrid grid)
    {
        _grid = grid;
        QueueRedraw();
    }

    public void SetCoverageHighlight(IEnumerable<(int, int)> tiles, Color color)
    {
        _coverageHighlight = new HashSet<(int, int)>(tiles);
        _coverageColor = color;
        QueueRedraw();
    }

    public void ClearCoverageHighlight()
    {
        _coverageHighlight.Clear();
        _coverageColor = Colors.Transparent;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_grid == null) return;

        foreach (var tile in _grid.AllTiles())
        {
            var rect = new Rect2(tile.X * TileSize, tile.Y * TileSize, TileSize - 1, TileSize - 1);

            Color color;
            switch (tile.Zone)
            {
                case ZoneType.Residential:
                case ZoneType.Commercial:
                case ZoneType.Industrial:
                {
                    // Scale brightness with population fill level
                    var baseColor = tile.Zone switch
                    {
                        ZoneType.Residential => ColorResidential,
                        ZoneType.Commercial  => ColorCommercial,
                        _                    => ColorIndustrial,
                    };
                    var fillFraction = Mathf.Clamp(tile.Population / 50f, 0f, 1f);
                    var emptyColor = baseColor * 0.35f;
                    color = emptyColor.Lerp(baseColor, fillFraction);
                    break;
                }
                case ZoneType.Road:
                    color = ColorRoad;
                    break;
                case ZoneType.PowerPlant:
                    color = ColorPowerPlant;
                    break;
                case ZoneType.PowerLine:
                    color = ColorPowerLine;
                    break;
                case ZoneType.FireStation:
                    color = ColorFireStation;
                    break;
                case ZoneType.PoliceStation:
                    color = ColorPoliceStation;
                    break;
                case ZoneType.School:
                    color = ColorSchool;
                    break;
                default:
                    color = tile.Terrain switch
                    {
                        Loopolis.Core.Grid.TerrainType.Water  => ColorWater,
                        Loopolis.Core.Grid.TerrainType.Forest => ColorForest,
                        Loopolis.Core.Grid.TerrainType.Hill   => ColorHill,
                        _                                      => ColorEmpty,
                    };
                    break;
            }

            DrawRect(rect, color);

            // Water tiles have no overlays — skip pollution, power tint, and demand dot
            if (tile.Terrain == Loopolis.Core.Grid.TerrainType.Water) continue;

            // Dark overlay on zones that are zoned but not powered
            var isZone = tile.Zone is ZoneType.Residential or ZoneType.Commercial or ZoneType.Industrial;
            if (isZone && !tile.HasPower)
                DrawRect(rect, UnpoweredTint);

            // Red semi-transparent overlay for pollution
            if (tile.PollutionLevel > 0.05f)
            {
                var pollutionColor = new Color(1f, 0f, 0f, (float)tile.PollutionLevel * 0.55f);
                DrawRect(rect, pollutionColor);
            }

            // Yellow dot in corner for residential tiles with demand boost
            if (tile.HasDemandBoost && tile.Zone == ZoneType.Residential)
            {
                var dotRect = new Rect2(rect.Position + rect.Size - new Vector2(6, 6), new Vector2(5, 5));
                DrawRect(dotRect, new Color(1f, 0.9f, 0.1f, 0.8f));
            }
        }

        // Coverage radius overlay: draw semi-transparent color over highlighted tiles
        if (_coverageHighlight.Count > 0)
        {
            var overlayColor = new Color(_coverageColor.R, _coverageColor.G, _coverageColor.B, 0.3f);
            foreach (var (cx, cy) in _coverageHighlight)
            {
                var rect = new Rect2(cx * TileSize, cy * TileSize, TileSize - 1, TileSize - 1);
                DrawRect(rect, overlayColor);
            }
        }
    }
}
