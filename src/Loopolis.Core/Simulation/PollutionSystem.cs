using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Propagates pollution from industrial zones and CoalPlant tiles to surrounding tiles.
///
/// Industrial zones emit at full strength (strength factor 1.0).
/// CoalPlant tiles emit at strength factor 0.4 — meaningful but less than full industrial.
/// NuclearPlant emits zero pollution.
/// Pollution radius is 3. Strength decays linearly with Euclidean distance.
/// Multiple sources accumulate, clamped to [0, 1].
///
/// Call Propagate() each tick before HappinessSystem.
/// </summary>
public class PollutionSystem
{
    private const int    PollutionRadius         = 3;
    private const double IndustrialStrength       = 1.0;
    private const double CoalPlantStrength        = 0.4;

    public void Propagate(CityGrid grid)
    {
        grid.ClearPollution();

        foreach (var tile in grid.AllTiles())
        {
            double emissionStrength;
            if (tile.Zone == ZoneType.Industrial)
            {
                // Unpowered industrial: no production → no smoke, no pollution.
                if (!tile.HasPower) continue;
                emissionStrength = IndustrialStrength;
            }
            else if (tile.Zone == ZoneType.CoalPlant || tile.Zone == ZoneType.PowerPlant)
                emissionStrength = CoalPlantStrength;
            else
                continue;

            EmitPollution(grid, tile.X, tile.Y, emissionStrength);
        }
    }

    private static void EmitPollution(CityGrid grid, int srcX, int srcY, double emissionStrength)
    {
        for (var dx = -PollutionRadius; dx <= PollutionRadius; dx++)
        for (var dy = -PollutionRadius; dy <= PollutionRadius; dy++)
        {
            var nx = srcX + dx;
            var ny = srcY + dy;
            if (!grid.IsInBounds(nx, ny)) continue;

            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > PollutionRadius) continue;

            var strength = emissionStrength * (1.0 - (distance / PollutionRadius)); // decays to 0 at edge
            grid.AddPollution(nx, ny, strength);
        }
    }

    public double AveragePollution(CityGrid grid)
    {
        var residential = grid.TilesOfType(ZoneType.Residential).ToList();
        if (residential.Count == 0) return 0;
        return residential.Average(t => t.PollutionLevel);
    }
}
