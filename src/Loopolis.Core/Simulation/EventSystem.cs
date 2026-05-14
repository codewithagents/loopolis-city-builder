using System.Linq;
using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

public enum CityEventType { None, FireBreak, CrimeWave, PowerOutage, DemandSlump }

public record CityEvent(CityEventType Type, string Name, string Description, int DurationTicks);

/// <summary>
/// Tracks an active event that is awaiting (or has received) a player response.
/// </summary>
public record PendingEventResponse(
    string EventType,   // "FireBreak", "CrimeWave", "PowerOutage", "DemandSlump"
    int Cost,           // $ cost of the Intervene option
    bool Responded      // true once the player has chosen to intervene
);

public class EventSystem
{
    private readonly Random _rng;
    private CityEvent? _activeEvent;
    private int _ticksRemaining;
    private int _cooldownTicks;      // ticks until next event can fire
    private const int MinCooldown = 100;
    private const int MaxCooldown = 200;

    // Fire damage tracking — the tile currently on fire during a FireBreak event.
    // (-1,-1) means no fire tile this event.
    public int FireTileX { get; private set; } = -1;
    public int FireTileY { get; private set; } = -1;

    // Set to true on the tick the fire ends without fire-station coverage.
    // SimulationEngine reads this flag each tick and demolishes the tile if set.
    public bool FireDamageOccurred { get; private set; }

    /// <summary>
    /// Set when an event fires; cleared when the event ends (naturally or via player response).
    /// Non-null and Responded==false means a response is awaiting player input.
    /// </summary>
    public PendingEventResponse? ActiveResponse { get; private set; }

    public CityEvent? ActiveEvent => _activeEvent;
    public bool HasActiveEvent => _activeEvent != null;

    /// <summary>Intervention costs per event type.</summary>
    private static int GetInterventionCost(CityEventType type) => type switch
    {
        CityEventType.FireBreak    => 800,
        CityEventType.CrimeWave   => 600,
        CityEventType.PowerOutage  => 1200,
        CityEventType.DemandSlump  => 900,
        _                          => 0,
    };

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
        // Do not fall back to Random.Shared — each SimulationEngine passes a seeded Random.
        // If called without an argument (e.g. from tests that pre-date seeding), generate a
        // new unseeded Random so the call site at least doesn't share state with other game instances.
        _rng = rng ?? new Random();
        _cooldownTicks = 60; // give player time to set up before first event
    }

    /// <summary>
    /// Player chooses to intervene in the current event.
    /// Deducts the intervention cost from <paramref name="budget"/> and resolves the event in 5 ticks.
    /// Returns false if there is no active event, it was already responded to, or funds are insufficient.
    /// </summary>
    public bool RespondToEvent(BudgetSystem budget)
    {
        if (ActiveResponse == null) return false;
        if (ActiveResponse.Responded) return false;
        if (budget.Balance < ActiveResponse.Cost) return false;

        budget.Charge(ActiveResponse.Cost);
        ActiveResponse = ActiveResponse with { Responded = true };

        // For FireBreak: prevent demolition — clear the fire tile so it is treated as if
        // a fire station extinguished it.
        if (_activeEvent?.Type == CityEventType.FireBreak)
            FireTileX = FireTileY = -1;

        // Resolve in at most 5 more ticks instead of the full remaining duration
        if (_ticksRemaining > 5)
            _ticksRemaining = 5;

        return true;
    }

    /// <summary>Run each simulation tick. Returns a new event if one just fired (for banner display).</summary>
    public CityEvent? Tick(CityGrid grid, int population)
    {
        // Clear one-shot flags
        FireDamageOccurred = false;

        // Count-down cooldown
        if (_cooldownTicks > 0) { _cooldownTicks--; return null; }

        // Active event: count down
        if (_activeEvent != null)
        {
            _ticksRemaining--;
            if (_ticksRemaining <= 0)
            {
                // FireBreak ends: if no fire station covered the tile, mark fire damage
                // (skip if the player already intervened — fire tile was already cleared)
                if (_activeEvent.Type == CityEventType.FireBreak && FireTileX >= 0)
                {
                    bool fireStationExists = grid.AllTiles().Any(t => t.Zone == ZoneType.FireStation);
                    if (!fireStationExists)
                        FireDamageOccurred = true;
                    else
                        // Fire station extinguished it — no damage, clear fire tile
                        FireTileX = FireTileY = -1;
                }

                _activeEvent = null;
                ActiveResponse = null;   // clear response tracking when event ends
                _cooldownTicks = _rng.Next(MinCooldown, MaxCooldown);
            }
            return null;
        }

        // Only trigger events once city has some population
        if (population < 100) return null;

        // Random trigger: ~2% chance per tick once cooldown expires
        if (_rng.NextDouble() > 0.02) return null;

        // Pick event type based on coverage status and population
        bool hasFireStation   = grid.AllTiles().Any(t => t.Zone == ZoneType.FireStation);
        bool hasPoliceStation = grid.AllTiles().Any(t => t.Zone == ZoneType.PoliceStation);

        CityEventType type;
        if (population < 200)
        {
            // Small cities only get fire/crime events (teaches player early services)
            type = PickUncoveredServiceEvent(hasFireStation, hasPoliceStation)
                   ?? (_rng.NextDouble() < 0.5 ? CityEventType.FireBreak : CityEventType.CrimeWave);
        }
        else
        {
            // Larger cities: prefer uncovered service events, but also PowerOutage / DemandSlump
            var uncovered = PickUncoveredServiceEvent(hasFireStation, hasPoliceStation);
            if (uncovered != null)
            {
                type = uncovered.Value;
            }
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

        // For FireBreak: pick a random occupied residential or commercial tile to catch fire
        if (type == CityEventType.FireBreak)
        {
            FireTileX = FireTileY = -1; // reset previous fire tile
            var candidates = grid.AllTiles()
                .Where(t => t.Zone is ZoneType.Residential or ZoneType.Commercial && t.Population > 0)
                .ToArray();
            if (candidates.Length > 0)
            {
                var chosen = candidates[_rng.Next(candidates.Length)];
                FireTileX = chosen.X;
                FireTileY = chosen.Y;
            }
        }

        _activeEvent = type switch
        {
            CityEventType.FireBreak => new CityEvent(type, "🔥 Fire Break!",
                isCovered ? "Fire stations contain the blaze" : "No fire stations! Building at risk!",
                duration),
            CityEventType.CrimeWave => new CityEvent(type, "🚔 Crime Wave!",
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

        // Set pending response for the newly fired event
        ActiveResponse = new PendingEventResponse(
            EventType:  type.ToString(),
            Cost:       GetInterventionCost(type),
            Responded:  false);

        return _activeEvent;
    }

    /// <summary>
    /// Returns the event type to fire when a service gap exists, or null when both services are covered.
    /// When both are missing, randomly picks one; when only one is missing, returns that type directly.
    /// </summary>
    private CityEventType? PickUncoveredServiceEvent(bool hasFireStation, bool hasPoliceStation)
    {
        if (!hasFireStation && !hasPoliceStation)
            return _rng.NextDouble() < 0.5 ? CityEventType.FireBreak : CityEventType.CrimeWave;
        if (!hasFireStation)  return CityEventType.FireBreak;
        if (!hasPoliceStation) return CityEventType.CrimeWave;
        return null;
    }
}
