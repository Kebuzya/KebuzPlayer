using KebuzPlayer.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KebuzPlayer.Services;

public enum RepeatMode { None, RepeatAll, RepeatOne }

public sealed record PlaybackHold(Track Track, TimeSpan Position, bool WasPlaying);

public class AudioService : IDisposable
{
    private readonly SynchronizationContext _sync;
    private WaveStream?              _fileReader;
    private FftSampleProvider?       _fftProvider;
    private VolumeSampleProvider?    _volumeProvider;
    private WaveOutEvent?            _outputDevice;
    private System.Threading.Timer? _positionTimer;

    private List<Track> _queue = new();
    private int[] _playOrder = Array.Empty<int>();
    private int _currentOrderIndex = -1;
    private int _failedInRow;
    private TimeSpan _resumeAt;

    public FftSampleProvider? FftProvider => _fftProvider;
    public Equalizer Equalizer { get; } = new();
    public IReadOnlyList<Track> Queue => _queue;

    public Track? CurrentTrack { get; private set; }
    public PlaybackState State => _outputDevice?.PlaybackState ?? PlaybackState.Stopped;
    public TimeSpan Position => _fileReader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _fileReader?.TotalTime ?? TimeSpan.Zero;
    public float MasterVolume { get; private set; } = 0.8f;
    public bool IsShuffleOn { get; private set; }
    public RepeatMode RepeatMode { get; private set; } = RepeatMode.None;

    public event EventHandler<Track>? TrackChanged;
    public event EventHandler<PlaybackState>? PlaybackStateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? TrackEnded;
    public event EventHandler? GainChanged;
    public event EventHandler<Track>? TrackOpenFailed;
    public event EventHandler? QueueChanged;

    public AudioService()
    {
        _sync = SynchronizationContext.Current ?? new SynchronizationContext();
        _positionTimer = new System.Threading.Timer(OnPositionTick, null,
            Timeout.Infinite, Timeout.Infinite);
    }

    public void Play(Track track)
    {
        DisposePlayback();
        CurrentTrack = track;

        try
        {
            OpenOutput(track);
            _outputDevice!.Play();
            _positionTimer!.Change(250, 250);
            _failedInRow = 0;

            RaiseOnSync(TrackChanged, track);
            RaiseOnSync(PlaybackStateChanged, PlaybackState.Playing);
        }
        catch
        {
            DisposePlayback();
            RaiseOnSync(PlaybackStateChanged, PlaybackState.Stopped);
            RaiseOnSync(TrackOpenFailed, track);
            SkipAfterFailure();
        }
    }

    private void OpenOutput(Track track)
    {
        _fileReader     = AudioReaderFactory.Open(track.Path);
        _fftProvider    = new FftSampleProvider(new EqualizerSampleProvider(_fileReader.ToSampleProvider(), Equalizer));
        _volumeProvider = new VolumeSampleProvider(_fftProvider);
        ApplyVolume();

        _outputDevice = new WaveOutEvent();
        _outputDevice.Init(new SampleToWaveProvider(_volumeProvider));
        _outputDevice.PlaybackStopped += OnPlaybackStopped;
    }

    private bool HasNext =>
        _currentOrderIndex < _playOrder.Length - 1 || RepeatMode == RepeatMode.RepeatAll;

    private void SkipAfterFailure()
    {
        if (_queue.Count == 0 || _playOrder.Length == 0) return;
        if (++_failedInRow >= _playOrder.Length || !HasNext)
        {
            _failedInRow = 0;
            return;
        }
        _sync.Post(_ => Next(), null);
    }

    public void RestoreTrackState(Track track, TimeSpan position = default)
    {
        if (!ReferenceEquals(CurrentTrack, track)) DisposePlayback();
        CurrentTrack = track;
        _resumeAt = position;
    }

    public PlaybackHold? Release(string path)
    {
        if (_fileReader == null || CurrentTrack is not { } track
            || !string.Equals(track.Path, path, StringComparison.OrdinalIgnoreCase))
            return null;

        var hold = new PlaybackHold(track, _fileReader.CurrentTime, State == PlaybackState.Playing);
        DisposePlayback();
        return hold;
    }

