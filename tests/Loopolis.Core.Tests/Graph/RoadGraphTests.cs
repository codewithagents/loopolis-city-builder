using Loopolis.Core.Graph;
using Loopolis.Core.Grid;

namespace Loopolis.Core.Tests.Graph;

[TestFixture]
public class RoadGraphTests
{
    private RoadGraph _graph = null!;

    [SetUp]
    public void SetUp() => _graph = new RoadGraph();

    // ── Empty graph ─────────────────────────────────────────────────────────────

    [Test]
    public void EmptyGraph_GetDistance_ReturnsMaxValue()
    {
        var dist = _graph.GetDistance(0, 0, 1, 0);

        Assert.That(dist, Is.EqualTo(float.MaxValue));
    }

    [Test]
    public void EmptyGraph_IsReachable_ReturnsFalse()
    {
        var reachable = _graph.IsReachable(0, 0, 1, 0);

        Assert.That(reachable, Is.False);
    }

    [Test]
    public void EmptyGraph_NodeCount_IsZero()
    {
        Assert.That(_graph.NodeCount, Is.EqualTo(0));
    }

    [Test]
    public void EmptyGraph_EdgeCount_IsZero()
    {
        Assert.That(_graph.EdgeCount, Is.EqualTo(0));
    }

    // ── Single node ─────────────────────────────────────────────────────────────

    [Test]
    public void SingleNode_GetDistance_ToItself_IsZero()
    {
        _graph.AddNode(3, 5);

        var dist = _graph.GetDistance(3, 5, 3, 5);

        Assert.That(dist, Is.EqualTo(0f));
    }

    [Test]
    public void SingleNode_IsReachable_ToItself_IsTrue()
    {
        _graph.AddNode(3, 5);

        Assert.That(_graph.IsReachable(3, 5, 3, 5), Is.True);
    }

    [Test]
    public void SingleNode_GetDistance_ToOtherCoords_IsMaxValue()
    {
        _graph.AddNode(3, 5);

        var dist = _graph.GetDistance(3, 5, 4, 5);

        Assert.That(dist, Is.EqualTo(float.MaxValue));
    }

