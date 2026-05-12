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
    private static readonly Color ColorAvenue       = new Color(0.62f, 0.62f, 0.62f);
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
    // Idle zone border — amber dashed outline on zoned tiles with no road access
    private static readonly Color IdleBorderColor   = new Color(1f, 0.561f, 0f, 0.85f); // #FF8F00

    private bool _isBrownout = false;

    // Rectangle paint preview
    private bool _hasRectPreview = false;
    private Vector2I _rectPreviewStart;
    private Vector2I _rectPreviewEnd;
    private Color _rectPreviewColor = Colors.Transparent;

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

    /// <summary>Shows a semi-transparent rectangle preview for drag-to-place zone painting.</summary>
    public void SetRectPreview(Vector2I start, Vector2I end, Color color)
    {
        _hasRectPreview    = true;
        _rectPreviewStart  = start;
        _rectPreviewEnd    = end;
        _rectPreviewColor  = color;
        QueueRedraw();
    }

    /// <summary>Removes the rectangle paint preview overlay.</summary>
    public void ClearRectPreview()
    {
        _hasRectPreview = false;
        QueueRedraw();
    }

    /// <summary>
    /// Draws a dashed rectangular border around the tile at (px, py) with the given size.
    /// Each edge is walked in pixel steps; a dash is drawn for <paramref name="dashLen"/> pixels,
    /// then skipped for <paramref name="gapLen"/> pixels, cycling continuously.
    /// The phase carries across edges so the pattern looks continuous around the perimeter.
    /// </summary>
    private void DrawDashedBorder(float px, float py, int size, Color color, float dashLen, float gapLen, float width)
    {
        float period = dashLen + gapLen;
        float halfW  = width * 0.5f;
        float inset  = halfW; // keep the line fully inside the tile rect

        // The four corners of the inset border, going clockwise: TL → TR → BR → BL → TL
        var corners = new (float x, float y)[]
        {
            (px + inset,            py + inset),
            (px + size - inset,     py + inset),
            (px + size - inset,     py + size - inset),
            (px + inset,            py + size - inset),
        };

        float phase = 0f; // position within the current dash+gap cycle

        for (int i = 0; i < corners.Length; i++)
        {
            var (ax, ay) = corners[i];
            var (bx, by) = corners[(i + 1) % corners.Length];

            float edgeLen = Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            float dx = (bx - ax) / edgeLen;
            float dy = (by - ay) / edgeLen;

            float t = 0f;
            while (t < edgeLen)
            {
                // Position within the dash/gap cycle
                float cyclePos = phase % period;
                float remaining = period - cyclePos;

                if (cyclePos < dashLen)
                {
                    // We are in a dash segment
                    float dashRemaining = dashLen - cyclePos;
                    float drawLen = Mathf.Min(dashRemaining, edgeLen - t);

                    var start = new Vector2(ax + dx * t,       ay + dy * t);
                    var end   = new Vector2(ax + dx * (t + drawLen), ay + dy * (t + drawLen));
                    DrawLine(start, end, color, width, false);

                    phase += drawLen;
                    t     += drawLen;
                }
                else
                {
                    // We are in a gap segment
                    float gapRemaining = period - cyclePos;
                    float skipLen = Mathf.Min(gapRemaining, edgeLen - t);

                    phase += skipLen;
                    t     += skipLen;
                }
            }
        }
    }

    /// <summary>
    /// Draws 1–5 small filled circles in the centre of a road tile based on traffic congestion.
    /// load / threshold: &lt;20% → 0 dots, 20-40% → 1 white, 40-60% → 2 white, 60-80% → 3 yellow,
    /// 80-100% → 4 orange, &gt;100% → 5 red.
    /// </summary>
    private void DrawTrafficDots(int load, int threshold, float px, float py)
    {
        if (threshold <= 0) return;
        var ratio = (double)load / threshold;

        int dots;
        Color dotColor;
        if (ratio < 0.2)      { dots = 0; dotColor = Colors.White; }
        else if (ratio < 0.4) { dots = 1; dotColor = Colors.White; }
        else if (ratio < 0.6) { dots = 2; dotColor = Colors.White; }
        else if (ratio < 0.8) { dots = 3; dotColor = new Color(1f, 0.95f, 0.2f); }
        else if (ratio < 1.0) { dots = 4; dotColor = new Color(1f, 0.55f, 0.1f); }
        else                   { dots = 5; dotColor = new Color(1f, 0.2f,  0.2f); }

        if (dots == 0) return;

        const float radius = 2.5f;
        const float spacing = 6f;
        // Centre all dots horizontally in the tile
        float totalWidth = (dots - 1) * spacing;
        float startX = px + TileSize * 0.5f - totalWidth * 0.5f;
        float centreY = py + TileSize * 0.5f;

        for (var i = 0; i < dots; i++)
            DrawCircle(new Vector2(startX + i * spacing, centreY), radius, dotColor);
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

                    // Dashed amber border on zones with no road access — these cost
                    // maintenance every tick but can never develop a building.
                    if (!tile.HasRoadAccess)
                        DrawDashedBorder(px, py, TileSize, IdleBorderColor, dashLen: 4f, gapLen: 4f, width: 2f);

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
                case ZoneType.Avenue:
                {
                    var roadColor = tile.Zone == ZoneType.Avenue ? ColorAvenue : ColorRoad;
                    var roadFull = new Rect2(px, py, TileSize, TileSize);
                    DrawRect(roadFull, roadColor);

                    // Darker border only on non-road/avenue edges
                    bool rLeft  = IsSameZone(ZoneType.Road, tile.X - 1, tile.Y) || IsSameZone(ZoneType.Avenue, tile.X - 1, tile.Y);
                    bool rRight = IsSameZone(ZoneType.Road, tile.X + 1, tile.Y) || IsSameZone(ZoneType.Avenue, tile.X + 1, tile.Y);
                    bool rUp    = IsSameZone(ZoneType.Road, tile.X,     tile.Y - 1) || IsSameZone(ZoneType.Avenue, tile.X, tile.Y - 1);
                    bool rDown  = IsSameZone(ZoneType.Road, tile.X,     tile.Y + 1) || IsSameZone(ZoneType.Avenue, tile.X, tile.Y + 1);
                    var roadEdge = roadColor * 0.55f;

                    if (!rLeft)  DrawRect(new Rect2(px,                  py, 2, TileSize), roadEdge);
                    if (!rRight) DrawRect(new Rect2(px + TileSize - 2,   py, 2, TileSize), roadEdge);
                    if (!rUp)    DrawRect(new Rect2(px, py,               TileSize, 2),    roadEdge);
                    if (!rDown)  DrawRect(new Rect2(px, py + TileSize - 2, TileSize, 2),   roadEdge);

                    // Avenue: draw a white center stripe to distinguish from Road
                    if (tile.Zone == ZoneType.Avenue)
                    {
                        var stripeColor = new Color(1f, 1f, 1f, 0.25f);
                        // Determine if mostly horizontal or vertical road
                        bool hasHorizNeighbour = rLeft || rRight;
                        bool hasVertNeighbour  = rUp   || rDown;
                        if (hasHorizNeighbour && !hasVertNeighbour)
                            DrawRect(new Rect2(px, py + TileSize / 2 - 1, TileSize, 2), stripeColor);
                        else if (hasVertNeighbour && !hasHorizNeighbour)
                            DrawRect(new Rect2(px + TileSize / 2 - 1, py, 2, TileSize), stripeColor);
                        else
                        {
                            // Intersection or isolated — draw both stripes
                            DrawRect(new Rect2(px, py + TileSize / 2 - 1, TileSize, 2), stripeColor);
                            DrawRect(new Rect2(px + TileSize / 2 - 1, py, 2, TileSize), stripeColor);
                        }
                    }

                    // Traffic load dots: show congestion level on road/avenue tiles
                    DrawTrafficDots(tile.TrafficLoad, tile.Zone == ZoneType.Avenue ? 16 : 8, px, py);

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

        // Rectangle paint preview: semi-transparent fill + solid border
        if (_hasRectPreview)
        {
            var minX = Mathf.Min(_rectPreviewStart.X, _rectPreviewEnd.X);
            var maxX = Mathf.Max(_rectPreviewStart.X, _rectPreviewEnd.X);
            var minY = Mathf.Min(_rectPreviewStart.Y, _rectPreviewEnd.Y);
            var maxY = Mathf.Max(_rectPreviewStart.Y, _rectPreviewEnd.Y);

            var rx = minX * TileSize;
            var ry = minY * TileSize;
            var rw = (maxX - minX + 1) * TileSize;
            var rh = (maxY - minY + 1) * TileSize;

            // Semi-transparent fill
            DrawRect(new Rect2(rx, ry, rw, rh), _rectPreviewColor);

            // Solid border (same color, full opacity)
            var borderC = new Color(_rectPreviewColor.R, _rectPreviewColor.G, _rectPreviewColor.B, 0.9f);
            const int previewBorderW = 2;
            DrawRect(new Rect2(rx,            ry,            rw, previewBorderW), borderC); // top
            DrawRect(new Rect2(rx,            ry + rh - previewBorderW, rw, previewBorderW), borderC); // bottom
            DrawRect(new Rect2(rx,            ry,            previewBorderW, rh), borderC); // left
            DrawRect(new Rect2(rx + rw - previewBorderW, ry, previewBorderW, rh), borderC); // right

            // Size label in the top-left corner of the preview (e.g. "3×4")
            var w = maxX - minX + 1;
            var h = maxY - minY + 1;
            if (w > 1 || h > 1)
            {
                var labelPos = new Vector2(rx + previewBorderW + 2, ry + previewBorderW + 1);
                DrawString(ThemeDB.FallbackFont, labelPos, $"{w}×{h}", HorizontalAlignment.Left, -1, 11,
                    new Color(1f, 1f, 1f, 0.9f));
            }
        }
    }
}