    public void Restore(PlaybackHold hold, string? newPath = null)
    {
        if (newPath != null) hold.Track.Path = newPath;
        if (!ReferenceEquals(CurrentTrack, hold.Track) || _outputDevice != null) return;
        try
        {
            OpenOutput(hold.Track);
            _fileReader!.CurrentTime = hold.Position;
            if (hold.WasPlaying)
            {
                _outputDevice!.Play();
                _positionTimer!.Change(250, 250);
                RaiseOnSync(PlaybackStateChanged, PlaybackState.Playing);
            }
            else
            {
                RaiseOnSync(PlaybackStateChanged, PlaybackState.Paused);
            }
        }
        catch
        {
            DisposePlayback();
            RaiseOnSync(PlaybackStateChanged, PlaybackState.Stopped);
        }
    }

    public void Abandon(PlaybackHold hold)
    {
        if (!ReferenceEquals(CurrentTrack, hold.Track) || _outputDevice != null) return;
        if (_queue.Count > 1 && HasNext && hold.WasPlaying) Next();
        else RaiseOnSync(PlaybackStateChanged, PlaybackState.Stopped);
    }

    public void SetRepeatMode(RepeatMode mode) => RepeatMode = mode;

    public void SetShuffle(bool enabled) { if (IsShuffleOn != enabled) ToggleShuffle(); }

    public void PlayPause()
    {
        if (_outputDevice == null)
        {
            if (CurrentTrack == null) return;
            var resumeAt = _resumeAt;
            _resumeAt = TimeSpan.Zero;
            Play(CurrentTrack);
            if (resumeAt > TimeSpan.Zero && _fileReader != null
                && resumeAt < _fileReader.TotalTime - TimeSpan.FromSeconds(2))
                _fileReader.CurrentTime = resumeAt;
            return;
        }

        if (_outputDevice.PlaybackState == PlaybackState.Playing)
        {
            _outputDevice.Pause();
            _positionTimer!.Change(Timeout.Infinite, Timeout.Infinite);
            RaiseOnSync(PlaybackStateChanged, PlaybackState.Paused);
        }
        else
        {
            if (_outputDevice.PlaybackState == PlaybackState.Stopped && _fileReader != null
                && _fileReader.CurrentTime >= _fileReader.TotalTime - TimeSpan.FromSeconds(1))
                _fileReader.CurrentTime = TimeSpan.Zero;
            _outputDevice.Play();
            _positionTimer!.Change(250, 250);
            RaiseOnSync(PlaybackStateChanged, PlaybackState.Playing);
        }
    }

    public void Stop()
    {
        _outputDevice?.Stop();
        _positionTimer!.Change(Timeout.Infinite, Timeout.Infinite);
        RaiseOnSync(PlaybackStateChanged, PlaybackState.Stopped);
    }

