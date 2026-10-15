using System.Threading;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;

namespace KebuzPlayer.ViewModels;

public partial class TrackRow : ObservableObject
{
    private readonly CoverThumbCache _thumbs;
    private CancellationTokenSource? _cts;

    public Track Track { get; }

    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    private Bitmap? _thumb;

    public TrackRow(Track track, int number, CoverThumbCache thumbs)
    {
        Track = track;
        _number = number;
        _thumbs = thumbs;
    }

    public async void OnCardAttached()
    {
        if (Thumb != null) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            var bmp = await _thumbs.GetTrackThumbAsync(Track.Id, ct);
            if (!ct.IsCancellationRequested)
                Thumb = bmp;
        }
        catch { }
    }

    public void OnCardDetached()
    {
        _cts?.Cancel();
        _cts = null;
        Thumb = null;
    }

    public async void ReloadThumb()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Thumb = null;
        try
        {
            var bmp = await _thumbs.GetTrackThumbAsync(Track.Id, ct);
            if (!ct.IsCancellationRequested)
                Thumb = bmp;
        }
        catch { }
    }
}
