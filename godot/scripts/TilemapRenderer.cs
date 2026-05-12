using Godot;
using System.Collections.Generic;
using Loopolis.Core.Buildings;
using Loopolis.Core.Grid;

namespace LoopolisGodot;

public partial class TilemapRenderer : Node2D
{
    private CityGrid? _grid;
    public const int TileSize = 32;

    // Height and forest maps — parallel arrays updated with Refresh()
    // These are separate from CityGrid because Core doesn't have HeightLevel on Tile yet.
    // Viewer mode populates them from SharedTile.Height/HasForest.
    // Standalone mode derives them from TerrainType until Core adds HeightLevel.
    private int[,]? _heightMap;
    private bool[,]? _forestMap;

    private HashSet<(int, int)> _coverageHighlight = new();
    private Color _coverageColor = Colors.Transparent;

    private static readonly Color ColorEmpty         = new Color(0.15f, 0.15f, 0.15f);
    private static readonly Color ColorWater        = new Color(0.18f, 0.42f, 0.72f); // blue
    private static readonly Color ColorForest       = new Color(0.13f, 0.42f, 0.18f); // dark green
    private static readonly Color ColorHill         = new Color(0.831f, 0.663f, 0.416f); // warm tan #D4A96A
    private static readonly Color ColorHillHatch    = new Color(0.627f, 0.471f, 0.353f); // darker hatch lines #A0785A
    private static readonly Color ColorHillShadow   = new Color(0.627f, 0.471f, 0.353f, 0.8f); // bottom/right edge shadow

    // Height-based land colors
    private static readonly Color ColorDeepWater    = new Color(0.082f, 0.396f, 0.753f); // #1565C0
    private static readonly Color ColorShallowWater = new Color(0.098f, 0.463f, 0.824f); // #1976D2
    private static readonly Color ColorLowland      = new Color(0.180f, 0.490f, 0.196f); // #2E7D32
    private static readonly Color ColorMidland      = new Color(0.220f, 0.557f, 0.235f); // #388E3C
    private static readonly Color ColorHighland     = new Color(0.831f, 0.663f, 0.416f); // #D4A96A (matches existing hill)
    private static readonly Color ColorUpland       = new Color(0.553f, 0.431f, 0.388f); // #8D6E63
    private static readonly Color ColorPeak         = new Color(0.620f, 0.620f, 0.620f); // #9E9E9E

    // Forest overlay
    private static readonly Color ColorForestOverlay = new Color(0.062f, 0.380f, 0.090f, 0.40f); // dark green 40% alpha
    private static readonly Color ColorForestDot     = new Color(0.062f, 0.380f, 0.090f, 0.85f); // forest center dot

    // Cliff edge indicator
    private static readonly Color CliffEdgeColor    = new Color(0.3f, 0.2f, 0.1f, 0.9f); // dark brown

