using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Propagates pollution from industrial zones to surrounding tiles.
///
/// Industrial zones emit pollution in a radius-3 circle. Pollution strength decays
/// linearly with Euclidean distance — full strength at the source, zero at the edge.
/// Multiple sources accumulate, clamped to [0, 1].
///
/// Call Propagate() each tick before HappinessSystem.
/// </summary>
public class PollutionSystem
{
    private const int PollutionRadius = 3;

    public void Propagate(CityGrid grid)
    {
        grid.ClearPollution();

        foreach (var source in grid.TilesOfType(ZoneType.Industrial))
        {
            for (var dx = -PollutionRadius; dx <= PollutionRadius; dx++)
            for (var dy = -PollutionRadius; dy <= PollutionRadius; dy++)
            {
                var nx = source.X + dx;
                var ny = source.Y + dy;
                if (!grid.IsInBounds(nx, ny)) continue;

                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > PollutionRadius) continue;

                var strength = 1.0 - (distance / PollutionRadius); // 1.0 at center, 0 at edge
                grid.AddPollution(nx, ny, strength);
            }
        }
    }

    public double AveragePollution(CityGrid grid)
    {
        var residential = grid.TilesOfType(ZoneType.Residential).ToList();
        if (residential.Count == 0) return 0;
        return residential.Average(t => t.PollutionLevel);
    }
}
