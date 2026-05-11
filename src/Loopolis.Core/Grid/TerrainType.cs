namespace Loopolis.Core.Grid;

public enum TerrainType
{
    Flat,    // default — no extra cost
    Hill,    // buildable, +$50 placement, +$0.25/tick maintenance
    Forest,  // buildable, +$75 placement (clearing cost)
    Water,   // not buildable — blocks all zone placement
}
