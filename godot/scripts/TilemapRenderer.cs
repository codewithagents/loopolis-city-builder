using Godot;
using System.Collections.Generic;
using Loopolis.Core.Buildings;
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
    // M8 zone colors
    private static readonly Color ColorPoliceHQ     = new Color(0.102f, 0.137f, 0.494f); // #1a237e deep blue
    private static readonly Color ColorFireHQ       = new Color(0.718f, 0.110f, 0.110f); // #b71c1c deep red
    private static readonly Color ColorHospital     = new Color(0.647f, 0.839f, 0.647f); // #a5d6a7 soft green-white
    private static readonly Color ColorCoalPlant    = new Color(0.259f, 0.259f, 0.259f); // #424242 dark grey
    private static readonly Color ColorNuclearPlant = new Color(0.976f, 0.659f, 0.145f); // #f9a825 yellow-green
    // Unpowered zones get a dark overlay — show the mechanic visually
    private static readonly Color UnpoweredTint     = new Color(0f, 0f, 0f, 0.45f);
    // Brownout overlay — amber tint on BFS-powered tiles when capacity < demand
    private static readonly Color BrownoutTint      = new Color(1f, 0.55f, 0f, 0.22f);

    private bool _isBrownout = false;

    /// <summary>Set brownout state so the renderer can apply the amber tint on next redraw.</summary>
    public void SetBrownout(bool brownout)
    {
        if (_isBrownout == brownout) return;
        _isBrownout = brownout;
        QueueRedraw();
    }

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

    private bool IsSameZone(ZoneType zone, int x, int y)
    {
        if (_grid == null) return false;
        if (!_grid.IsInBounds(x, y)) return false;
        return _grid.GetTile(x, y).Zone == zone;
    }

    public override void _Draw()
    {
        if (_grid == null) return;

        foreach (var tile in _grid.AllTiles())
        {
            float px = tile.X * TileSize;
            float py = tile.Y * TileSize;

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

                    // Fill full tile — no gap between same-zone neighbours
                    var fullRect = new Rect2(px, py, TileSize, TileSize);
                    DrawRect(fullRect, color);

                    // Draw dark border only on edges that face a different zone (cluster boundary)
                    var borderColor = color * 0.45f;
                    const int borderW = 2;

                    bool adjLeft  = IsSameZone(tile.Zone, tile.X - 1, tile.Y);
                    bool adjRight = IsSameZone(tile.Zone, tile.X + 1, tile.Y);
                    bool adjUp    = IsSameZone(tile.Zone, tile.X,     tile.Y - 1);
                    bool adjDown  = IsSameZone(tile.Zone, tile.X,     tile.Y + 1);

                    if (!adjLeft)  DrawRect(new Rect2(px,                      py,      borderW, TileSize), borderColor);
                    if (!adjRight) DrawRect(new Rect2(px + TileSize - borderW, py,      borderW, TileSize), borderColor);
                    if (!adjUp)    DrawRect(new Rect2(px,  py,                 TileSize, borderW),           borderColor);
                    if (!adjDown)  DrawRect(new Rect2(px,  py + TileSize - borderW, TileSize, borderW),      borderColor);

                    // Density-based inner building rectangle
                    if (fillFraction > 0.25f)
                    {
                        var buildingScale = Mathf.Lerp(0.4f, 0.75f, (fillFraction - 0.25f) / 0.75f);
                        var margin = (int)(TileSize * (1f - buildingScale) / 2f);
                        var buildingRect = new Rect2(
                            px + margin, py + margin,
                            TileSize - margin * 2, TileSize - margin * 2
                        );
                        var buildingColor = color * 1.25f;
                        buildingColor.A = 1f;
                        DrawRect(buildingRect, buildingColor);
                    }

                    // Dark overlay on zones that are zoned but not powered
                    if (!tile.HasPower)
                        DrawRect(fullRect, UnpoweredTint);

                    // Red semi-transparent overlay for pollution
                    if (tile.PollutionLevel > 0.05f)
                    {
                        var pollutionColor = new Color(1f, 0f, 0f, (float)tile.PollutionLevel * 0.55f);
                        DrawRect(fullRect, pollutionColor);
                    }

                    // Yellow dot in corner for residential tiles with demand boost
                    if (tile.HasDemandBoost && tile.Zone == ZoneType.Residential)
                    {
                        var dotRect = new Rect2(px + TileSize - 7, py + TileSize - 7, 5, 5);
                        DrawRect(dotRect, new Color(1f, 0.9f, 0.1f, 0.8f));
                    }

                    continue;
                }
                case ZoneType.Road:
                {
                    var roadFull = new Rect2(px, py, TileSize, TileSize);
                    DrawRect(roadFull, ColorRoad);

                    // Darker border only on non-road edges
                    bool rLeft  = IsSameZone(ZoneType.Road, tile.X - 1, tile.Y);
                    bool rRight = IsSameZone(ZoneType.Road, tile.X + 1, tile.Y);
                    bool rUp    = IsSameZone(ZoneType.Road, tile.X,     tile.Y - 1);
                    bool rDown  = IsSameZone(ZoneType.Road, tile.X,     tile.Y + 1);
                    var roadEdge = ColorRoad * 0.55f;

                    if (!rLeft)  DrawRect(new Rect2(px,                  py, 2, TileSize), roadEdge);
                    if (!rRight) DrawRect(new Rect2(px + TileSize - 2,   py, 2, TileSize), roadEdge);
                    if (!rUp)    DrawRect(new Rect2(px, py,               TileSize, 2),    roadEdge);
                    if (!rDown)  DrawRect(new Rect2(px, py + TileSize - 2, TileSize, 2),   roadEdge);

                    continue;
                }
                case ZoneType.PowerPlant:   // legacy alias — renders same as CoalPlant
                case ZoneType.CoalPlant:
                    color = ColorCoalPlant;
                    break;
                case ZoneType.NuclearPlant:
                    color = ColorNuclearPlant;
                    break;
                case ZoneType.PowerLine:
                    color = ColorPowerLine;
                    break;
                case ZoneType.FireStation:
                    color = ColorFireStation;
                    break;
                case ZoneType.FireHQ:
                    color = ColorFireHQ;
                    break;
                case ZoneType.PoliceStation:
                    color = ColorPoliceStation;
                    break;
                case ZoneType.PoliceHQ:
                    color = ColorPoliceHQ;
                    break;
                case ZoneType.School:
                    color = ColorSchool;
                    break;
                case ZoneType.Hospital:
                    color = ColorHospital;
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

            // Service buildings and terrain: keep 1px gap (stand-alone structures)
            var rect = new Rect2(px, py, TileSize - 1, TileSize - 1);
            DrawRect(rect, color);

            // Water tiles have no overlays — skip pollution, power tint, and demand dot
            if (tile.Terrain == Loopolis.Core.Grid.TerrainType.Water) continue;
        }

        // Brownout overlay: amber tint on all BFS-powered tiles when capacity < demand.
        // This is a different/weaker signal than the existing unpowered dark tint.
        if (_isBrownout && _grid != null)
        {
            foreach (var tile in _grid.AllTiles())
            {
                if (!tile.HasPower) continue;
                // Only overlay zoned tiles — roads/plants/terrain don't get the tint
                if (tile.Zone is ZoneType.Empty or ZoneType.Road or ZoneType.PowerLine
                    or ZoneType.PowerPlant or ZoneType.CoalPlant or ZoneType.NuclearPlant)
                    continue;
                var rect = new Rect2(tile.X * TileSize, tile.Y * TileSize, TileSize, TileSize);
                DrawRect(rect, BrownoutTint);
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

        // Multi-tile building outlines: draw a bright border around footprints larger than 1x1
        if (_grid != null)
        {
            foreach (var building in _grid.Buildings.Values)
            {
                if (building.TileCount <= 1) continue; // skip 1x1 buildings

                var borderColor = building.Zone switch
                {
                    ZoneType.Residential => new Color(0.0f, 1.0f, 0.3f, 0.85f),   // bright green
                    ZoneType.Commercial  => new Color(0.3f, 0.7f, 1.0f, 0.85f),   // bright blue
                    ZoneType.Industrial  => new Color(1.0f, 0.9f, 0.0f, 0.85f),   // bright yellow
                    _                    => new Color(1.0f, 1.0f, 1.0f, 0.85f),
                };

                const int outlineW = 3;
                float bx = building.AnchorX * TileSize;
                float by = building.AnchorY * TileSize;
                float bw = building.Width  * TileSize;
                float bh = building.Height * TileSize;

                // Draw four border edges (top, bottom, left, right)
                DrawRect(new Rect2(bx,                by,                bw, outlineW), borderColor);      // top
                DrawRect(new Rect2(bx,                by + bh - outlineW, bw, outlineW), borderColor);     // bottom
                DrawRect(new Rect2(bx,                by,                outlineW, bh), borderColor);      // left
                DrawRect(new Rect2(bx + bw - outlineW, by,                outlineW, bh), borderColor);    // right
            }
        }
    }
}
