using Loopolis.Core.Graph;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Simulation;

[TestFixture]
public class WorkerFlowSystemTests
{
    private WorkerFlowSystem _system  = null!;
    private RoadGraph        _graph   = null!;

    [SetUp]
    public void SetUp()
    {
        _system = new WorkerFlowSystem();
        _graph  = new RoadGraph();
    }

    // ── Empty / no-op cases ──────────────────────────────────────────────────

    [Test]
    public void EmptyCity_ZeroWorkersRouted()
    {
        var grid = new CityGrid(10, 10);

        var result = _system.Route(grid, _graph);

        Assert.That(result.WorkersRouted,   Is.EqualTo(0));
        Assert.That(result.UnroutedWorkers, Is.EqualTo(0));
    }

    [Test]
    public void NoIndustrialTiles_AllWorkersUnrouted()
    {
        // Residential with population, no industrial target
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();

        grid.SetZone(5, 5, ZoneType.Road);
        _graph.AddNode(5, 5);

        grid.SetZone(4, 5, ZoneType.Residential);
        grid.SetPopulation(4, 5, 20);
        grid.SetRoadAccess(4, 5, true);

        var result = _system.Route(grid, _graph);

        Assert.That(result.WorkersRouted,   Is.EqualTo(0));
        Assert.That(result.UnroutedWorkers, Is.GreaterThan(0));
    }

    [Test]
    public void ResidentialTileWithNoRoadAccess_IsUnrouted()
    {
        // Industrial has road access, residential does not
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();

        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(6, 5, ZoneType.Industrial);
        _graph.AddNode(5, 5);

        // Industrial tile with a building + road access
        grid.SetBuildingId(6, 5, "i1");
        grid.SetRoadAccess(6, 5, true);

        // Residential with population but NO road access
        grid.SetZone(3, 3, ZoneType.Residential);
        grid.SetPopulation(3, 3, 20);
        // deliberate: no SetRoadAccess call → HasRoadAccess = false

        var result = _system.Route(grid, _graph);

        Assert.That(result.WorkersRouted,   Is.EqualTo(0));
        Assert.That(result.UnroutedWorkers, Is.GreaterThan(0));
    }

    // ── Basic routing ────────────────────────────────────────────────────────

    [Test]
    public void SingleResidentialToSingleIndustrial_WorkersRouted()
    {
        // Layout: R(4,5) — Road(5,5) — Road(6,5) — I(7,5)
        var grid = new CityGrid(12, 12);
        grid.SetFlatTerrain();

        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(6, 5, ZoneType.Road);
        _graph.AddNode(5, 5);
        _graph.AddNode(6, 5);

        grid.SetZone(4, 5, ZoneType.Residential);
        grid.SetPopulation(4, 5, 20);
        grid.SetRoadAccess(4, 5, true);

        grid.SetZone(7, 5, ZoneType.Industrial);
        grid.SetBuildingId(7, 5, "i1");
        grid.SetRoadAccess(7, 5, true);

        var result = _system.Route(grid, _graph);

        Assert.That(result.WorkersRouted,   Is.GreaterThan(0));
        Assert.That(result.UnroutedWorkers, Is.EqualTo(0));
    }

    [Test]
    public void SingleResidentialToSingleIndustrial_EdgeTrafficAccumulated()
    {
        // Layout: R(4,5) — Road(5,5) — Road(6,5) — I(7,5)
        // Workers from R(4,5) enter road at (5,5), traverse (5,5)→(6,5)
        var grid = new CityGrid(12, 12);
        grid.SetFlatTerrain();

        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(6, 5, ZoneType.Road);
        _graph.AddNode(5, 5);
        _graph.AddNode(6, 5);

        grid.SetZone(4, 5, ZoneType.Residential);
        grid.SetPopulation(4, 5, 20);
        grid.SetRoadAccess(4, 5, true);

        grid.SetZone(7, 5, ZoneType.Industrial);
        grid.SetBuildingId(7, 5, "i1");
        grid.SetRoadAccess(7, 5, true);

        _system.Route(grid, _graph);

        // The single road-to-road edge (5,5)→(6,5) must be loaded
        Assert.That(_graph.GetEdgeTraffic(5, 5, 6, 5), Is.GreaterThan(0));
    }

