using Godot;

namespace LoopolisGodot;

/// <summary>
/// Full-screen bankrupt overlay. Shown when GameState == "Bankrupt".
/// Sits on CanvasLayer 15 (on top of everything).
/// Emits NewGameRequested when the player wants to restart.
/// </summary>
public partial class GameOverPanel : CanvasLayer
{
    [Signal]
    public delegate void NewGameRequestedEventHandler();

    private Control   _overlay    = null!;
    private Label     _titleLabel = null!;
    private Label     _statsLabel = null!;
    private Label     _hintLabel  = null!;

    public override void _Ready()
    {
        Layer = 15;

        // Semi-transparent full-screen overlay
        _overlay = new ColorRect();
        ((ColorRect)_overlay).Color = new Color(0f, 0f, 0f, 0.72f);
        _overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.Visible = false;
        _overlay.MouseFilter = Control.MouseFilterEnum.Stop; // block input to world below
        AddChild(_overlay);

        // Centered content container
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(center);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        center.AddChild(vbox);

        // Title
        _titleLabel = new Label();
        _titleLabel.Text = "CITY BANKRUPT";
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _titleLabel.AddThemeColorOverride("font_color", new Color(1f, 0.15f, 0.15f));
        _titleLabel.AddThemeFontSizeOverride("font_size", 48);
        vbox.AddChild(_titleLabel);

        // Stats summary
        _statsLabel = new Label();
        _statsLabel.Text = "";
        _statsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statsLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        _statsLabel.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(_statsLabel);

        // Hint
        _hintLabel = new Label();
        _hintLabel.Text = "";
        _hintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _hintLabel.AddThemeColorOverride("font_color", new Color(1f, 0.75f, 0.2f));
        _hintLabel.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(_hintLabel);

        // Spacer
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 16);
        vbox.AddChild(spacer);

        // New Game button
        var newGameBtn = new Button();
        newGameBtn.Text = "New Game";
        newGameBtn.CustomMinimumSize = new Vector2(180, 52);
        newGameBtn.AddThemeFontSizeOverride("font_size", 20);
        newGameBtn.Pressed += OnNewGamePressed;
        vbox.AddChild(newGameBtn);
    }

    /// <summary>Show the panel with stats from the given shared state (viewer mode).</summary>
    public void ShowBankrupt(SharedState state)
    {
        PopulateStats(state.Tick, state.Balance, state.Population);
        _overlay.Visible = true;
    }

    /// <summary>Show the panel with stats from standalone mode values.</summary>
    public void ShowBankrupt(int tick, double balance, int population)
    {
        PopulateStats(tick, balance, population);
        _overlay.Visible = true;
    }

    public new void Hide()
    {
        _overlay.Visible = false;
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private void PopulateStats(int tick, double balance, int population)
    {
        _statsLabel.Text =
            $"Final tick: {tick:N0}   |   " +
            $"Balance: ${balance:N0}   |   " +
            $"Population: {population:N0}";

        _hintLabel.Text = BuildHint(balance, population);
    }

    private static string BuildHint(double balance, int population)
    {
        if (balance < -10_000)
            return "Tip: Build residential zones to grow taxes and offset maintenance costs.";
        if (population == 0)
            return "Tip: Connect your zones to a power plant via roads to attract residents.";
        return "Tip: Balance tax income with maintenance — avoid over-building infrastructure.";
    }

    private void OnNewGamePressed()
    {
        _overlay.Visible = false;
        EmitSignal(SignalName.NewGameRequested);
    }
}
