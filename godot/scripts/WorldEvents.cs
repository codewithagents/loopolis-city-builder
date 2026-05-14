using Godot;
using System;
using System.Linq;

namespace LoopolisGodot;

/// <summary>
/// Events and visual-effects partial — building birth/crumble toasts, upgrade result polling,
/// crisis event intervention, and spawn-label/ripple helpers.
/// </summary>
public partial class World : Node2D
{
	// Event response panel tracking — which event we last showed so we don't re-show after dismiss
	private string? _lastShownEventType;

	// ── Building birth toasts ──────────────────────────────────────────────

	/// <summary>
	/// Fires building-birth toast notifications for a batch of new building typeIds.
	/// Deduplicates: 3+ of the same type → "3× Name built!" instead of 3 toasts.
	/// Throttles: only shows res_house_1x1 toasts before tick 50; higher-tier always shown.
	/// </summary>
	private void FireBuildingBirthToasts(System.Collections.Generic.IEnumerable<string> typeIds, int currentTick)
	{
		// One chime per tick batch regardless of how many buildings spawned
		_audio.PlayBuildingBorn();

		// Count occurrences per typeId
		var counts = new System.Collections.Generic.Dictionary<string, int>();
		foreach (var id in typeIds)
		{
			if (!counts.ContainsKey(id)) counts[id] = 0;
			counts[id]++;
		}

		foreach (var kvp in counts)
		{
			var typeId = kvp.Key;
			var count  = kvp.Value;

			// Throttle: don't show cottage toasts after tick 50
			if (typeId == "res_house_1x1" && currentTick > 50) continue;

			var friendlyName = GetFriendlyBuildingName(typeId);
			var (emoji, color) = GetBuildingToastStyle(typeId);

			string text;
			if (count >= 3)
				text = $"{emoji} {count}\xd7 {friendlyName} built!";
			else if (count == 2)
				text = $"{emoji} {friendlyName} (×2) built!";
			else
				text = $"{emoji} {friendlyName} built!";

			_toastSystem.AddToast(text, color, 5f);
		}
	}

	/// <summary>Returns (emoji, color) for a building toast based on zone and typeId.</summary>
	private static (string emoji, Color color) GetBuildingToastStyle(string typeId)
	{
		if (typeId.StartsWith("res_"))
			return ("🏘", new Color(1f, 0.75f, 0.35f));
		if (typeId.StartsWith("com_"))
			return ("🏪", new Color(0.4f, 0.8f, 1f));
		// Industrial specialisations
		if (typeId == "ind_mill_2x2")
			return ("🪵", new Color(0.55f, 0.9f, 0.3f));
		if (typeId == "ind_quarry_2x2")
			return ("⛏", new Color(0.8f, 0.75f, 0.5f));
		return ("🏭", new Color(0.9f, 0.85f, 0.3f));
	}

	/// <summary>Maps a building typeId to a friendly display name.</summary>
	private static string GetFriendlyBuildingName(string typeId) => typeId switch
	{
		"res_house_1x1"      => "Cottage",
		"res_townhouse_2x2"  => "Townhouse",
		"res_villa_2x3"      => "Villa",
		"res_villa_3x2"      => "Villa",
		"res_apartment_4x4"  => "Apartment Block",
		"com_shop_1x1"       => "Shop",
		"com_strip_1x3"      => "Strip Mall",
		"com_strip_3x1"      => "Strip Mall",
		"com_shopping_3x3"   => "Shopping Center",
		"ind_factory_1x1"    => "Factory",
		"ind_warehouse_2x2"  => "Warehouse",
		"ind_park_4x2"       => "Industrial Park",
		"ind_park_2x4"       => "Industrial Park",
		"ind_mill_2x2"       => "Timber Mill",
		"ind_quarry_2x2"     => "Quarry",
		_                    => typeId
	};

	// ── Upgrade result polling (viewer mode) ───────────────────────────────