    [Test]
    public void TwoResidentialsShareEdge_TrafficIsSumOfBoth()
    {
        // Layout:
        //   R(4,5) — Road(5,5) — Road(6,5) — I(7,5)
        //   R(4,6) — Road(5,6) ——↗
        // Both R tiles route through (5,5)→(6,5) on the way to I
        var grid = new CityGrid(12, 12);
        grid.SetFlatTerrain();

        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(6, 5, ZoneType.Road);
        grid.SetZone(5, 6, ZoneType.Road);
        _graph.AddNode(5, 5);
        _graph.AddNode(6, 5);
        _graph.AddNode(5, 6);

        // First residential at (4,5)
        grid.SetZone(4, 5, ZoneType.Residential);
        grid.SetPopulation(4, 5, 20);
        grid.SetRoadAccess(4, 5, true);

        // Second residential at (4,6) — nearest road is (5,6)
        grid.SetZone(4, 6, ZoneType.Residential);
        grid.SetPopulation(4, 6, 20);
        grid.SetRoadAccess(4, 6, true);

        // Industrial
        grid.SetZone(7, 5, ZoneType.Industrial);
        grid.SetBuildingId(7, 5, "i1");
        grid.SetRoadAccess(7, 5, true);

        _system.Route(grid, _graph);

        // Both R tiles route through (5,5)→(6,5)
        var sharedEdge = _graph.GetEdgeTraffic(5, 5, 6, 5);
        Assert.That(sharedEdge, Is.GreaterThan(0));

        // Each R tile: Math.Max(1, 20/4) = 5 workers → both use this edge → total ≥ 10
        Assert.That(sharedEdge, Is.GreaterThanOrEqualTo(10));
    }

    // ── ResetEdgeTraffic ─────────────────────────────────────────────────────

    [Test]
    public void ResetEdgeTraffic_ZeroesAllEdges()
    {
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);
        _graph.IncrementEdgeTraffic(0, 0, 1, 0, 50);

        Assert.That(_graph.GetEdgeTraffic(0, 0, 1, 0), Is.EqualTo(50));

        _graph.ResetEdgeTraffic();

