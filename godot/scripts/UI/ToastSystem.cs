using Godot;
using System;
using System.Collections.Generic;

namespace LoopolisGodot;

/// <summary>
/// Toast notification system — shows up to 3 stacked toasts above bottom-center.
/// Toasts fade in (0.3s), stay visible for their duration, then fade out (0.5s).
/// Duplicate texts are suppressed. Layer = 8.
/// </summary>
public partial class ToastSystem : CanvasLayer
{
    private const int MaxToasts    = 3;
    private const float FadeInTime = 0.3f;
    private const float FadeOutTime = 0.5f;
    private const float ToastHeight = 44f;
    private const float ToastWidth  = 500f;
    private const float BottomMargin = 80f; // distance from bottom of screen

    private record ToastEntry(
        PanelContainer Panel,
        Label TextLabel,
        float Duration,
        float FadeIn,
        float FadeOut,
        bool FadingOut,
        float TimeLeft);

    private readonly List<ToastEntry> _toasts = new();
    private bool _gameOver = false;

    public override void _Ready()
    {
        Layer = 8;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Add a toast. If 3 are already shown, the oldest is discarded.</summary>
    public void AddToast(string text, Color color, float duration = 8f)
    {
        if (_gameOver) return;

        // Deduplicate: don't add if same text already shown
        foreach (var t in _toasts)
            if (t.TextLabel.Text == text) return;

        // Evict oldest if at capacity
        if (_toasts.Count >= MaxToasts)
            RemoveToast(0);

        var panel = BuildToastPanel(text, color, out var lbl);
        AddChild(panel);

        var entry = new ToastEntry(
            Panel:     panel,
            TextLabel: lbl,
            Duration:  duration,
            FadeIn:    FadeInTime,
            FadeOut:   0f,
            FadingOut: false,
            TimeLeft:  duration);

        _toasts.Add(entry);
        RepositionToasts();
    }

    /// <summary>Hint-styled toast (white, 12s).</summary>
    public void AddHint(string text)  => AddToast(text, new Color(0.9f, 0.9f, 0.9f), 12f);

    /// <summary>Event-styled toast (orange, 8s).</summary>
    public void AddEvent(string text) => AddToast(text, new Color(1f, 0.6f, 0.15f), 8f);

    /// <summary>Milestone-styled toast (gold, 6s).</summary>
    public void AddMilestone(string text) => AddToast(text, new Color(1f, 0.9f, 0.2f), 6f);

    /// <summary>Alert-styled toast (yellow, 10s).</summary>
    public void AddAlert(string text) => AddToast(text, new Color(1f, 0.85f, 0.1f), 10f);

    /// <summary>Hides all toasts immediately when the game ends.</summary>
    public void SetGameOver()
    {
        _gameOver = true;
        for (int i = _toasts.Count - 1; i >= 0; i--)
            RemoveToast(i);
    }

    // ── _Process: animate fade-in / fade-out / expiry ──────────────────────

    public override void _Process(double delta)
    {
        if (_toasts.Count == 0) return;

        var dt = (float)delta;
        bool anyRemoved = false;

        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            var t = _toasts[i];

            if (t.FadingOut)
            {
                var newFadeOut = t.FadeOut - dt;
                if (newFadeOut <= 0f)
                {
                    RemoveToast(i);
                    anyRemoved = true;
                    continue;
                }
                var alpha = newFadeOut / FadeOutTime;
                t.Panel.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(alpha, 0f, 1f));
                _toasts[i] = t with { FadeOut = newFadeOut };
            }
            else if (t.FadeIn > 0f)
            {
                var newFadeIn = t.FadeIn - dt;
                var alpha = 1f - Math.Max(newFadeIn, 0f) / FadeInTime;
                t.Panel.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(alpha, 0f, 1f));
                _toasts[i] = t with { FadeIn = Math.Max(newFadeIn, 0f) };
            }
            else
            {
                // Fully visible — count down timer
                t.Panel.Modulate = new Color(1f, 1f, 1f, 1f);
                var newTimeLeft = t.TimeLeft - dt;
                if (newTimeLeft <= 0f)
                {
                    // Start fade-out
                    _toasts[i] = t with { FadingOut = true, FadeOut = FadeOutTime, TimeLeft = 0f };
                }
                else
                {
                    _toasts[i] = t with { TimeLeft = newTimeLeft };
                }
            }
        }

        if (anyRemoved)
            RepositionToasts();
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private void RemoveToast(int index)
    {
        if (index < 0 || index >= _toasts.Count) return;
        var t = _toasts[index];
        t.Panel.QueueFree();
        _toasts.RemoveAt(index);
    }

    private void RepositionToasts()
    {
        // Toasts stack upward from the bottom center of the screen.
        // Each toast is positioned at BottomMargin + (index * (ToastHeight + 6)) from bottom.
        var viewport = GetViewport();
        var vpSize   = viewport?.GetVisibleRect().Size ?? new Vector2(1280f, 720f);

        for (int i = 0; i < _toasts.Count; i++)
        {
            var panel = _toasts[i].Panel;
            var yFromBottom = BottomMargin + i * (ToastHeight + 6f);
            var x = (vpSize.X - ToastWidth) / 2f;
            var y = vpSize.Y - yFromBottom - ToastHeight;
            panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            panel.Position = new Vector2(x, y);
        }
    }

    private static PanelContainer BuildToastPanel(string text, Color color, out Label lbl)
    {
        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(ToastWidth, ToastHeight);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.05f, 0.05f, 0.05f, 0.78f);
        style.BorderColor = color;
        style.BorderWidthTop    = 2;
        style.BorderWidthBottom = 2;
        style.BorderWidthLeft   = 2;
        style.BorderWidthRight  = 2;
        style.CornerRadiusTopLeft     = 5;
        style.CornerRadiusTopRight    = 5;
        style.CornerRadiusBottomLeft  = 5;
        style.CornerRadiusBottomRight = 5;
        style.ContentMarginLeft   = 12;
        style.ContentMarginRight  = 12;
        style.ContentMarginTop    = 6;
        style.ContentMarginBottom = 6;
        panel.AddThemeStyleboxOverride("panel", style);

        var hbox = new HBoxContainer();
        hbox.SizeFlagsVertical = Control.SizeFlags.Fill;
        hbox.AddThemeConstantOverride("separation", 8);
        panel.AddChild(hbox);

        lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeColorOverride("font_color", color);
        lbl.AddThemeFontSizeOverride("font_size", 14);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        lbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        lbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        hbox.AddChild(lbl);

        // Dismiss [×] button
        var closeBtn = new Button();
        closeBtn.Text = "×";
        closeBtn.FocusMode = Control.FocusModeEnum.None;
        closeBtn.CustomMinimumSize = new Vector2(24, 24);
        closeBtn.AddThemeFontSizeOverride("font_size", 16);
        closeBtn.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        var btnStyle = new StyleBoxFlat();
        btnStyle.BgColor = new Color(0f, 0f, 0f, 0f);
        closeBtn.AddThemeStyleboxOverride("normal",  btnStyle);
        closeBtn.AddThemeStyleboxOverride("hover",   btnStyle);
        closeBtn.AddThemeStyleboxOverride("pressed", btnStyle);
        closeBtn.AddThemeStyleboxOverride("focus",   btnStyle);

        // Capture reference to panel for closure
        var panelRef = panel;
        closeBtn.Pressed += () => panelRef.QueueFree();

        hbox.AddChild(closeBtn);

        panel.Modulate = new Color(1f, 1f, 1f, 0f); // start invisible — fades in via _Process
        return panel;
    }
}