    // Plateau highlight
    private static readonly Color PlateauShimmer    = new Color(1f, 1f, 1f, 0.25f); // white 25% alpha
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
        // Read HeightLevel and HasForest directly from the Core tile data.
        var w = grid.Width;
        var h = grid.Height;
        _heightMap = new int[w, h];
        _forestMap = new bool[w, h];
        for (var x = 0; x < w; x++)
        for (var y = 0; y < h; y++)
        {
            _heightMap[x, y] = grid.GetHeightLevel(x, y);
            _forestMap[x, y] = grid.HasForestAt(x, y);
        }
        QueueRedraw();
    }

    /// <summary>
    /// Refresh with explicit height and forest maps (viewer mode, populated from SharedTile.Height / HasForest).
    /// </summary>
    public void RefreshWithHeight(CityGrid grid, int[,] heightMap, bool[,] forestMap)
    {
        _grid      = grid;
        _heightMap = heightMap;
        _forestMap = forestMap;
        QueueRedraw();
    }

    /// <summary>Returns the height level for the given tile coordinate, or 1 if no height map is loaded.</summary>
    public int GetTileHeight(int x, int y) => GetHeight(x, y);

    /// <summary>Returns whether the given tile has a forest overlay, or false if no forest map is loaded.</summary>
    public bool GetTileForest(int x, int y)
    {
        if (_forestMap == null) return false;
        if (x < 0 || x >= _forestMap.GetLength(0)) return false;
        if (y < 0 || y >= _forestMap.GetLength(1)) return false;
        return _forestMap[x, y];
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

    // ── Height rendering helpers ────────────────────────────────────────────

    /// <summary>Returns the base land color for a given height level.</summary>
    private static Color HeightToColor(int height)
    {
        return height switch
        {
            <= 0 => ColorDeepWater,
            1    => ColorLowland,
            2    => ColorMidland,
            3    => ColorHighland,
            4    => ColorUpland,
            _    => ColorPeak,
        };
    }

    /// <summary>
    /// Returns a subtle brightness multiplier for a zoned tile based on height.
    /// Height 1 = no change, 2–3 = slightly brighter (+10% L), ≥4 = slightly darker, ≤0 = blue-tint error.
    /// </summary>
    private static Color ApplyHeightTintToZoneColor(Color baseColor, int height)
    {
        if (height <= 0)
        {
            // Water — should never be zoned, show blue-tinted error
            return baseColor.Lerp(new Color(0.2f, 0.4f, 0.9f), 0.35f);
        }
        if (height >= 4)
        {
            // Upland/peak — slightly darker (more dramatic terrain)
            return new Color(
                Mathf.Clamp(baseColor.R * 0.92f, 0f, 1f),
                Mathf.Clamp(baseColor.G * 0.92f, 0f, 1f),
                Mathf.Clamp(baseColor.B * 0.92f, 0f, 1f),
                baseColor.A);
        }
        if (height >= 2)
        {
            // Midland/highland — slightly brighter (+10% on each channel)
            return new Color(
                Mathf.Clamp(baseColor.R * 1.10f, 0f, 1f),
                Mathf.Clamp(baseColor.G * 1.10f, 0f, 1f),
                Mathf.Clamp(baseColor.B * 1.10f, 0f, 1f),
                baseColor.A);
        }
        // height == 1: standard, no modification
        return baseColor;
    }

    /// <summary>
    /// Returns the height for a neighbour tile, defaulting to 1 if out of bounds.
    /// </summary>
    private int GetHeight(int x, int y)
    {
        if (_heightMap == null) return 1;
        if (x < 0 || x >= _heightMap.GetLength(0)) return 1;
        if (y < 0 || y >= _heightMap.GetLength(1)) return 1;
        return _heightMap[x, y];
    }

    /// <summary>
    /// Draws a height-based empty terrain tile with water depth, cliff edges, plateau highlight,
    /// and forest overlay.
    /// </summary>
    private void DrawHeightTile(int tileX, int tileY, float px, float py)
    {
        var height = GetHeight(tileX, tileY);
        var isForest = _forestMap != null
            && tileX >= 0 && tileX < _forestMap.GetLength(0)
            && tileY >= 0 && tileY < _forestMap.GetLength(1)
            && _forestMap[tileX, tileY];

        // ── Base color ──────────────────────────────────────────────────────

        Color baseColor;
        if (height <= 0)
        {
            // Water: check if any cardinal neighbour is ≥ 1 → shallow coast, else deep
            var hasLandNeighbour =
                GetHeight(tileX - 1, tileY) >= 1 ||
                GetHeight(tileX + 1, tileY) >= 1 ||
                GetHeight(tileX, tileY - 1) >= 1 ||
                GetHeight(tileX, tileY + 1) >= 1;
            baseColor = hasLandNeighbour ? ColorShallowWater : ColorDeepWater;
        }
        else
        {
            baseColor = HeightToColor(height);
        }

        DrawRect(new Rect2(px, py, TileSize - 1, TileSize - 1), baseColor);

        // ── Water depth effect: darker small rect in center ────────────────
        if (height <= 0)
        {
            var depthColor = new Color(
                Mathf.Max(baseColor.R - 0.07f, 0f),
                Mathf.Max(baseColor.G - 0.07f, 0f),
                Mathf.Max(baseColor.B - 0.08f, 0f),
                0.65f);
            var depthSize = TileSize * 0.35f;
            var depthOffset = (TileSize - depthSize) * 0.5f;
            DrawRect(new Rect2(px + depthOffset, py + depthOffset, depthSize, depthSize), depthColor);
            // No overlays for water tiles
            return;
        }

        // ── Forest overlay ─────────────────────────────────────────────────
        if (isForest)
        {
            DrawRect(new Rect2(px, py, TileSize - 1, TileSize - 1), ColorForestOverlay);
            // Small dark green dot in center
            var dotSize = 5f;
            DrawRect(new Rect2(
                px + (TileSize - dotSize) * 0.5f,
                py + (TileSize - dotSize) * 0.5f,
                dotSize, dotSize), ColorForestDot);
        }

        // ── Cliff edge indicator: 3px dark brown line on edges with height diff > 1 ──
        const float cliffWidth = 3f;
        // Left edge
        if (System.Math.Abs(height - GetHeight(tileX - 1, tileY)) > 1)
            DrawRect(new Rect2(px, py, cliffWidth, TileSize - 1), CliffEdgeColor);
        // Right edge
        if (System.Math.Abs(height - GetHeight(tileX + 1, tileY)) > 1)
            DrawRect(new Rect2(px + TileSize - 1 - cliffWidth, py, cliffWidth, TileSize - 1), CliffEdgeColor);
        // Top edge
        if (System.Math.Abs(height - GetHeight(tileX, tileY - 1)) > 1)
            DrawRect(new Rect2(px, py, TileSize - 1, cliffWidth), CliffEdgeColor);
        // Bottom edge
        if (System.Math.Abs(height - GetHeight(tileX, tileY + 1)) > 1)
            DrawRect(new Rect2(px, py + TileSize - 1 - cliffWidth, TileSize - 1, cliffWidth), CliffEdgeColor);

        // ── Plateau highlight: all 4 cardinal neighbours within ±1 height ──
        if (height >= 2)
        {
            var dLeft  = System.Math.Abs(height - GetHeight(tileX - 1, tileY));
            var dRight = System.Math.Abs(height - GetHeight(tileX + 1, tileY));
            var dUp    = System.Math.Abs(height - GetHeight(tileX, tileY - 1));
            var dDown  = System.Math.Abs(height - GetHeight(tileX, tileY + 1));
            if (dLeft <= 1 && dRight <= 1 && dUp <= 1 && dDown <= 1)
            {
                // Small white triangle in the top-left corner
                const float shimmerSize = 7f;
                var v1 = new Vector2(px + 1, py + 1);
                var v2 = new Vector2(px + 1 + shimmerSize, py + 1);
                var v3 = new Vector2(px + 1, py + 1 + shimmerSize);
                DrawTriangle(v1, v2, v3, PlateauShimmer);
            }
        }
    }

    /// <summary>
    /// Draws cliff edge indicators on an already-rendered zoned tile.
    /// Applies 3px dark brown lines only on edges where height difference > 1.
    /// </summary>
    private void DrawZonedCliffEdges(int tileX, int tileY, float px, float py)
    {
        if (_heightMap == null) return;
        var height = GetHeight(tileX, tileY);
        const float cliffWidth = 3f;
        if (System.Math.Abs(height - GetHeight(tileX - 1, tileY)) > 1)
            DrawRect(new Rect2(px, py, cliffWidth, TileSize), CliffEdgeColor);
        if (System.Math.Abs(height - GetHeight(tileX + 1, tileY)) > 1)
            DrawRect(new Rect2(px + TileSize - cliffWidth, py, cliffWidth, TileSize), CliffEdgeColor);
        if (System.Math.Abs(height - GetHeight(tileX, tileY - 1)) > 1)
            DrawRect(new Rect2(px, py, TileSize, cliffWidth), CliffEdgeColor);
        if (System.Math.Abs(height - GetHeight(tileX, tileY + 1)) > 1)
            DrawRect(new Rect2(px, py + TileSize - cliffWidth, TileSize, cliffWidth), CliffEdgeColor);
    }

    /// <summary>
    /// Draws a filled triangle using three DrawLine calls approximated by a polygon.
    /// Godot 4's Node2D _Draw() exposes DrawPolygon for filled shapes.
    /// </summary>
    private void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        DrawPolygon(new[] { a, b, c }, new[] { color, color, color });
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
    /// Uses absolute worker-count tiers (TrafficLoad = workers passing through node per tick):
    ///   0        → no dots
    ///   1–10     → 1 dot  (light,  white)
    ///   11–30    → 2 dots (moderate, white)
    ///   31–60    → 3 dots (busy,   yellow)
    ///   61–100   → 4 dots (heavy,  orange)
    ///   101+     → 5 dots (jammed, red)
    /// </summary>
    private void DrawTrafficDots(int load, float px, float py)
    {
        int dots;
        Color dotColor;
        if      (load <= 0)   { return; }
        else if (load <= 10)  { dots = 1; dotColor = Colors.White; }
        else if (load <= 30)  { dots = 2; dotColor = Colors.White; }
        else if (load <= 60)  { dots = 3; dotColor = new Color(1f, 0.95f, 0.2f); }
        else if (load <= 100) { dots = 4; dotColor = new Color(1f, 0.55f, 0.1f); }
        else                  { dots = 5; dotColor = new Color(1f, 0.2f,  0.2f); }

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

    /// <summary>
    /// Draws the Hill terrain tile: warm tan base + diagonal hatch lines suggesting elevation
    /// + a 2px darker shadow on the bottom and right edges for a subtle raised appearance.
    /// </summary>
    private void DrawHillTile(float px, float py)
    {
        // Base fill (slightly smaller to match the 1px gap on service buildings)
        DrawRect(new Rect2(px, py, TileSize - 1, TileSize - 1), ColorHill);

        // Three diagonal hatch lines (top-left → bottom-right), evenly spaced
        const float hatchWidth = 1.2f;
        const int   steps      = 3;
        for (int i = 0; i < steps; i++)
        {
            float offset = (TileSize - 1) * (i + 1) / (steps + 1);
            // Start on the top edge, end on the left edge when offset < TileSize, else wrap
            var a = new Vector2(px + offset, py);
            var b = new Vector2(px, py + offset);
            DrawLine(a, b, ColorHillHatch, hatchWidth, false);

            // Mirror: start on right edge, end on bottom edge
            var c = new Vector2(px + TileSize - 1 - offset, py + TileSize - 1);
            var d = new Vector2(px + TileSize - 1, py + TileSize - 1 - offset);
            DrawLine(c, d, ColorHillHatch, hatchWidth, false);
        }

        // 2px shadow on bottom edge (suggests a drop in elevation below)
        DrawRect(new Rect2(px, py + TileSize - 3, TileSize - 1, 2), ColorHillShadow);
        // 2px shadow on right edge
        DrawRect(new Rect2(px + TileSize - 3, py, 2, TileSize - 1), ColorHillShadow);
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
                    // Apply subtle height-based brightness modifier before fill lerp
                    var heightTinted = ApplyHeightTintToZoneColor(baseColor, GetHeight(tile.X, tile.Y));
                    var fillFraction = Mathf.Clamp(tile.Population / 50f, 0f, 1f);
                    var emptyColor = heightTinted * 0.35f;
                    color = emptyColor.Lerp(heightTinted, fillFraction);

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

                    // Cliff edges: draw dark brown lines on edges bordering tiles with height diff > 1
                    DrawZonedCliffEdges(tile.X, tile.Y, px, py);

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

                    // Border connection tile: yellow downward triangle + yellow border outline
                    if (tile.IsBorderConnection)
                    {
                        var borderYellow = new Color(1f, 0.85f, 0f, 1f);

                        // Thin yellow 2px border around the tile edges
                        DrawRect(new Rect2(px,                  py, TileSize, 2),           borderYellow); // top
                        DrawRect(new Rect2(px,                  py + TileSize - 2, TileSize, 2), borderYellow); // bottom
                        DrawRect(new Rect2(px,                  py, 2, TileSize),           borderYellow); // left
                        DrawRect(new Rect2(px + TileSize - 2,   py, 2, TileSize),           borderYellow); // right

                        // Downward-pointing triangle centered on tile, ~40% of tile size
                        const float triSize = TileSize * 0.40f;
                        var cx = px + TileSize * 0.5f;
                        var cy = py + TileSize * 0.5f;
                        var triTop    = cy - triSize * 0.5f;
                        var triBottom = cy + triSize * 0.5f;
                        var triLeft   = cx - triSize * 0.5f;
                        var triRight  = cx + triSize * 0.5f;
                        // Triangle: top-left, top-right, bottom-center (points downward)
                        DrawTriangle(
                            new Vector2(triLeft,  triTop),
                            new Vector2(triRight, triTop),
                            new Vector2(cx,       triBottom),
                            borderYellow);
                    }

                    // Traffic load dots: show congestion level on road/avenue tiles
                    DrawTrafficDots(tile.TrafficLoad, px, py);

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
                    // Empty tile: height-based gradient rendering with cliff edges, plateau highlight, forest overlay
                    DrawHeightTile(tile.X, tile.Y, px, py);
                    continue;
            }

            // Service buildings and utility tiles: keep 1px gap (stand-alone structures)
            var rect = new Rect2(px, py, TileSize - 1, TileSize - 1);
            DrawRect(rect, color);
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
