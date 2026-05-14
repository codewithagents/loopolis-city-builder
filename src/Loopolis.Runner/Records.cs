using System.Text.Json.Serialization;

namespace Loopolis.Runner;

record TickSnapshot(
    int Tick,
    int Population,
    double Balance,
    bool IsInDeficit,
    double TaxIncome,
    double MaintenanceCost,
    double NetPerTick,
    double AverageHappiness,
    double AveragePollution);

record SimulationReport(
    string Scenario,
    int TotalTicks,
    int FinalPopulation,
    double FinalBalance,
    bool Survived,
    int ResidentialZones,
    int PoweredResidentialZones,
    int RoadAccessResidentialZones,
    int ReadyResidentialZones,
    int PoweredTiles,
    int CommercialZones,
    int IndustrialZones,
    double AveragePollution,
    double AverageHappiness,
    string GameState,
    List<string> MilestonesReached,
    List<TickSnapshot> History);

/// <summary>Enriched building entry in state.json — includes population and capacity.</summary>
record BuildingStateInfo(
    [property: JsonPropertyName("typeId")]     string TypeId,
    [property: JsonPropertyName("x")]          int    X,
    [property: JsonPropertyName("y")]          int    Y,
    [property: JsonPropertyName("width")]      int    Width,
    [property: JsonPropertyName("height")]     int    Height,
    [property: JsonPropertyName("population")] int    Population,
    [property: JsonPropertyName("capacity")]   int    Capacity);

record HappinessBreakdown(
    [property: JsonPropertyName("serviceCoverage")]     double ServiceCoverage,
    [property: JsonPropertyName("taxModifier")]         double TaxModifier,
    [property: JsonPropertyName("unemploymentPenalty")] double UnemploymentPenalty,
    [property: JsonPropertyName("eventPenalty")]        double EventPenalty,
    [property: JsonPropertyName("neglectDecay")]        double NeglectDecay,
    [property: JsonPropertyName("commutePenalty")]      double CommutePenalty = 0.0,
    [property: JsonPropertyName("averageNeglect")]      double AverageNeglect = 0.0);

record EmploymentState(
    [property: JsonPropertyName("jobs")]             int    Jobs,
    [property: JsonPropertyName("workers")]          int    Workers,
    [property: JsonPropertyName("unemploymentRate")] double UnemploymentRate);

record NextMilestoneInfo(
    [property: JsonPropertyName("name")]                string Name,
    [property: JsonPropertyName("requiredPopulation")]  int    RequiredPopulation,
    [property: JsonPropertyName("currentPopulation")]   int    CurrentPopulation);

record TileState(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("zone")] string Zone,
    [property: JsonPropertyName("hasPower")] bool HasPower,
    [property: JsonPropertyName("hasRoadAccess")] bool HasRoadAccess,
    [property: JsonPropertyName("population")] int Population,
    [property: JsonPropertyName("pollutionLevel")] double PollutionLevel,
    [property: JsonPropertyName("happiness")] double Happiness,
    [property: JsonPropertyName("hasDemandBoost")] bool HasDemandBoost,
    [property: JsonPropertyName("buildingId")] string? BuildingId = null,
    [property: JsonPropertyName("buildingType")] string? BuildingType = null,
    [property: JsonPropertyName("trafficLoad")] int TrafficLoad = 0,
    [property: JsonPropertyName("terrain")] string? Terrain = null,
    [property: JsonPropertyName("height")] int HeightLevel = 1,
    [property: JsonPropertyName("hasForest")] bool HasForest = false,
    [property: JsonPropertyName("isBorderConnection")] bool IsBorderConnection = false);

record TerrainSummary(
    [property: JsonPropertyName("averageHeight")]    double AverageHeight,
    [property: JsonPropertyName("waterTileCount")]   int    WaterTileCount,
    [property: JsonPropertyName("elevatedTileCount")] int   ElevatedTileCount,
    [property: JsonPropertyName("plateauTileCount")] int    PlateauTileCount);

record CoverageSummary(
    [property: JsonPropertyName("poweredZonedTilesCount")]   int    PoweredZonedTilesCount,
    [property: JsonPropertyName("unpoweredZonedTilesCount")] int    UnpoweredZonedTilesCount,
    [property: JsonPropertyName("policeCoveragePercent")]    double PoliceCoveragePercent,
    [property: JsonPropertyName("fireCoveragePercent")]      double FireCoveragePercent,
    [property: JsonPropertyName("schoolCoveragePercent")]    double SchoolCoveragePercent,
    [property: JsonPropertyName("hospitalCoveragePercent")]  double HospitalCoveragePercent,
    [property: JsonPropertyName("avgPollution")]             double AvgPollution,
    [property: JsonPropertyName("avgHappiness")]             double AvgHappiness,
    [property: JsonPropertyName("overloadedRoadCount")]      int    OverloadedRoadCount = 0,
    [property: JsonPropertyName("avgTrafficLoad")]           double AvgTrafficLoad = 0.0,
    [property: JsonPropertyName("landValueAvg")]             double LandValueAvg = 0.0,
    [property: JsonPropertyName("landValueMax")]             double LandValueMax = 0.0,
    // G4: capacity model fields
    [property: JsonPropertyName("schoolSeatsUsed")]          int    SchoolSeatsUsed = 0,
    [property: JsonPropertyName("schoolSeatsTotal")]         int    SchoolSeatsTotal = 0,
    [property: JsonPropertyName("policeCapacityUsed")]       int    PoliceCapacityUsed = 0,
    [property: JsonPropertyName("policeCapacityTotal")]      int    PoliceCapacityTotal = 0,
    [property: JsonPropertyName("fireCapacityUsed")]         int    FireCapacityUsed = 0,
    [property: JsonPropertyName("fireCapacityTotal")]        int    FireCapacityTotal = 0,
    [property: JsonPropertyName("hospitalBedsUsed")]         int    HospitalBedsUsed = 0,
    [property: JsonPropertyName("hospitalBedsTotal")]        int    HospitalBedsTotal = 0);