	/// <summary>
	/// Polls the viewer state for upgrade results and shows toasts when a new result arrives.
	/// Called from _Process in viewer mode.
	/// </summary>
	private void PollViewerUpgradeResult()
	{
		var state = _reader?.LastState;
		if (state?.LastUpgradeResult == null) return;

		var result = state.LastUpgradeResult;
		if (result == _lastViewerUpgradeResult) return;
		_lastViewerUpgradeResult = result;

		if (result.StartsWith("ok:"))
		{
			// Format: "ok:building_type_id:-cost"
			var parts = result.Split(':');
			if (parts.Length >= 3)
			{
				var typeId = parts[1];
				if (int.TryParse(parts[2], out var cost))
				{
					var name = GetFriendlyBuildingName(typeId);
					_toastSystem.AddToast($"Upgraded to {name} (-${System.Math.Abs(cost):N0})", new Color(1f, 0.85f, 0.2f), 5f);
				}
			}
		}
		else if (result.StartsWith("err:"))
		{
			var reason = result.Length > 4 ? result[4..] : "Unknown error";
			_toastSystem.AddToast($"Can't upgrade: {reason}", new Color(0.8f, 0.5f, 0.2f), 4f);
		}
	}

	// ── Crisis event intervention ──────────────────────────────────────────

	/// <summary>
	/// Called when the player presses "Intervene" on the EventResponsePanel.
	/// Standalone: calls engine.RespondToCurrentEvent() directly.
	/// Viewer: sends event_respond command to the running server.
	/// </summary>
	private void OnEventInterveneRequested()
	{
		if (_viewerMode)
		{
			var sid = _reader?.SessionId;
			if (sid != null)
				WriteCommand($"{{\"cmd\":\"event_respond\",\"sessionId\":\"{sid}\"}}");
			_audio?.PlayIntervene();
			// Show a generic toast — we don't know the cost in viewer mode until next state tick
			_toastSystem.AddToast("Crisis intervention requested!", new Color(1f, 0.72f, 0.18f), 5f);
		}
		else
		{
			var cost = _engine.PendingEventCost;
			var eventType = _engine.PendingEventType;
			var success = _engine.RespondToCurrentEvent();
			if (success)
			{
				_audio?.PlayIntervene();
				var label = eventType switch
				{
					"FireBreak"   => "Fire contained",
					"CrimeWave"   => "Crime suppressed",
					"PowerOutage" => "Grid restored",
					"DemandSlump" => "Businesses subsidised",
					_             => "Crisis resolved",
				};
				_toastSystem.AddToast($"{label}! (-${cost:N0})", new Color(1f, 0.72f, 0.18f), 6f);
				_eventLog?.AddEntry($"Player intervened: {label} (-${cost:N0})");
			}
			else
			{
				_toastSystem.AddToast("Cannot afford intervention!", new Color(0.9f, 0.3f, 0.2f), 4f);
			}
		}
	}

	// ── Visual spawn effects ───────────────────────────────────────────────

	/// <summary>
	/// Spawns a RippleEffect centred on the tile at (tileX, tileY).
	/// Color is gold for power plants, light grey for roads.
	/// </summary>
	private void SpawnRipple(int tileX, int tileY, string zone)
	{
		var ripple = new RippleEffect();
		ripple.RippleColor = zone switch
		{
			"PowerPlant" or "CoalPlant" or "NuclearPlant" => new Color(1f, 0.835f, 0.31f),  // #FFD54F gold
			_                                              => new Color(0.690f, 0.745f, 0.773f), // #B0BEC5 light grey
		};
		_renderer.AddChild(ripple);
		var center = new Vector2(
			(tileX + 0.5f) * TilemapRenderer.TileSize,
			(tileY + 0.5f) * TilemapRenderer.TileSize);
		ripple.Start(center);
	}

	/// <summary>
	/// Spawns a BuildingBirthLabel floating above the given anchor tile.
	/// </summary>
	private void SpawnBuildingBirthLabel(string typeId, int anchorX, int anchorY)
	{
		var label = new BuildingBirthLabel();
		_renderer.AddChild(label);
		var center = new Vector2(
			(anchorX + 0.5f) * TilemapRenderer.TileSize,
			(anchorY + 0.5f) * TilemapRenderer.TileSize);
		label.Start(center, FormatBirthText(typeId));
	}

