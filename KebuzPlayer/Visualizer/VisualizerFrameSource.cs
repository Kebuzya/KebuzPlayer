using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using KebuzPlayer.Interop;
using KebuzPlayer.Services;
using NAudio.Wave;

namespace KebuzPlayer.Visualizer;

public interface IVizFrameSink
{
    bool IsActive { get; }
    bool NeedsRawSamples { get; }
    void OnFrame(float[] spectrum, float[] raw, bool rawValid);
}

public sealed class VisualizerFrameSource
{
    private const int GraceMs        = 200;
    private const int KeepAliveMs    = 250;
    private const int StalledMs      = 200;
    private const int CoarseTimerFps = 60;

    private readonly AudioService        _audio;
    private readonly VisualizerSettings  _settings;
    private readonly List<IVizFrameSink> _sinks     = new();
    private readonly float[]             _spectrum  = new float[FftSampleProvider.SpectrumSize];
    private readonly float[]             _raw       = new float[1024];
    private readonly DispatcherTimer     _keepAlive;
    private readonly Stopwatch           _clock     = Stopwatch.StartNew();

    private System.Threading.Timer? _pacer;
    private int    _pacerFps;
    private bool   _posted;
    private long   _graceUntil;
    private double _lastCallbackMs;
    private bool   _armed;
    private int    _generation;

    public float BinHz { get; private set; } = SpectrumProcessor.ReferenceBinHz;

    public VisualizerFrameSource(AudioService audio, VisualizerSettings settings)
    {
        _audio    = audio;
        _settings = settings;
        _keepAlive = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(KeepAliveMs) };
        _keepAlive.Tick += (_, _) => KeepAlive();
        audio.TrackChanged += (_, _) => _graceUntil = Environment.TickCount64 + GraceMs;
        settings.Changed += (_, _) => SyncPacer();
    }

    public void Register(IVizFrameSink sink)
    {
        if (_sinks.Contains(sink)) return;
        _sinks.Add(sink);
        if (!_keepAlive.IsEnabled) _keepAlive.Start();
        SyncPacer();
        Arm();
    }

    public void Unregister(IVizFrameSink sink)
    {
        _sinks.Remove(sink);
        if (_sinks.Count > 0) return;
        _keepAlive.Stop();
        StopPacer();
    }

    private void SyncPacer()
    {
        int fps = _settings.FrameRate;
        if (_sinks.Count == 0 || fps <= 0)
        {
            StopPacer();
            return;
        }
        if (_pacer != null && _pacerFps == fps) return;

        _pacerFps = fps;
        int period = Math.Max(1, (int)Math.Round(1000.0 / fps));
        if (fps > CoarseTimerFps) TimerResolution.Raise();
        else TimerResolution.Release();

        if (_pacer == null)
            _pacer = new System.Threading.Timer(_ => PostTick(), null, period, period);
        else
            _pacer.Change(period, period);
    }

    private void StopPacer()
    {
        _pacer?.Dispose();
        _pacer = null;
        _pacerFps = 0;
        TimerResolution.Release();
    }

    private void PostTick()
    {
        if (_posted) return;
        _posted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _posted = false;
            Tick();
        }, DispatcherPriority.Render);
    }

    private void KeepAlive()
    {
        if (_sinks.Count == 0 || _pacer != null) return;
        if (_clock.Elapsed.TotalMilliseconds - _lastCallbackMs < StalledMs) return;
        _armed = false;
        Arm();
    }

    private void Arm()
    {
        if (_armed || _pacer != null || _sinks.Count == 0) return;
        if (ClockTopLevel() is not { } top) return;
        _armed = true;
        int generation = ++_generation;
        top.RequestAnimationFrame(_ => OnAnimationFrame(generation));
    }

    private TopLevel? ClockTopLevel()
    {
        for (int i = 0; i < _sinks.Count; i++)
            if (_sinks[i] is Visual visual && TopLevel.GetTopLevel(visual) is { } top)
                return top;
        return null;
    }

    private void OnAnimationFrame(int generation)
    {
        if (generation != _generation) return;
        _armed = false;
        _lastCallbackMs = _clock.Elapsed.TotalMilliseconds;
        if (_pacer != null) return;
        Arm();
        Tick();
    }

    private void Tick()
    {
        bool any = false, needRaw = false;
        for (int i = 0; i < _sinks.Count; i++)
        {
            if (!_sinks[i].IsActive) continue;
            any = true;
            if (_sinks[i].NeedsRawSamples) needRaw = true;
        }
        if (!any) return;

        var provider = _audio.FftProvider;
        bool live = provider != null
            && _audio.State == PlaybackState.Playing
            && Environment.TickCount64 >= _graceUntil;
        if (live)
        {
            BinHz = provider!.BinHz;
            provider.CopySpectrum(_spectrum);
        }
        else Array.Clear(_spectrum);

        bool rawValid = live && needRaw;
        if (rawValid) provider!.CopyRawSamples(_raw);

        for (int i = 0; i < _sinks.Count; i++)
            if (_sinks[i].IsActive)
                _sinks[i].OnFrame(_spectrum, _raw, rawValid);
    }
}
