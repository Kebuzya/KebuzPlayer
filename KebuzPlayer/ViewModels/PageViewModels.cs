using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KebuzPlayer.Home;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Services;

namespace KebuzPlayer.ViewModels;

public sealed record PlaylistPick(int Id, string Name);

public partial class HomeViewModel : ViewModelBase, IDisposable
{
    private const string LayoutSettingKey = "HomeWidgetLayout";
    private const int FallbackColumns = 4;
    private readonly LibraryService _library;
    private readonly AudioService _audio;
    private readonly CoverThumbCache _thumbs;
    private readonly JsonSettingsService _settings;
    private readonly Action<int> _openPlaylist;

    public ObservableCollection<HomeWidgetViewModel> Widgets { get; } = [];
    public ObservableCollection<PlaylistPick> Playlists { get; } = [];

    public IReadOnlyList<WidgetCatalogEntry> CatalogEntries { get; } = WidgetCatalog.Entries;

    public HomeViewModel(LibraryService library, AudioService audio, CoverThumbCache thumbs,
                         JsonSettingsService settings, Action<int> openPlaylist)
    {
        _library = library;
        _audio = audio;
        _thumbs = thumbs;
        _settings = settings;
        _openPlaylist = openPlaylist;
        LibraryNotifier.PlaylistDeleted += OnPlaylistDeleted;
        _ = LoadAsync();
    }

    public void Dispose()
    {
        LibraryNotifier.PlaylistDeleted -= OnPlaylistDeleted;
        foreach (var w in Widgets)
        {
            (w.Content as IDisposable)?.Dispose();
            w.Detach();
        }
    }

    private void OnPlaylistDeleted(int playlistId)
    {
        var orphans = Widgets.Where(w => w.Type == HomeWidgetType.Playlist && w.PlaylistId == playlistId).ToList();
        if (orphans.Count == 0) return;
        foreach (var w in orphans)
        {
            (w.Content as IDisposable)?.Dispose();
            w.Detach();
            Widgets.Remove(w);
        }
        _ = SaveAsync();
    }

    private object? BuildContent(HomeWidgetViewModel w) => w.Type switch
    {
        HomeWidgetType.Recent => new RecentWidgetContent(_library, _audio, _thumbs),
        HomeWidgetType.Frequent => new FrequentWidgetContent(_library, _audio, _thumbs, w.PeriodDays,
            days => { w.PeriodDays = days; _ = SaveAsync(); }),
        HomeWidgetType.Resume => new ResumeWidgetContent(_library, _audio, _settings, _thumbs,
            w.Cols == w.Rows),
        HomeWidgetType.Playlist when w.PlaylistId is int pid =>
            new PlaylistWidgetContent(_library, _audio, _thumbs, pid, _openPlaylist,
                w is { Cols: 1, Rows: 1 }),
        _ => null,
    };

    public async Task LoadPlaylistsAsync()
    {
        try
        {
            var lists = await _library.GetPlaylistsAsync();
            Playlists.Clear();
            foreach (var p in lists)
                Playlists.Add(new PlaylistPick(p.Id, p.Name));
        }
        catch { }
    }

    public void AddWidget(HomeWidgetType type, WidgetSize size, int cols, int? playlistId = null)
    {
        var widget = new HomeWidgetViewModel(type, size) { PlaylistId = playlistId };
        widget.Persist = () => _ = SaveAsync();
        (widget.Row, widget.Col) = FindFreeCell(size.Cols, size.Rows, cols, null);
        widget.Content = BuildContent(widget);
        Widgets.Add(widget);
        _ = SaveAsync();
    }

    public void RemoveWidget(HomeWidgetViewModel widget)
    {
        (widget.Content as IDisposable)?.Dispose();
        widget.Detach();
        Widgets.Remove(widget);
        _ = SaveAsync();
    }

    public void ChangePlaylist(HomeWidgetViewModel widget, int playlistId)
    {
        if (widget.PlaylistId == playlistId) return;
        widget.PlaylistId = playlistId;
        (widget.Content as IDisposable)?.Dispose();
        widget.Content = BuildContent(widget);
        _ = SaveAsync();
    }

    public void ChangeSize(HomeWidgetViewModel widget, WidgetSize size, int cols)
    {
        widget.Cols = size.Cols;
        widget.Rows = size.Rows;
        if (widget.Col + widget.Cols > cols ||
            Collides(widget.Row, widget.Col, widget.Cols, widget.Rows, widget, null))
            (widget.Row, widget.Col) = FindFreeCell(widget.Cols, widget.Rows, cols, widget);
        (widget.Content as IDisposable)?.Dispose();
        widget.Content = BuildContent(widget);
        _ = SaveAsync();
    }

    public bool CanPlace(HomeWidgetViewModel widget, int row, int col, int cols,
                         out HomeWidgetViewModel? swap,
                         IReadOnlyDictionary<HomeWidgetViewModel, (int Row, int Col)>? shown = null)
    {
        swap = null;
        if (row < 0 || col < 0 || col + widget.Cols > Math.Max(cols, widget.Cols)) return false;

        (int Row, int Col) At(HomeWidgetViewModel w) =>
            shown != null && shown.TryGetValue(w, out var cell) ? cell : (w.Row, w.Col);

        HomeWidgetViewModel? single = null;
        foreach (var other in Widgets)
        {
            if (other == widget) continue;
            var o = At(other);
            if (!RectsOverlap(row, col, widget.Cols, widget.Rows, o.Row, o.Col, other.Cols, other.Rows))
                continue;
            if (single != null) return false;
            single = other;
        }
        if (single == null) return true;

        var (sRow, sCol) = At(widget);
        if (sCol + single.Cols > Math.Max(cols, single.Cols)) return false;
        if (RectsOverlap(row, col, widget.Cols, widget.Rows, sRow, sCol, single.Cols, single.Rows)) return false;
        foreach (var other in Widgets)
        {
            if (other == widget || other == single) continue;
            var o = At(other);
            if (RectsOverlap(sRow, sCol, single.Cols, single.Rows, o.Row, o.Col, other.Cols, other.Rows))
                return false;
        }
        swap = single;
        return true;
    }