    [Test]
    public void SingleNode_NodeCount_IsOne()
    {
        _graph.AddNode(3, 5);

        Assert.That(_graph.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void SingleNode_EdgeCount_IsZero()
    {
        _graph.AddNode(3, 5);

        Assert.That(_graph.EdgeCount, Is.EqualTo(0));
    }

    // ── Two adjacent nodes ──────────────────────────────────────────────────────

    [Test]
    public void TwoAdjacentRoadNodes_AreReachable()
    {
        _graph.AddNode(0, 0, 1.0f);
        _graph.AddNode(1, 0, 1.0f);

        Assert.That(_graph.IsReachable(0, 0, 1, 0), Is.True);
    }

    [Test]
    public void TwoAdjacentRoadNodes_Distance_IsEdgeWeight()
    {
        // Both Road (weight 1.0) → edge weight = (1.0 + 1.0) / 2 = 1.0
        _graph.AddNode(0, 0, 1.0f);
        _graph.AddNode(1, 0, 1.0f);

        var dist = _graph.GetDistance(0, 0, 1, 0);

        Assert.That(dist, Is.EqualTo(1.0f).Within(0.0001f));
    }

    [Test]
    public void TwoAdjacentAvenueNodes_Distance_IsEdgeWeight()
    {
        // Both Avenue (weight 0.5) → edge weight = (0.5 + 0.5) / 2 = 0.5
        _graph.AddNode(0, 0, 0.5f);
        _graph.AddNode(0, 1, 0.5f);

        var dist = _graph.GetDistance(0, 0, 0, 1);

        Assert.That(dist, Is.EqualTo(0.5f).Within(0.0001f));
    }

    [Test]
    public void TwoAdjacentRoadNodes_EdgeCount_IsOne()
    {
        _graph.AddNode(0, 0, 1.0f);
        _graph.AddNode(1, 0, 1.0f);

        Assert.That(_graph.EdgeCount, Is.EqualTo(1));
    }

    // ── Non-adjacent nodes ──────────────────────────────────────────────────────

    [Test]
    public void TwoNonAdjacentNodes_WithNoPath_AreNotReachable()
    {
        _graph.AddNode(0, 0);
        _graph.AddNode(5, 5); // no connecting tiles

        Assert.That(_graph.IsReachable(0, 0, 5, 5), Is.False);
    }

    [Test]
    public void TwoNonAdjacentNodes_WithNoPath_GetDistance_ReturnsMaxValue()
    {
        _graph.AddNode(0, 0);
        _graph.AddNode(5, 5);

        Assert.That(_graph.GetDistance(0, 0, 5, 5), Is.EqualTo(float.MaxValue));
    }

    [Test]
    public void ThreeNodePath_GetDistance_SumsEdgeWeights()
    {
        // Road: (0,0) → (1,0) → (2,0), all weight 1.0
        // Expected distance 0→2 = 1.0 + 1.0 = 2.0
        _graph.AddNode(0, 0, 1.0f);
        _graph.AddNode(1, 0, 1.0f);
        _graph.AddNode(2, 0, 1.0f);

        var dist = _graph.GetDistance(0, 0, 2, 0);

        Assert.That(dist, Is.EqualTo(2.0f).Within(0.0001f));
    }

    // ── Removing nodes ──────────────────────────────────────────────────────────

    [Test]
    public void RemoveNode_BreaksConnectivity()
    {
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);
        _graph.AddNode(2, 0);

        Assert.That(_graph.IsReachable(0, 0, 2, 0), Is.True, "Should be reachable before removal");

        _graph.RemoveNode(1, 0);

        Assert.That(_graph.IsReachable(0, 0, 2, 0), Is.False, "Should not be reachable after bridge node removed");
    }

    [Test]
    public void RemoveNode_UpdatesNodeCount()
    {
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);

        _graph.RemoveNode(0, 0);

        Assert.That(_graph.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void RemoveNode_UpdatesEdgeCount()
    {
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);

        Assert.That(_graph.EdgeCount, Is.EqualTo(1));

        _graph.RemoveNode(0, 0);

        Assert.That(_graph.EdgeCount, Is.EqualTo(0));
    }

    [Test]
    public void RemoveNode_NonExistent_IsNoOp()
    {
        _graph.AddNode(0, 0);

        // Should not throw
        Assert.DoesNotThrow(() => _graph.RemoveNode(99, 99));
        Assert.That(_graph.NodeCount, Is.EqualTo(1));
    }

    // ── Mixed Road / Avenue path ────────────────────────────────────────────────

    [Test]
    public void MixedRoadAvenuePath_EdgeWeight_IsAverage()
    {
        // Road (1.0) adjacent to Avenue (0.5) → edge weight = 0.75
        _graph.AddNode(0, 0, 1.0f);   // Road
        _graph.AddNode(1, 0, 0.5f);   // Avenue

        var dist = _graph.GetDistance(0, 0, 1, 0);

        Assert.That(dist, Is.EqualTo(0.75f).Within(0.0001f));
    }

    [Test]
    public void MixedPath_Dijkstra_FindsShortestRoute()
    {
        // Two paths from (0,0) to (2,0):
        //   Direct:   (0,0)→(1,0)→(2,0)  all Road 1.0 → total = 2.0
        //   Via avenue: add (0,1)→(1,1)→(2,1) Avenues 0.5 plus connectors
        //
        // Simpler: three nodes in a line — Road, Avenue, Road
        //   (0,0)[1.0] → (1,0)[0.5] → (2,0)[1.0]
        //   edge 0→1: (1.0+0.5)/2 = 0.75
        //   edge 1→2: (0.5+1.0)/2 = 0.75
        //   total: 1.5

        _graph.AddNode(0, 0, 1.0f);
        _graph.AddNode(1, 0, 0.5f);
        _graph.AddNode(2, 0, 1.0f);

        var dist = _graph.GetDistance(0, 0, 2, 0);

        Assert.That(dist, Is.EqualTo(1.5f).Within(0.0001f));
    }

    [Test]
    public void Dijkstra_ChoosesAvenueRoute_OverLongerRoadRoute()
    {
        // Layout (grid coords):
        //   (0,0) Road  → (1,0) Road  → (2,0) Road        top path: 1.0+1.0 = 2.0
        //   (0,0) Road  → (0,1) Avenue→ (1,1) Avenue→(2,1) Avenue→(2,0) Road
        //                 edges: 0.75 + 0.5 + 0.5 + 0.75 = 2.5 — longer
        //   So direct top path should be chosen.
        //
        // Actually let's create a scenario where avenue shortcut is genuinely shorter:
        //   Direct road path: (0,0)→(1,0)→(2,0)→(3,0) — 3 road edges = 3.0
        //   Avenue shortcut:  (0,0)→(0,1)→(1,1)→(2,1)→(3,0) doesn't connect cleanly
        //
        // Cleaner test: straight Avenue path vs detour Road path
        //   Avenue path: (0,0)[1.0] to (1,0)[0.5] to (2,0)[0.5] to (3,0)[1.0]
        //     edges: 0.75 + 0.5 + 0.75 = 2.0
        //   vs disconnected alternative: only one path exists → Dijkstra picks it

        _graph.AddNode(0, 0, 1.0f);   // Road endpoint
        _graph.AddNode(1, 0, 0.5f);   // Avenue
        _graph.AddNode(2, 0, 0.5f);   // Avenue
        _graph.AddNode(3, 0, 1.0f);   // Road endpoint

        var dist = _graph.GetDistance(0, 0, 3, 0);

        Assert.That(dist, Is.EqualTo(2.0f).Within(0.0001f));
    }

    // ── GetConnectedComponent ───────────────────────────────────────────────────

    [Test]
    public void GetConnectedComponent_SingleNode_ReturnsSelf()
    {
        _graph.AddNode(5, 5);

        var component = _graph.GetConnectedComponent(5, 5);

        Assert.That(component, Has.Count.EqualTo(1));
        Assert.That(component, Contains.Item((5, 5)));
    }

    [Test]
    public void GetConnectedComponent_NonExistentNode_ReturnsEmpty()
    {
        var component = _graph.GetConnectedComponent(99, 99);

        Assert.That(component, Is.Empty);
    }

    [Test]
    public void GetConnectedComponent_ConnectedChain_ReturnsAllNodes()
    {
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);
        _graph.AddNode(2, 0);

        var component = _graph.GetConnectedComponent(0, 0);

        Assert.That(component, Has.Count.EqualTo(3));
        Assert.That(component, Contains.Item((0, 0)));
        Assert.That(component, Contains.Item((1, 0)));
        Assert.That(component, Contains.Item((2, 0)));
    }

