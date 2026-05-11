namespace Loopolis.Core.Buildings;

public record Building(
    string Id,
    string TypeId,
    Grid.ZoneType Zone,
    int AnchorX,
    int AnchorY,
    int Width,
    int Height
)
{
    public int TileCount => Width * Height;
    public int MaxPopulation => TileCount * 50;

    public bool ContainsTile(int x, int y) =>
        x >= AnchorX && x < AnchorX + Width &&
        y >= AnchorY && y < AnchorY + Height;

    public IEnumerable<(int X, int Y)> Tiles()
    {
        for (var dx = 0; dx < Width; dx++)
            for (var dy = 0; dy < Height; dy++)
                yield return (AnchorX + dx, AnchorY + dy);
    }
}