	/// <summary>
	/// Spawns a BuildingCrumbleLabel floating above the given anchor tile.
	/// </summary>
	private void SpawnBuildingCrumbleLabel(string typeId, int anchorX, int anchorY)
	{
		var label = new BuildingCrumbleLabel();
		_renderer.AddChild(label);
		var center = new Vector2(
			(anchorX + 0.5f) * TilemapRenderer.TileSize,
			(anchorY + 0.5f) * TilemapRenderer.TileSize);
		label.Start(center, FormatCrumbleText(typeId));
	}

	/// <summary>
	/// Converts a building TypeId to a degradation announcement string.
	/// e.g. "res_townhouse_2x2" → "⚠ Townhouse crumbled"
	/// </summary>
	private static string FormatCrumbleText(string typeId)
	{
		if (string.IsNullOrEmpty(typeId)) return "⚠ Building crumbled";

		// Strip zone prefix
		var s = typeId;
		foreach (var prefix in new[] { "res_", "com_", "ind_" })
		{
			if (s.StartsWith(prefix)) { s = s[prefix.Length..]; break; }
		}

		// Strip trailing _WxH
		var parts = s.Split('_');
		var nameParts = parts;
		if (parts.Length > 1)
		{
			var last = parts[^1];
			if (last.Contains('x') && last.Length <= 5)
				nameParts = parts[..^1];
		}

		// Title-case
		var name = string.Join(" ", System.Array.ConvertAll(nameParts,
			p => p.Length == 0 ? p : char.ToUpper(p[0]) + p[1..]));

		return $"⚠ {name} crumbled";
	}

	/// <summary>
	/// Converts a building TypeId to a short birth announcement string.
	/// e.g. "res_townhouse_2x2" → "+Townhouse", "res_apartment_4x4" → "+Apartment!"
	/// </summary>
	private static string FormatBirthText(string typeId)
	{
		// Strip zone prefix
		var s = typeId;
		foreach (var prefix in new[] { "res_", "com_", "ind_" })
		{
			if (s.StartsWith(prefix)) { s = s[prefix.Length..]; break; }
		}

		// Strip trailing _WxH
		var parts = s.Split('_');
		var nameParts = parts;
		if (parts.Length > 1)
		{
			var last = parts[^1];
			if (last.Contains('x') && last.Length <= 5)
				nameParts = parts[..^1];
		}

		// Title-case
		var name = string.Join(" ", System.Array.ConvertAll(nameParts,
			p => p.Length == 0 ? p : char.ToUpper(p[0]) + p[1..]));

		// Exclamation for largest tier buildings
		var exclaim = typeId.Contains("apartment") || typeId.Contains("shopping") || typeId.Contains("park")
			? "!" : "";

		return $"+{name}{exclaim}";
	}

	// ── Petition toasts ────────────────────────────────────────────────────────

	/// <summary>
	/// Fires toast notifications for new and resolved petitions this tick.
	/// New petitions → orange alert toast. Resolved petitions → green hint toast.
	/// Call this from the same update path as FireBuildingBirthToasts.
	/// </summary>
	internal void FirePetitionToasts(SharedState state)
	{
		// New petitions — orange alert toast
		if (state.NewPetitionThisTick != null)
		{
			foreach (var district in state.NewPetitionThisTick)
			{
				// Look up full text from active petitions list for context
				var petition = state.ActivePetitions?.FirstOrDefault(p => p.DistrictName == district);
				var text = petition != null
					? $"\U0001f4dc Petition: {petition.Text}"
					: $"\U0001f4dc Petition from {district}";
				_toastSystem.AddAlert(text);
			}
		}

		// Resolved petitions — hint-styled toast (softer)
		if (state.ResolvedPetitionThisTick != null)
		{
			foreach (var district in state.ResolvedPetitionThisTick)
				_toastSystem.AddHint($"✓ Petition from {district} resolved!");
		}
	}
}
