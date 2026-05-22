using Godot;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Loopolis.Core.Scenarios;

namespace LoopolisGodot;

public partial class MainMenu : Control
{
    // Tracks whether we're showing the scenario picker (or the main menu)
    private Control  _mainPanel     = null!;
    private Control  _scenarioPanel = null!;
    private LineEdit _cityNameEdit  = null!;

    public override void _Ready()
    {
        // Kill any server left from a previous session (via PID file)
        KillOrphanedServer();

        // Also kill if we navigated back from World scene
        World.KillServerIfRunning();

        // Build the shared city-name LineEdit once (reused across panels)
        _cityNameEdit = BuildCityNameEdit();

        // Full-screen dark background
        var bg = new ColorRect();
        bg.Color = new Color(0.08f, 0.08f, 0.12f, 1f);
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // ── Main menu panel ────────────────────────────────────────────────
        _mainPanel = BuildMainPanel();
        AddChild(_mainPanel);

        // ── Scenario picker panel (hidden by default) ──────────────────────
        _scenarioPanel = BuildScenarioPanel();
        _scenarioPanel.Visible = false;
        AddChild(_scenarioPanel);

        // Controls hint at bottom (always visible)
        var hint = new Label();
        hint.Text = "Scroll to zoom  ·  Right-click drag to pan  ·  Space to pause";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.45f));
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.SetAnchorsPreset(LayoutPreset.BottomWide);
        hint.GrowVertical = GrowDirection.Begin;
        hint.Position = new Vector2(0, -28);
        AddChild(hint);
    }

    // ── Main panel ────────────────────────────────────────────────────────

    private Control BuildMainPanel()
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 24);
        center.AddChild(vbox);

        // Title
        var title = new Label();
        title.Text = "LOOPOLIS";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.55f));
        title.AddThemeFontSizeOverride("font_size", 56);
        vbox.AddChild(title);

        // Subtitle
        var subtitle = new Label();
        subtitle.Text = "Build your city. Keep the peace.";
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        subtitle.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(subtitle);

        // Spacer
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 20);
        vbox.AddChild(spacer);

        // City name input row
        vbox.AddChild(BuildCityNameRow(_cityNameEdit));

        // Tutorial button — recommended for new players, amber styling
        var tutorialBtn = MakeMenuButton("Tutorial — Start here!");
        ApplyAmberButtonStyle(tutorialBtn);
        tutorialBtn.Pressed += () =>
        {
            ApplyCityName();
            // Default the city name to "My First City" if the player left it blank
            if (string.IsNullOrWhiteSpace(_cityNameEdit.Text))
                World.CityName = "My First City";
            World.PendingScenarioId = "tutorial";
            GetTree().ChangeSceneToFile("res://scenes/World.tscn");
        };
        vbox.AddChild(tutorialBtn);

        // "New Game" now opens the scenario picker
        var newGameBtn = MakeMenuButton("New Game");
        newGameBtn.Pressed += () =>
        {
            ApplyCityName();
            _mainPanel.Visible    = false;
            _scenarioPanel.Visible = true;
        };
        vbox.AddChild(newGameBtn);

        // New Game (Server Mode) button — sandbox only for now
        var serverBtn = MakeMenuButton("New Game (Server)");
        serverBtn.TooltipText = "Runs a separate simulation process — more accurate physics, supports skip-ahead";
        serverBtn.Pressed += () => { ApplyCityName(); OnServerGamePressed(); };
        vbox.AddChild(serverBtn);

        // Quit button
        var quitBtn = MakeMenuButton("Quit");
        quitBtn.Pressed += () => GetTree().Quit();
        vbox.AddChild(quitBtn);

        return center;
    }

    // ── Scenario picker panel ─────────────────────────────────────────────

    private Control BuildScenarioPanel()
    {
        var root = new Control();
        root.SetAnchorsPreset(LayoutPreset.FullRect);

        // Outer centering
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddChild(center);

        // Scrollable vertical card list (max ~600px tall)
        var outerVbox = new VBoxContainer();
        outerVbox.AddThemeConstantOverride("separation", 16);
        outerVbox.CustomMinimumSize = new Vector2(440, 0);
        center.AddChild(outerVbox);

        // Header row: title + Back button
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 12);
        outerVbox.AddChild(headerRow);

        var pickerTitle = new Label();
        pickerTitle.Text = "Choose Scenario";
        pickerTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pickerTitle.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.55f));
        pickerTitle.AddThemeFontSizeOverride("font_size", 28);
        headerRow.AddChild(pickerTitle);

        var backBtn = new Button();
        backBtn.Text = "← Back";
        backBtn.CustomMinimumSize = new Vector2(90, 36);
        backBtn.AddThemeFontSizeOverride("font_size", 14);
        ApplyButtonStyle(backBtn, new Color(0.12f, 0.12f, 0.18f));
        backBtn.Pressed += () =>
        {
            _scenarioPanel.Visible = false;
            _mainPanel.Visible     = true;
        };
        headerRow.AddChild(backBtn);

        // ScrollContainer to hold cards (if there are many scenarios)
        // Height must be explicit — CenterContainer won't expand-fill for us.
        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(440, 520);
        outerVbox.AddChild(scroll);

        var cardVbox = new VBoxContainer();
        cardVbox.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(cardVbox);

        // Load personal bests once for all cards
        var leaderboard = LoadLeaderboard();

        // Sandbox card first
        cardVbox.AddChild(BuildSandboxCard());

        // Scenario cards
        foreach (var scenario in ScenarioLibrary.All)
        {
            leaderboard.TryGetValue(scenario.Id, out var best);
            cardVbox.AddChild(BuildScenarioCard(scenario, best));
        }

        return root;
    }

    /// <summary>Loads the personal-best leaderboard using the same path logic as World.cs.</summary>
    private static Dictionary<string, LeaderboardEntry> LoadLeaderboard()
    {
        try
        {
            string path;
            try
            {
                path = System.IO.Path.Combine(OS.GetUserDataDir(), "leaderboard.json");
            }
            catch
            {
                var projectDir = ProjectSettings.GlobalizePath("res://");
                path = System.IO.Path.Combine(projectDir, "saves", "leaderboard.json");
            }
            return LeaderboardSystem.Load(path);
        }
        catch
        {
            return new Dictionary<string, LeaderboardEntry>();
        }
    }

    /// <summary>Sandbox card — no goal, no time limit, current default behavior.</summary>
    private Control BuildSandboxCard()
    {
        var card = MakeCard();
        card.MouseFilter = Control.MouseFilterEnum.Stop;

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 12);
        card.AddChild(hbox);

        var leftVbox = new VBoxContainer();
        leftVbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftVbox.AddThemeConstantOverride("separation", 4);
        hbox.AddChild(leftVbox);

        var nameLabel = new Label();
        nameLabel.Text = "Sandbox";
        nameLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.55f));
        nameLabel.AddThemeFontSizeOverride("font_size", 18);
        leftVbox.AddChild(nameLabel);

        var descLabel = new Label();
        descLabel.Text = "No goal. No time limit. Build freely.";
        descLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.70f, 0.70f));
        descLabel.AddThemeFontSizeOverride("font_size", 13);
        descLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        leftVbox.AddChild(descLabel);

        var rightVbox = new VBoxContainer();
        rightVbox.AddThemeConstantOverride("separation", 2);
        hbox.AddChild(rightVbox);

        var goalLabel = new Label();
        goalLabel.Text = "Free Play";
        goalLabel.HorizontalAlignment = HorizontalAlignment.Right;
        goalLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 0.5f));
        goalLabel.AddThemeFontSizeOverride("font_size", 13);
        rightVbox.AddChild(goalLabel);

        var limLabel = new Label();
        limLabel.Text = "No limit";
        limLabel.HorizontalAlignment = HorizontalAlignment.Right;
        limLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
        limLabel.AddThemeFontSizeOverride("font_size", 12);
        rightVbox.AddChild(limLabel);

        // Click to start sandbox
        var btn = new Button();
        btn.Text = "Play";
        btn.CustomMinimumSize = new Vector2(72, 32);
        btn.AddThemeFontSizeOverride("font_size", 14);
        ApplyButtonStyle(btn, new Color(0.15f, 0.30f, 0.15f));
        btn.Pressed += () =>
        {
            ApplyCityName();
            World.PendingScenarioId = null; // sandbox = no scenario
            GetTree().ChangeSceneToFile("res://scenes/World.tscn");
        };
        rightVbox.AddChild(btn);

        // Hover highlight
        AttachHoverHighlight(card);

        return card;
    }

    /// <summary>One scenario card showing name, description, goal, time limit, medal thresholds, and personal best.</summary>
    private Control BuildScenarioCard(ScenarioDefinition scenario, LeaderboardEntry? best)
    {
        var card = MakeCard();
        card.MouseFilter = Control.MouseFilterEnum.Stop;

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 12);
        card.AddChild(hbox);

        // Left column: name + description
        var leftVbox = new VBoxContainer();
        leftVbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftVbox.AddThemeConstantOverride("separation", 4);
        hbox.AddChild(leftVbox);

        var nameLabel = new Label();
        nameLabel.Text = scenario.Name;
        nameLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.55f));
        nameLabel.AddThemeFontSizeOverride("font_size", 18);
        leftVbox.AddChild(nameLabel);

        var descLabel = new Label();
        descLabel.Text = scenario.Description;
        descLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.70f, 0.70f));
        descLabel.AddThemeFontSizeOverride("font_size", 13);
        descLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        leftVbox.AddChild(descLabel);

        // Medal thresholds row (skip for tutorial — show "Recommended" badge instead)
        var medalsLabel = new Label();
        if (scenario.Id == "tutorial")
        {
            medalsLabel.Text = "Recommended for new players";
            medalsLabel.AddThemeColorOverride("font_color", new Color(0.90f, 0.72f, 0.20f));
        }
        else
        {
            medalsLabel.Text = $"🥇{scenario.Medals.Gold}t  🥈{scenario.Medals.Silver}t  🥉{scenario.Medals.Bronze}t";
            medalsLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.75f, 0.55f));
        }
        medalsLabel.AddThemeFontSizeOverride("font_size", 12);
        leftVbox.AddChild(medalsLabel);

        // Disabled zone constraint badge (shown for constrained scenarios)
        if (scenario.DisabledZones != null && scenario.DisabledZones.Count > 0)
        {
            var constraintLabel = new Label();
            var names = string.Join(", ", scenario.DisabledZones.Select(z => z.ToString()));
            constraintLabel.Text = $"⛔ No {names}";
            constraintLabel.AddThemeColorOverride("font_color", new Color(0.90f, 0.55f, 0.20f));
            constraintLabel.AddThemeFontSizeOverride("font_size", 11);
            leftVbox.AddChild(constraintLabel);
        }

        // Personal best row (hidden for tutorial — no medals)
        if (scenario.Id != "tutorial")
        {
            var pbLabel = new Label();
            if (best != null)
            {
                var (medalEmoji, medalName, medalColor) = best.Medal switch
                {
                    "Gold"   => ("🥇", "Gold",   new Color(1.0f, 0.82f, 0.15f)),
                    "Silver" => ("🥈", "Silver", new Color(0.80f, 0.82f, 0.88f)),
                    _        => ("🥉", "Bronze", new Color(0.80f, 0.55f, 0.25f)),
                };
                pbLabel.Text = $"{medalEmoji} {medalName} · T:{best.Tick}";
                pbLabel.AddThemeColorOverride("font_color", medalColor);
            }
            else
            {
                pbLabel.Text = "Not yet played";
                pbLabel.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.45f));
            }
            pbLabel.AddThemeFontSizeOverride("font_size", 12);
            leftVbox.AddChild(pbLabel);
        }

        // Right column: goal, limit, play button
        var rightVbox = new VBoxContainer();
        rightVbox.AddThemeConstantOverride("separation", 2);
        hbox.AddChild(rightVbox);

        var goalLabel = new Label();
        goalLabel.Text = scenario.Id == "tutorial"
            ? "Guided tutorial"
            : $"Goal: {scenario.Goal.TargetPopulation:N0} pop";
        goalLabel.HorizontalAlignment = HorizontalAlignment.Right;
        goalLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 0.5f));
        goalLabel.AddThemeFontSizeOverride("font_size", 13);
        rightVbox.AddChild(goalLabel);

        var limitLabel = new Label();
        limitLabel.Text = scenario.TickLimit > 0 ? $"{scenario.TickLimit}t limit" : "No limit";
        limitLabel.HorizontalAlignment = HorizontalAlignment.Right;
        limitLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
        limitLabel.AddThemeFontSizeOverride("font_size", 12);
        rightVbox.AddChild(limitLabel);

        var mapLabel = new Label();
        mapLabel.Text = $"{scenario.MapWidth}×{scenario.MapHeight}";
        mapLabel.HorizontalAlignment = HorizontalAlignment.Right;
        mapLabel.AddThemeColorOverride("font_color", new Color(0.50f, 0.50f, 0.50f));
        mapLabel.AddThemeFontSizeOverride("font_size", 11);
        rightVbox.AddChild(mapLabel);

        var playBtn = new Button();
        playBtn.Text = "Play";
        playBtn.CustomMinimumSize = new Vector2(72, 32);
        playBtn.AddThemeFontSizeOverride("font_size", 14);
        ApplyButtonStyle(playBtn, new Color(0.12f, 0.20f, 0.35f));

        var scenarioId = scenario.Id; // capture for closure
        playBtn.Pressed += () =>
        {
            ApplyCityName();
            World.PendingScenarioId = scenarioId;
            GetTree().ChangeSceneToFile("res://scenes/World.tscn");
        };
        rightVbox.AddChild(playBtn);

        // Hover highlight
        AttachHoverHighlight(card);

        return card;
    }

    // ── Card factory ──────────────────────────────────────────────────────

    private static PanelContainer MakeCard()
    {
        var card = new PanelContainer();
        card.CustomMinimumSize = new Vector2(440, 110);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.12f, 0.12f, 0.18f);
        style.BorderColor = new Color(0.28f, 0.28f, 0.40f);
        style.BorderWidthBottom = style.BorderWidthTop =
            style.BorderWidthLeft = style.BorderWidthRight = 1;
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight =
            style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 6;
        style.ContentMarginLeft = style.ContentMarginRight = 16;
        style.ContentMarginTop = style.ContentMarginBottom = 12;
        card.AddThemeStyleboxOverride("panel", style);

        return card;
    }

    /// <summary>
    /// Attaches hover detection to a card by using the GuiInput signal.
    /// On hover, brightens the card's background; on leave, restores it.
    /// </summary>
    private static void AttachHoverHighlight(PanelContainer card)
    {
        // Use a transparent Button overlay to capture hover events cleanly.
        // The card itself uses MouseFilter.Stop to pass events to children.
        // We rely on mouse enter/exit signals from the card.
        card.MouseEntered += () =>
        {
            var s = new StyleBoxFlat();
            s.BgColor = new Color(0.20f, 0.20f, 0.32f);
            s.BorderColor = new Color(0.45f, 0.55f, 0.85f);
            s.BorderWidthBottom = s.BorderWidthTop =
                s.BorderWidthLeft = s.BorderWidthRight = 2;
            s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
                s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 6;
            s.ContentMarginLeft = s.ContentMarginRight = 16;
            s.ContentMarginTop = s.ContentMarginBottom = 12;
            card.AddThemeStyleboxOverride("panel", s);
        };
        card.MouseExited += () =>
        {
            var s = new StyleBoxFlat();
            s.BgColor = new Color(0.12f, 0.12f, 0.18f);
            s.BorderColor = new Color(0.28f, 0.28f, 0.40f);
            s.BorderWidthBottom = s.BorderWidthTop =
                s.BorderWidthLeft = s.BorderWidthRight = 1;
            s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
                s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 6;
            s.ContentMarginLeft = s.ContentMarginRight = 16;
            s.ContentMarginTop = s.ContentMarginBottom = 12;
            card.AddThemeStyleboxOverride("panel", s);
        };
    }

    // ── City name helpers ──────────────────────────────────────────────────

    /// <summary>Builds the shared city-name LineEdit (created once in _Ready).</summary>
    private static LineEdit BuildCityNameEdit()
    {
        var edit = new LineEdit();
        edit.PlaceholderText = "My City";
        edit.MaxLength = 24;
        edit.CustomMinimumSize = new Vector2(200, 0);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.10f, 0.10f, 0.16f);
        style.BorderColor = new Color(0.4f, 0.4f, 0.6f);
        style.BorderWidthBottom = style.BorderWidthTop =
            style.BorderWidthLeft = style.BorderWidthRight = 2;
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight =
            style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 5;
        style.ContentMarginLeft = style.ContentMarginRight = 10;
        style.ContentMarginTop = style.ContentMarginBottom = 6;
        edit.AddThemeStyleboxOverride("normal", style);
        edit.AddThemeStyleboxOverride("focus", style);
        edit.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
        edit.AddThemeColorOverride("font_placeholder_color", new Color(0.5f, 0.5f, 0.5f));
        edit.AddThemeFontSizeOverride("font_size", 16);
        return edit;
    }

    /// <summary>Wraps the shared LineEdit in a label+input HBox row.</summary>
    private static HBoxContainer BuildCityNameRow(LineEdit edit)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);

        var lbl = new Label();
        lbl.Text = "City Name:";
        lbl.VerticalAlignment = VerticalAlignment.Center;
        lbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        lbl.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(lbl);

        row.AddChild(edit);
        return row;
    }

    /// <summary>Reads the city name LineEdit and stores the result on World.CityName.</summary>
    private void ApplyCityName()
    {
        var text = _cityNameEdit.Text.Trim();
        World.CityName = string.IsNullOrEmpty(text) ? "My City" : text;
    }

    // ── Server mode ────────────────────────────────────────────────────────

    private void OnServerGamePressed()
    {
        // Derive the repo root from the Godot project location (res:// == godot/, parent == repo root)
        var projectPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        var cmd = $"cd {projectPath} && " +
                  $"dotnet run --project src/Loopolis.Runner -- server default --speed 2 " +
                  $"> /tmp/loopolis-server.log 2>&1 &";

        var pid = OS.CreateProcess("bash", ["-c", cmd]);
        World.SetServerPid(pid);

        // Also write PID to file so it can be cleaned up across Godot restarts
        try { File.WriteAllText("/tmp/loopolis-server.pid", pid.ToString()); }
        catch { /* non-critical */ }

        // Brief wait for server to initialize, then enter viewer mode
        var timer = GetTree().CreateTimer(1.5);
        timer.Timeout += () => GetTree().ChangeSceneToFile("res://scenes/World.tscn");
    }

    private static void KillOrphanedServer()
    {
        const string pidFile = "/tmp/loopolis-server.pid";
        if (!File.Exists(pidFile)) return;

        try
        {
            var pidText = File.ReadAllText(pidFile).Trim();
            if (int.TryParse(pidText, out var pid))
            {
                try { OS.Kill(pid); }
                catch { /* process may have already exited */ }
            }
            File.Delete(pidFile);
        }
        catch { /* non-critical */ }
    }

    // ── Button factory ─────────────────────────────────────────────────────

    private static Button MakeMenuButton(string text)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(200, 52);
        btn.AddThemeFontSizeOverride("font_size", 18);
        ApplyButtonStyle(btn, new Color(0.15f, 0.15f, 0.22f));
        return btn;
    }

    /// <summary>
    /// Applies amber/gold border styling to a button to mark it as the recommended action.
    /// </summary>
    private static void ApplyAmberButtonStyle(Button btn)
    {
        var amberBorder = new Color(0.85f, 0.65f, 0.15f);
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.18f, 0.14f, 0.06f);
        style.BorderColor = amberBorder;
        style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 2;
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 5;
        style.ContentMarginLeft = style.ContentMarginRight = 12;
        style.ContentMarginTop = style.ContentMarginBottom = 8;
        btn.AddThemeStyleboxOverride("normal", style);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(0.26f, 0.20f, 0.08f);
        hoverStyle.BorderColor = new Color(1.0f, 0.82f, 0.30f);
        hoverStyle.BorderWidthBottom = hoverStyle.BorderWidthTop = hoverStyle.BorderWidthLeft = hoverStyle.BorderWidthRight = 2;
        hoverStyle.CornerRadiusTopLeft = hoverStyle.CornerRadiusTopRight = hoverStyle.CornerRadiusBottomLeft = hoverStyle.CornerRadiusBottomRight = 5;
        hoverStyle.ContentMarginLeft = hoverStyle.ContentMarginRight = 12;
        hoverStyle.ContentMarginTop = hoverStyle.ContentMarginBottom = 8;
        btn.AddThemeStyleboxOverride("hover", hoverStyle);
        btn.AddThemeStyleboxOverride("pressed", hoverStyle);

        btn.AddThemeColorOverride("font_color", new Color(1.0f, 0.90f, 0.55f));
    }

    private static void ApplyButtonStyle(Button btn, Color normalBg)
    {
        var style = new StyleBoxFlat();
        style.BgColor = normalBg;
        style.BorderColor = new Color(0.4f, 0.4f, 0.6f);
        style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 2;
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 5;
        style.ContentMarginLeft = style.ContentMarginRight = 12;
        style.ContentMarginTop = style.ContentMarginBottom = 8;
        btn.AddThemeStyleboxOverride("normal", style);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = normalBg.Lightened(0.12f);
        hoverStyle.BorderColor = new Color(0.6f, 0.6f, 0.9f);
        hoverStyle.BorderWidthBottom = hoverStyle.BorderWidthTop = hoverStyle.BorderWidthLeft = hoverStyle.BorderWidthRight = 2;
        hoverStyle.CornerRadiusTopLeft = hoverStyle.CornerRadiusTopRight = hoverStyle.CornerRadiusBottomLeft = hoverStyle.CornerRadiusBottomRight = 5;
        hoverStyle.ContentMarginLeft = hoverStyle.ContentMarginRight = 12;
        hoverStyle.ContentMarginTop = hoverStyle.ContentMarginBottom = 8;
        btn.AddThemeStyleboxOverride("hover", hoverStyle);
        btn.AddThemeStyleboxOverride("pressed", hoverStyle);

        btn.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
    }
}
