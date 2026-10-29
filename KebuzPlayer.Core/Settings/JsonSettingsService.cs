using System.Text.Json;
using System.Text.Json.Serialization;

namespace KebuzPlayer.Services;

public enum OverlayStyle { Modest, Vivid }

public enum TrackViewMode { Table, Grid, Compact }

public enum TrackSortMode { Manual, Title, Artist, Album, DateAdded, Duration }

public enum FileTransferMode { Copy, Move }

public sealed class UserSettings
{
    public string Theme { get; set; } = "MonoNoir";
    public float MasterVolume { get; set; } = 0.8f;
    public bool IsShuffleOn { get; set; }
    public RepeatMode RepeatMode { get; set; } = RepeatMode.None;
    public int? LastTrackId { get; set; }
    public double LastPositionSeconds { get; set; }
    public int ActiveTabIndex { get; set; }
    public bool WheelSelectsInDropdowns { get; set; }
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 820;
    public bool WindowMaximized { get; set; }
    public bool MaximizeOverTaskbar { get; set; }
    public OverlayStyle OverlayStyle { get; set; } = OverlayStyle.Modest;
    public int CoverPreviewBatch { get; set; } = 9;
    public bool OverlayVisualizerEnabled { get; set; }
    public bool OverlayPinned { get; set; }
    public int? OverlayPosX { get; set; }
    public int? OverlayPosY { get; set; }
    public int DetachedRetentionDays { get; set; } = 90;
    public TrackViewMode LibraryViewMode { get; set; } = TrackViewMode.Table;
    public Dictionary<int, TrackViewMode> PlaylistViewModes { get; set; } = new();
    public TrackSortMode LibrarySortMode { get; set; } = TrackSortMode.Title;
    public bool LibrarySortDescending { get; set; }
    public Dictionary<int, TrackSortMode> PlaylistSortModes { get; set; } = new();
    public Dictionary<int, bool> PlaylistSortDescending { get; set; } = new();
    public FileTransferMode FileTransferMode { get; set; } = FileTransferMode.Copy;
    public List<string> FolderOrder { get; set; } = new();
    public string? Language { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }
    public string? AvailableUpdateVersion { get; set; }
    public string? AvailableUpdateUrl { get; set; }
    public string? DismissedUpdateVersion { get; set; }
    public bool EqualizerEnabled { get; set; }
    public EqualizerPreset EqualizerPreset { get; set; } = EqualizerPreset.Flat;
    public float[] EqualizerCustomGains { get; set; } = new float[EqualizerPresets.BandCount];
}

public sealed class JsonSettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public UserSettings Current { get; private set; }

    public event EventHandler? Saved;

    public JsonSettingsService(string filePath)
    {
        _filePath = filePath;
        Current = Load();
    }

    private UserSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new UserSettings();
            var json = File.ReadAllText(_filePath);
            return Sanitize(JsonSerializer.Deserialize<UserSettings>(json, Options) ?? new UserSettings());
        }
        catch
        {
            KeepAside();
            return new UserSettings();
        }
    }

    private void KeepAside()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Copy(_filePath, $"{_filePath}.bad-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch { }
    }

    private static UserSettings Sanitize(UserSettings s)
    {
        var defaults = new UserSettings();
        if (!Enum.IsDefined(s.RepeatMode)) s.RepeatMode = defaults.RepeatMode;
        if (!Enum.IsDefined(s.OverlayStyle)) s.OverlayStyle = defaults.OverlayStyle;
        if (!Enum.IsDefined(s.LibraryViewMode)) s.LibraryViewMode = defaults.LibraryViewMode;
        if (!Enum.IsDefined(s.LibrarySortMode)) s.LibrarySortMode = defaults.LibrarySortMode;
        if (!Enum.IsDefined(s.FileTransferMode)) s.FileTransferMode = defaults.FileTransferMode;
        if (!Enum.IsDefined(s.EqualizerPreset)) s.EqualizerPreset = defaults.EqualizerPreset;
        s.EqualizerCustomGains = EqualizerPresets.Normalize(s.EqualizerCustomGains);
        s.PlaylistViewModes ??= new();
        s.PlaylistSortModes ??= new();
        s.PlaylistSortDescending ??= new();
        s.FolderOrder ??= new();
        return s;
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Options));
        File.Move(tmp, _filePath, overwrite: true);
        Saved?.Invoke(this, EventArgs.Empty);
    }
}
