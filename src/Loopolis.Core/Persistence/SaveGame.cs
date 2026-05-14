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
    [property: JsonPropertyName("gridHeight")]  int             GridHeight = 32,
    /// <summary>
    /// List of active policy type names (e.g. "GreenCity", "OpenCity").
    /// Null in older saves — treated as no active policies.
    /// </summary>
    [property: JsonPropertyName("activePolicies")] string[]?   ActivePolicies = null,

    // ── CharterSystem state ─────────────────────────────────────────────────

    /// <summary>Town era charter type name, or null/None when not chosen.</summary>
    [property: JsonPropertyName("activeCharter")]     string?  ActiveCharter     = null,
    /// <summary>City era charter type name, or null/None when not chosen.</summary>
    [property: JsonPropertyName("cityCharter")]       string?  CityCharter       = null,
    /// <summary>Metropolis era charter type name, or null/None when not chosen.</summary>
    [property: JsonPropertyName("metropolisCharter")] string?  MetropolisCharter = null,
    /// <summary>True when Town milestone was reached but Town charter has not yet been chosen.</summary>
    [property: JsonPropertyName("townCharterPending")]       bool TownCharterPending       = false,
    /// <summary>True when City milestone was reached but City charter has not yet been chosen.</summary>
    [property: JsonPropertyName("cityCharterPending")]       bool CityCharterPending       = false,
    /// <summary>True when Metropolis milestone was reached but Metropolis charter has not yet been chosen.</summary>
    [property: JsonPropertyName("metropolisCharterPending")] bool MetropolisCharterPending = false,

    // ── MilestoneSystem state ───────────────────────────────────────────────

    /// <summary>
    /// Ordered list of milestone names already reached (e.g. ["Town", "City"]).
    /// Null in older saves — MilestoneSystem will be recovered from GameState on first tick.
    /// </summary>
    [property: JsonPropertyName("milestonesReached")] string[]? MilestonesReached = null,

    // ── ServiceFatigueSystem state ──────────────────────────────────────────

    /// <summary>
    /// Per-tile capacity multiplier snapshot: flat array of (x, y, capacity) triplets.
    /// Null in older saves — all tiles default to 1.0 (full capacity).
    /// </summary>
    [property: JsonPropertyName("serviceFatigue")] SavedFatigueEntry[]? ServiceFatigue = null
);

/// <summary>
/// One entry in the serialized service fatigue snapshot.
/// Represents the effective capacity multiplier (0.40–1.0) for a single service tile.
/// </summary>
public record SavedFatigueEntry(
    [property: JsonPropertyName("x")] int    X,
    [property: JsonPropertyName("y")] int    Y,
    [property: JsonPropertyName("c")] double Capacity
);
