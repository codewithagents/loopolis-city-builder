using Godot;
using System;

namespace LoopolisGodot;

/// <summary>
/// Static helper that contains all per-building-type procedural drawing logic.
/// Extracted from TilemapRenderer.DrawBuildingDetail to keep that file focused on
/// grid-level concerns (passes, overlays, animations).
///
/// All draw primitives are passed in as delegates so this class has zero Godot scene-tree
/// dependencies and can be tested or mocked independently.
/// </summary>
public static class BuildingDrawer
{
	/// <summary>
	/// Draws procedural geometric detail for a single building type.
	/// </summary>
	/// <param name="typeId">Building TypeId (e.g. "res_house_1x1").</param>
	/// <param name="buildingWidth">Building footprint width in tiles.</param>
	/// <param name="buildingHeight">Building footprint height in tiles.</param>
	/// <param name="ox">World-pixel X origin of the anchor tile.</param>
	/// <param name="oy">World-pixel Y origin of the anchor tile.</param>
	/// <param name="scale">Spawn animation scale (1.0 = fully visible, &lt;1 = mid-animation).</param>
	/// <param name="tileSize">Pixels per tile (normally TilemapRenderer.TileSize = 32).</param>
	/// <param name="drawRect">Draws a filled rectangle in world space.</param>
	/// <param name="drawTriangle">Draws a filled triangle from three world-space vertices.</param>
	/// <param name="drawLine">Draws a line between two world-space points with given width.</param>
	/// <param name="drawCircle">Draws a filled circle at a world-space center with given radius.</param>
	public static void Draw(
		string typeId,
		int buildingWidth,
		int buildingHeight,
		float ox,
		float oy,
		float scale,
		int tileSize,
		Action<Rect2, Color> drawRect,
		Action<Vector2, Vector2, Vector2, Color> drawTriangle,
		Action<Vector2, Vector2, Color, float> drawLine,
		Action<Vector2, float, Color> drawCircle)
	{
		var fullW = buildingWidth  * tileSize;
		var fullH = buildingHeight * tileSize;

		// ── Local draw helpers with scale transform ──────────────────────────

		void R(float rx, float ry, float rw, float rh, Color c)
		{
			if (scale < 0.99f)
			{
				var cx = fullW * 0.5f;
				var cy = fullH * 0.5f;
				rx = cx + (rx - cx) * scale;
				ry = cy + (ry - cy) * scale;
				rw *= scale;
				rh *= scale;
			}
			drawRect(new Rect2(ox + rx, oy + ry, rw, rh), c);
		}

		void T(float ax, float ay, float bx, float by, float cx2, float cy2, Color c)
		{
			if (scale < 0.99f)
			{
				var centreX = fullW * 0.5f;
				var centreY = fullH * 0.5f;
				ax  = centreX + (ax  - centreX) * scale; ay  = centreY + (ay  - centreY) * scale;
				bx  = centreX + (bx  - centreX) * scale; by  = centreY + (by  - centreY) * scale;
				cx2 = centreX + (cx2 - centreX) * scale; cy2 = centreY + (cy2 - centreY) * scale;
			}
			drawTriangle(
				new Vector2(ox + ax, oy + ay),
				new Vector2(ox + bx, oy + by),
				new Vector2(ox + cx2, oy + cy2),
				c);
		}

		void L(float ax, float ay, float bx, float by, Color c, float w = 1f)
		{
			if (scale < 0.99f)
			{
				var cx = fullW * 0.5f;
				var cy = fullH * 0.5f;
				ax = cx + (ax - cx) * scale; ay = cy + (ay - cy) * scale;
				bx = cx + (bx - cx) * scale; by = cy + (by - cy) * scale;
			}
			drawLine(new Vector2(ox + ax, oy + ay), new Vector2(ox + bx, oy + by), c, w);
		}

		void C(float cx2, float cy2, float r, Color c)
		{
			if (scale < 0.99f)
			{
				var centreX = fullW * 0.5f;
				var centreY = fullH * 0.5f;
				cx2 = centreX + (cx2 - centreX) * scale;
				cy2 = centreY + (cy2 - centreY) * scale;
				r *= scale;
			}
			drawCircle(new Vector2(ox + cx2, oy + cy2), r, c);
		}

		// ── Per-type drawing logic ───────────────────────────────────────────

		switch (typeId)
		{
			case "res_house_1x1":
			{
				// Body — cream
				R(4, 14, 24, 14, new Color(0.93f, 0.89f, 0.82f));
				// Roof — dark brown triangle [(1,14),(31,14),(16,3)]
				T(1, 14, 31, 14, 16, 3, new Color(0.52f, 0.28f, 0.12f));
				// Door — brown rect
				R(12, 20, 8, 8, new Color(0.38f, 0.22f, 0.10f));
				// Windows — light blue
				R(4,  16, 6, 5, new Color(0.75f, 0.88f, 1.0f, 0.9f));
				R(22, 16, 6, 5, new Color(0.75f, 0.88f, 1.0f, 0.9f));
				break;
			}

			case "res_townhouse_2x2":
			{
				// Two side-by-side townhouse units (64×64 px footprint)
				var bodyColor  = new Color(0.85f, 0.78f, 0.68f);
				var roofColor  = new Color(0.72f, 0.38f, 0.18f);
				var winColor   = new Color(0.75f, 0.88f, 1.0f, 0.85f);
				var doorColor  = new Color(0.38f, 0.22f, 0.10f);
				var divColor   = new Color(0.25f, 0.18f, 0.12f, 0.8f);

				// Left unit body
				R(4, 24, 24, 36, bodyColor);
				// Left unit roof (peaked triangle)
				T(2, 24, 30, 24, 16, 8, roofColor);
				// Left unit door
				R(13, 44, 8, 16, doorColor);
				// Left unit windows — floor 1
				R(5, 27, 7, 6, winColor);
				R(20, 27, 7, 6, winColor);
				// Left unit windows — floor 2
				R(5, 37, 7, 6, winColor);
				R(20, 37, 7, 6, winColor);

				// Right unit body (mirrored at x=32)
				R(36, 24, 24, 36, bodyColor);
				// Right unit roof
				T(34, 24, 62, 24, 48, 8, roofColor);
				// Right unit door
				R(45, 44, 8, 16, doorColor);
				// Right unit windows — floor 1
				R(37, 27, 7, 6, winColor);
				R(52, 27, 7, 6, winColor);
				// Right unit windows — floor 2
				R(37, 37, 7, 6, winColor);
				R(52, 37, 7, 6, winColor);

				// Dividing wall
				R(30, 20, 4, 40, divColor);
				break;
			}

			case "res_villa_2x3":
			{
				// 2w×3h = 64×96 px. Wide sprawling house.
				var bodyColor   = new Color(0.95f, 0.92f, 0.85f);
				var roofColor   = new Color(0.42f, 0.55f, 0.28f); // green — forest setting
				var winColor    = new Color(0.75f, 0.88f, 1.0f, 0.85f);
				var doorColor   = new Color(0.38f, 0.22f, 0.10f);
				var gardenColor = new Color(0.30f, 0.68f, 0.22f, 0.85f);

				// Garden at top (north edge)
				R(8, 4, 48, 10, gardenColor);
				// Body
				R(5, 16, 54, 68, bodyColor);
				// Roof — low-pitch triangle covering full width
				T(2, 16, 62, 16, 32, 4, roofColor);
				// Door
				R(24, 64, 12, 20, doorColor);
				// Windows — 2 columns × 2 rows
				R(8,  22, 10, 8, winColor);
				R(46, 22, 10, 8, winColor);
				R(8,  40, 10, 8, winColor);
				R(46, 40, 10, 8, winColor);
				break;
			}

			case "res_villa_3x2":
			{
				// 3w×2h = 96×64 px. Wide sprawling house.
				var bodyColor   = new Color(0.95f, 0.92f, 0.85f);
				var roofColor   = new Color(0.42f, 0.55f, 0.28f);
				var winColor    = new Color(0.75f, 0.88f, 1.0f, 0.85f);
				var doorColor   = new Color(0.38f, 0.22f, 0.10f);
				var gardenColor = new Color(0.30f, 0.68f, 0.22f, 0.85f);

				// Garden strip at left edge
				R(4, 8, 10, 48, gardenColor);
				// Body
				R(16, 8, 72, 48, bodyColor);
				// Roof — low-pitch wide triangle
				T(14, 8, 90, 8, 52, 0, roofColor);
				// Door
				R(42, 40, 12, 16, doorColor);
				// Windows — 3 columns × 1 row
				R(20, 14, 12, 9, winColor);
				R(42, 14, 12, 9, winColor);
				R(64, 14, 12, 9, winColor);
				break;
			}

			case "res_apartment_4x4":
			{
				// 4w×4h = 128×128 px. Concrete slab apartment block.
				var bodyColor   = new Color(0.72f, 0.74f, 0.78f); // concrete grey
				var lobbyColor  = new Color(0.60f, 0.62f, 0.66f);
				var winColor    = new Color(1.0f, 0.95f, 0.80f, 0.85f);
				var roofTopColor= new Color(0.58f, 0.60f, 0.64f);

				// Main building body with small setback
				R(6, 10, 116, 110, bodyColor);
				// Ground floor lobby — slightly darker
				R(6, 94, 116, 26, lobbyColor);
				// Wide lobby windows
				R(10, 97, 24, 18, winColor);
				R(40, 97, 24, 18, winColor);
				R(70, 97, 24, 18, winColor);
				R(100, 97, 18, 18, winColor);
				// Rooftop structure
				R(40, 4, 48, 8, roofTopColor);
				// Window grid: 4 columns × 4 rows (upper floors only)
				for (var col = 0; col < 4; col++)
				for (var row = 0; row < 4; row++)
					R(10 + col * 28, 14 + row * 18, 18, 12, winColor);
				break;
			}

			case "com_shop_1x1":
			{
				// Glass shopfront, awning, sign band
				var glassColor  = new Color(0.82f, 0.90f, 0.96f, 0.85f);
				var awningColor = new Color(0.22f, 0.48f, 0.82f);
				var signColor   = new Color(0.92f, 0.78f, 0.15f);
				var doorBorder  = new Color(0.15f, 0.10f, 0.05f, 0.9f);

				// Glass façade
				R(3, 10, 26, 18, glassColor);
				// Awning strip
				R(2, 10, 28, 4, awningColor);
				// Sign band
				R(4, 14, 24, 4, signColor);
				// Door outline (thin lines instead of fill)
				L(12, 20, 12, 28, doorBorder, 1.5f);
				L(20, 20, 20, 28, doorBorder, 1.5f);
				L(12, 20, 20, 20, doorBorder, 1.5f);
				break;
			}

			case "com_strip_1x3":
			{
				// Three shopfronts side by side in a 32×96px column
				var awningColors = new[]
				{
					new Color(0.22f, 0.48f, 0.82f),  // blue
					new Color(0.82f, 0.30f, 0.18f),  // terracotta
					new Color(0.18f, 0.60f, 0.35f),  // green
				};
				var glassColor = new Color(0.82f, 0.90f, 0.96f, 0.80f);
				var signColor  = new Color(0.92f, 0.78f, 0.15f);

				for (var i = 0; i < 3; i++)
				{
					float sy = i * 32f;
					// Glass
					R(3, sy + 10, 26, 16, glassColor);
					// Awning
					R(2, sy + 10, 28, 4, awningColors[i]);
					// Sign at slightly different height to add variety
					R(4, sy + 14 + i * 1, 24, 4, signColor);
				}
				break;
			}

			case "com_strip_3x1":
			{
				// Three shopfronts side by side in a 96×32px row
				var awningColors = new[]
				{
					new Color(0.22f, 0.48f, 0.82f),
					new Color(0.82f, 0.30f, 0.18f),
					new Color(0.18f, 0.60f, 0.35f),
				};
				var glassColor = new Color(0.82f, 0.90f, 0.96f, 0.80f);
				var signColor  = new Color(0.92f, 0.78f, 0.15f);

				for (var i = 0; i < 3; i++)
				{
					float sx = i * 32f;
					// Glass
					R(sx + 3, 10, 26, 16, glassColor);
					// Awning
					R(sx + 2, 10, 28, 4, awningColors[i]);
					// Sign
					R(sx + 4, 14 + i * 1, 24, 4, signColor);
				}
				break;
			}

			case "com_shopping_3x3":
			{
				// 96×96 px shopping centre — glass-and-steel look
				var frameColor   = new Color(0.68f, 0.70f, 0.74f);
				var glassStrip   = new Color(0.82f, 0.90f, 0.96f, 0.80f);
				var entryColor   = new Color(0.55f, 0.58f, 0.62f);
				var winColor     = new Color(0.82f, 0.90f, 0.96f, 0.70f);
				var parkingColor = new Color(0.30f, 0.30f, 0.30f, 0.80f);
				var lineColor    = new Color(0.65f, 0.65f, 0.65f, 0.6f);

				// Building body
				R(4, 4, 88, 80, frameColor);
				// Upper window band (near top)
				R(6, 8, 84, 16, glassStrip);
				// Window rows — 3 columns × 2 rows of glass panels
				for (var col = 0; col < 3; col++)
				for (var row = 0; row < 2; row++)
					R(8 + col * 28, 28 + row * 18, 22, 14, winColor);
				// Ground floor entrance
				R(20, 66, 56, 18, entryColor);
				// Entry doors (glass)
				R(30, 66, 16, 18, new Color(0.82f, 0.90f, 0.96f, 0.60f));
				R(50, 66, 16, 18, new Color(0.82f, 0.90f, 0.96f, 0.60f));
				// Parking area at bottom strip
				R(4, 84, 88, 8, parkingColor);
				// Parking space lines (2×3 grid of dots)
				for (var col = 0; col < 3; col++)
				for (var row = 0; row < 2; row++)
					C(18f + col * 26f, 87f + row * 3f, 2f, lineColor);
				break;
			}

			case "ind_factory_1x1":
			{
				// Industrial body
				R(3, 12, 26, 16, new Color(0.52f, 0.50f, 0.46f));
				// Chimney shaft
				R(21, 4, 4, 10, new Color(0.40f, 0.38f, 0.35f));
				// Chimney cap
				C(23, 4, 3, new Color(0.35f, 0.33f, 0.30f));
				// 3 orange glow windows
				R(4,  16, 4, 5, new Color(1.0f, 0.65f, 0.15f, 0.85f));
				R(11, 16, 4, 5, new Color(1.0f, 0.65f, 0.15f, 0.85f));
				R(18, 16, 4, 5, new Color(1.0f, 0.65f, 0.15f, 0.85f));
				break;
			}

			case "ind_mill_2x2":
			{
				// 64×64px Timber Mill — warm wood tone
				var woodColor  = new Color(0.62f, 0.42f, 0.22f);
				var logColor   = new Color(0.45f, 0.30f, 0.18f);
				var roofColor  = new Color(0.50f, 0.34f, 0.18f);
				var sawColor   = new Color(0.75f, 0.72f, 0.68f, 0.9f);

				// Mill body (central ~50% of footprint)
				R(14, 16, 36, 40, woodColor);
				// Low-pitch roof
				T(12, 16, 52, 16, 32, 8, roofColor);
				// Log stack along left side — 3 circular log ends
				C(8f,  28f, 5f, logColor);
				C(8f,  38f, 5f, logColor);
				C(8f,  48f, 5f, logColor);
				// Log highlights (lighter circle inside each)
				C(8f, 28f, 3f, new Color(0.55f, 0.38f, 0.22f));
				C(8f, 38f, 3f, new Color(0.55f, 0.38f, 0.22f));
				C(8f, 48f, 3f, new Color(0.55f, 0.38f, 0.22f));
				// Saw blade hint at bottom-right corner: circle outline + radial tick lines
				C(52f, 52f, 7f, sawColor);
				for (var i = 0; i < 6; i++)
				{
					var angle = i * Mathf.Pi / 3f;
					L(52f + Mathf.Cos(angle) * 5f, 52f + Mathf.Sin(angle) * 5f,
					  52f + Mathf.Cos(angle) * 7f, 52f + Mathf.Sin(angle) * 7f,
					  sawColor, 1f);
				}
				break;
			}

			case "ind_quarry_2x2":
			{
				// 64×64px Quarry — open pit with concentric arcs
				var pitFloor = new Color(0.35f, 0.33f, 0.30f);
				var pitWall  = new Color(0.58f, 0.55f, 0.50f);
				var equipColor = new Color(0.42f, 0.40f, 0.38f);

				// Outermost ring (pit wall)
				C(32f, 36f, 24f, pitWall);
				// Mid ring
				C(32f, 36f, 16f, new Color(0.46f, 0.44f, 0.40f));
				// Pit floor (centre)
				C(32f, 36f, 8f, pitFloor);
				// Crane/excavator silhouette — L-shape in upper-right
				R(44, 6, 4, 18, equipColor); // vertical arm
				R(44, 6, 16, 4, equipColor); // horizontal arm
				// Hanging cable
				L(60f, 10f, 60f, 22f, equipColor, 1f);
				break;
			}

			case "ind_warehouse_2x2":
			{
				// 64×64px Warehouse — wide flat-roofed building
				var wallColor  = new Color(0.68f, 0.65f, 0.60f);
				var roofColor  = new Color(0.58f, 0.55f, 0.52f);
				var bayColor   = new Color(0.25f, 0.23f, 0.20f, 0.9f);
				var hatchColor = new Color(0.52f, 0.50f, 0.46f, 0.6f);

				// Building body
				R(4, 20, 56, 40, wallColor);
				// Flat roof area
				R(4, 12, 56, 10, roofColor);
				// Corrugated roof hint — thin horizontal lines
				for (var i = 0; i < 4; i++)
					L(4f, 13f + i * 2.2f, 60f, 13f + i * 2.2f, hatchColor, 0.8f);
				// Loading bay doors (3 dark rectangles along bottom edge)
				R(5,  44, 14, 16, bayColor);
				R(25, 44, 14, 16, bayColor);
				R(45, 44, 14, 16, bayColor);
				// Bay door highlights (single line)
				L(5f,  52f, 19f, 52f, new Color(0.45f, 0.42f, 0.38f, 0.5f), 1f);
				L(25f, 52f, 39f, 52f, new Color(0.45f, 0.42f, 0.38f, 0.5f), 1f);
				L(45f, 52f, 59f, 52f, new Color(0.45f, 0.42f, 0.38f, 0.5f), 1f);
				break;
			}

			case "ind_park_4x2":
			{
				// 128×64px Industrial Park — 2 factory units + pipes + parking
				var wallColor  = new Color(0.52f, 0.50f, 0.46f);
				var chimneyCol = new Color(0.40f, 0.38f, 0.35f);
				var glowColor  = new Color(1.0f, 0.65f, 0.15f, 0.85f);
				var pipeColor  = new Color(0.45f, 0.43f, 0.40f);
				var parkColor  = new Color(0.30f, 0.30f, 0.30f, 0.7f);

				// Unit 1 (left)
				R(4,  16, 52, 40, wallColor);
				// Unit 2 (right)
				R(72, 16, 52, 40, wallColor);
				// Chimney unit 1
				R(21, 4, 6, 14, chimneyCol);
				C(24f, 4f, 4f, chimneyCol);
				// Chimney unit 2
				R(89, 4, 6, 14, chimneyCol);
				C(92f, 4f, 4f, chimneyCol);
				// Glow windows unit 1
				R(6,  24, 6, 6, glowColor);
				R(16, 24, 6, 6, glowColor);
				R(26, 24, 6, 6, glowColor);
				// Glow windows unit 2
				R(74, 24, 6, 6, glowColor);
				R(84, 24, 6, 6, glowColor);
				R(94, 24, 6, 6, glowColor);
				// Connecting pipe between units
				L(56f, 36f, 72f, 36f, pipeColor, 3f);
				L(56f, 28f, 72f, 28f, pipeColor, 2f);
				// Parking area at bottom-right
				R(60, 50, 64, 10, parkColor);
				break;
			}

			case "ind_park_2x4":
			{
				// 64×128px Industrial Park — 2 factory units stacked + pipes + parking
				var wallColor  = new Color(0.52f, 0.50f, 0.46f);
				var chimneyCol = new Color(0.40f, 0.38f, 0.35f);
				var glowColor  = new Color(1.0f, 0.65f, 0.15f, 0.85f);
				var pipeColor  = new Color(0.45f, 0.43f, 0.40f);
				var parkColor  = new Color(0.30f, 0.30f, 0.30f, 0.7f);

				// Unit 1 (top)
				R(8,  4,  48, 52, wallColor);
				// Unit 2 (bottom)
				R(8,  72, 48, 52, wallColor);
				// Chimney unit 1
				R(21, 4, 6, 0, chimneyCol); // shaft (zero height — at rooftop)
				C(24f, 4f, 4f, chimneyCol);
				// Chimney unit 2
				R(21, 72, 6, 0, chimneyCol);
				C(24f, 72f, 4f, chimneyCol);
				// Glow windows unit 1
				R(10, 20, 6, 6, glowColor);
				R(20, 20, 6, 6, glowColor);
				R(30, 20, 6, 6, glowColor);
				// Glow windows unit 2
				R(10, 88, 6, 6, glowColor);
				R(20, 88, 6, 6, glowColor);
				R(30, 88, 6, 6, glowColor);
				// Connecting pipe
				L(32f, 56f, 32f, 72f, pipeColor, 3f);
				L(20f, 56f, 20f, 72f, pipeColor, 2f);
				// Parking area on right side
				R(58, 40, 4, 48, parkColor);
				break;
			}

			case "res_highrise_6x6":
			{
				// 192×192px Residential High-Rise — glass skyscraper, crown jewel of residential.
				var towerBody   = new Color(0.72f, 0.78f, 0.88f);         // light blue-grey glass
				var towerDark   = new Color(0.60f, 0.66f, 0.76f);         // slightly darker wing mass
				var windowColor = new Color(1.0f, 0.97f, 0.88f, 0.90f);  // warm lit windows
				var roofColor   = new Color(0.38f, 0.40f, 0.44f);         // dark rooftop structure
				var lobbyColor  = new Color(0.82f, 0.88f, 0.96f, 0.85f); // wide lobby glass
				var frameColor  = new Color(0.50f, 0.55f, 0.65f, 0.70f); // subtle window frame

				// Side wings slightly darker — suggest full building mass behind the tower
				R(0,  30, 20, 152, towerDark);   // left wing
				R(172, 30, 20, 152, towerDark);  // right wing

				// Tower setback base (slightly wider at ground level, 3% each side)
				// Upper tower body — inset 6px on each side, from y=10 upwards
				R(6, 10, 180, 150, towerBody);
				// Wider base (ground level setback): full footprint width, lower section
				R(0, 140, 192, 42, towerBody);

				// Ground floor lobby — wider windows suggesting arcade/atrium
				R(8,  156, 36, 26, lobbyColor);
				R(52, 156, 36, 26, lobbyColor);
				R(96, 156, 36, 26, lobbyColor);
				R(140, 156, 36, 26, lobbyColor);
				// Lobby archway hint (thin frame lines across each lobby panel)
				L(8f, 156f, 44f, 156f, frameColor, 1.5f);
				L(52f, 156f, 88f, 156f, frameColor, 1.5f);
				L(96f, 156f, 132f, 156f, frameColor, 1.5f);
				L(140f, 156f, 176f, 156f, frameColor, 1.5f);

				// Window grid: 6 columns × 8 rows across tower body (above lobby)
				// Each window: 8×6px with 12px column pitch and 17px row pitch
				for (var col = 0; col < 6; col++)
				for (var row = 0; row < 8; row++)
					R(14 + col * 27, 18 + row * 16, 14, 10, windowColor);

				// Roof mechanical room — dark rect at top
				R(60, 4, 72, 8, roofColor);
				// Antenna: vertical line rising from roof centre
				L(96f, 4f, 96f, 0f, roofColor, 2f);
				// Rooftop parapet edges (thin lines)
				R(6, 8, 180, 3, roofColor);
				break;
			}

			case "com_office_4x4":
			{
				// 128×128px Office Tower — glass-and-steel, modern commercial.
				var baseBody    = new Color(0.58f, 0.68f, 0.88f);         // steel blue base
				var upperBody   = new Color(0.65f, 0.76f, 0.94f);         // lighter upper section
				var glassWin    = new Color(0.78f, 0.88f, 0.96f);         // cool blue-white windows
				var steelFrame  = new Color(0.32f, 0.35f, 0.38f);         // dark grey vertical frames
				var plazaColor  = new Color(0.70f, 0.72f, 0.75f);         // entrance plaza light grey
				var doorColor   = new Color(0.82f, 0.90f, 0.96f, 0.75f); // glass door
				var roofColor   = new Color(0.30f, 0.32f, 0.36f);         // flat rooftop

				// Base section (full width, lower 40%)
				R(4, 76, 120, 48, baseBody);
				// Upper section (slightly inset — taper effect)
				R(10, 10, 108, 68, upperBody);

				// Window grid: 4 rows × 6 columns of horizontal glass bands
				// Upper section windows
				for (var col = 0; col < 6; col++)
				for (var row = 0; row < 3; row++)
					R(12 + col * 17, 16 + row * 20, 12, 13, glassWin);
				// Base section windows (2 rows × 6 columns, wider)
				for (var col = 0; col < 6; col++)
				for (var row = 0; row < 2; row++)
					R(8 + col * 18, 82 + row * 20, 13, 12, glassWin);

				// Vertical steel frame lines between window columns (upper section)
				for (var col = 0; col <= 6; col++)
					L(10f + col * 18f, 10f, 10f + col * 18f, 78f, steelFrame, 1f);

				// Entrance plaza at ground level
				R(4, 116, 120, 10, plazaColor);
				// Two glass doorway openings in plaza
				R(28, 112, 18, 14, doorColor);
				R(82, 112, 18, 14, doorColor);

				// Flat rooftop dark cap
				R(10, 4, 108, 8, roofColor);
				// Satellite dish: small filled circle on rooftop
				C(100f, 8f, 4f, steelFrame);
				break;
			}

			case "ind_complex_4x4":
			{
				// 128×128px Industrial Complex — 4 units in 2×2 grid, chimneys, loading bays, parking.
				var wallColor   = new Color(0.50f, 0.48f, 0.44f);         // dark industrial base
				var roofLight   = new Color(0.58f, 0.56f, 0.52f);         // slightly lighter roof
				var roofDark    = new Color(0.44f, 0.42f, 0.38f);         // darker roof variant
				var bayColor    = new Color(0.20f, 0.18f, 0.16f, 0.92f); // loading bay dark rect
				var hatchColor  = new Color(0.62f, 0.60f, 0.56f, 0.55f); // corrugated roof lines
				var glowColor   = new Color(1.0f, 0.65f, 0.15f, 0.80f);  // industrial glow strip
				var pipeColor   = new Color(0.38f, 0.36f, 0.34f);         // inter-unit pipes
				var chimneyCol  = new Color(0.30f, 0.28f, 0.26f);         // chimney shaft
				var parkColor   = new Color(0.28f, 0.28f, 0.28f, 0.80f); // parking strip

				// ── 4 units in 2×2 layout ──────────────────────────────────────
				// Unit A — top-left (0,0) to (60,58) — tallest roof
				R(4,  4,  56, 52, wallColor);
				R(4,  4,  56,  8, roofLight);  // roof cap slightly lighter
				for (var i = 0; i < 4; i++)
					L(4f, 5f + i * 2f, 60f, 5f + i * 2f, hatchColor, 0.8f);  // corrugated lines
				R(6,  36, 12, 18, bayColor);   // loading bay door
				R(22, 36, 12, 18, bayColor);
				R(6,  44, 12,  2, glowColor);  // glow strip above bay
				R(22, 44, 12,  2, glowColor);
				R(40, 16, 8,   6, new Color(0.72f, 0.80f, 0.88f, 0.80f)); // small window strip

				// Unit B — top-right (68,0) to (124,56) — slightly shorter roof
				R(68, 8,  56, 48, wallColor);
				R(68, 8,  56,  7, roofDark);
				for (var i = 0; i < 3; i++)
					L(68f, 9f + i * 2.2f, 124f, 9f + i * 2.2f, hatchColor, 0.8f);
				R(70, 36, 12, 18, bayColor);
				R(86, 36, 12, 18, bayColor);
				R(70, 44, 12,  2, glowColor);
				R(86, 44, 12,  2, glowColor);
				R(104, 18, 8,  6, new Color(0.72f, 0.80f, 0.88f, 0.80f));

				// Unit C — bottom-left (0,68) to (60,124)
				R(4,  68, 56, 52, wallColor);
				R(4,  68, 56,  8, roofLight);
				for (var i = 0; i < 4; i++)
					L(4f, 69f + i * 2f, 60f, 69f + i * 2f, hatchColor, 0.8f);
				R(6,  100, 12, 18, bayColor);
				R(22, 100, 12, 18, bayColor);
				R(6,  108, 12,  2, glowColor);
				R(22, 108, 12,  2, glowColor);
				R(40,  80, 8,   6, new Color(0.72f, 0.80f, 0.88f, 0.80f));

				// Unit D — bottom-right (68,68) to (124,124)
				R(68, 68, 56, 52, wallColor);
				R(68, 68, 56,  7, roofDark);
				for (var i = 0; i < 3; i++)
					L(68f, 69f + i * 2.2f, 124f, 69f + i * 2.2f, hatchColor, 0.8f);
				R(70, 100, 12, 18, bayColor);
				R(86, 100, 12, 18, bayColor);
				R(70, 108, 12,  2, glowColor);
				R(86, 108, 12,  2, glowColor);
				R(104,  80, 8,  6, new Color(0.72f, 0.80f, 0.88f, 0.80f));

				// ── Connecting corridor / pipes between units ──────────────────
				// Horizontal pipe between left and right units (upper pair)
				L(60f, 28f, 68f, 28f, pipeColor, 4f);
				L(60f, 36f, 68f, 36f, pipeColor, 2.5f);
				// Vertical pipe between top and bottom units (left pair)
				L(32f, 56f, 32f, 68f, pipeColor, 4f);
				L(20f, 56f, 20f, 68f, pipeColor, 2.5f);
				// Horizontal pipe lower pair
				L(60f, 92f, 68f, 92f, pipeColor, 4f);
				L(60f, 100f, 68f, 100f, pipeColor, 2.5f);
				// Vertical pipe right pair
				L(96f, 56f, 96f, 68f, pipeColor, 4f);
				L(108f, 56f, 108f, 68f, pipeColor, 2.5f);

				// ── Chimneys at diagonally opposite corners ────────────────────
				// Top-left chimney (unit A, top-left area)
				R(16,  0, 5, 6, chimneyCol);   // shaft
				C(18.5f, 4f, 4f, chimneyCol);  // cap
				// Bottom-right chimney (unit D, bottom-right area)
				R(102, 116, 5, 6, chimneyCol);
				C(104.5f, 120f, 4f, chimneyCol);

				// ── Parking strip along bottom edge ────────────────────────────
				R(4, 120, 120, 6, parkColor);
				// 4 car dots in parking strip
				C(18f, 123f, 3f, new Color(0.42f, 0.42f, 0.42f, 0.8f));
				C(42f, 123f, 3f, new Color(0.42f, 0.42f, 0.42f, 0.8f));
				C(66f, 123f, 3f, new Color(0.42f, 0.42f, 0.42f, 0.8f));
				C(90f, 123f, 3f, new Color(0.42f, 0.42f, 0.42f, 0.8f));
				break;
			}
		}
	}
}
