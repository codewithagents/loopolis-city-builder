using Godot;
using Loopolis.Core.Grid;

namespace LoopolisGodot;

public partial class TilemapRenderer : Node2D
{
    private CityGrid? _grid;
    public const int TileSize = 32;

    private static readonly Color ColorEmpty         = new Color(0.15f, 0.15f, 0.15f);
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

    public override void _Draw()
    {
        if (_grid == null) return;

        foreach (var tile in _grid.AllTiles())
        {
            var rect = new Rect2(tile.X * TileSize, tile.Y * TileSize, TileSize - 1, TileSize - 1);

            var color = tile.Zone switch
            {
                ZoneType.Residential   => ColorResidential,
                ZoneType.Commercial    => ColorCommercial,
                ZoneType.Industrial    => ColorIndustrial,
                ZoneType.Road          => ColorRoad,
                ZoneType.PowerPlant    => ColorPowerPlant,
                ZoneType.PowerLine     => ColorPowerLine,
                ZoneType.FireStation   => ColorFireStation,
                ZoneType.PoliceStation => ColorPoliceStation,
                ZoneType.School        => ColorSchool,
                _                      => ColorEmpty,
            };

            DrawRect(rect, color);

            // Dark overlay on zones that are zoned but not powered
            var isZone = tile.Zone is ZoneType.Residential or ZoneType.Commercial or ZoneType.Industrial;
            if (isZone && !tile.HasPower)
                DrawRect(rect, UnpoweredTint);
        }
    }
}
