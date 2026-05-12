using Loopolis.Core.Graph;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace Loopolis.Core.Tests.Grid;

/// <summary>
/// Tests for the IsBorderConnection flag on tiles, the external anchor API on RoadGraph,
/// and the 1.2× migration growth multiplier for R-tiles near a border connection.
/// </summary>
[TestFixture]
public class BorderConnectionTests
{
    // ── CityGrid.PlaceBorderConnection ─────────────────────────────────────────

    [Test]
    public void PlaceBorderConnection_SetsRoadAndFlag()
    {
        var grid = new CityGrid(10, 10);

        grid.PlaceBorderConnection(5, 9);

        var tile = grid.GetTile(5, 9);
        Assert.That(tile.Zone, Is.EqualTo(ZoneType.Road),
            "Border connection tile must have Zone = Road");
        Assert.That(tile.IsBorderConnection, Is.True,
            "Border connection tile must have IsBorderConnection = true");
    }

    [Test]
    public void PlaceBorderConnection_CannotBeOverwritten()
    {
        var grid = new CityGrid(10, 10);
        grid.PlaceBorderConnection(5, 9);

        // Attempt to overwrite with different zone type — should be silently ignored
        grid.SetZone(5, 9, ZoneType.Residential);
        grid.SetZone(5, 9, ZoneType.Empty);

        var tile = grid.GetTile(5, 9);
        Assert.That(tile.Zone, Is.EqualTo(ZoneType.Road),
            "SetZone must not overwrite a border connection tile");
        Assert.That(tile.IsBorderConnection, Is.True,
            "IsBorderConnection flag must remain true");
    }

    [Test]
    public void PlaceBorderConnection_CannotBeErased_ViaSimulationEngine()
    {
        var grid   = new CityGrid(10, 10);
        grid.SetFlatTerrain();
        grid.PlaceBorderConnection(5, 9);

        var engine = new SimulationEngine(
            grid,
            new BudgetSystem(),
            new PopulationSystem(),
            new PowerNetwork(),
            new RoadNetwork(),
            new DemandSystem());

        // EraseTile must be a no-op for border connection tiles
        engine.EraseTile(5, 9);

        var tile = grid.GetTile(5, 9);
        Assert.That(tile.Zone, Is.EqualTo(ZoneType.Road),
            "EraseTile must not remove a border connection tile");
        Assert.That(tile.IsBorderConnection, Is.True,
            "IsBorderConnection flag must survive EraseTile");
    }

    [Test]
    public void PlaceBorderConnection_DefaultIsFalse()
    {
        var grid = new CityGrid(10, 10);
        Assert.That(grid.GetTile(3, 3).IsBorderConnection, Is.False,
            "New tiles must default to IsBorderConnection = false");
    }

    // ── RoadGraph external anchors ──────────────────────────────────────────────

    [Test]
    public void SetExternalAnchor_IsReturnedByExternalAnchors()
    {
        var graph = new RoadGraph();
        graph.AddNode(5, 9, 1.0f);

        graph.SetExternalAnchor(5, 9);

        Assert.That(graph.IsExternalAnchor(5, 9), Is.True);
        Assert.That(graph.ExternalAnchors, Does.Contain((5, 9)));
    }

    [Test]
    public void SetExternalAnchor_NonAnchorNode_ReturnsFalse()
    {
        var graph = new RoadGraph();
        graph.AddNode(5, 9, 1.0f);
        // No SetExternalAnchor call

        Assert.That(graph.IsExternalAnchor(5, 9), Is.False);
    }

    [Test]
    public void ExternalAnchors_EmptyByDefault()
    {
        var graph = new RoadGraph();
        Assert.That(graph.ExternalAnchors.Count, Is.EqualTo(0));
    }

    [Test]
    public void RemoveNode_ClearsExternalAnchor()
    {
        var graph = new RoadGraph();
        graph.AddNode(5, 9, 1.0f);
        graph.SetExternalAnchor(5, 9);

        graph.RemoveNode(5, 9);

        Assert.That(graph.IsExternalAnchor(5, 9), Is.False);
        Assert.That(graph.ExternalAnchors.Count, Is.EqualTo(0));
    }

    [Test]
    public void SeedRoadGraphFromGrid_BorderConnectionBecomesExternalAnchor()
    {
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();
        grid.PlaceBorderConnection(5, 9);

        var engine = new SimulationEngine(
            grid,
            new BudgetSystem(),
            new PopulationSystem(),
            new PowerNetwork(),
            new RoadNetwork(),
            new DemandSystem());
        engine.SeedRoadGraphFromGrid();

        Assert.That(engine.RoadGraph.IsRoadNode(5, 9), Is.True,
            "Border connection must be a road node in the graph");
        Assert.That(engine.RoadGraph.IsExternalAnchor(5, 9), Is.True,
            "Border connection must be an external anchor in the graph");
    }

