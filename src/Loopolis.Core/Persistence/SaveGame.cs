using System.Text.Json.Serialization;

namespace Loopolis.Core.Persistence;

public record SavedTile(
    [property: JsonPropertyName("x")]    int    X,
    [property: JsonPropertyName("y")]    int    Y,
    [property: JsonPropertyName("zone")] string Zone,
    [property: JsonPropertyName("pop")]  int    Population
);

public record SaveGame(
    [property: JsonPropertyName("version")]     int         Version,
    [property: JsonPropertyName("tick")]        int         Tick,
    [property: JsonPropertyName("balance")]     double      Balance,
    [property: JsonPropertyName("taxLevel")]    string      TaxLevel,
    [property: JsonPropertyName("gameState")]   string      GameState,
    [property: JsonPropertyName("terrainSeed")] int         TerrainSeed,
    [property: JsonPropertyName("tiles")]       SavedTile[] Tiles
);