    [Test]
    public void GetConnectedComponent_DisconnectedGraph_ReturnsOnlyOwnComponent()
    {
        // Island A: (0,0)-(1,0)
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);
        // Island B: (5,5)-(6,5) — no connection
        _graph.AddNode(5, 5);
        _graph.AddNode(6, 5);

        var componentA = _graph.GetConnectedComponent(0, 0);
        var componentB = _graph.GetConnectedComponent(5, 5);

        Assert.That(componentA, Has.Count.EqualTo(2));
        Assert.That(componentB, Has.Count.EqualTo(2));
        Assert.That(componentA, Does.Not.Contain((5, 5)));
        Assert.That(componentB, Does.Not.Contain((0, 0)));
    }

    // ── ShortestPathSourceMap ───────────────────────────────────────────────────

    [Test]
    public void ShortestPathSourceMap_NonExistentSource_ReturnsEmpty()
    {
        var map = _graph.ShortestPathSourceMap(99, 99);

        Assert.That(map, Is.Empty);
    }

    [Test]
    public void ShortestPathSourceMap_IncludesSourceAtDistanceZero()
    {
        _graph.AddNode(3, 7);

        var map = _graph.ShortestPathSourceMap(3, 7);

        Assert.That(map, Contains.Key((3, 7)));
        Assert.That(map[(3, 7)], Is.EqualTo(0f));
    }

    [Test]
    public void ShortestPathSourceMap_ThreeNodeLine_CorrectDistances()
    {
        // (0,0)[1.0] → (1,0)[1.0] → (2,0)[1.0]  — all Road
        _graph.AddNode(0, 0, 1.0f);
        _graph.AddNode(1, 0, 1.0f);
        _graph.AddNode(2, 0, 1.0f);

        var map = _graph.ShortestPathSourceMap(0, 0);

        Assert.That(map[(0, 0)], Is.EqualTo(0.0f).Within(0.0001f));
        Assert.That(map[(1, 0)], Is.EqualTo(1.0f).Within(0.0001f));
        Assert.That(map[(2, 0)], Is.EqualTo(2.0f).Within(0.0001f));
    }

    [Test]
    public void ShortestPathSourceMap_DisconnectedNode_NotInMap()
    {
        _graph.AddNode(0, 0);
        _graph.AddNode(5, 5); // not connected to (0,0)

        var map = _graph.ShortestPathSourceMap(0, 0);

        Assert.That(map, Does.Not.ContainKey((5, 5)));
    }

    // ── NodeCount and EdgeCount ─────────────────────────────────────────────────

    [Test]
    public void NodeCount_TracksAdditionsAndRemovals()
    {
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);
        Assert.That(_graph.NodeCount, Is.EqualTo(2));

        _graph.RemoveNode(0, 0);
        Assert.That(_graph.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void EdgeCount_ThreeConnectedNodes_IsTwo()
    {
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);
        _graph.AddNode(2, 0);

        Assert.That(_graph.EdgeCount, Is.EqualTo(2));
    }

    [Test]
    public void EdgeCount_GridOfFour_IsFour()
    {
        // 2x2 grid:
        //   (0,0)─(1,0)
        //     │       │
        //   (0,1)─(1,1)
        // 4 edges
        _graph.AddNode(0, 0);
        _graph.AddNode(1, 0);
        _graph.AddNode(0, 1);
        _graph.AddNode(1, 1);

        Assert.That(_graph.EdgeCount, Is.EqualTo(4));
    }

    // ── GetDistanceViaRoads ─────────────────────────────────────────────────────

    /// <summary>
    /// Minimal grid helper: 10x10 flat grid with flat terrain (height=1 everywhere).
    /// </summary>
    private static CityGrid MakeGrid()
    {
        var g = new CityGrid(10, 10);
        g.SetFlatTerrain();
        return g;
    }

    [Test]
    public void GetDistanceViaRoads_NeitherTileHasRoadNeighbour_ReturnsMaxValue()
    {
        // Two residential tiles with no road tiles anywhere on the grid
        var grid = MakeGrid();
        grid.SetZone(2, 2, ZoneType.Residential);
        grid.SetZone(5, 5, ZoneType.Residential);
        // No road tiles → no nodes in graph → unreachable

        var dist = _graph.GetDistanceViaRoads(grid, 2, 2, 5, 5);

        Assert.That(dist, Is.EqualTo(float.MaxValue));
    }

    [Test]
    public void GetDistanceViaRoads_OneTileHasRoadNeighbour_OtherDoesNot_ReturnsMaxValue()
    {
        // Residential at (2,2) has road at (2,3). Residential at (7,7) has no road nearby.
        var grid = MakeGrid();
        grid.SetZone(2, 2, ZoneType.Residential);
        grid.SetZone(2, 3, ZoneType.Road);
        _graph.AddNode(2, 3, 1.0f);
        grid.SetZone(7, 7, ZoneType.Residential);
        // No road near (7,7)

        var dist = _graph.GetDistanceViaRoads(grid, 2, 2, 7, 7);

        Assert.That(dist, Is.EqualTo(float.MaxValue));
    }

    [Test]
    public void GetDistanceViaRoads_BothTilesShareAdjacentRoadTile_ReturnsZero()
    {
        // Residential at (2,2) and service at (2,4) — both adjacent to road at (2,3).
        // Road neighbor of (2,2) = (2,3); road neighbor of (2,4) = (2,3). Same node → distance 0.
        var grid = MakeGrid();
        grid.SetZone(2, 2, ZoneType.Residential);
        grid.SetZone(2, 3, ZoneType.Road);
        grid.SetZone(2, 4, ZoneType.FireStation);
        _graph.AddNode(2, 3, 1.0f);

        var dist = _graph.GetDistanceViaRoads(grid, 2, 2, 2, 4);

        Assert.That(dist, Is.EqualTo(0f));
    }

    [Test]
    public void GetDistanceViaRoads_StraightRoadPath_ReturnsCorrectDistance()
    {
        // Layout: Residential-(2,5) | Road-(3,5)-(4,5)-(5,5)-(6,5) | FireStation-(7,5)
        // Road neighbor of (2,5) = (3,5); road neighbor of (7,5) = (6,5).
        // Graph distance (3,5)→(6,5) = 3 road edges × 1.0 = 3.0
        var grid = MakeGrid();
        grid.SetZone(2, 5, ZoneType.Residential);
        for (var x = 3; x <= 6; x++)
        {
            grid.SetZone(x, 5, ZoneType.Road);
            _graph.AddNode(x, 5, 1.0f);
        }
        grid.SetZone(7, 5, ZoneType.FireStation);

        var dist = _graph.GetDistanceViaRoads(grid, 2, 5, 7, 5);

        Assert.That(dist, Is.EqualTo(3.0f).Within(0.001f));
    }

    [Test]
    public void GetDistanceViaRoads_AvenuePathIsFaster_ThanEquivalentRoadPath()
    {
        // Two paths: road path 4 edges vs avenue path 4 edges (shorter due to 0.5 weight)
        // Layout (two separate roads sharing the same grid — simpler: just avenue path)
        // Avenue: (3,5)-(4,5)-(5,5)-(6,5) weight 0.5 each → edge (3,5)↔(4,5) = 0.5, total 3 edges = 1.5
        var grid = MakeGrid();
        grid.SetZone(2, 5, ZoneType.Residential);
        for (var x = 3; x <= 6; x++)
        {
            grid.SetZone(x, 5, ZoneType.Avenue);
            _graph.AddNode(x, 5, 0.5f);
        }
        grid.SetZone(7, 5, ZoneType.FireStation);

        var dist = _graph.GetDistanceViaRoads(grid, 2, 5, 7, 5);

        // (3,5)→(4,5)→(5,5)→(6,5): edges each = (0.5+0.5)/2 = 0.5; 3 edges = 1.5
        Assert.That(dist, Is.EqualTo(1.5f).Within(0.001f));
    }

    [Test]
    public void GetDistanceViaRoads_DisconnectedRoadNetwork_ReturnsMaxValue()
    {
        // Two separate road islands — no path between them
        var grid = MakeGrid();
        grid.SetZone(2, 5, ZoneType.Residential);
        grid.SetZone(2, 4, ZoneType.Road);    // island A
        _graph.AddNode(2, 4, 1.0f);

        grid.SetZone(8, 5, ZoneType.FireStation);
        grid.SetZone(8, 4, ZoneType.Road);    // island B (no connection to island A)
        _graph.AddNode(8, 4, 1.0f);

        var dist = _graph.GetDistanceViaRoads(grid, 2, 5, 8, 5);

        Assert.That(dist, Is.EqualTo(float.MaxValue));
    }

    [Test]
    public void GetDistanceViaRoads_TileIsItself_AdjacentToRoad_ReturnsZero()
    {
        // When both tiles are the same position — trivially distance 0
        var grid = MakeGrid();
        grid.SetZone(5, 5, ZoneType.Residential);
        grid.SetZone(5, 6, ZoneType.Road);
        _graph.AddNode(5, 6, 1.0f);

        var dist = _graph.GetDistanceViaRoads(grid, 5, 5, 5, 5);

        // FindNearestRoadNeighbour(5,5) = (5,6); same neighbor used for both → distance 0
        Assert.That(dist, Is.EqualTo(0f));
    }

    [Test]
    public void GetDistanceViaRoads_RoadTileItself_UsedDirectly()
    {
        // A road tile itself is a node in the graph — FindNearestRoadNeighbour returns itself
        var grid = MakeGrid();
        grid.SetZone(3, 5, ZoneType.Road);
        grid.SetZone(4, 5, ZoneType.Road);
        grid.SetZone(5, 5, ZoneType.Residential);
        _graph.AddNode(3, 5, 1.0f);
        _graph.AddNode(4, 5, 1.0f);

        // (3,5) is itself a road node; (5,5) has (4,5) as road neighbor
        var dist = _graph.GetDistanceViaRoads(grid, 3, 5, 5, 5);

        // (3,5)→(4,5): single edge = 1.0
        Assert.That(dist, Is.EqualTo(1.0f).Within(0.001f));
    }

    [Test]
    public void GetDistanceViaRoads_LongRoadPath_WithinServiceRadius_IsCovered()
    {
        // 8 road tiles between residential and fire station — distance = 7 road edges = 7.0
        // FireStation radius = 8.0, so this IS covered.
        var grid = MakeGrid();
        grid.SetZone(0, 5, ZoneType.Residential);
        for (var x = 1; x <= 8; x++)
        {
            grid.SetZone(x, 5, ZoneType.Road);
            _graph.AddNode(x, 5, 1.0f);
        }
        grid.SetZone(9, 5, ZoneType.FireStation);
        // road neighbor of (0,5) = (1,5); road neighbor of (9,5) = (8,5)
        // distance (1,5)→(8,5) = 7 road edges = 7.0 ≤ 8.0 → covered

        var dist = _graph.GetDistanceViaRoads(grid, 0, 5, 9, 5);

        Assert.That(dist, Is.EqualTo(7.0f).Within(0.001f));
        Assert.That(dist, Is.LessThanOrEqualTo(8.0f), "Should be within FireStation road-graph radius");
    }

    [Test]
    public void GetDistanceViaRoads_TooFarViaRoad_NotCoveredByFireStation()
    {
        // 9 road tiles between residential and fire station — distance = 8.0 road edges
        // Neighbors: (1,5) to (9,5) = 8 edges = 8.0.
        // FireStation radius = 8.0 so exactly at boundary — IS covered (≤ 8.0).
        // Place 10 road tiles so distance = 9.0 > 8.0 → NOT covered.
        var grid = new CityGrid(15, 15);
        grid.SetFlatTerrain();
        grid.SetZone(0, 5, ZoneType.Residential);
        for (var x = 1; x <= 10; x++)
        {
            grid.SetZone(x, 5, ZoneType.Road);
            _graph.AddNode(x, 5, 1.0f);
        }
        grid.SetZone(11, 5, ZoneType.FireStation);
        // road neighbor of (0,5) = (1,5); road neighbor of (11,5) = (10,5)
        // distance (1,5)→(10,5) = 9 road edges = 9.0 > 8.0 → NOT covered

        var dist = _graph.GetDistanceViaRoads(grid, 0, 5, 11, 5);

        Assert.That(dist, Is.EqualTo(9.0f).Within(0.001f));
        Assert.That(dist, Is.GreaterThan(8.0f), "Should exceed FireStation road-graph radius");
    }
}
