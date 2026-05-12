using System.Text.Json.Serialization;

namespace Loopolis.Core.Persistence;

public record SavedTile(
    [property: JsonPropertyName("x")]    int    X,
    [property: JsonPropertyName("y")]    int    Y,
    [property: JsonPropertyName("zone")] string Zone,
    [property: JsonPropertyName("pop")]  int    Population,
    [property: JsonPropertyName("bid")]  string? BuildingId = null,
    [property: JsonPropertyName("bc")]   bool   IsBorderConnection = false
);

public record SavedBuilding(
    [property: JsonPropertyName("id")]     string Id,
    [property: JsonPropertyName("typeId")] string TypeId,
    [property: JsonPropertyName("zone")]   string Zone,
    [property: JsonPropertyName("ax")]     int    AnchorX,
    [property: JsonPropertyName("ay")]     int    AnchorY,
    [property: JsonPropertyName("w")]      int    Width,
    [property: JsonPropertyName("h")]      int    Height
);

public record SaveGame(
    [property: JsonPropertyName("version")]     int            Version,
    [property: JsonPropertyName("tick")]        int            Tick,
    [property: JsonPropertyName("balance")]     double         Balance,
    [property: JsonPropertyName("taxLevel")]    string         TaxLevel,
    [property: JsonPropertyName("gameState")]   string         GameState,
    [property: JsonPropertyName("terrainSeed")] int            TerrainSeed,
    [property: JsonPropertyName("tiles")]       SavedTile[]    Tiles,
    [property: JsonPropertyName("buildings")]   SavedBuilding[]? Buildings = null,
    /// <summary>
    /// Flat row-major height map: index = x + y * width.
    /// Null in version 1–2 saves; RestoreGrid defaults to flat (height=1) when null.
    /// </summary>
    [property: JsonPropertyName("heightMap")]   int[]?          HeightMap = null,
    /// <summary>
    /// Flat row-major forest map: index = x + y * width.
    /// Null in version 1–2 saves; RestoreGrid defaults to no forest when null.
    /// </summary>
    [property: JsonPropertyName("forestMap")]   bool[]?         ForestMap = null,
    [property: JsonPropertyName("gridWidth")]   int             GridWidth = 32,
    [property: JsonPropertyName("gridHeight")]  int             GridHeight = 32
);
