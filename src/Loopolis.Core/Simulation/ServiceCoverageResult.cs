namespace Loopolis.Core.Simulation;

/// <summary>
/// Snapshot of service coverage after a capacity-aware propagation pass.
/// Produced by <see cref="HappinessSystem.ComputeServiceCoverage"/> each tick.
/// Stored on <see cref="SimulationEngine.LastServiceCoverage"/>.
/// </summary>
public record ServiceCoverageResult(
    float SchoolCoveragePercent,
    float PoliceCoveragePercent,
    float FireCoveragePercent,
    float HospitalCoveragePercent,
    int SchoolSeatsUsed,
    int SchoolSeatsTotal,
    int PoliceCapacityUsed,
    int PoliceCapacityTotal,
    int FireCapacityUsed,
    int FireCapacityTotal,
    int HospitalBedsUsed,
    int HospitalBedsTotal
)
{
    /// <summary>
    /// Returns a zeroed result (used when no residential tiles or no service buildings are present).
    /// </summary>
    public static readonly ServiceCoverageResult Empty = new(
        SchoolCoveragePercent:   0f,
        PoliceCoveragePercent:   0f,
        FireCoveragePercent:     0f,
        HospitalCoveragePercent: 0f,
        SchoolSeatsUsed:    0,
        SchoolSeatsTotal:   0,
        PoliceCapacityUsed: 0,
        PoliceCapacityTotal: 0,
        FireCapacityUsed:   0,
        FireCapacityTotal:  0,
        HospitalBedsUsed:   0,
        HospitalBedsTotal:  0
    );
}
