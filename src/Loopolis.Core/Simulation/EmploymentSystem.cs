using Loopolis.Core.Grid;

namespace Loopolis.Core.Simulation;

/// <summary>
/// Models employment: Industrial zones produce jobs, Residential zones need jobs to grow at full speed.
///
/// Design:
/// - Each unit of Industrial activity (0–50 scale) produces JobsPerActivityUnit jobs.
///   A fully active Industrial tile (activity=50) provides 20 jobs.
/// - First FreeJobsThreshold (100) residents have no employment requirement.
/// - Beyond the threshold, each resident requires a job to sustain full growth.
/// - If jobs &lt; required: employment ratio &lt; 1.0, which reduces residential growth rate.
/// - Growth is never fully stopped — MinGrowthMultiplier (0.2) is the floor.
/// </summary>
public class EmploymentSystem
{
    public const double JobsPerActivityUnit = 0.4;   // 50 activity * 0.4 = 20 jobs per full tile
    public const int    FreeJobsThreshold   = 100;   // first 100 residents need no jobs
    public const double MinGrowthMultiplier = 0.2;   // unemployment never completely stops growth

    public int    AvailableJobs   { get; private set; }
    public int    RequiredJobs    { get; private set; }
    public double EmploymentRatio { get; private set; }

    /// <summary>
    /// Recalculates employment from current grid + population.
    /// Returns the growth multiplier [MinGrowthMultiplier, 1.0] to pass to PopulationSystem.Tick().
    /// Call once per tick, before Population.Tick().
    /// </summary>
    public double Propagate(CityGrid grid, int totalPopulation)
    {
        AvailableJobs = (int)grid.TilesOfType(ZoneType.Industrial)
            .Where(t => t.HasPower && t.HasRoadAccess)
            .Sum(t => t.Population * JobsPerActivityUnit);

        RequiredJobs = Math.Max(0, totalPopulation - FreeJobsThreshold);

        EmploymentRatio = RequiredJobs <= 0
            ? 1.0
            : Math.Min(1.0, (double)AvailableJobs / RequiredJobs);

        return Math.Max(MinGrowthMultiplier, EmploymentRatio);
    }
}
