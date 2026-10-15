using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;
using KebuzPlayer.Services;
using KebuzPlayer.Lang;

namespace KebuzPlayer.ViewModels;

public partial class PlaylistCardViewModel : ObservableObject
{
    public int Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private int _trackCount;

    [ObservableProperty]
    private Bitmap? _cover;

    public string CountText => Loc.Tracks(TrackCount);

    partial void OnTrackCountChanged(int value) => OnPropertyChanged(nameof(CountText));

    public PlaylistCardViewModel(Playlist playlist)
    {
        Id = playlist.Id;
        _name = playlist.Name;
        _trackCount = playlist.TrackCount;
    }
}

public partial class PlaylistsViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly AudioService _audio;
    private readonly JsonSettingsService _settings;
    private readonly CoverThumbCache _thumbs;

    public ObservableCollection<PlaylistCardViewModel> Playlists { get; } = [];

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private PlaylistDetailViewModel? _detail;

    public bool IsOverviewVisible => Detail == null;

    partial void OnDetailChanged(PlaylistDetailViewModel? value) =>
        OnPropertyChanged(nameof(IsOverviewVisible));

    public PlaylistsViewModel(LibraryService library, AudioService audio,
                              JsonSettingsService settings, CoverThumbCache thumbs)
    {
        _library = library;
        _audio = audio;
        _settings = settings;
        _thumbs = thumbs;
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        try
        {
            var lists = await _library.GetPlaylistsAsync();
            Playlists.Clear();
            foreach (var p in lists)
                Playlists.Add(new PlaylistCardViewModel(p));
            IsEmpty = Playlists.Count == 0;
            StatusText = IsEmpty ? "" : Loc.F("Playlists.Count", Playlists.Count);
            foreach (var card in Playlists)
                card.Cover = await _thumbs.GetPlaylistCoverAsync(card.Id);
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.LoadErrorWith", ex.Message);
        }
    }

    public void Open(PlaylistCardViewModel card) =>
        Detail = new PlaylistDetailViewModel(_library, _audio, _settings, _thumbs, card, () =>
        {
            Detail = null;
            ReloadCommand.Execute(null);
        });

    public async Task OpenByIdAsync(int playlistId)
    {
        var lists = await _library.GetPlaylistsAsync();
        var match = lists.Find(p => p.Id == playlistId);
        if (match != null)
            Open(new PlaylistCardViewModel(match));
    }

    public async Task CreateAsync(string name)
    {
        try
        {
            await _library.CreatePlaylistAsync(name);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public async Task RenameAsync(PlaylistCardViewModel card, string name)
    {
        try
        {
            await _library.RenamePlaylistAsync(card.Id, name);
            card.Name = name.Trim();
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public async Task DeleteAsync(PlaylistCardViewModel card)
    {
        try
        {
            await _library.DeletePlaylistAsync(card.Id);
            _thumbs.InvalidatePlaylist(card.Id);
            bool changed = _settings.Current.PlaylistViewModes.Remove(card.Id);
            changed |= _settings.Current.PlaylistSortModes.Remove(card.Id);
            changed |= _settings.Current.PlaylistSortDescending.Remove(card.Id);
            if (changed)
                _settings.Save();
            Infrastructure.LibraryNotifier.RaisePlaylistDeleted(card.Id);
            Playlists.Remove(card);
            IsEmpty = Playlists.Count == 0;
            StatusText = IsEmpty ? "" : Loc.F("Playlists.Count", Playlists.Count);
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }
}
