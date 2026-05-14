using Godot;
using System;
using System.Collections.Generic;

namespace LoopolisGodot;

/// <summary>
/// Procedural audio system — zero external assets.
/// All sounds are generated in real-time using sine waves, noise, and envelopes.
///
/// Owns two AudioStreamPlayer nodes:
///   _sfxPlayer   — one-shot SFX (road placed, building born, milestone)
///   _ambientPlayer — continuous ambient drone, scaled by population
///
/// Samples are pushed in _Process to satisfy Godot's AudioStreamGenerator requirement.
/// </summary>
public partial class AudioSystem : Node
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int   SampleRate   = 44100;
    private const float Tau          = MathF.PI * 2f;

    // ── Ambient state ─────────────────────────────────────────────────────────

    private AudioStreamPlayer _ambientPlayer = null!;
    private float _ambientTime       = 0f;            // advances monotonically for the drone
    private float _ambientLevel      = 0f;            // 0..1, set by SetAmbientLevel()
    private float _targetAmbientLevel = 0f;           // smoothly lerp to target

    // ── SFX state ─────────────────────────────────────────────────────────────

    private AudioStreamPlayer _sfxPlayer = null!;

    /// <summary>
    /// Each pending SFX is a generator delegate + how many samples have been pushed so far.
    /// Multiple simultaneous sounds are summed (e.g. building born fires right after road placed).
    /// </summary>
    private readonly List<PendingSfx> _pendingSfx = new();

    private struct PendingSfx
    {
        /// <summary>Returns the sample value for the given sample index (0-based).</summary>
        public Func<int, float> Generator;
        /// <summary>Total number of samples in this sound.</summary>
        public int TotalSamples;
        /// <summary>How many samples have already been pushed.</summary>
        public int SamplesPushed;
    }

    // ── RNG for noise ─────────────────────────────────────────────────────────

    private readonly Random _rng = new(42); // deterministic seed for reproducible noise

    // ── Godot lifecycle ───────────────────────────────────────────────────────

    public override void _Ready()
    {
        // ── SFX player ───────────────────────────────────────────────────────
        var sfxStream = new AudioStreamGenerator();
        sfxStream.MixRate     = SampleRate;
        sfxStream.BufferLength = 0.2f; // 200 ms — enough headroom for bursts

        _sfxPlayer        = new AudioStreamPlayer();
        _sfxPlayer.Stream = sfxStream;
        _sfxPlayer.Bus    = "Master";
        _sfxPlayer.VolumeDb = 0f;
        AddChild(_sfxPlayer);
        _sfxPlayer.Play();

        // ── Ambient player ───────────────────────────────────────────────────
        var ambStream = new AudioStreamGenerator();
        ambStream.MixRate      = SampleRate;
        ambStream.BufferLength  = 0.15f; // 150 ms — short so we can steer volume quickly

        _ambientPlayer        = new AudioStreamPlayer();
        _ambientPlayer.Stream  = ambStream;
        _ambientPlayer.Bus     = "Master";
        _ambientPlayer.VolumeDb = 0f;
        AddChild(_ambientPlayer);
        _ambientPlayer.Play();
    }

    public override void _Process(double delta)
    {
        // Smooth ambient level towards target (avoids clicks when population jumps)
        _ambientLevel = Lerp(_ambientLevel, _targetAmbientLevel, (float)delta * 2f);

        PushAmbientSamples();
        PushSfxSamples();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Queue a short "chunk" thud for road/avenue placement.
    /// Duration: 80 ms.
    /// </summary>
    public void PlayRoadPlaced()
    {
        var totalSamples = MsToSamples(80);
        // Pre-sample noise so it is deterministic and avoids per-frame RNG state sharing
        var noise = new float[totalSamples];
        for (var i = 0; i < totalSamples; i++)
            noise[i] = (float)(_rng.NextDouble() - 0.5);

        Enqueue(totalSamples, i =>
        {
            var t = i / (float)SampleRate;
            // Low-frequency thump
            var thump = MathF.Sin(t * Tau * 60f) * MathF.Exp(-t * 40f);
            // Noise texture
            var tex   = noise[i] * 0.15f * MathF.Exp(-t * 60f);
            return (thump + tex) * 0.35f;
        });
    }

    /// <summary>
    /// Queue a bright ascending chime for a building being born.
    /// Duration: 300 ms. One chime per tick batch — caller must throttle.
    /// </summary>
    public void PlayBuildingBorn()
    {
        var totalSamples = MsToSamples(300);

        Enqueue(totalSamples, i =>
        {
            var t     = i / (float)SampleRate;
            var decay = MathF.Exp(-t * 8f);

            // A minor chord: A4 (440) + C5 (523) + E5 (659)
            var chord = MathF.Sin(t * Tau * 440f) * 0.4f
                      + MathF.Sin(t * Tau * 523f) * 0.3f
                      + MathF.Sin(t * Tau * 659f) * 0.3f;

            var sample = chord * decay * 0.3f;

            // Short attack ramp (first 20 ms)
            if (t < 0.02f)
                sample *= t / 0.02f;

            return sample;
        });
    }

    /// <summary>
    /// Queue a three-note ascending fanfare for milestone events.
    /// Duration: 600 ms.
    /// </summary>
    public void PlayMilestone()
    {
        var totalSamples = MsToSamples(600);
        // Note pitches and their start offsets
        float[] pitches  = { 440f, 554f, 660f };
        float[] offsets  = { 0f,   0.2f, 0.4f };

        Enqueue(totalSamples, i =>
        {
            var t      = i / (float)SampleRate;
            var sample = 0f;

            for (var n = 0; n < 3; n++)
            {
                var off = offsets[n];
                if (t >= off && t < off + 0.25f)
                {
                    var lt    = t - off;
                    var decay = MathF.Exp(-lt * 6f);
                    sample += MathF.Sin(lt * Tau * pitches[n]) * decay * 0.25f;
                }
            }

            return sample;
        });
    }

    /// <summary>
    /// Sets the ambient drone intensity.  Call each HUD update tick.
    /// <paramref name="populationRatio"/> should be population / 5000f, clamped [0, 1].
    /// </summary>
    public void SetAmbientLevel(float populationRatio)
    {
        _targetAmbientLevel = Math.Clamp(populationRatio, 0f, 1f);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>Converts milliseconds to sample count.</summary>
    private static int MsToSamples(int ms) => SampleRate * ms / 1000;

    /// <summary>Adds a sound generator to the pending SFX queue.</summary>
    private void Enqueue(int totalSamples, Func<int, float> generator)
    {
        _pendingSfx.Add(new PendingSfx
        {
            Generator    = generator,
            TotalSamples = totalSamples,
            SamplesPushed = 0
        });
    }

    /// <summary>Float linear interpolation.</summary>
    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    // ── Sample pushers ────────────────────────────────────────────────────────

    /// <summary>
    /// Fills the SFX player buffer with the sum of all pending sounds.
    /// Sounds that exhaust their sample budget are removed from the queue.
    /// </summary>
    private void PushSfxSamples()
    {
        if (_sfxPlayer == null) return;

        AudioStreamGeneratorPlayback? playback = null;
        try { playback = _sfxPlayer.GetStreamPlayback() as AudioStreamGeneratorPlayback; }
        catch { return; }
        if (playback == null) return;

        var available = playback.GetFramesAvailable();
        if (available <= 0) return;

        for (var frame = 0; frame < available; frame++)
        {
            var sum = 0f;
            for (var s = 0; s < _pendingSfx.Count; s++)
            {
                var sfx = _pendingSfx[s];
                if (sfx.SamplesPushed < sfx.TotalSamples)
                {
                    sum += sfx.Generator(sfx.SamplesPushed);
                    sfx.SamplesPushed++;
                    _pendingSfx[s] = sfx; // structs must be written back
                }
            }

            // Soft-clip to prevent any accidental distortion
            sum = Math.Clamp(sum, -1f, 1f);
            playback.PushFrame(new Vector2(sum, sum));
        }

        // Remove exhausted sounds
        _pendingSfx.RemoveAll(s => s.SamplesPushed >= s.TotalSamples);
    }

    /// <summary>
    /// Keeps the ambient drone buffer filled.
    /// Two layered sine waves (55 Hz + 82.5 Hz) with slow 0.2 Hz amplitude modulation,
    /// scaled by the current ambient level.
    /// </summary>
    private void PushAmbientSamples()
    {
        if (_ambientPlayer == null) return;

        AudioStreamGeneratorPlayback? playback = null;
        try { playback = _ambientPlayer.GetStreamPlayback() as AudioStreamGeneratorPlayback; }
        catch { return; }
        if (playback == null) return;

        var available = playback.GetFramesAvailable();
        if (available <= 0) return;

        var dt = 1f / SampleRate;

        for (var i = 0; i < available; i++)
        {
            // Slow breathing modulation (0.2 Hz)
            var breathe = 0.5f + 0.5f * MathF.Sin(_ambientTime * Tau * 0.2f);

            // Low drone: 55 Hz (A1) + 82.5 Hz (E2, perfect fifth)
            var drone = MathF.Sin(_ambientTime * Tau * 55f)   * 0.6f
                      + MathF.Sin(_ambientTime * Tau * 82.5f) * 0.4f;

            var sample = drone * breathe * 0.08f * _ambientLevel;

            playback.PushFrame(new Vector2(sample, sample));
            _ambientTime += dt;

            // Wrap time to prevent floating-point drift over long sessions.
            // Wrap at 4 seconds (one full breathing cycle) keeping phase coherent.
            // 4.0 s is exactly 5 breathing cycles (0.2 Hz) and an integer multiple
            // of both 55 Hz and 82.5 Hz, so wrapping introduces no phase discontinuity
            // for the drone frequencies but does introduce a tiny click at the breathe
            // boundary — accepted trade-off vs. float overflow after many hours.
            // A safer wrap: 20 s (= 4×LCM(55,82.5)=220 Hz period × integer).
            if (_ambientTime >= 20f)
                _ambientTime -= 20f;
        }
    }
}