    private static bool RectsOverlap(int r1, int c1, int w1, int h1, int r2, int c2, int w2, int h2) =>
        c1 < c2 + w2 && c2 < c1 + w1 && r1 < r2 + h2 && r2 < r1 + h1;

    public bool PlaceWidget(HomeWidgetViewModel widget, int row, int col, int cols,
                            IReadOnlyDictionary<HomeWidgetViewModel, (int Row, int Col)>? shown = null)
    {
        if (!CanPlace(widget, row, col, cols, out var swap, shown)) return false;
        if (shown != null)
            foreach (var w in Widgets)
                if (shown.TryGetValue(w, out var cell))
                    (w.Row, w.Col) = cell;
        int oldRow = widget.Row, oldCol = widget.Col;
        widget.Row = row;
        widget.Col = col;
        if (swap != null)
        {
            swap.Row = oldRow;
            swap.Col = oldCol;
        }
        _ = SaveAsync();
        return true;
    }

    private (int Row, int Col) FindFreeCell(int spanCols, int spanRows, int cols,
                                            HomeWidgetViewModel? ignore)
    {
        int limit = Math.Max(cols, spanCols);
        for (int row = 0; ; row++)
            for (int col = 0; col + spanCols <= limit; col++)
                if (!Collides(row, col, spanCols, spanRows, ignore, null))
                    return (row, col);
    }

    private bool Collides(int row, int col, int spanCols, int spanRows,
                          HomeWidgetViewModel? ignoreA, HomeWidgetViewModel? ignoreB)
    {
        foreach (var other in Widgets)
        {
            if (other == ignoreA || other == ignoreB) continue;
            if (Overlaps(other, row, col, spanCols, spanRows)) return true;
        }
        return false;
    }

    private static bool Overlaps(HomeWidgetViewModel other, int row, int col, int spanCols, int spanRows) =>
        col < other.Col + other.Cols && other.Col < col + spanCols &&
        row < other.Row + other.Rows && other.Row < row + spanRows;

    private async Task LoadAsync()
    {
        var json = await _library.GetSettingAsync(LayoutSettingKey);
        if (string.IsNullOrEmpty(json)) return;

        HomeWidgetLayout? layout;
        try { layout = JsonSerializer.Deserialize<HomeWidgetLayout>(json); }
        catch (JsonException) { return; }
        if (layout == null) return;

        HashSet<int>? playlistIds = null;
        try { playlistIds = (await _library.GetPlaylistsAsync()).Select(p => p.Id).ToHashSet(); }
        catch { }

        bool migrated = false;
        foreach (var entry in layout.Widgets)
        {
            if (playlistIds != null && entry.Type == HomeWidgetType.Playlist &&
                (entry.PlaylistId is not int pid || !playlistIds.Contains(pid)))
            {
                migrated = true;
                continue;
            }

            var size = WidgetCatalog.Coerce(entry.Type, new WidgetSize(entry.Cols, entry.Rows));
            if (size.Cols != entry.Cols || size.Rows != entry.Rows)
                migrated = true;

            var widget = new HomeWidgetViewModel(entry.Type, size)
            {
                PlaylistId = entry.PlaylistId,
                PeriodDays = entry.PeriodDays,
                Row = entry.Row,
                Col = entry.Col,
                OwnViz = entry.Viz,
            };
            widget.Persist = () => _ = SaveAsync();
            if (widget.Row < 0 || widget.Col < 0 ||
                Collides(widget.Row, widget.Col, widget.Cols, widget.Rows, null, null))
            {
                (widget.Row, widget.Col) = FindFreeCell(widget.Cols, widget.Rows, FallbackColumns, null);
                migrated = true;
            }
            widget.Content = BuildContent(widget);
            Widgets.Add(widget);
        }
        if (migrated)
            await SaveAsync();
    }

    public async Task SaveAsync()
    {
        var layout = new HomeWidgetLayout();
        foreach (var w in Widgets)
            layout.Widgets.Add(new HomeWidgetLayoutEntry
            {
                Type = w.Type,
                Cols = w.Cols,
                Rows = w.Rows,
                Row = w.Row,
                Col = w.Col,
                PlaylistId = w.PlaylistId,
                PeriodDays = w.PeriodDays,
                Viz = w.OwnViz,
            });
        await _library.SetSettingAsync(LayoutSettingKey, JsonSerializer.Serialize(layout));
    }
}

public partial class VisualizerViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private bool _vizEnabled = true;

    public VisualizerViewModel()
    {
        var hub = Visualizer.VisualizerHub.Instance;
        if (hub == null) return;
        _vizEnabled = hub.Settings.Tab.Enabled;
        hub.Settings.Changed += OnVizSettingsChanged;
    }

    private void OnVizSettingsChanged(object? sender, EventArgs e)
    {
        if (Visualizer.VisualizerHub.Instance is { } hub)
            VizEnabled = hub.Settings.Tab.Enabled;
    }

    public void Dispose()
    {
        if (Visualizer.VisualizerHub.Instance is { } hub)
            hub.Settings.Changed -= OnVizSettingsChanged;
    }
}