    public void SeekTo(TimeSpan position)
    {
        if (_fileReader == null) return;
        var clamped = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, _fileReader.TotalTime.Ticks));
        _fileReader.CurrentTime = clamped;
    }

    public void Next()
    {
        if (_queue.Count == 0) return;
        _currentOrderIndex = (_currentOrderIndex + 1) % _playOrder.Length;
        Play(_queue[_playOrder[_currentOrderIndex]]);
    }

    public void Previous()
    {
        if (_queue.Count == 0) return;
        if (Position.TotalSeconds > 3)
        {
            SeekTo(TimeSpan.Zero);
            return;
        }
        _currentOrderIndex = (_currentOrderIndex - 1 + _playOrder.Length) % _playOrder.Length;
        Play(_queue[_playOrder[_currentOrderIndex]]);
    }

    public void SetMasterVolume(float volume)
    {
        MasterVolume = Math.Clamp(volume, 0f, 1f);
        ApplyVolume();
    }

    public void ApplyCurrentTrackGain()
    {
        ApplyVolume();
        GainChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AdjustTrackGain(Track track, float delta)
    {
        track.UserGainOffset = Math.Clamp(track.UserGainOffset + delta, -20f, 20f);
        if (ReferenceEquals(track, CurrentTrack))
            ApplyCurrentTrackGain();
    }

    public void SetQueue(IReadOnlyList<Track> tracks, int startIndex = 0)
    {
        _queue = new List<Track>(tracks);
        RebuildPlayOrder();
        _currentOrderIndex = PlaceFirst(startIndex);
        _failedInRow = 0;
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Enqueue(Track track)
    {
        _queue.Add(track);
        _playOrder = [.. _playOrder, _queue.Count - 1];
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private int PlaceFirst(int trackIndex)
    {
        if (!IsShuffleOn) return Math.Clamp(trackIndex, 0, Math.Max(0, _playOrder.Length - 1));
        int pos = Array.IndexOf(_playOrder, trackIndex);
        if (pos > 0) (_playOrder[0], _playOrder[pos]) = (_playOrder[pos], _playOrder[0]);
        return 0;
    }

    public void ToggleShuffle()
    {
        IsShuffleOn = !IsShuffleOn;
        var currentTrackIndex = _currentOrderIndex >= 0 && _currentOrderIndex < _playOrder.Length
            ? _playOrder[_currentOrderIndex] : 0;
        RebuildPlayOrder();
        _currentOrderIndex = PlaceFirst(currentTrackIndex);
    }

    public void CycleRepeat()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.None      => RepeatMode.RepeatAll,
            RepeatMode.RepeatAll => RepeatMode.RepeatOne,
            RepeatMode.RepeatOne => RepeatMode.None,
            _ => RepeatMode.None
        };
    }

    private void ApplyVolume()
    {
        if (_volumeProvider == null || CurrentTrack == null) return;
        var gainLinear = MathF.Pow(10f, CurrentTrack.UserGainOffset / 20f);
        _volumeProvider.Volume = Math.Clamp(MasterVolume * gainLinear, 0f, 2f);
    }

    private void RebuildPlayOrder()
    {
        _playOrder = Enumerable.Range(0, _queue.Count).ToArray();
        if (IsShuffleOn)
            FisherYatesShuffle(_playOrder);
    }

    private static void FisherYatesShuffle(int[] arr)
    {
        var rng = Random.Shared;
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        _positionTimer!.Change(Timeout.Infinite, Timeout.Infinite);

        if (e.Exception != null)
        {
            _sync.Post(_ =>
            {
                RaiseOnSync(PlaybackStateChanged, PlaybackState.Stopped);
                SkipAfterFailure();
            }, null);
            return;
        }

        if (RepeatMode == RepeatMode.RepeatOne)
        {
            _sync.Post(_ => { SeekTo(TimeSpan.Zero); _outputDevice?.Play(); _positionTimer!.Change(250, 250); }, null);
            return;
        }

        if (HasNext)
            _sync.Post(_ => Next(), null);
        else
            _sync.Post(_ => { RaiseOnSync(PlaybackStateChanged, PlaybackState.Stopped); _sync.Post(_ => TrackEnded?.Invoke(this, EventArgs.Empty), null); }, null);
    }

    private void OnPositionTick(object? _)
    {
        _sync.Post(__ =>
        {
            var reader = _fileReader;
            if (reader != null)
                PositionChanged?.Invoke(this, reader.CurrentTime);
        }, null);
    }

    private void RaiseOnSync<T>(EventHandler<T>? handler, T arg)
    {
        if (handler == null) return;
        _sync.Post(_ => handler.Invoke(this, arg), null);
    }

    private void DisposePlayback()
    {
        _positionTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        if (_outputDevice != null)
        {
            _outputDevice.PlaybackStopped -= OnPlaybackStopped;
            _outputDevice.Stop();
            _outputDevice.Dispose();
            _outputDevice = null;
        }
        _volumeProvider = null;
        _fftProvider    = null;
        _fileReader?.Dispose();
        _fileReader = null;
    }

    public void Dispose()
    {
        DisposePlayback();
        _positionTimer?.Dispose();
        _positionTimer = null;
    }
}
