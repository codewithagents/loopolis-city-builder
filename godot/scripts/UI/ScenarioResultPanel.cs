using Godot;

namespace LoopolisGodot;

/// <summary>
/// Modal overlay shown when a scenario is completed or failed.
/// Layer = 20 (above all other UI layers).
/// Emits PlayAgainRequested(scenarioId) and MainMenuRequested.
/// </summary>
public partial class ScenarioResultPanel : CanvasLayer
{
    [Signal] public delegate void PlayAgainRequestedEventHandler(string scenarioId);
    [Signal] public delegate void MainMenuRequestedEventHandler();

    private ColorRect  _backdrop    = null!;
    private Label      _titleLabel  = null!;
    private Label      _medalLabel  = null!;
    private Label      _statsLabel  = null!;
    private string     _scenarioId  = "";

    public override void _Ready()
    {
        Layer = 20;

        // Semi-transparent dark backdrop
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0f, 0f, 0f, 0.78f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.Visible = false;
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        // Card panel
        var card = new PanelContainer();
        card.CustomMinimumSize = new Vector2(480, 0);
        var cardStyle = new StyleBoxFlat();
        cardStyle.BgColor = new Color(0.08f, 0.08f, 0.13f, 0.97f);
        cardStyle.BorderColor = new Color(0.4f, 0.4f, 0.65f);
        cardStyle.BorderWidthBottom = cardStyle.BorderWidthTop =
            cardStyle.BorderWidthLeft = cardStyle.BorderWidthRight = 2;
        cardStyle.CornerRadiusTopLeft = cardStyle.CornerRadiusTopRight =
            cardStyle.CornerRadiusBottomLeft = cardStyle.CornerRadiusBottomRight = 8;
        cardStyle.ContentMarginLeft = cardStyle.ContentMarginRight = 32;
        cardStyle.ContentMarginTop = cardStyle.ContentMarginBottom = 28;
        card.AddThemeStyleboxOverride("panel", cardStyle);
        center.AddChild(card);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        card.AddChild(vbox);

        // Title label (e.g. "SCENARIO COMPLETE" or "SCENARIO FAILED")
        _titleLabel = new Label();
        _titleLabel.Text = "SCENARIO COMPLETE";
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _titleLabel.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(_titleLabel);

        // Medal emoji + name (large, colored)
        _medalLabel = new Label();
        _medalLabel.Text = "";
        _medalLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _medalLabel.AddThemeFontSizeOverride("font_size", 52);
        vbox.AddChild(_medalLabel);

        // Stats text (population, ticks)
        _statsLabel = new Label();
        _statsLabel.Text = "";
        _statsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statsLabel.AddThemeFontSizeOverride("font_size", 17);
        _statsLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        _statsLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(_statsLabel);

        // Spacer
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 8);
        vbox.AddChild(spacer);

        // Button row
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 16);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        var playAgainBtn = MakeButton("Play Again", new Color(0.20f, 0.45f, 0.20f));
        playAgainBtn.Pressed += () =>
        {
            _backdrop.Visible = false;
            EmitSignal(SignalName.PlayAgainRequested, _scenarioId);
        };
        btnRow.AddChild(playAgainBtn);

        var mainMenuBtn = MakeButton("Main Menu", new Color(0.15f, 0.15f, 0.22f));
        mainMenuBtn.Pressed += () =>
        {
            _backdrop.Visible = false;
            EmitSignal(SignalName.MainMenuRequested);
        };
        btnRow.AddChild(mainMenuBtn);
    }

    /// <summary>
    /// Show the completion result with medal, population, and tick count.
    /// </summary>
    public void ShowComplete(string scenarioName, string medal, int population, int targetPop, int ticksUsed, string activeScenarioId)
    {
        _scenarioId = activeScenarioId;

        _titleLabel.Text = "SCENARIO COMPLETE";
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.55f)); // green

        var (emoji, medalColor) = GetMedalStyle(medal);
        _medalLabel.Text = $"{emoji}  {medal}";
        _medalLabel.AddThemeColorOverride("font_color", medalColor);

        _statsLabel.Text =
            $"{World.CityName} achieved {medal}!\n" +
            $"Scenario: {scenarioName}\n" +
            $"Population: {population:N0} / {targetPop:N0}\n" +
            $"Completed in {ticksUsed:N0} ticks";

        _backdrop.Visible = true;
    }

    /// <summary>
    /// Show the failure result — ran out of time without reaching the goal.
    /// </summary>
    public void ShowFailed(string scenarioName, int population, int targetPop, string activeScenarioId)
    {
        _scenarioId = activeScenarioId;

        _titleLabel.Text = "SCENARIO FAILED";
        _titleLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f)); // red

        _medalLabel.Text = "⏱";
        _medalLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));

        _statsLabel.Text =
            $"{World.CityName} ran out of time.\n" +
            $"Scenario: {scenarioName}\n" +
            $"Population: {population:N0} / {targetPop:N0}";

        _backdrop.Visible = true;
    }

    public new void Hide()
    {
        _backdrop.Visible = false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (string emoji, Color color) GetMedalStyle(string medal) => medal switch
    {
        "Gold"   => ("🥇", new Color(1.0f, 0.85f, 0.10f)),
        "Silver" => ("🥈", new Color(0.78f, 0.82f, 0.88f)),
        _        => ("🥉", new Color(0.85f, 0.60f, 0.15f)), // Bronze
    };

    private static Button MakeButton(string text, Color bgColor)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(160, 48);
        btn.AddThemeFontSizeOverride("font_size", 17);
        btn.FocusMode = Control.FocusModeEnum.None;

        var normal = new StyleBoxFlat();
        normal.BgColor = bgColor;
        normal.BorderColor = new Color(0.4f, 0.4f, 0.6f);
        normal.BorderWidthBottom = normal.BorderWidthTop =
            normal.BorderWidthLeft = normal.BorderWidthRight = 2;
        normal.CornerRadiusTopLeft = normal.CornerRadiusTopRight =
            normal.CornerRadiusBottomLeft = normal.CornerRadiusBottomRight = 5;
        normal.ContentMarginLeft = normal.ContentMarginRight = 12;
        normal.ContentMarginTop = normal.ContentMarginBottom = 8;
        btn.AddThemeStyleboxOverride("normal", normal);

        var hover = new StyleBoxFlat();
        hover.BgColor = bgColor.Lightened(0.15f);
        hover.BorderColor = new Color(0.6f, 0.6f, 0.9f);
        hover.BorderWidthBottom = hover.BorderWidthTop =
            hover.BorderWidthLeft = hover.BorderWidthRight = 2;
        hover.CornerRadiusTopLeft = hover.CornerRadiusTopRight =
            hover.CornerRadiusBottomLeft = hover.CornerRadiusBottomRight = 5;
        hover.ContentMarginLeft = hover.ContentMarginRight = 12;
        hover.ContentMarginTop = hover.ContentMarginBottom = 8;
        btn.AddThemeStyleboxOverride("hover", hover);
        btn.AddThemeStyleboxOverride("pressed", hover);

        btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
        return btn;
    }
}