    // ── 1.2× migration growth multiplier ───────────────────────────────────────

    [Test]
    public void GrowthMultiplier_AppliedToRTileNearBorder()
    {
        // Layout: border at (5,9) — road spine (5,8)(5,7)(5,6) — R-tile at (4,6) adjacent to road (5,6).
        // Road-graph distance from R-tile entry (5,6) to border anchor (5,9) = 3 ≤ 12 → gets 1.2×.
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();

        grid.PlaceBorderConnection(5, 9);
        grid.SetZone(5, 8, ZoneType.Road);
        grid.SetZone(5, 7, ZoneType.Road);
        grid.SetZone(5, 6, ZoneType.Road);
        grid.SetZone(4, 6, ZoneType.Residential);

        // Mark R-tile as developed (powered + road-adjacent + BuildingId)
        grid.SetPower(4, 6, true);
        grid.SetRoadAccess(4, 6, true);
        grid.SetBuildingId(4, 6, "test");

        var pop = new PopulationSystem();
        var roadGraph = new RoadGraph();
        roadGraph.AddNode(5, 9, 1.0f);
        roadGraph.SetExternalAnchor(5, 9);
        roadGraph.AddNode(5, 8, 1.0f);
        roadGraph.AddNode(5, 7, 1.0f);
        roadGraph.AddNode(5, 6, 1.0f);

        // Tick once with road graph — should apply border multiplier
        pop.Tick(grid, roadGraph: roadGraph);
        var popWithBorder = grid.GetPopulation(4, 6);

        // Reset and tick once WITHOUT road graph — no multiplier
        grid.SetPopulation(4, 6, 0);
        var pop2 = new PopulationSystem();
        pop2.Tick(grid, roadGraph: null);
        var popWithoutBorder = grid.GetPopulation(4, 6);

        Assert.That(popWithBorder, Is.GreaterThan(popWithoutBorder),
            "R-tile within distance 12 of border should grow faster (1.2× multiplier)");
    }

    [Test]
    public void GrowthMultiplier_NotAppliedToFarRTile()
    {
        // Layout: border at (0,0) — R-tile at (9,9) with a road at (9,8) but no road path to border.
        // No road connection → multiplier must NOT be applied.
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();

        grid.PlaceBorderConnection(0, 0); // border at top-left corner
        grid.SetZone(9, 8, ZoneType.Road);
        grid.SetZone(9, 9, ZoneType.Residential);
        grid.SetPower(9, 9, true);
        grid.SetRoadAccess(9, 9, true);
        grid.SetBuildingId(9, 9, "test");

        var pop = new PopulationSystem();
        // Road graph contains only the border and the isolated road at (9,8) — NOT connected
        var roadGraph = new RoadGraph();
        roadGraph.AddNode(0, 0, 1.0f);
        roadGraph.SetExternalAnchor(0, 0);
        roadGraph.AddNode(9, 8, 1.0f);
        // No edges connect (0,0) to (9,8)

        // Also tick WITHOUT road graph for comparison
        grid.SetPopulation(9, 9, 0);
        var pop2 = new PopulationSystem();
        pop2.Tick(grid, roadGraph: null);
        var popNoGraph = grid.GetPopulation(9, 9);

        grid.SetPopulation(9, 9, 0);
        pop.Tick(grid, roadGraph: roadGraph);
        var popWithGraph = grid.GetPopulation(9, 9);

        // Both should be the same — no border multiplier for unreachable tile
        Assert.That(popWithGraph, Is.EqualTo(popNoGraph),
            "R-tile with no road path to border must not get the 1.2× multiplier");
    }

    [Test]
    public void GrowthMultiplier_NotAppliedWhenNoAnchors()
    {
        // Road graph present but no external anchors — should behave like no road graph.
        var grid = new CityGrid(10, 10);
        grid.SetFlatTerrain();
        grid.SetZone(5, 5, ZoneType.Road);
        grid.SetZone(5, 4, ZoneType.Residential);
        grid.SetPower(5, 4, true);
        grid.SetRoadAccess(5, 4, true);
        grid.SetBuildingId(5, 4, "test");

        var roadGraph = new RoadGraph();
        roadGraph.AddNode(5, 5, 1.0f); // no SetExternalAnchor call

        var pop1 = new PopulationSystem();
        pop1.Tick(grid, roadGraph: roadGraph);
        var popWithEmptyAnchorGraph = grid.GetPopulation(5, 4);

        grid.SetPopulation(5, 4, 0);
        var pop2 = new PopulationSystem();
        pop2.Tick(grid, roadGraph: null);
        var popNoGraph = grid.GetPopulation(5, 4);

        Assert.That(popWithEmptyAnchorGraph, Is.EqualTo(popNoGraph),
            "Road graph without external anchors must produce same growth as no road graph");
    }
}
