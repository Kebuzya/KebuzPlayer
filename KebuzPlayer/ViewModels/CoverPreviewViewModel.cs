using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;
using KebuzPlayer.Services;
using KebuzPlayer.Tags;
using KebuzPlayer.Lang;
using NAudio.Wave;

namespace KebuzPlayer.ViewModels;

public partial class CoverVariantItem : ObservableObject
{
    public int Number { get; }
    public byte[] Bytes { get; }
    public Bitmap Image { get; }
    public double CellSize { get; }

    [ObservableProperty]
    private bool _isSelected;

    public CoverVariantItem(int number, byte[] bytes, double cellSize)
    {
        Number = number;
        Bytes = bytes;
        CellSize = cellSize;
        using var ms = new MemoryStream(bytes);
        Image = new Bitmap(ms);
    }
}

public partial class CoverPreviewViewModel : ViewModelBase
{
    private const double GridSide = 310;

    private readonly LibraryService _library;
    private readonly JsonSettingsService _settings;
    private readonly AudioService? _radio;
    private readonly RadioCoverStore? _radioCovers;
    private CancellationTokenSource? _radioWork;
    private DispatcherTimer? _dotsTimer;
    private string? _radioAddress;
    private int _radioRevision;
    private bool _capturingRadioCover;
    private RadioCoverVariants? _radioVariants;

    public bool IsRadio => _radio != null;
    public bool CanBrowseVariants => !IsRadio || IsGridVisible;
    public bool IsRadioBusy => IsRadio && IsBusy;
    public bool HasSavedRadioVariants => _radioVariants != null && !IsGridVisible;
    public string RadioCaptureLabel => Loc.T(_radioVariants == null ? "Radio.Cover.Generate" : "Radio.Cover.RecordAgain");

