using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Runner;

/// <summary>
/// Persistent server mode: watches a command file, ticks the simulation, writes state.json.
/// </summary>
static class SimulationServer
{
    public static void Start(string scenario, double initialSpeed, int terrainSeed = 0)
    {
        var sharedDir  = Path.Combine(StateWriter.FindSolutionRoot(), "godot", "shared");
        Directory.CreateDirectory(sharedDir);

        var sessionId   = Guid.NewGuid().ToString("N")[..8]; // e.g. "a3f7c219"
        var stateFile   = Path.Combine(sharedDir, $"state-{sessionId}.json");
        var tmpPath     = Path.Combine(sharedDir, $"state-{sessionId}.tmp.json");
        var commandFile = Path.Combine(sharedDir, $"command-{sessionId}.json");

        Console.WriteLine($"[loopolis] session={sessionId}");

        var (grid, engine) = ScenarioSetup.Setup(scenario, terrainSeed);

        var paused           = false;
        var speed            = initialSpeed;
        var skipRemaining    = 0;
        var skipRequested    = 0;    // total ticks requested in the last skip command
        var pauseAfterSkip   = false;
        var pauseOnEvent     = true;
        var skipPauseReason  = (string?)null;
        var recentEvents     = new List<string>();
        var lastUpgradeResult = (string?)null;  // "ok:newTypeId:-cost" or "err:reason"

        StateWriter.WriteState(tmpPath, stateFile, engine, grid, paused, sessionId, pauseReason: null, ticksRun: null, recentEvents: recentEvents);
        Console.WriteLine($"[server] Started. Scenario: {scenario}, Speed: {speed} t/s, Session: {sessionId}");
        Console.WriteLine($"[server] State: {stateFile}");
        Console.WriteLine($"[server] Commands: {commandFile}");

        try
        {
            while (true)
            {
                // 1. Read command
                var pausedBefore = paused;
                lastUpgradeResult = null;  // clear each iteration; set by ProcessCommand on manual_upgrade
                CommandHandler.ProcessCommand(commandFile, sharedDir, tmpPath, stateFile, ref paused, ref speed,
                    ref skipRemaining, ref skipRequested, ref pauseAfterSkip, ref pauseOnEvent,
                    ref grid, ref engine, sessionId, recentEvents, ref lastUpgradeResult);
                // grid/engine may have been replaced by new_game — use current references below

                // If the game was just paused, immediately flush state so state.json reflects Paused: true
                if (!pausedBefore && paused)
                    StateWriter.WriteState(tmpPath, stateFile, engine, grid, paused, sessionId, pauseReason: null, ticksRun: null, recentEvents: recentEvents, lastUpgradeResult: lastUpgradeResult);

                // 2. Tick (or skip, or wait)
                if (skipRemaining > 0)
                {
                    // Fast-forward: no writes, no sleep
                    var milestonesBefore = engine.MilestoneSystem.Reached.Count;
                    engine.Tick();
                    skipRemaining--;

                    // Check early-pause conditions when pauseOnEvent is enabled
                    if (pauseOnEvent)
                    {
                        // Tier 2 passive events: log but don't pause
                        var passiveEvent = DetectPassiveEvent(engine);
                        if (passiveEvent != null && !recentEvents.Contains(passiveEvent))
                        {
                            recentEvents.Add(passiveEvent);
                            Console.WriteLine($"[skip] passive event (no pause): {passiveEvent} at tick {engine.TickCount}");
                        }

                        // Tier 1 actionable events: pause immediately
                        var earlyReason = DetectActionableSkipPauseReason(engine, grid, milestonesBefore);
                        if (earlyReason != null)
                        {
                            var ticksDone = skipRequested - skipRemaining;
                            skipPauseReason = earlyReason;
                            skipRemaining = 0;
                            Console.WriteLine(
                                $"[skip] paused early at tick {engine.TickCount}: {earlyReason} " +
                                $"(ran {ticksDone}/{skipRequested} ticks)");
                            paused = true;
                            pauseAfterSkip = false;
                            StateWriter.WriteState(tmpPath, stateFile, engine, grid, paused, sessionId,
                                pauseReason: earlyReason, ticksRun: ticksDone, recentEvents: recentEvents);
                            recentEvents = new List<string>();
                            skipPauseReason = null;
                            continue;
                        }
                    }

                    if (skipRemaining == 0)
                    {
                        Console.WriteLine(
                            $"[skip] complete. Tick: {engine.TickCount}, Pop: {engine.Population.Population}, Balance: ${engine.Budget.Balance:N2}");
                        if (pauseAfterSkip)
                        {
                            paused = true;
                            pauseAfterSkip = false;
                        }
                        StateWriter.WriteState(tmpPath, stateFile, engine, grid, paused, sessionId,
                            pauseReason: null, ticksRun: skipRequested, recentEvents: recentEvents);
                        recentEvents = new List<string>();
                    }
                }
                else if (!paused)
                {
                    engine.Tick();
                    StateWriter.WriteState(tmpPath, stateFile, engine, grid, paused, sessionId, pauseReason: null, ticksRun: null, recentEvents: recentEvents, lastUpgradeResult: lastUpgradeResult);
                    var milestone = engine.MilestoneSystem.LatestMilestone;
                    var milestoneTag = milestone != null ? $" [{milestone.Name} {milestone.Emoji}]" : "";
                    Console.WriteLine(
                        $"[tick {engine.TickCount,4}] pop={engine.Population.Population} " +
                        $"happiness={engine.HappinessSystem.AverageHappiness(grid):F2} " +
                        $"pollution={engine.PollutionSystem.AveragePollution(grid):F2} " +
                        $"balance=${engine.Budget.Balance:N0} net=${engine.Budget.NetIncomePerTick:+0.#;-0.#}{milestoneTag}");
                    var sleepMs = (int)(1000.0 / speed);
                    Thread.Sleep(sleepMs);
                }
                else
                {
                    // Paused — poll for commands at 20 Hz.
                    // If a manual_upgrade was just processed while paused, flush state immediately
                    // so the result is visible to the Godot client.
                    if (lastUpgradeResult != null)
                        StateWriter.WriteState(tmpPath, stateFile, engine, grid, paused, sessionId, pauseReason: null, ticksRun: null, recentEvents: recentEvents, lastUpgradeResult: lastUpgradeResult);
                    Thread.Sleep(50);
                }
            }
        }
        finally
        {
            // Clean up session state file on exit so Godot discovery does not pick up stale data
            if (File.Exists(stateFile)) File.Delete(stateFile);
            Console.WriteLine($"[server] Cleaned up {stateFile}");
        }
    }

