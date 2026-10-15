using System.Globalization;
using KebuzPlayer.Data;

namespace KebuzPlayer.Services;

public sealed class AppSettingsService
{
    private readonly Database _db;

    public AppSettingsService(Database db) => _db = db;

    public async Task<float> GetCoverChaosAsync()
    {
        var s = await _db.GetSettingAsync("CoverChaos");
        return s != null && int.TryParse(s, out int pct) ? Math.Clamp(pct, 0, 100) / 100f : 0f;
    }

    public Task SetCoverChaosPercentAsync(int percent) =>
        _db.SetSettingAsync("CoverChaos", Math.Clamp(percent, 0, 100).ToString());

    public async Task<float?> GetMasterVolumeAsync()
    {
        var s = await _db.GetSettingAsync("MasterVolume");
        return s != null && float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out float v)
            ? v : null;
    }

    public Task SetMasterVolumeAsync(float volume) =>
        _db.SetSettingAsync("MasterVolume", volume.ToString(CultureInfo.InvariantCulture));

    public async Task<RepeatMode?> GetRepeatModeAsync()
    {
        var s = await _db.GetSettingAsync("RepeatMode");
        return s != null && Enum.TryParse<RepeatMode>(s, out var mode) ? mode : null;
    }

    public Task SetRepeatModeAsync(RepeatMode mode) =>
        _db.SetSettingAsync("RepeatMode", mode.ToString());

    public async Task<bool> GetShuffleAsync()
    {
        var s = await _db.GetSettingAsync("IsShuffleOn");
        return s == "true";
    }

    public Task SetShuffleAsync(bool enabled) =>
        _db.SetSettingAsync("IsShuffleOn", enabled ? "true" : "false");

    public async Task<int?> GetLastTrackIdAsync()
    {
        var s = await _db.GetSettingAsync("LastTrackId");
        return s != null && int.TryParse(s, out int id) ? id : null;
    }

    public Task SetLastTrackIdAsync(int trackId) =>
        _db.SetSettingAsync("LastTrackId", trackId.ToString());

    public async Task LoadVisualizerSettingsAsync(VisualizerSettings settings, bool overlayEnabledDefault = true)
    {
        bool ParseBool(string? v, bool def) => v == null ? def : v == "true";
        float ParseFloat(string? v, float def) =>
            v != null && float.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out float f) ? f : def;
        int ParseInt(string? v, int def) => v != null && int.TryParse(v, out int i) ? i : def;

        settings.FrameRate = ParseInt(await _db.GetSettingAsync("Viz2_FrameRate"), 30);

        foreach (var (key, panel) in PanelKeys(settings))
        {
            bool enabledDefault = key == "Overlay" ? overlayEnabledDefault : true;
            panel.Enabled         = ParseBool(await _db.GetSettingAsync($"Viz2_{key}_Enabled"), enabledDefault);
            panel.Intensity       = ParseFloat(await _db.GetSettingAsync($"Viz2_{key}_Intensity"), 0.75f);
            panel.ShowPeakRunners = ParseBool(await _db.GetSettingAsync($"Viz2_{key}_Peaks"), true);
            panel.BarCount        = ParseInt(await _db.GetSettingAsync($"Viz2_{key}_BarCount"), 64);

            var styleStr = await _db.GetSettingAsync($"Viz2_{key}_Style");
            if (styleStr != null && Enum.TryParse<VisualizerStyle>(styleStr, out var style))
                panel.Style = style;

            var modeStr = await _db.GetSettingAsync($"Viz2_{key}_ColorMode");
            if (modeStr != null && Enum.TryParse<VizColorMode>(modeStr, out var mode))
                panel.ColorMode = mode;
        }
    }

    public async Task SaveVisualizerSettingsAsync(VisualizerSettings settings)
    {
        string B(bool v) => v ? "true" : "false";
        string F(float v) => v.ToString(CultureInfo.InvariantCulture);

        await _db.SetSettingAsync("Viz2_FrameRate", settings.FrameRate.ToString());

        foreach (var (key, panel) in PanelKeys(settings))
        {
            await _db.SetSettingAsync($"Viz2_{key}_Enabled",   B(panel.Enabled));
            await _db.SetSettingAsync($"Viz2_{key}_Intensity", F(panel.Intensity));
            await _db.SetSettingAsync($"Viz2_{key}_Peaks",     B(panel.ShowPeakRunners));
            await _db.SetSettingAsync($"Viz2_{key}_Style",     panel.Style.ToString());
            await _db.SetSettingAsync($"Viz2_{key}_ColorMode", panel.ColorMode.ToString());
            await _db.SetSettingAsync($"Viz2_{key}_BarCount",  panel.BarCount.ToString());
        }
    }

    private static IEnumerable<(string Key, PanelVisualizerSettings Panel)> PanelKeys(VisualizerSettings s) =>
    [
        ("Library", s.Library),
        ("Tab",     s.Tab),
        ("Overlay", s.Overlay),
        ("Home",    s.Home),
    ];
}
