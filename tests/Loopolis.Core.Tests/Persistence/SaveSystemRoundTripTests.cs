using Loopolis.Core.Charters;
using Loopolis.Core.Grid;
using Loopolis.Core.Persistence;
using Loopolis.Core.Policies;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Persistence;

/// <summary>
/// Round-trip tests for all systems that must survive save/load.
/// Each test:
///   1. Sets up state in a system.
///   2. Captures → serialises → deserialises → restores.
///   3. Asserts the restored state matches the original.
///
/// Also includes "gap exposure" tests that prove the OLD behaviour (no-op restore) would have
/// broken game state — these pass only because the fix is now in place.
/// </summary>
[TestFixture]
public class SaveSystemRoundTripTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static SimulationEngine MakeEngine(CityGrid grid)
    {
        var budget   = new BudgetSystem(initialBalance: 5_000);
        var pop      = new PopulationSystem();
        var power    = new PowerNetwork();
        var roads    = new RoadNetwork();
        var demand   = new DemandSystem();
        return new SimulationEngine(grid, budget, pop, power, roads, demand, seed: 42);
    }

    private static (SaveGame save, string json) CaptureAndSerialise(SimulationEngine engine, CityGrid grid)
    {
        var save = SaveSystem.Capture(engine, grid, terrainSeed: 1, taxLevel: "normal", tick: 100);
        var json = SaveSystem.Serialize(save);
        return (save, json);
    }

    // ── Version number ───────────────────────────────────────────────────────────

    [Test]
    public void CurrentVersion_Is4()
    {
        Assert.That(SaveSystem.CurrentVersion, Is.EqualTo(4),
            "Version should be bumped to 4 after adding charter/milestone/fatigue fields.");
    }

    // ── PolicySystem round-trip ──────────────────────────────────────────────────

    [Test]
    public void Policies_ActivePolicy_RoundTrips()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        engine.PolicySystem.ActivatePolicy(PolicyType.GreenCity);
        engine.PolicySystem.ActivatePolicy(PolicyType.OpenCity);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestorePolicies(freshEngine.PolicySystem, loaded);

        Assert.That(freshEngine.PolicySystem.IsActive(PolicyType.GreenCity),
            Is.True, "GreenCity should be restored.");
        Assert.That(freshEngine.PolicySystem.IsActive(PolicyType.OpenCity),
            Is.True, "OpenCity should be restored.");
        Assert.That(freshEngine.PolicySystem.IsActive(PolicyType.IndustrialHub),
            Is.False, "IndustrialHub was not active — should not be restored.");
    }

    [Test]
    public void Policies_NoPoliciesActive_RestoresClean()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        // Activate then save with none active
        engine.PolicySystem.ActivatePolicy(PolicyType.CommercialBoost);
        engine.PolicySystem.DeactivatePolicy(PolicyType.CommercialBoost);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        // Pre-activate something that should be cleared
        freshEngine.PolicySystem.ActivatePolicy(PolicyType.GreenCity);
        SaveSystem.RestorePolicies(freshEngine.PolicySystem, loaded);

        Assert.That(freshEngine.PolicySystem.ActivePolicies, Is.Empty,
            "No policies should be active after restoring a save with no active policies.");
    }

    // ── CharterSystem round-trip ─────────────────────────────────────────────────

    [Test]
    public void Charters_TownCharter_RoundTrips()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        engine.Charters.SelectCharter(CharterType.Merchant);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreCharters(freshEngine.Charters, loaded);

        Assert.That(freshEngine.Charters.ActiveCharter, Is.EqualTo(CharterType.Merchant),
            "Town charter (Merchant) should round-trip.");
        Assert.That(freshEngine.Charters.TownCharterPending, Is.False,
            "TownCharterPending should be false once a charter is chosen.");
    }

    [Test]
    public void Charters_CityCharter_RoundTrips()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        engine.Charters.SelectCityCharter(CharterType.GreenCanopy);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreCharters(freshEngine.Charters, loaded);

        Assert.That(freshEngine.Charters.CityCharter, Is.EqualTo(CharterType.GreenCanopy),
            "City charter (GreenCanopy) should round-trip.");
    }

    [Test]
    public void Charters_MetropolisCharter_RoundTrips()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        engine.Charters.SelectMetropolisCharter(CharterType.EmpireOfSteel);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreCharters(freshEngine.Charters, loaded);

        Assert.That(freshEngine.Charters.MetropolisCharter, Is.EqualTo(CharterType.EmpireOfSteel),
            "Metropolis charter (EmpireOfSteel) should round-trip.");
    }

    [Test]
    public void Charters_AllThreeChartersSimultaneously_RoundTrip()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        engine.Charters.SelectCharter(CharterType.Industrial);
        engine.Charters.SelectCityCharter(CharterType.TradeCorridors);
        engine.Charters.SelectMetropolisCharter(CharterType.NexusCity);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreCharters(freshEngine.Charters, loaded);

        Assert.That(freshEngine.Charters.ActiveCharter,     Is.EqualTo(CharterType.Industrial));
        Assert.That(freshEngine.Charters.CityCharter,       Is.EqualTo(CharterType.TradeCorridors));
        Assert.That(freshEngine.Charters.MetropolisCharter, Is.EqualTo(CharterType.NexusCity));
    }

    [Test]
    public void Charters_TownCharterPending_RoundTrips()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        // Simulate just reaching Town: pending flag set, no charter chosen yet
        engine.Charters.NotifyTownMilestone();

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreCharters(freshEngine.Charters, loaded);

        Assert.That(freshEngine.Charters.TownCharterPending, Is.True,
            "TownCharterPending should be preserved when player has not yet chosen a charter.");
        Assert.That(freshEngine.Charters.ActiveCharter, Is.EqualTo(CharterType.None),
            "ActiveCharter should still be None when pending.");
    }

    [Test]
    public void Charters_NoCharters_OlderSavesDeserialiseClean()
    {
        // Simulate a v3 save that has no charter fields (null)
        var v3Save = new SaveGame(
            Version:    3,
            Tick:       50,
            Balance:    5_000,
            TaxLevel:   "normal",
            GameState:  "Town",
            TerrainSeed: 0,
            Tiles:      [],
            ActiveCharter:     null,
            CityCharter:       null,
            MetropolisCharter: null
        );

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreCharters(freshEngine.Charters, v3Save);

        Assert.That(freshEngine.Charters.ActiveCharter,     Is.EqualTo(CharterType.None));
        Assert.That(freshEngine.Charters.CityCharter,       Is.EqualTo(CharterType.None));
        Assert.That(freshEngine.Charters.MetropolisCharter, Is.EqualTo(CharterType.None));
    }

    [Test]
    public void Charters_MerchantCharter_MultipliersAreCorrectAfterRestore()
    {
        // Critical: verify wrong multipliers don't apply silently after load
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        engine.Charters.SelectCharter(CharterType.Merchant);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreCharters(freshEngine.Charters, loaded);

        Assert.That(freshEngine.Charters.CommercialGrowthMultiplier, Is.EqualTo(1.30).Within(0.001),
            "Merchant charter 1.30× commercial multiplier must survive save/load.");
        Assert.That(freshEngine.Charters.LandValueBonus, Is.EqualTo(0.06).Within(0.001),
            "Merchant charter +0.06 land value bonus must survive save/load.");
    }

    [Test]
    public void Charters_IndustrialCharter_MultipliersAreCorrectAfterRestore()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        engine.Charters.SelectCharter(CharterType.Industrial);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreCharters(freshEngine.Charters, loaded);

        Assert.That(freshEngine.Charters.IndustrialGrowthMultiplier, Is.EqualTo(1.35).Within(0.001),
            "Industrial charter 1.35× industrial multiplier must survive save/load.");
        Assert.That(freshEngine.Charters.JobsPerTileBonus, Is.EqualTo(10),
            "Industrial charter +10 jobs/tile bonus must survive save/load.");
    }

    // ── MilestoneSystem round-trip ───────────────────────────────────────────────

    [Test]
    public void Milestones_ActiveState_RoundTrips()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        // Simulate reaching City milestone
        engine.MilestoneSystem.Check(population: 5_001, balance: 1_000, netPerTick: 10, tick: 50);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreMilestones(freshEngine.MilestoneSystem, loaded);

        Assert.That(freshEngine.MilestoneSystem.CurrentState, Is.EqualTo(GameState.City),
            "City milestone state must survive save/load.");
    }

    [Test]
    public void Milestones_TownState_RoundTrips()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        engine.MilestoneSystem.Check(population: 600, balance: 1_000, netPerTick: 5, tick: 30);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreMilestones(freshEngine.MilestoneSystem, loaded);

        Assert.That(freshEngine.MilestoneSystem.CurrentState, Is.EqualTo(GameState.Town));
    }

    [Test]
    public void Milestones_ActiveState_DoesNotDefaultToActive()
    {
        // This is the "gap exposure" test: without the fix, CurrentState would reset to Active
        // even for a City-level save, causing wrong building unlocks.
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        engine.MilestoneSystem.Check(population: 6_000, balance: 1_000, netPerTick: 10, tick: 60);

        Assert.That(engine.MilestoneSystem.CurrentState, Is.EqualTo(GameState.City),
            "Pre-condition: engine should be at City milestone.");

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        // Without RestoreMilestones the state would be Active:
        SaveSystem.RestoreMilestones(freshEngine.MilestoneSystem, loaded);

        Assert.That(freshEngine.MilestoneSystem.CurrentState, Is.Not.EqualTo(GameState.Active),
            "After restore the milestone state must NOT silently reset to Active.");
    }

    [Test]
    public void Milestones_ReachedList_RoundTrips()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        engine.MilestoneSystem.Check(population: 600,   balance: 1_000, netPerTick: 5,  tick: 20);
        engine.MilestoneSystem.Check(population: 5_001, balance: 1_000, netPerTick: 10, tick: 50);

        Assert.That(engine.MilestoneSystem.Reached, Has.Count.EqualTo(2), "Pre-condition: Town + City reached.");

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreMilestones(freshEngine.MilestoneSystem, loaded);

        Assert.That(freshEngine.MilestoneSystem.Reached.Select(r => r.Name),
            Is.EquivalentTo(new[] { "Town", "City" }),
            "MilestonesReached list must round-trip correctly.");
    }

    [Test]
    public void Milestones_OlderSave_NoMilestonesField_ReconstructsFromGameState()
    {
        // Simulate a v3 save with no MilestonesReached field (null)
        var v3Save = new SaveGame(
            Version:    3,
            Tick:       80,
            Balance:    5_000,
            TaxLevel:   "normal",
            GameState:  "City",   // player was at City
            TerrainSeed: 0,
            Tiles:      [],
            MilestonesReached: null  // older save
        );

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreMilestones(freshEngine.MilestoneSystem, v3Save);

        Assert.That(freshEngine.MilestoneSystem.CurrentState, Is.EqualTo(GameState.City),
            "v3 save: GameState='City' should restore CurrentState to City.");
        Assert.That(freshEngine.MilestoneSystem.Reached.Select(r => r.Name),
            Does.Contain("Town"), "v3 save: Reached should be reconstructed with Town milestone.");
        Assert.That(freshEngine.MilestoneSystem.Reached.Select(r => r.Name),
            Does.Contain("City"), "v3 save: Reached should be reconstructed with City milestone.");
    }

    // ── ServiceFatigueSystem round-trip ──────────────────────────────────────────

    [Test]
    public void ServiceFatigue_CapacitySnapshot_RoundTrips()
    {
        var grid   = new CityGrid(10, 10);
        grid.SetZone(3, 3, ZoneType.FireStation);
        var engine = MakeEngine(grid);

        // Manually inject a non-default capacity to simulate wear
        var snapshot = new Dictionary<(int x, int y), double>
        {
            { (3, 3), 0.72 }
        };
        engine.ServiceFatigue.RestoreSnapshot(snapshot);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreServiceFatigue(freshEngine.ServiceFatigue, loaded);

        Assert.That(freshEngine.ServiceFatigue.GetCapacity(3, 3), Is.EqualTo(0.72).Within(0.001),
            "Service fatigue capacity should round-trip correctly.");
    }

    [Test]
    public void ServiceFatigue_MultipleServiceTiles_AllRoundTrip()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);

        var snapshot = new Dictionary<(int x, int y), double>
        {
            { (1, 1), 0.85 },
            { (5, 5), 0.55 },
            { (8, 8), 0.40 },
        };
        engine.ServiceFatigue.RestoreSnapshot(snapshot);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreServiceFatigue(freshEngine.ServiceFatigue, loaded);

        Assert.That(freshEngine.ServiceFatigue.GetCapacity(1, 1), Is.EqualTo(0.85).Within(0.001));
        Assert.That(freshEngine.ServiceFatigue.GetCapacity(5, 5), Is.EqualTo(0.55).Within(0.001));
        Assert.That(freshEngine.ServiceFatigue.GetCapacity(8, 8), Is.EqualTo(0.40).Within(0.001));
    }

    [Test]
    public void ServiceFatigue_UnknownTile_DefaultsToFullCapacity()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);

        // Only tile (2, 2) is fatigued — tile (7, 7) is unknown (not in snapshot)
        var snapshot = new Dictionary<(int x, int y), double>
        {
            { (2, 2), 0.60 }
        };
        engine.ServiceFatigue.RestoreSnapshot(snapshot);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreServiceFatigue(freshEngine.ServiceFatigue, loaded);

        Assert.That(freshEngine.ServiceFatigue.GetCapacity(7, 7), Is.EqualTo(1.0).Within(0.001),
            "Tiles not in snapshot should return full capacity (1.0).");
    }

    [Test]
    public void ServiceFatigue_NullInOlderSave_IsNoOp()
    {
        var v3Save = new SaveGame(
            Version:    3,
            Tick:       50,
            Balance:    5_000,
            TaxLevel:   "normal",
            GameState:  "City",
            TerrainSeed: 0,
            Tiles:      [],
            ServiceFatigue: null  // older save
        );

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        // Should not throw; fatigued tile remains at default 1.0
        Assert.DoesNotThrow(() => SaveSystem.RestoreServiceFatigue(freshEngine.ServiceFatigue, v3Save));
        Assert.That(freshEngine.ServiceFatigue.GetCapacity(0, 0), Is.EqualTo(1.0),
            "Restoring null ServiceFatigue should be a no-op — all tiles remain at 1.0.");
    }

    [Test]
    public void ServiceFatigue_FatigueWarning_NeedsRenovationFlagPreserved()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);

        // Capacity at 0.55 — below FatigueWarning threshold (0.60) → NeedsRenovation = true
        var snapshot = new Dictionary<(int x, int y), double> { { (4, 4), 0.55 } };
        engine.ServiceFatigue.RestoreSnapshot(snapshot);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreServiceFatigue(freshEngine.ServiceFatigue, loaded);

        Assert.That(freshEngine.ServiceFatigue.NeedsRenovation(4, 4), Is.True,
            "NeedsRenovation flag should be true for tiles below 0.60 capacity after restore.");
    }

    // ── Full engine restore integration test ─────────────────────────────────────

    [Test]
    public void FullRestore_CharterEffectsApplyOnNextTick()
    {
        // Prove that charter multipliers from a restored game are actually applied during
        // the first tick after load (not silently lost).
        var grid   = new CityGrid(10, 10);
        grid.SetFlatTerrain();
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(6, 5, ZoneType.Commercial);
        var engine = MakeEngine(grid);
        engine.Charters.SelectCharter(CharterType.Merchant);

        var save  = SaveSystem.Capture(engine, grid, terrainSeed: 0, taxLevel: "normal", tick: 10);
        var json  = SaveSystem.Serialize(save);
        var loaded = SaveSystem.Deserialize(json)!;

        var freshGrid   = new CityGrid(10, 10);
        SaveSystem.RestoreGrid(freshGrid, loaded);
        var freshEngine = MakeEngine(freshGrid);
        SaveSystem.RestorePolicies(freshEngine.PolicySystem, loaded);
        SaveSystem.RestoreMilestones(freshEngine.MilestoneSystem, loaded);
        SaveSystem.RestoreCharters(freshEngine.Charters, loaded);

        // Merchant charter gives 1.30× commercial growth — verify multiplier is live
        Assert.That(freshEngine.Charters.EffectiveCommercialGrowthMultiplier,
            Is.EqualTo(1.30).Within(0.001),
            "Merchant charter commercial multiplier must be active on the restored engine.");
    }

    [Test]
    public void FullRestore_MilestoneStatePreventsDuplicateCharterDialog()
    {
        // If Town milestone is already reached + charter chosen, restoring should NOT
        // set TownCharterPending = true again (which would re-pop the charter dialog).
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        engine.MilestoneSystem.Check(600, 1_000, 5, tick: 20);  // Town milestone
        engine.Charters.NotifyTownMilestone();
        engine.Charters.SelectCharter(CharterType.Civic);

        var (_, json) = CaptureAndSerialise(engine, grid);
        var loaded    = SaveSystem.Deserialize(json)!;

        var freshEngine = MakeEngine(new CityGrid(10, 10));
        SaveSystem.RestoreMilestones(freshEngine.MilestoneSystem, loaded);
        SaveSystem.RestoreCharters(freshEngine.Charters, loaded);

        Assert.That(freshEngine.Charters.TownCharterPending, Is.False,
            "TownCharterPending must be false when charter was already chosen before save.");
        Assert.That(freshEngine.Charters.ActiveCharter, Is.EqualTo(CharterType.Civic),
            "Civic charter must be restored correctly.");
    }

    // ── SaveGame record has all new fields serialised ────────────────────────────

    [Test]
    public void SaveGame_NewFields_ArePresentInJson()
    {
        var grid   = new CityGrid(10, 10);
        var engine = MakeEngine(grid);
        engine.Charters.SelectCharter(CharterType.Industrial);
        engine.Charters.SelectCityCharter(CharterType.InnovationHub);
        engine.MilestoneSystem.Check(600, 1_000, 5, tick: 10);
        engine.MilestoneSystem.Check(5_001, 1_000, 10, tick: 50);

        var snapshot = new Dictionary<(int x, int y), double> { { (2, 2), 0.77 } };
        engine.ServiceFatigue.RestoreSnapshot(snapshot);

        var save = SaveSystem.Capture(engine, grid, terrainSeed: 0, taxLevel: "normal", tick: 55);
        var json = SaveSystem.Serialize(save);

        // Verify all critical new fields appear in serialised JSON
        Assert.That(json, Does.Contain("\"activeCharter\""),       "activeCharter key missing from JSON.");
        Assert.That(json, Does.Contain("\"cityCharter\""),         "cityCharter key missing from JSON.");
        Assert.That(json, Does.Contain("Industrial"),              "Charter value missing from JSON.");
        Assert.That(json, Does.Contain("\"milestonesReached\""),   "milestonesReached key missing from JSON.");
        Assert.That(json, Does.Contain("\"Town\""),                "Town milestone missing from JSON.");
        Assert.That(json, Does.Contain("\"serviceFatigue\""),      "serviceFatigue key missing from JSON.");
    }
}
