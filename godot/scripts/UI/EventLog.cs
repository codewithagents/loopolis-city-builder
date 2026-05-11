using Godot;
using System.Collections.Generic;

namespace LoopolisGodot;

public partial class EventLog : CanvasLayer
{
    private readonly Queue<(string text, double age)> _entries = new();
    private readonly List<Label> _labels = new();
    private VBoxContainer _vbox = null!;
    private const int MaxEntries = 6;
    private const double FadeAfter = 10.0;   // start fading
    private const double RemoveAfter = 13.0; // remove from log

    public override void _Ready()
    {
        Layer = 7; // below hint overlay (8), below toolbar (9)

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        panel.GrowVertical = Control.GrowDirection.Begin;
        panel.Position = new Vector2(8, -64); // above toolbar
        panel.AddThemeStyleboxOverride("panel", MakePanelStyle());
        AddChild(panel);

        _vbox = new VBoxContainer();
        _vbox.AddThemeConstantOverride("separation", 1);
        panel.AddChild(_vbox);
    }

    public override void _Process(double delta)
    {
        // Age all entries
        var keys = new List<(string, double)>(_entries);
        _entries.Clear();
        bool changed = false;
        foreach (var (text, age) in keys)
        {
            var newAge = age + delta;
            if (newAge < RemoveAfter)
                _entries.Enqueue((text, newAge));
            else
                changed = true;
        }
        if (changed) RebuildLabels();

        // Update alpha on labels
        int i = 0;
        foreach (var (text, age) in _entries)
        {
            if (i >= _labels.Count) break;
            var alpha = age > FadeAfter ? 1f - (float)((age - FadeAfter) / (RemoveAfter - FadeAfter)) : 1f;
            _labels[i].Modulate = new Color(1, 1, 1, alpha);
            i++;
        }
    }

    public void AddEntry(string text)
    {
        // If same text as last entry, skip (dedup)
        if (_entries.Count > 0)
        {
            var last = new List<(string, double)>(_entries);
            if (last[^1].Item1 == text) return;
        }

        _entries.Enqueue((text, 0));
        while (_entries.Count > MaxEntries)
            _entries.Dequeue();
        RebuildLabels();
    }

    private void RebuildLabels()
    {
        foreach (var lbl in _labels) lbl.QueueFree();
        _labels.Clear();

        foreach (var (text, _) in _entries)
        {
            var lbl = new Label();
            lbl.Text = text;
            lbl.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
            lbl.AddThemeFontSizeOverride("font_size", 12);
            _vbox.AddChild(lbl);
            _labels.Add(lbl);
        }
    }

    private static StyleBoxFlat MakePanelStyle()
    {
        var s = new StyleBoxFlat();
        s.BgColor = new Color(0f, 0f, 0f, 0.55f);
        s.ContentMarginLeft = s.ContentMarginRight = 6;
        s.ContentMarginTop = s.ContentMarginBottom = 4;
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 3;
        return s;
    }
}
