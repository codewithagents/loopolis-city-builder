using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

public enum CityEventType { None, FireBreak, CrimeWave, PowerOutage, DemandSlump }

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
        CityEventType.FireBreak   => -0.15,
        CityEventType.CrimeWave  => -0.10,
        CityEventType.PowerOutage => -0.12,
        CityEventType.DemandSlump => -0.05,
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

        // Pick event type based on coverage status and population
        bool hasFireStation   = grid.AllTiles().Any(t => t.Zone == ZoneType.FireStation);
        bool hasPoliceStation = grid.AllTiles().Any(t => t.Zone == ZoneType.PoliceStation);

        CityEventType type;
        if (population < 200)
        {
            // Small cities only get fire/crime events (teaches player early services)
            if (!hasFireStation && !hasPoliceStation)
                type = _rng.NextDouble() < 0.5 ? CityEventType.FireBreak : CityEventType.CrimeWave;
            else if (!hasFireStation)
                type = CityEventType.FireBreak;
            else if (!hasPoliceStation)
                type = CityEventType.CrimeWave;
            else
                type = _rng.NextDouble() < 0.5 ? CityEventType.FireBreak : CityEventType.CrimeWave;
        }
        else
        {
            // Larger cities: prefer uncovered service events, but also PowerOutage / DemandSlump
            if (!hasFireStation && !hasPoliceStation)
                type = _rng.NextDouble() < 0.5 ? CityEventType.FireBreak : CityEventType.CrimeWave;
            else if (!hasFireStation)
                type = CityEventType.FireBreak;
            else if (!hasPoliceStation)
                type = CityEventType.CrimeWave;
            else
            {
                // All services covered — weighted pick across all 4 types
                var roll = _rng.NextDouble();
                type = roll < 0.30 ? CityEventType.FireBreak
                     : roll < 0.60 ? CityEventType.CrimeWave
                     : roll < 0.80 ? CityEventType.PowerOutage
                     : CityEventType.DemandSlump;
            }
        }

        bool isCovered = (type == CityEventType.FireBreak  && hasFireStation) ||
                         (type == CityEventType.CrimeWave && hasPoliceStation);

        int duration;
        if (type == CityEventType.PowerOutage)
        {
            var powerPlantCount = grid.AllTiles().Count(t => t.Zone == ZoneType.PowerPlant);
            duration = powerPlantCount >= 2 ? 10 : 30;
        }
        else if (type == CityEventType.DemandSlump)
        {
            duration = 40; // economic cycle — no mitigation
        }
        else
        {
            duration = isCovered ? 20 : 60; // covered cities resolve faster
        }

        _activeEvent = type switch
        {
            CityEventType.FireBreak => new CityEvent(type, "Fire Break!",
                isCovered ? "Fire stations contain the blaze" : "No fire stations! Happiness dropping fast",
                duration),
            CityEventType.CrimeWave => new CityEvent(type, "Crime Wave!",
                isCovered ? "Police keep the streets safe" : "No police stations! Citizens fleeing",
                duration),
            CityEventType.PowerOutage => new CityEvent(type, "⚡ Power Outage!",
                duration == 10 ? "Backup power plants restore grid quickly" : "No backup power — outage lasts longer",
                duration),
            _ => new CityEvent(type, "📉 Demand Slump!",
                "Economic cycle — shops less profitable, happiness dips",
                duration),
        };
        _ticksRemaining = duration;
        return _activeEvent;
    }
}