        Assert.That(_graph.GetEdgeTraffic(0, 0, 1, 0), Is.EqualTo(0));
    }

    [Test]
    public void ResetEdgeTraffic_OnEmptyGraph_IsNoOp()
    {
        Assert.DoesNotThrow(() => _graph.ResetEdgeTraffic());
    }

    // ── GetNodeTraffic ───────────────────────────────────────────────────────

    [Test]
    public void GetNodeTraffic_NoEdges_ReturnsZero()
    {
        _graph.AddNode(5, 5);

        Assert.That(_graph.GetNodeTraffic(5, 5), Is.EqualTo(0));
    }

    [Test]
    public void GetNodeTraffic_NodeNotInGraph_ReturnsZero()
    {
        Assert.That(_graph.GetNodeTraffic(99, 99), Is.EqualTo(0));
    }

    [Test]
    public void GetNodeTraffic_SingleEdge_HalfOfEdgeTraffic()
    {
        // A—B with 10 workers.
        // GetNodeTraffic(A) = sum(A's edges) / 2 = 10 / 2 = 5
        // GetNodeTraffic(B) = sum(B's edges) / 2 = 10 / 2 = 5
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);
        _graph.IncrementEdgeTraffic(0, 0, 1, 0, 10);

        Assert.That(_graph.GetNodeTraffic(0, 0), Is.EqualTo(5));
        Assert.That(_graph.GetNodeTraffic(1, 0), Is.EqualTo(5));
    }

    [Test]
    public void GetNodeTraffic_Chokepoint_SumsAllEdges()
    {
        // A—B—C: 10 workers on A→B, 20 on B→C
        // GetNodeTraffic(B) = (10 + 20) / 2 = 15
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);
        _graph.AddNode(2, 0);
        _graph.IncrementEdgeTraffic(0, 0, 1, 0, 10);
        _graph.IncrementEdgeTraffic(1, 0, 2, 0, 20);

        Assert.That(_graph.GetNodeTraffic(1, 0), Is.EqualTo(15));
    }

    // ── IncrementEdgeTraffic / GetEdgeTraffic ────────────────────────────────

    [Test]
    public void IncrementEdgeTraffic_DirectionAgnostic()
    {
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);

        _graph.IncrementEdgeTraffic(0, 0, 1, 0, 5);
        _graph.IncrementEdgeTraffic(1, 0, 0, 0, 3);

        // Both calls affect the same undirected edge
        Assert.That(_graph.GetEdgeTraffic(0, 0, 1, 0), Is.EqualTo(8));
        Assert.That(_graph.GetEdgeTraffic(1, 0, 0, 0), Is.EqualTo(8));
    }

    [Test]
    public void GetEdgeTraffic_UnknownEdge_ReturnsZero()
    {
        Assert.That(_graph.GetEdgeTraffic(0, 0, 1, 0), Is.EqualTo(0));
    }

    // ── Path reconstruction ──────────────────────────────────────────────────

    [Test]
    public void PathReconstruction_LinearPath_CorrectSequence()
    {
        // A(0,0) — B(1,0) — C(2,0) — D(3,0)
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);
        _graph.AddNode(2, 0);
        _graph.AddNode(3, 0);

        var (_, parents) = _graph.ShortestPathWithParents(0, 0);
        var path = RoadGraph.ReconstructPath(parents, (0, 0), (3, 0));

        Assert.That(path, Is.EqualTo(new List<(int, int)> { (0, 0), (1, 0), (2, 0), (3, 0) }));
    }

    [Test]
    public void PathReconstruction_UnreachableTarget_ReturnsEmpty()
    {
        _graph.AddNode(0, 0);
        // (5,5) is not in the graph

        var (_, parents) = _graph.ShortestPathWithParents(0, 0);
        var path = RoadGraph.ReconstructPath(parents, (0, 0), (5, 5));

        Assert.That(path, Is.Empty);
    }

    [Test]
    public void PathReconstruction_SameSourceAndTarget_ReturnsSingleNode()
    {
        _graph.AddNode(3, 3);

        var (_, parents) = _graph.ShortestPathWithParents(3, 3);
        var path = RoadGraph.ReconstructPath(parents, (3, 3), (3, 3));

        Assert.That(path, Is.EqualTo(new List<(int, int)> { (3, 3) }));
    }

    // ── WorkerFlowResult values ──────────────────────────────────────────────

    [Test]
    public void WorkerFlowResult_AverageCommuteDistance_IsPositive_WhenRoutingSucceeds()
    {
        // Layout: R(4,5) — Road(5,5) — Road(6,5) — Road(7,5) — I(8,5)
        var grid = new CityGrid(12, 12);
        grid.SetFlatTerrain();

        for (var x = 5; x <= 7; x++)
        {
            grid.SetZone(x, 5, ZoneType.Road);
            _graph.AddNode(x, 5);
        }

        grid.SetZone(4, 5, ZoneType.Residential);
        grid.SetPopulation(4, 5, 40);
        grid.SetRoadAccess(4, 5, true);

        grid.SetZone(8, 5, ZoneType.Industrial);
        grid.SetBuildingId(8, 5, "i1");
        grid.SetRoadAccess(8, 5, true);

        var result = _system.Route(grid, _graph);

        Assert.That(result.WorkersRouted,          Is.GreaterThan(0));
        Assert.That(result.AverageCommuteDistance, Is.GreaterThan(0f));
    }
}
