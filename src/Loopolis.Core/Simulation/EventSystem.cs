using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

public enum CityEventType { None, FireBreak, CrimeWave }

public record CityEvent(CityEventType Type, string Name, string Description, int DurationTicks);

public class EventSystem
{
    private readonly Random _rng;
    private CityEvent? _activeEvent;
    private int _ticksRemaining;
    private int _cooldownTicks;      // ticks until next event can fire
    private const int MinCooldown = 150;
    private const int MaxCooldown = 300;

    public CityEvent? ActiveEvent => _activeEvent;
    public bool HasActiveEvent => _activeEvent != null;

    public double HappinessPenalty => _activeEvent?.Type switch
    {
        CityEventType.FireBreak  => -0.15,
        CityEventType.CrimeWave => -0.10,
        _ => 0.0
    };

    public EventSystem(Random? rng = null)
    {
        _rng = rng ?? Random.Shared;
        _cooldownTicks = 100; // give player time to set up before first event
    }

    /// <summary>Run each simulation tick. Returns a new event if one just fired (for banner display).</summary>
    public CityEvent? Tick(CityGrid grid, int population)
    {
        // Count-down cooldown
        if (_cooldownTicks > 0) { _cooldownTicks--; return null; }

        // Active event: count down
        if (_activeEvent != null)
        {
            _ticksRemaining--;
            if (_ticksRemaining <= 0)
            {
                _activeEvent = null;
                _cooldownTicks = _rng.Next(MinCooldown, MaxCooldown);
            }
            return null;
        }

        // Only trigger events once city has some population
        if (population < 100) return null;

        // Random trigger: ~1% chance per tick once cooldown expires
        if (_rng.NextDouble() > 0.01) return null;

        // Pick event type based on coverage status
        bool hasFireStation   = grid.AllTiles().Any(t => t.Zone == ZoneType.FireStation);
        bool hasPoliceStation = grid.AllTiles().Any(t => t.Zone == ZoneType.PoliceStation);

        // Prefer to trigger events for uncovered situations (teaches player)
        CityEventType type;
        if (!hasFireStation && !hasPoliceStation)
            type = _rng.NextDouble() < 0.5 ? CityEventType.FireBreak : CityEventType.CrimeWave;
        else if (!hasFireStation)
            type = CityEventType.FireBreak;
        else if (!hasPoliceStation)
            type = CityEventType.CrimeWave;
        else
            // Both covered → random, shorter duration (stations help)
            type = _rng.NextDouble() < 0.5 ? CityEventType.FireBreak : CityEventType.CrimeWave;

        bool isCovered = (type == CityEventType.FireBreak && hasFireStation) ||
                         (type == CityEventType.CrimeWave && hasPoliceStation);

        var duration = isCovered ? 20 : 60; // covered cities resolve faster

        _activeEvent = type switch
        {
            CityEventType.FireBreak => new CityEvent(type, "Fire Break!",
                isCovered ? "Fire stations contain the blaze" : "No fire stations! Happiness dropping fast",
                duration),
            _ => new CityEvent(type, "Crime Wave!",
                isCovered ? "Police keep the streets safe" : "No police stations! Citizens fleeing",
                duration),
        };
        _ticksRemaining = duration;
        return _activeEvent;
    }
}
