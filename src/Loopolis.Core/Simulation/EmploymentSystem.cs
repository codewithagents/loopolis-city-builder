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
    /// Jobs provided by an unpowered industrial tile (placeholder, no production).
    /// 2 jobs = minimal staffing (security, maintenance) but no real factory output.
    /// </summary>
    public const int UnpoweredIndustrialJobs = 2;

    /// <summary>
    /// Recalculates employment from current grid + population.
    /// Returns the growth multiplier [MinGrowthMultiplier, 1.0] to pass to PopulationSystem.Tick().
    /// Call once per tick, before Population.Tick().
    ///
    /// Powered industrial: activity × 0.4 jobs (up to 20 per full tile).
    /// Unpowered industrial: 2 fixed jobs (placeholder — no production, no smoke).
    /// </summary>
    /// <param name="grid">The city grid.</param>
    /// <param name="totalPopulation">Current total population (for required-jobs calculation).</param>
    /// <param name="jobsPerIndustrialTileBonus">
    /// Flat bonus jobs added per powered road-accessible industrial tile.
    /// Pass PolicySystem.JobsPerIndustrialTileBonus (default 0; +3 with IndustrialHub policy).
    /// </param>
    public double Propagate(CityGrid grid, int totalPopulation, int jobsPerIndustrialTileBonus = 0)
    {
        AvailableJobs = 0;
        foreach (var t in grid.TilesOfType(ZoneType.Industrial))
        {
            if (!t.HasRoadAccess) continue;
            if (t.HasPower)
            {
                AvailableJobs += (int)(t.Population * JobsPerActivityUnit);
                AvailableJobs += jobsPerIndustrialTileBonus; // IndustrialHub policy bonus
            }
            else
                AvailableJobs += UnpoweredIndustrialJobs;
        }

        RequiredJobs = Math.Max(0, totalPopulation - FreeJobsThreshold);

        EmploymentRatio = RequiredJobs <= 0
            ? 1.0
            : Math.Min(1.0, (double)AvailableJobs / RequiredJobs);

        return Math.Max(MinGrowthMultiplier, EmploymentRatio);
    }
}