    public ObservableCollection<CoverVariantItem> Variants { get; } = [];

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    private string _previewHint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRadioBusy))]
    [NotifyCanExecuteChangedFor(nameof(CaptureRadioCoverCommand), nameof(RestoreRadioVariantsCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBrowseVariants), nameof(HasSavedRadioVariants))]
    private bool _isGridVisible;

    [ObservableProperty]
    private string _busyDots = "";

    [ObservableProperty]
    private bool _canWriteToFile;

    [ObservableProperty]
    private bool _writeToFile;

    private Track? _target;
    private TrackFeatures? _features;
    private int _batchStart;
    private int _batchCount;
    private byte[]? _pendingBytes;
    private bool _pendingClear;

    public event EventHandler<Track>? CoverSaved;

    public CoverPreviewViewModel(LibraryService library, JsonSettingsService settings,
                                 AudioService? radio = null, RadioCoverStore? radioCovers = null)
    {
        _library = library;
        _settings = settings;
        _radio = radio;
        _radioCovers = radioCovers;
    }

    private int BatchSize =>
        _settings.Current.CoverPreviewBatch is 1 or 4 or 9 or 16
            ? _settings.Current.CoverPreviewBatch
            : 9;

    [RelayCommand]
    private async Task LoadAsync(Track? track)
    {
        if (track == null) return;

        if (_target?.Id != track.Id)
        {
            _features = null;
            _batchStart = 0;
            _batchCount = 0;
        }
        _target = track;
        _pendingBytes = null;
        _pendingClear = false;
        ClearVariants();
        CanWriteToFile = false;
        WriteToFile = false;

        try
        {
            var bytes = await _library.GetCoverArtAsync(track.Id);
            CanWriteToFile = !await LibraryService.HasForeignCoverAsync(track.Path, bytes);
            SetPreview(bytes);
            PreviewHint = bytes is { Length: > 0 } ? Loc.T("Cover.Hint.Current") : Loc.T("Cover.Hint.None");
        }
        catch
        {
            SetPreview(null);
            PreviewHint = Loc.T("Cover.Hint.ReadError");
        }
        PreviousVariantCommand.NotifyCanExecuteChanged();
    }

    public void LoadRadio(Track track, string address)
    {
        if (_radioAddress == address && ReferenceEquals(_target, track)) return;
        ResetRadio();
        _target = track;
        _radioAddress = address;
        try
        {
            var bytes = _radioCovers!.Load(address);
            SetPreview(bytes);
            _radioVariants = _radioCovers.LoadVariants(address);
            PreviewHint = Loc.T(bytes is { Length: > 0 } ? "Cover.Hint.Current" : "Cover.Hint.None");
        }
        catch
        {
            PreviewHint = Loc.T("Cover.Hint.ReadError");
        }
        CaptureRadioCoverCommand.NotifyCanExecuteChanged();
        RefreshRadioVariants();
    }

    private void ResetRadio()
    {
        _radioRevision++;
        _radioWork?.Cancel();
        _dotsTimer?.Stop();
        BusyDots = "";
        _radioAddress = null;
        _target = null;
        _features = null;
        _radioVariants = null;
        _batchStart = 0;
        _batchCount = 0;
        _pendingBytes = null;
        ClearVariants();
        SetPreview(null);
        PreviewHint = "";
        PreviousVariantCommand.NotifyCanExecuteChanged();
        CaptureRadioCoverCommand.NotifyCanExecuteChanged();
        RefreshRadioVariants();
    }

    public void RadioPlaybackChanged()
    {
        if (_target != null)
        {
            var current = _radio!.CurrentTrack;
            if (_radioAddress != _radio.RadioAddress?.GetLeftPart(UriPartial.Query) || current == null)
                ResetRadio();
            else
            {
                if (!ReferenceEquals(_target, current)
                    || (_capturingRadioCover && _radio.State != PlaybackState.Playing))
                    _radioWork?.Cancel();
                _target = current;
            }
        }
        CaptureRadioCoverCommand.NotifyCanExecuteChanged();
    }

    private void RefreshRadioVariants()
    {
        OnPropertyChanged(nameof(HasSavedRadioVariants));
        OnPropertyChanged(nameof(RadioCaptureLabel));
        RestoreRadioVariantsCommand.NotifyCanExecuteChanged();
    }

    private bool CanCaptureRadioCover() =>
        !IsBusy && _target != null && ReferenceEquals(_target, _radio?.CurrentTrack)
        && _radio?.State == PlaybackState.Playing;

    [RelayCommand(CanExecute = nameof(CanCaptureRadioCover))]
    private Task CaptureRadioCoverAsync() => RenderBatchAsync(1, captureRadio: true);

    private bool CanRestoreRadioVariants() => _radioVariants != null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRestoreRadioVariants))]
    private Task RestoreRadioVariantsAsync() => RenderBatchAsync(_radioVariants!.FirstVariant);

    [RelayCommand]
    private void CancelRadioCover() => _radioWork?.Cancel();

    private void StartRadioAnimation()
    {
        BusyDots = ".";
        if (_dotsTimer == null)
        {
            _dotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _dotsTimer.Tick += (_, _) => BusyDots = BusyDots.Length == 3 ? "." : BusyDots + ".";
        }
        _dotsTimer.Start();
    }

    [RelayCommand]
    private Task RegenerateVariantAsync() =>
        RenderBatchAsync(_batchStart == 0 ? 1 : _batchStart + _batchCount);

    [RelayCommand(CanExecute = nameof(CanStepBack))]
    private Task PreviousVariantAsync() =>
        RenderBatchAsync(Math.Max(1, _batchStart - (_radioVariants?.BatchSize ?? BatchSize)));

    private bool CanStepBack => _batchStart > 1;

    private async Task RenderBatchAsync(int start, bool captureRadio = false)
    {
        var track = _target;
        var radioVariants = captureRadio ? null : _radioVariants;
        if (track == null || IsBusy || start < 1 || (IsRadio && !captureRadio && radioVariants == null)) return;

        using var work = IsRadio ? new CancellationTokenSource() : null;
        _radioWork = work;
        var cancellation = work?.Token ?? CancellationToken.None;
        var revision = _radioRevision;
        bool IsCurrent() => !IsRadio || (revision == _radioRevision && ReferenceEquals(track, _radio!.CurrentTrack));
        _capturingRadioCover = captureRadio;
        IsBusy = true;
        PreviewHint = Loc.T("Cover.Hint.Generating");
        if (IsRadio) StartRadioAnimation();
        try
        {
            var features = IsRadio ? radioVariants?.Features : _features;
            if (captureRadio)
            {
                const int seconds = 30;
                var recording = true;
                PreviewHint = Loc.F("Radio.Cover.Recording", 0, seconds);
                var progress = new Progress<int>(second =>
                {
                    if (recording && !cancellation.IsCancellationRequested && IsCurrent())
                        PreviewHint = Loc.F("Radio.Cover.Recording", second, seconds);
                });
                using (var clip = await _radio!.CaptureRadioAsync(seconds, progress, cancellation))
                {
                    recording = false;
                    cancellation.ThrowIfCancellationRequested();
                    PreviewHint = Loc.T("Radio.Cover.Analyzing");
                    features = await Task.Run(() => TrackAnalyzer.Analyze(clip, cancellation), cancellation);
                }
            }
            if (IsRadio) PreviewHint = Loc.T("Radio.Cover.Rendering");

            var chaos = radioVariants?.Chaos ?? await _library.GetCoverChaosAsync();
            int count = radioVariants?.BatchSize ?? BatchSize;
            var seedTrack = IsRadio ? new Track { Title = radioVariants?.Title ?? track.Title,
                                                   Artist = radioVariants?.Artist ?? track.Artist } : track;
            double cell = Math.Floor(GridSide / (count switch { 1 => 1, 4 => 2, 9 => 3, _ => 4 }));

            var (analyzed, items) = await Task.Run(() =>
            {
                var f = features ?? TrackAnalyzer.Analyze(track.Path);
                var result = new System.Collections.Generic.List<CoverVariantItem>(count);
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int variant = start + i;
                        byte[] bytes;
                        if (f.IsFallback)
                        {
                            bytes = MetaCoverArtGenerator.Generate(seedTrack, variant, chaos);
                        }
                        else
                        {
                            int seed = StableHash.Combine(CoverArtGenerator.FeatureSeed(f), variant);
                            var style = CoverArtGenerator.AutoSelectStyle(f, seed, chaos);
                            bytes = CoverArtGenerator.Generate(style, seed, f, chaos);
                        }
                        result.Add(new CoverVariantItem(variant, bytes, cell));
                    }
                    return (f, result);
                }
                catch
                {
                    foreach (var item in result) item.Image.Dispose();
                    throw;
                }
            }, cancellation);

            if (!IsCurrent() || cancellation.IsCancellationRequested)
            {
                foreach (var item in items) item.Image.Dispose();
                cancellation.ThrowIfCancellationRequested();
                return;
            }
            _features = analyzed;
            _batchStart = start;
            _batchCount = count;
            ClearVariants();
            foreach (var item in items)
                Variants.Add(item);
            IsGridVisible = true;
            SelectVariant(Variants[0]);
            if (IsRadio && _radioAddress is { } address)
            {
                _radioVariants = new RadioCoverVariants(analyzed, chaos, count, start, seedTrack.Title, seedTrack.Artist);
                RefreshRadioVariants();
                try { _radioCovers!.SaveVariants(address, _radioVariants); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    PreviewHint = Loc.T("Radio.Cover.VariantsSaveError");
                }
            }
        }
        catch (OperationCanceledException) when (IsRadio)
        {
            if (IsCurrent()) PreviewHint = Loc.T("Radio.Cover.Canceled");
        }
        catch
        {
            if (IsCurrent()) PreviewHint = Loc.T("Cover.Hint.GenerateError");
        }
        finally
        {
            _radioWork = null;
            _capturingRadioCover = false;
            _dotsTimer?.Stop();
            BusyDots = "";
            IsBusy = false;
            PreviousVariantCommand.NotifyCanExecuteChanged();
        }
    }

    public void SelectVariant(CoverVariantItem item)
    {
        foreach (var v in Variants)
            v.IsSelected = v == item;
        _pendingBytes = item.Bytes;
        _pendingClear = false;
        PreviewHint = Loc.F("Cover.Hint.Variant", item.Number);
    }

    [RelayCommand]
    private async Task ShowOriginalAsync()
    {
        var track = _target;
        if (IsRadio || track == null || IsBusy) return;

        IsBusy = true;
        try
        {
            var bytes = await Task.Run(() => TagService.TryGetEmbeddedCoverBytes(track.Path, skipOwn: true));
            ClearVariants();
            if (bytes is { Length: > 0 })
            {
                _pendingBytes = bytes;
                _pendingClear = false;
                SetPreview(bytes);
                PreviewHint = Loc.T("Cover.Hint.Original");
            }
            else
            {
                _pendingBytes = null;
                _pendingClear = true;
                SetPreview(null);
                PreviewHint = Loc.T("Cover.Hint.Cleared");
            }
        }
        catch
        {
            PreviewHint = Loc.T("Cover.Hint.ReadError");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var track = _target;
        if (track == null || (_pendingBytes == null && !_pendingClear)) return;

        try
        {
            if (IsRadio)
            {
                if (IsBusy || _radioAddress == null || !ReferenceEquals(track, _radio!.CurrentTrack)) return;
                _radioCovers!.Save(_radioAddress, _pendingBytes!);
                _pendingBytes = null;
                PreviewHint = Loc.T("Cover.Hint.Saved");
                CoverSaved?.Invoke(this, track);
                return;
            }

            if (_pendingClear)
            {
                await _library.ClearCoverArtAsync(track.Id);
                _pendingClear = false;
                SetPreview(null);
            }
            else
            {
                var saved = _pendingBytes!;
                await _library.SaveCoverArtAsync(track.Id, saved);
                _pendingBytes = null;
                if (!IsGridVisible)
                {
                    var old = PreviewImage;
                    using var ms = new MemoryStream(saved);
                    PreviewImage = new Bitmap(ms);
                    old?.Dispose();
                }
            }
            PreviewHint = Loc.T("Cover.Hint.Saved");
            CoverSaved?.Invoke(this, track);
            if (WriteToFile && CanWriteToFile)
                await WriteSavedToFileAsync(track);
        }
        catch
        {
            PreviewHint = Loc.T("Cover.Hint.SaveError");
        }
    }

    private async Task WriteSavedToFileAsync(Track track)
    {
        var result = await _library.WriteCoversToFilesAsync([track]);
        if (result.Written > 0)
        {
            WriteToFile = false;
            PreviewHint = Loc.T("Cover.Hint.SavedToFile");
        }
        else if (result.Failed.Count > 0)
        {
            PreviewHint = Loc.F("Cover.Hint.FileWriteError", result.Failed[0]);
        }
    }

    private void ClearVariants()
    {
        foreach (var v in Variants)
            v.Image.Dispose();
        Variants.Clear();
        IsGridVisible = false;
    }

    private void SetPreview(byte[]? bytes)
    {
        var old = PreviewImage;
        if (bytes is { Length: > 0 })
        {
            using var ms = new MemoryStream(bytes);
            PreviewImage = new Bitmap(ms);
        }
        else
        {
            PreviewImage = null;
        }
        old?.Dispose();
    }
}
