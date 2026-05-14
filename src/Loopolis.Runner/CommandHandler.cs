using System.Text.Json;
using Loopolis.Core.Charters;
using Loopolis.Core.Grid;
using Loopolis.Core.Policies;
using Loopolis.Core.Simulation;

namespace Loopolis.Runner;

/// <summary>
/// Processes a single command from the command file and mutates server state accordingly.
/// </summary>
static class CommandHandler
{
    public static void ProcessCommand(
        string cmdPath,
        string sharedDir,
        string tmpPath,
        string statePath,
        ref bool paused,
        ref double speed,
        ref int skipRemaining,
        ref int skipRequested,
        ref bool pauseAfterSkip,
        ref bool pauseOnEvent,
        ref CityGrid grid,
        ref SimulationEngine engine,
        string sessionId,
        List<string> recentEvents,
        ref string? lastUpgradeResult)
    {
        string json;
        try
        {
            if (!File.Exists(cmdPath)) return;
            json = File.ReadAllText(cmdPath);
            File.Delete(cmdPath);
        }
        catch (IOException)
        {
            return;
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return; }

        using (doc)
        {
            var root = doc.RootElement;

            // Session ID validation: if command carries a sessionId that doesn't match ours, ignore it
            if (root.TryGetProperty("sessionId", out var cmdSessionProp))
            {
                var cmdSession = cmdSessionProp.GetString();
                if (cmdSession != null && cmdSession != sessionId)
                {
                    Console.WriteLine($"[command] Warning: session mismatch (got {cmdSession}, expected {sessionId}) — ignoring stale command.");
                    return;
                }
            }

            if (!root.TryGetProperty("cmd", out var cmdProp)) return;
            var cmd = cmdProp.GetString() ?? "";

            switch (cmd)
            {
                case "pause":
                    paused = true;
                    Console.WriteLine($"[paused] Tick {engine.TickCount}");
                    break;

                case "resume":
                    paused = false;
                    Console.WriteLine("[resumed]");
                    break;

                case "place_zone":
                    if (root.TryGetProperty("x", out var xProp) &&
                        root.TryGetProperty("y", out var yProp) &&
                        root.TryGetProperty("zone", out var zoneProp))
                    {
                        var x    = xProp.GetInt32();
                        var y    = yProp.GetInt32();
                        var zone = zoneProp.GetString() ?? "";
                        if (!grid.IsInBounds(x, y))
                        {
                            var errMsg = $"Placement out of bounds: ({x},{y}) is outside grid ({grid.Width}x{grid.Height})";
                            Console.WriteLine($"[place_zone] {errMsg}");
                            StateWriter.WriteStateWithError(tmpPath, statePath, engine, grid, paused, sessionId, errMsg, recentEvents);
                            break;
                        }
                        var placementCost = BudgetSystem.PlacementCosts.GetValueOrDefault(zone, 0.0);
                        if (!engine.Budget.CanAfford(placementCost))
                        {
                            Console.WriteLine($"[place_zone] insufficient_funds: need ${placementCost:N0}, have ${engine.Budget.Balance:N0}");
                            break;
                        }
                        if (Enum.TryParse<ZoneType>(zone, out var zoneType))
                        {
                            // Reject before charging if tile is occupied and this is not an erase
                            if (zoneType != ZoneType.Empty && grid.GetTile(x, y).Zone != ZoneType.Empty)
                            {
                                Console.WriteLine($"[place_zone] ({x},{y}) occupied by {grid.GetTile(x,y).Zone} — use erase first");
                                break;
                            }
                            // Milestone gate: PoliceHQ, FireHQ, Hospital require City milestone
                            var (allowed, gateError) = engine.MilestoneSystem.CanPlace(zoneType, engine.Population.Population);
                            if (!allowed)
                            {
                                Console.WriteLine($"[place_zone] milestone_gate: {gateError}");
                                StateWriter.WriteStateWithError(tmpPath, statePath, engine, grid, paused, sessionId, gateError!, recentEvents);
                                break;
                            }
                            // Cliff guard: Road and Avenue cannot span height differences > 1
                            if (zoneType == ZoneType.Road || zoneType == ZoneType.Avenue)
                            {
                                var (roadOk, roadErr) = grid.CanPlaceRoad(x, y);
                                if (!roadOk)
                                {
                                    Console.WriteLine($"[place_zone] cliff_guard: {roadErr}");
                                    StateWriter.WriteStateWithError(tmpPath, statePath, engine, grid, paused, sessionId, roadErr!, recentEvents);
                                    break;
                                }
                            }
                            engine.Budget.Charge(placementCost);
                            engine.PlaceTile(x, y, zoneType);
                            Console.WriteLine($"[place_zone] ({x},{y}) => {zoneType} (cost: ${placementCost:N0})");
                        }
                        else
                        {
                            Console.WriteLine($"[place_zone] Unknown zone type: {zone}");
                        }
                    }
                    break;

                case "erase":
                    if (root.TryGetProperty("x", out var exProp) &&
                        root.TryGetProperty("y", out var eyProp))
                    {
                        var x = exProp.GetInt32();
                        var y = eyProp.GetInt32();
                        if (!grid.IsInBounds(x, y))
                        {
                            var errMsg = $"Placement out of bounds: ({x},{y}) is outside grid ({grid.Width}x{grid.Height})";
                            Console.WriteLine($"[erase] {errMsg}");
                            StateWriter.WriteStateWithError(tmpPath, statePath, engine, grid, paused, sessionId, errMsg, recentEvents);
                            break;
                        }
                        engine.EraseTile(x, y);
                        Console.WriteLine($"[erase] ({x},{y}) => Empty");
                    }
                    break;

                case "place_rect":
                    if (root.TryGetProperty("x1", out var rx1Prop) &&
                        root.TryGetProperty("y1", out var ry1Prop) &&
                        root.TryGetProperty("x2", out var rx2Prop) &&
                        root.TryGetProperty("y2", out var ry2Prop) &&
                        root.TryGetProperty("zone", out var rzProp))
                    {
                        var rZone = rzProp.GetString() ?? "";
                        // Normalise so x1 ≤ x2, y1 ≤ y2
                        var rx1 = Math.Min(rx1Prop.GetInt32(), rx2Prop.GetInt32());
                        var rx2 = Math.Max(rx1Prop.GetInt32(), rx2Prop.GetInt32());
                        var ry1 = Math.Min(ry1Prop.GetInt32(), ry2Prop.GetInt32());
                        var ry2 = Math.Max(ry1Prop.GetInt32(), ry2Prop.GetInt32());

                        if (!Enum.TryParse<ZoneType>(rZone, out var rZoneType))
                        {
                            Console.WriteLine($"[place_rect] Unknown zone type: {rZone}");
                            break;
                        }

                        var placedCount       = 0;
                        var skippedOob        = 0;
                        var skippedMilestone  = 0;
                        var skippedOccupied   = 0;
                        var skippedFunds      = 0;
                        var skippedCliff      = 0;
                        var placementCost     = BudgetSystem.PlacementCosts.GetValueOrDefault(rZone, 0.0);

                        for (var ry = ry1; ry <= ry2; ry++)
                        for (var rx = rx1; rx <= rx2; rx++)
                        {
                            // Out-of-bounds: skip but continue
                            if (!grid.IsInBounds(rx, ry)) { skippedOob++; continue; }

                            // Occupied tile: skip
                            if (rZoneType != ZoneType.Empty && grid.GetTile(rx, ry).Zone != ZoneType.Empty)
                            { skippedOccupied++; continue; }

                            // Milestone gate
                            var (rAllowed, _) = engine.MilestoneSystem.CanPlace(rZoneType, engine.Population.Population);
                            if (!rAllowed) { skippedMilestone++; continue; }

                            // Cliff guard for roads
                            if (rZoneType == ZoneType.Road || rZoneType == ZoneType.Avenue)
                            {
                                var (roadOk, _) = grid.CanPlaceRoad(rx, ry);
                                if (!roadOk) { skippedCliff++; continue; }
                            }

                            // Funds check per tile
                            if (!engine.Budget.CanAfford(placementCost)) { skippedFunds++; continue; }

                            engine.Budget.Charge(placementCost);
                            engine.PlaceTile(rx, ry, rZoneType);
                            placedCount++;
                        }

                        var rectMsg = $"place_rect placed {placedCount} tiles ({rZone}) in ({rx1},{ry1})–({rx2},{ry2})";
                        var warnings = new List<string>();
                        if (skippedOob       > 0) warnings.Add($"{skippedOob} tiles skipped (out of bounds)");
                        if (skippedMilestone > 0) warnings.Add($"{skippedMilestone} tiles skipped (milestone gate)");
                        if (skippedOccupied  > 0) warnings.Add($"{skippedOccupied} tiles skipped (occupied)");
                        if (skippedFunds     > 0) warnings.Add($"{skippedFunds} tiles skipped (insufficient funds)");
                        if (skippedCliff     > 0) warnings.Add($"{skippedCliff} tiles skipped (cliff — height diff too large)");
                        var warnStr = warnings.Count > 0 ? " | warnings: " + string.Join("; ", warnings) : "";
                        Console.WriteLine($"[place_rect] {rectMsg}{warnStr}");
                        StateWriter.WriteState(tmpPath, statePath, engine, grid, paused, sessionId,
                            pauseReason: null, ticksRun: null, recentEvents: recentEvents,
                            lastCommand: rectMsg + (warnings.Count > 0 ? " | " + string.Join("; ", warnings) : null));
                    }
                    break;

                case "erase_rect":
                    if (root.TryGetProperty("x1", out var erx1Prop) &&
                        root.TryGetProperty("y1", out var ery1Prop) &&
                        root.TryGetProperty("x2", out var erx2Prop) &&
                        root.TryGetProperty("y2", out var ery2Prop))
                    {
                        // Normalise so x1 ≤ x2, y1 ≤ y2
                        var erx1 = Math.Min(erx1Prop.GetInt32(), erx2Prop.GetInt32());
                        var erx2 = Math.Max(erx1Prop.GetInt32(), erx2Prop.GetInt32());
                        var ery1 = Math.Min(ery1Prop.GetInt32(), ery2Prop.GetInt32());
                        var ery2 = Math.Max(ery1Prop.GetInt32(), ery2Prop.GetInt32());

                        var erasedCount = 0;
                        var erSkippedOob = 0;

                        for (var ery = ery1; ery <= ery2; ery++)
                        for (var erx = erx1; erx <= erx2; erx++)
                        {
                            if (!grid.IsInBounds(erx, ery)) { erSkippedOob++; continue; }
                            engine.EraseTile(erx, ery);
                            erasedCount++;
                        }

                        var eraseMsg = $"erase_rect erased {erasedCount} tiles in ({erx1},{ery1})–({erx2},{ery2})";
                        var erWarnStr = erSkippedOob > 0 ? $" | {erSkippedOob} tiles skipped (out of bounds)" : "";
                        Console.WriteLine($"[erase_rect] {eraseMsg}{erWarnStr}");
                        StateWriter.WriteState(tmpPath, statePath, engine, grid, paused, sessionId,
                            pauseReason: null, ticksRun: null, recentEvents: recentEvents,
                            lastCommand: eraseMsg + (erSkippedOob > 0 ? $" | {erSkippedOob} tiles skipped (out of bounds)" : null));
                    }
                    break;

                case "new_game":
                {
                    var newGrid   = new CityGrid(32, 32);
                    var newBudget = new BudgetSystem(); // uses default $4,000
                    var newPop    = new PopulationSystem();
                    var newPower  = new PowerNetwork();
                    var newRoads  = new RoadNetwork();
                    var newDemand = new DemandSystem();
                    grid   = newGrid;
                    engine = new SimulationEngine(newGrid, newBudget, newPop, newPower, newRoads, newDemand);
                    Console.WriteLine("[new_game] Reset to empty 32x32 grid, $4000 starting balance.");
                    break;
                }

                case "set_speed":
                    if (root.TryGetProperty("ticksPerSecond", out var tpsProp))
                    {
                        speed = tpsProp.GetDouble();
                        Console.WriteLine($"[set_speed] {speed} t/s");
                    }
                    break;

                case "skip":
                    if (root.TryGetProperty("ticks", out var ticksProp))
                    {
                        var count = ticksProp.GetInt32();
                        skipRemaining  = count;
                        skipRequested  = count;
                        pauseAfterSkip = root.TryGetProperty("pauseAfter", out var paProp) && paProp.GetBoolean();
                        // pauseOnEvent defaults to true when absent
                        pauseOnEvent   = !root.TryGetProperty("pauseOnEvent", out var poeProp) || poeProp.GetBoolean();
                        Console.WriteLine($"[skip] Starting {count} ticks... pauseOnEvent={pauseOnEvent}");
                    }
                    break;

                case "set_tax":
                    if (root.TryGetProperty("level", out var levelProp))
                    {
                        var level = levelProp.GetString() ?? "normal";
                        engine.Budget.SetTaxRate(level);
                        Console.WriteLine($"[set_tax] level={level} taxRate={engine.Budget.TaxRate:P0} modifier={engine.Budget.TaxModifier:+0.00;-0.00;0.00}");
                    }
                    break;

                case "set_policy":
                    // {"cmd":"set_policy","policy":"GreenCity","active":true}
                    if (root.TryGetProperty("policy", out var policyProp) &&
                        root.TryGetProperty("active", out var activeProp))
                    {
                        var policyName = policyProp.GetString() ?? "";
                        var isActive   = activeProp.GetBoolean();
                        if (Enum.TryParse<PolicyType>(policyName, out var policyType))
                        {
                            if (isActive)
                                engine.PolicySystem.ActivatePolicy(policyType);
                            else
                                engine.PolicySystem.DeactivatePolicy(policyType);
                            Console.WriteLine($"[set_policy] {policyName}={isActive}, total cost={engine.PolicySystem.GetCostPerTick()}/tick");
                        }
                        else
                        {
                            Console.WriteLine($"[set_policy] Unknown policy: {policyName}");
                        }
                    }
                    break;

                case "query_overlay":
                    if (root.TryGetProperty("overlay", out var overlayProp))
                    {
                        var overlayType = overlayProp.GetString() ?? "";
                        StateWriter.WriteOverlay(sharedDir, sessionId, engine, grid, overlayType);
                    }
                    else
                    {
                        Console.WriteLine("[query_overlay] Missing 'overlay' property");
                    }
                    break;

                case "manual_upgrade":
                    if (root.TryGetProperty("x", out var muXProp) &&
                        root.TryGetProperty("y", out var muYProp))
                    {
                        var muX = muXProp.GetInt32();
                        var muY = muYProp.GetInt32();
                        if (!grid.IsInBounds(muX, muY))
                        {
                            var errMsg = $"manual_upgrade out of bounds: ({muX},{muY})";
                            Console.WriteLine($"[manual_upgrade] {errMsg}");
                            lastUpgradeResult = $"err:{errMsg}";
                            break;
                        }
                        var upgradeResult = engine.ManualUpgrade(muX, muY);
                        if (upgradeResult.Success)
                        {
                            lastUpgradeResult = $"ok:{upgradeResult.NewBuildingTypeId}:-{upgradeResult.Cost}";
                            Console.WriteLine(
                                $"[manual_upgrade] ({muX},{muY}) → {upgradeResult.NewBuildingTypeId} " +
                                $"(cost: ${upgradeResult.Cost:N0}, balance: ${engine.Budget.Balance:N0})");
                        }
                        else
                        {
                            lastUpgradeResult = $"err:{upgradeResult.Reason}";
                            Console.WriteLine($"[manual_upgrade] failed at ({muX},{muY}): {upgradeResult.Reason}");
                        }
                    }
                    break;

                case "event_respond":
                {
                    var success = engine.RespondToCurrentEvent();
                    if (success)
                        Console.WriteLine($"[event_respond] Player intervened. Cost: ${engine.EventSystem.ActiveResponse?.Cost ?? 0}, Balance: ${engine.Budget.Balance:N0}");
                    else
                        Console.WriteLine($"[event_respond] No pending event or insufficient funds.");
                    break;
                }

                case "renovate_service":
                {
                    if (root.TryGetProperty("x", out var rsXProp) &&
                        root.TryGetProperty("y", out var rsYProp))
                    {
                        var rx = rsXProp.GetInt32();
                        var ry = rsYProp.GetInt32();
                        var success = engine.RenovateService(rx, ry);
                        if (success)
                            Console.WriteLine($"[renovate_service] ({rx},{ry}) renovated. Balance: ${engine.Budget.Balance:N0}");
                        else
                            Console.WriteLine($"[renovate_service] ({rx},{ry}) failed — not a tracked service tile or insufficient funds (need ${ServiceFatigueSystem.RenovationCost:N0})");
                    }
                    break;
                }

                case "select_charter":
                    // {"cmd":"select_charter","charter":"Merchant"}
                    if (root.TryGetProperty("charter", out var charterProp))
                    {
                        var charterName = charterProp.GetString() ?? "";
                        if (Enum.TryParse<CharterType>(charterName, true, out var charterType))
                        {
                            engine.Charters.SelectCharter(charterType);
                            Console.WriteLine($"[select_charter] {charterName} selected. ActiveCharter={engine.Charters.ActiveCharter}");
                        }
                        else
                        {
                            Console.WriteLine($"[select_charter] Unknown charter: {charterName}");
                        }
                    }
                    break;

                case "select_city_charter":
                {
                    // {"cmd":"select_city_charter","charter":"InnovationHub"}
                    var cityCharterName = root.TryGetProperty("charter", out var ccProp)
                        ? ccProp.GetString() ?? ""
                        : "";
                    if (Enum.TryParse<CharterType>(cityCharterName, true, out var cityCharterType))
                    {
                        engine.Charters.SelectCityCharter(cityCharterType);
                        Console.WriteLine($"[select_city_charter] {cityCharterName} selected. CityCharter={engine.Charters.CityCharter}");
                    }
                    else
                    {
                        Console.WriteLine($"[select_city_charter] Unknown charter: {cityCharterName}");
                    }
                    break;
                }

                default:
                    Console.WriteLine($"[command] Unknown: {cmd}");
                    break;
            }
        }
    }
}
