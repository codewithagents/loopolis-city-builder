using Godot;
using Loopolis.Core.Grid;
using Loopolis.Core.Simulation;

namespace LoopolisGodot;

public partial class World : Node2D
{
	private CityGrid _grid = null!;
	private SimulationEngine _engine = null!;
	private double _tickTimer = 0;
	private const double TickInterval = 0.5; // seconds per sim tick

	private TilemapRenderer _renderer = null!;

	// ── Static server process management (used by MainMenu) ─────────────────
	private static int _serverPid = -1;

	public static void SetServerPid(int pid) => _serverPid = pid;

	public static void KillServerIfRunning()
	{
		if (_serverPid <= 0) return;
		try { OS.Kill(_serverPid); } catch { /* already dead */ }
		_serverPid = -1;
	}

	public override void _Ready()
	{
		// Find child nodes by name — avoids Export wiring in .tscn
		_renderer = GetNode<TilemapRenderer>("TilemapRenderer");

		_grid = new CityGrid(32, 32);

		// Default scenario: wired starter city
		_grid.SetZone(10, 10, ZoneType.PowerPlant);
		_grid.SetZone(10, 11, ZoneType.Road);
		_grid.SetZone(10, 12, ZoneType.Road);
		_grid.SetZone(9,  12, ZoneType.Residential);
		_grid.SetZone(11, 12, ZoneType.Residential);
		_grid.SetZone(9,  13, ZoneType.Residential);
		_grid.SetZone(10, 13, ZoneType.Road);
		_grid.SetZone(11, 13, ZoneType.Commercial);

		var budget     = new BudgetSystem(initialBalance: 10_000);
		var population = new PopulationSystem();
		var power      = new PowerNetwork();
		var roads      = new RoadNetwork();
		var demand     = new DemandSystem();

		_engine = new SimulationEngine(_grid, budget, population, power, roads, demand);

		_renderer.Refresh(_grid);
	}

	public override void _Process(double delta)
	{
		_tickTimer += delta;
		if (_tickTimer >= TickInterval)
		{
			_tickTimer = 0;
			_engine.Tick();
			_renderer.Refresh(_grid);
		}
	}
}
