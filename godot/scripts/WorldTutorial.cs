using Godot;
using System.Linq;
using Loopolis.Core.Grid;

namespace LoopolisGodot;

/// <summary>
/// Guided tutorial partial — 5-step tutorial state machine plus passive hint progression.
/// Tutorial fields live here because they are only used by tutorial logic.
/// </summary>
public partial class World : Node2D
{
	// ── Guided tutorial state machine ──────────────────────────────────────
	private bool _tutorialActive   = false;
	private int  _tutorialStep     = 0;  // 1-5; 0 = not started
	private TutorialPanel _tutorialPanel = null!;
	private bool _tutorialStep1Done = false; // road placed
	private bool _tutorialStep2Done = false; // 2+ R zones adjacent to road
	private bool _tutorialStep3Done = false; // coal/power plant placed
	private bool _tutorialStep4Done = false; // at least 1 R zone tile HasPower
	private bool _tutorialStep5Done = false; // first building appeared
	private float _tutorialStepFlashTimer = 0f; // brief pause after CompleteStep before advancing

	private static readonly string[] TutorialStepMessages =
	{
		"",  // index 0 unused
		"Step 1: Place a Road\nClick 'Road' in the toolbar and build north of the highway stub.",
		"Step 2: Zone Residential\nClick '🏠 Residential' and place 2+ zones next to your road.",
		"Step 3: Add Power\nOpen the Utilities tab (press U), then place a Coal Plant away from homes.",
		"Step 4: Connect Power Lines\nUse 'Pwr Line' to link the plant to your zones.",
		"Step 5: Watch it grow!\nPress Space or click Resume — wait for your first cottage!"
	};

	private void AdvanceTutorial(int nextStep)
	{
		_tutorialStep = nextStep;
		if (nextStep >= 1 && nextStep <= 5)
			_tutorialPanel.ShowStep(nextStep, TutorialStepMessages[nextStep]);

		// Step 5: auto-unpause so the simulation runs and buildings can spawn
		if (nextStep == 5)
		{
			_standalonePaused = false;
			_toolbar.SetPaused(false);
		}
	}

	/// <summary>
	/// Called from _Process after the flash timer expires.  Figures out which step
	/// to move to based on what has already been completed.
	/// </summary>
	private void AdvanceTutorialDelayed()
	{
		if (!_tutorialStep1Done)          { AdvanceTutorial(1); return; }
		if (!_tutorialStep2Done)          { AdvanceTutorial(2); return; }
		if (!_tutorialStep3Done)          { AdvanceTutorial(3); return; }
		if (!_tutorialStep4Done)          { AdvanceTutorial(4); return; }
		if (!_tutorialStep5Done)          { AdvanceTutorial(5); return; }
		CompleteTutorial();
	}

	/// <summary>
	/// Inspects the current grid / engine state and advances the tutorial when
	/// a step's completion condition is satisfied.  Safe to call every frame / every
	/// tile-placement — guards against re-triggering already-completed steps.
	/// </summary>
	private void CheckTutorialProgress()
	{
		if (!_tutorialActive) return;

		// Step 1: road placed
		if (_tutorialStep == 1 && !_tutorialStep1Done)
		{
			var hasRoad = _grid.AllTiles().Any(t =>
				t.Zone == ZoneType.Road ||
				t.Zone == ZoneType.Avenue);
			if (hasRoad)
			{
				_tutorialStep1Done = true;
				_tutorialPanel.CompleteStep();
				_tutorialStepFlashTimer = 1.2f; // brief delay before advancing
				return;
			}
		}

		// Step 2: 2+ residential zone tiles placed
		if (_tutorialStep == 2 && !_tutorialStep2Done)
		{
			var resCount = _grid.AllTiles().Count(t => t.Zone == ZoneType.Residential);
			if (resCount >= 2)
			{
				_tutorialStep2Done = true;
				_tutorialPanel.CompleteStep();
				_tutorialStepFlashTimer = 1.2f;
				return;
			}
		}

		// Step 3: coal/nuclear/power plant placed
		if (_tutorialStep == 3 && !_tutorialStep3Done)
		{
			var hasPlant = _grid.AllTiles().Any(t =>
				t.Zone == ZoneType.CoalPlant ||
				t.Zone == ZoneType.NuclearPlant ||
				t.Zone == ZoneType.PowerPlant);
			if (hasPlant)
			{
				_tutorialStep3Done = true;
				_tutorialPanel.CompleteStep();
				_tutorialStepFlashTimer = 1.2f;
				return;
			}
		}

		// Step 4: at least 1 residential zone tile HasPower
		if (_tutorialStep == 4 && !_tutorialStep4Done)
		{
			// Re-propagate power network so the check reflects the current layout
			// even when paused in build mode.
			_engine.PowerNetwork.Propagate(_grid);
			var anyPowered = _grid.AllTiles().Any(t =>
				t.Zone == ZoneType.Residential && t.HasPower);
			if (anyPowered)
			{
				_tutorialStep4Done = true;
				_tutorialPanel.CompleteStep();
				_tutorialStepFlashTimer = 1.2f;
				return;
			}
		}

		// Step 5: first building appears — checked after tick in _Process
		if (_tutorialStep == 5 && !_tutorialStep5Done)
		{
			var hasBuilding = _grid.Buildings.Count > 0;
			if (hasBuilding)
			{
				_tutorialStep5Done = true;
				CompleteTutorial();
			}
		}
	}

	private void CompleteTutorial()
	{
		_tutorialActive = false;
		_tutorialPanel.HideTutorial();
		_toastSystem.AddMilestone("Tutorial complete! Your city has started. Keep building!");
		Log("[tutorial] Completed — first building spawned!");
	}
}
