// Loopolis SimulationRunner — the agent feedback loop tool
// Usage: dotnet run -- <ticks> [scenario] [--ascii]
//        dotnet run -- server <scenario> [--speed <n>]
// Output: JSON report for agent analysis, or ASCII city map

using Loopolis.Runner;

// ── Dispatch ────────────────────────────────────────────────────────────────

if (args.Length > 0 && args[0] == "server")
{
    var scenario     = args.Length > 1 ? args[1] : "default";
    var speedIndex   = Array.IndexOf(args, "--speed");
    var initialSpeed = speedIndex >= 0 && speedIndex + 1 < args.Length
        ? double.Parse(args[speedIndex + 1])
        : 1.0;
    var seedIndex    = Array.IndexOf(args, "--seed");
    var terrainSeed  = seedIndex >= 0 && seedIndex + 1 < args.Length
        ? int.Parse(args[seedIndex + 1])
        : new Random().Next();

    SimulationServer.Start(scenario, initialSpeed, terrainSeed);
    return;
}

// ── Original CLI mode ────────────────────────────────────────────────────────

HeadlessRunner.Run(args);