record PowerState(
    [property: JsonPropertyName("supplyMW")]       int    SupplyMW,
    [property: JsonPropertyName("demandMW")]       int    DemandMW,
    [property: JsonPropertyName("capacityRatio")]  double CapacityRatio,
    [property: JsonPropertyName("isBrownout")]     bool   IsBrownout);

record WorkerFlowState(
    [property: JsonPropertyName("workersRouted")]          int    WorkersRouted,
    [property: JsonPropertyName("averageCommuteDistance")] double AverageCommuteDistance,
    [property: JsonPropertyName("unroutedWorkers")]        int    UnroutedWorkers,
    [property: JsonPropertyName("overloadedEdges")]        int    OverloadedEdges);

record OverlayTile(
    [property: JsonPropertyName("x")]     int    X,
    [property: JsonPropertyName("y")]     int    Y,
    [property: JsonPropertyName("value")] double Value);

/// <summary>
/// Sparse overlay snapshot. <c>tiles</c> contains only entries where value &gt; 0;
/// absent tiles should be treated as value = 0 by the reader.
/// </summary>
record OverlayState(
    [property: JsonPropertyName("overlay")] string            Overlay,
    [property: JsonPropertyName("tick")]    int               Tick,
    [property: JsonPropertyName("width")]   int               Width,
    [property: JsonPropertyName("height")]  int               Height,
    [property: JsonPropertyName("tiles")]   List<OverlayTile> Tiles);

record ServerState(
    int Tick,
    bool Paused,
    int Population,
    int MaxCapacity,
    double Balance,
    double TaxPerTick,
    double CommercialIncomePerTick,
    double MaintenancePerTick,
    double NetPerTick,
    double Happiness,
    [property: JsonPropertyName("milestoneReached")] string? MilestoneReached,
    double Pollution,
    string GameState,
    List<string> Milestones,
    List<TileState> Tiles,
    BuildingStateInfo[]? BuildingList = null,
    Dictionary<string, int>? BuildingSummary = null,
    string? NextMilestoneName = null,
    int NextMilestoneTarget = 0,
    NextMilestoneInfo? NextMilestone = null,
    string? ActiveEventName = null,
    string? ActiveEventDescription = null,
    string? LatestEventBanner = null,
    double TaxModifier = 0.0,
    string? SessionId = null,
    int AvailableJobs = 0,
    int WorkingAge = 0,
    double EmploymentRatio = 1.0,
    bool EmploymentWarning = false,
    int RequiredJobs = 0,
    double EventHappinessPenalty = 0.0,
    HappinessBreakdown? HappinessBreakdown = null,
    EmploymentState? Employment = null,
    CoverageSummary? CoverageSummary = null,
    string? PauseReason = null,
    int? TicksRun = null,
    List<string>? RecentEvents = null,
    string? Error = null,
    PowerState? Power = null,
    string? LastCommand = null,
    TerrainSummary? Terrain = null,
    int RoadGraphNodes = 0,
    WorkerFlowState? WorkerFlow = null,
    int EventTileX = -1,   // X coord of tile currently on fire (-1 = none)
    int EventTileY = -1,   // Y coord of tile currently on fire (-1 = none)
    string[]? LastDegradedBuildings = null,  // typeIds demolished by BuildingDegradationSystem this tick
    string[]? LastNewBuildingTypeIds = null, // typeIds created by BuildingGrowthSystem this tick
    // Scenario tracking (null/0 when sandbox)
    string? ActiveScenarioId = null,
    string? ActiveScenarioName = null,
    int ScenarioTargetPopulation = 0,
    int ScenarioTickLimit = 0,
    int ScenarioBronzeTick = 0,
    int ScenarioSilverTick = 0,
    int ScenarioGoldTick = 0,
    bool ScenarioComplete = false,
    string? MedalEarned = null,
    bool ScenarioFailed = false,
    int ParkTiles = 0,                      // count of Park zone tiles
    string? PersonalBestMedal = null,       // personal best medal from leaderboard
    int PersonalBestTick = 0,              // tick count of personal best run
    // Policy system
    bool PolicyGreenCity = false,
    bool PolicyIndustrialHub = false,
    bool PolicyCommercialBoost = false,
    bool PolicyOpenCity = false,
    int PolicyTotalCostPerTick = 0,
    // Manual upgrade result: "ok:newTypeId:-cost" or "err:reason" — null when no upgrade was attempted
    string? LastUpgradeResult = null,
    // Event response system — set when an event fires and player hasn't responded yet
    string? PendingEventType = null,
    int PendingEventCost = 0,
    // Zone constraints from active scenario (null = all zones allowed)
    List<string>? DisabledZones = null);