    /// <summary>
    /// Tier 1 — actionable events that require immediate player attention.
    /// Returns a pause-reason string if the skip should stop early, or null to continue.
    /// </summary>
    private static string? DetectActionableSkipPauseReason(SimulationEngine engine, CityGrid grid, int milestoneCountBefore)
    {
        // Milestone reached this tick
        if (engine.MilestoneSystem.Reached.Count > milestoneCountBefore)
            return "MilestoneReached";

        // Actionable events: FireBreak and PowerOutage need immediate player attention
        if (engine.LatestEventBanner != null)
        {
            var eventType = engine.EventSystem.ActiveEvent?.Type;
            if (eventType == CityEventType.FireBreak || eventType == CityEventType.PowerOutage)
                return eventType.ToString();
        }

        // Bankruptcy warning: balance < 1 tick of total costs
        var oneTick = engine.Budget.LastMaintenanceCost;
        if (engine.Budget.Balance < oneTick && engine.Budget.Balance < 0)
            return "BankruptcyWarning";

        // Brownout: supply dropped below demand this tick.
        // Only fire when there IS a power plant (IsActiveBrownout) AND the city is past the
        // 30-tick grace period.  No-plant early-game states (supply == 0) are NOT actionable.
        if (engine.PowerCapacitySystem.IsActiveBrownout && engine.TickCount > 30)
            return "Brownout";

        // Abandonment warning: happiness dropping below threshold
        var avgHappiness = engine.HappinessSystem.AverageHappiness(grid);
        if (avgHappiness < 0.30)
            return "AbandonmentWarning";

        return null;
    }

    /// <summary>
    /// Tier 2 — passive events that are informational only; no player action required.
    /// Returns the event name if a passive event just fired, or null if nothing new.
    /// </summary>
    private static string? DetectPassiveEvent(SimulationEngine engine)
    {
        if (engine.LatestEventBanner == null) return null;
        var eventType = engine.EventSystem.ActiveEvent?.Type;
        if (eventType == CityEventType.DemandSlump || eventType == CityEventType.CrimeWave)
            return eventType.ToString();
        return null;
    }
}
