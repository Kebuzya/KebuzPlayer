using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;
using KebuzPlayer.Services;
using KebuzPlayer.Tags;
using KebuzPlayer.Lang;

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

    public ObservableCollection<CoverVariantItem> Variants { get; } = [];

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    private string _previewHint = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isGridVisible;

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

    public CoverPreviewViewModel(LibraryService library, JsonSettingsService settings)
    {
        _library = library;
        _settings = settings;
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

    [RelayCommand]
    private Task RegenerateVariantAsync() =>
        RenderBatchAsync(_batchStart == 0 ? 1 : _batchStart + _batchCount);

    [RelayCommand(CanExecute = nameof(CanStepBack))]
    private Task PreviousVariantAsync() =>
        RenderBatchAsync(Math.Max(1, _batchStart - BatchSize));

    private bool CanStepBack => _batchStart > 1;

    private async Task RenderBatchAsync(int start)
    {
        var track = _target;
        if (track == null || IsBusy || start < 1) return;

        IsBusy = true;
        PreviewHint = Loc.T("Cover.Hint.Generating");
        try
        {
            var chaos = await _library.GetCoverChaosAsync();
            int count = BatchSize;
            double cell = Math.Floor(GridSide / (count switch { 1 => 1, 4 => 2, 9 => 3, _ => 4 }));

            var items = await Task.Run(() =>
            {
                _features ??= TrackAnalyzer.Analyze(track.Path);
                var f = _features;
                var result = new CoverVariantItem[count];
                for (int i = 0; i < count; i++)
                {
                    int variant = start + i;
                    byte[] bytes;
                    if (f.IsFallback)
                    {
                        bytes = MetaCoverArtGenerator.Generate(track, variant, chaos);
                    }
                    else
                    {
                        int seed = StableHash.Combine(CoverArtGenerator.FeatureSeed(f), variant);
                        var style = CoverArtGenerator.AutoSelectStyle(f, seed, chaos);
                        bytes = CoverArtGenerator.Generate(style, seed, f, chaos);
                    }
                    result[i] = new CoverVariantItem(variant, bytes, cell);
                }
                return result;
            });

            _batchStart = start;
            _batchCount = count;
            ClearVariants();
            foreach (var item in items)
                Variants.Add(item);
            IsGridVisible = true;
            SelectVariant(Variants[0]);
        }
        catch
        {
            PreviewHint = Loc.T("Cover.Hint.GenerateError");
        }
        finally
        {
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
        if (track == null || IsBusy) return;

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
