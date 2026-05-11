using Godot;

namespace LoopolisGodot;

public partial class MainMenu : Control
{
    public override void _Ready()
    {
        // Full-screen dark background
        var bg = new ColorRect();
        bg.Color = new Color(0.08f, 0.08f, 0.12f, 1f);
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Center container
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

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

        // New Game button
        var newGameBtn = MakeMenuButton("New Game");
        newGameBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/World.tscn");
        vbox.AddChild(newGameBtn);

        // Quit button
        var quitBtn = MakeMenuButton("Quit");
        quitBtn.Pressed += () => GetTree().Quit();
        vbox.AddChild(quitBtn);

        // Controls hint at bottom
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

    private static Button MakeMenuButton(string text)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(200, 52);
        btn.AddThemeFontSizeOverride("font_size", 18);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.15f, 0.15f, 0.22f);
        style.BorderColor = new Color(0.4f, 0.4f, 0.6f);
        style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 2;
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 5;
        style.ContentMarginLeft = style.ContentMarginRight = 12;
        style.ContentMarginTop = style.ContentMarginBottom = 8;
        btn.AddThemeStyleboxOverride("normal", style);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(0.22f, 0.22f, 0.35f);
        hoverStyle.BorderColor = new Color(0.6f, 0.6f, 0.9f);
        hoverStyle.BorderWidthBottom = hoverStyle.BorderWidthTop = hoverStyle.BorderWidthLeft = hoverStyle.BorderWidthRight = 2;
        hoverStyle.CornerRadiusTopLeft = hoverStyle.CornerRadiusTopRight = hoverStyle.CornerRadiusBottomLeft = hoverStyle.CornerRadiusBottomRight = 5;
        hoverStyle.ContentMarginLeft = hoverStyle.ContentMarginRight = 12;
        hoverStyle.ContentMarginTop = hoverStyle.ContentMarginBottom = 8;
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        btn.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        return btn;
    }
}
