using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Lang;
using KebuzPlayer.Services;

namespace KebuzPlayer.ViewModels;

public sealed record EqualizerPresetOption(EqualizerPreset Preset)
{
    public string Name => Loc.T("Eq.Preset." + Preset);
}

public partial class EqualizerBandViewModel : ObservableObject
{
    private readonly float  _hz;
    private readonly Action _changed;

    [ObservableProperty]
    private double _gain;

    public EqualizerBandViewModel(float hz, Action changed)
    {
        _hz      = hz;
        _changed = changed;
    }

    public string Label => _hz >= 1000 ? Loc.F("Eq.Khz", _hz / 1000) : _hz.ToString(CultureInfo.InvariantCulture);
    public string GainText => Gain.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));

    partial void OnGainChanged(double value)
    {
        OnPropertyChanged(nameof(GainText));
        _changed();
    }
}

public partial class EqualizerViewModel : ViewModelBase
{
    private readonly AudioService        _audio;
    private readonly JsonSettingsService _settings;
    private bool _loading;

    public EqualizerBandViewModel[] Bands { get; }

    [ObservableProperty]
    private IReadOnlyList<EqualizerPresetOption> _presets = BuildPresets();

    [ObservableProperty]
    private EqualizerPresetOption? _selectedPreset;

    [ObservableProperty]
    private bool _isEnabled;

    public EqualizerViewModel(AudioService audio, JsonSettingsService settings)
    {
        _audio    = audio;
        _settings = settings;
        Bands     = EqualizerPresets.BandFrequencies.Select(hz => new EqualizerBandViewModel(hz, OnBandChanged)).ToArray();

        _loading        = true;
        _isEnabled      = settings.Current.EqualizerEnabled;
        _selectedPreset = Presets.First(p => p.Preset == settings.Current.EqualizerPreset);
        LoadGains(GainsFor(settings.Current.EqualizerPreset));
        _loading        = false;
        Apply();
    }

    private static IReadOnlyList<EqualizerPresetOption> BuildPresets() =>
        Enum.GetValues<EqualizerPreset>().Select(p => new EqualizerPresetOption(p)).ToArray();

    private float[] GainsFor(EqualizerPreset preset) =>
        preset == EqualizerPreset.Custom ? _settings.Current.EqualizerCustomGains : EqualizerPresets.GainsFor(preset);

    private float[] CurrentGains() => Bands.Select(b => (float)b.Gain).ToArray();

    private void LoadGains(float[] gains)
    {
        bool wasLoading = _loading;
        _loading = true;
        for (int i = 0; i < Bands.Length; i++)
            Bands[i].Gain = gains[i];
        _loading = wasLoading;
    }

    private void Apply() => _audio.Equalizer.Apply(IsEnabled, CurrentGains());

    partial void OnIsEnabledChanged(bool value)
    {
        _settings.Current.EqualizerEnabled = value;
        Apply();
    }

    partial void OnSelectedPresetChanged(EqualizerPresetOption? value)
    {
        if (value == null || _loading) return;
        _settings.Current.EqualizerPreset = value.Preset;
        LoadGains(GainsFor(value.Preset));
        if (value.Preset != EqualizerPreset.Flat) IsEnabled = true;
        Apply();
    }

    private void OnBandChanged()
    {
        if (_loading) return;
        _settings.Current.EqualizerCustomGains = CurrentGains();
        if (SelectedPreset?.Preset != EqualizerPreset.Custom)
        {
            _loading       = true;
            SelectedPreset = Presets.First(p => p.Preset == EqualizerPreset.Custom);
            _loading       = false;
            _settings.Current.EqualizerPreset = EqualizerPreset.Custom;
        }
        IsEnabled = true;
        Apply();
    }

    [RelayCommand]
    private void Reset() => SelectedPreset = Presets.First(p => p.Preset == EqualizerPreset.Flat);

    public void RefreshTexts()
    {
        var selected = SelectedPreset?.Preset ?? EqualizerPreset.Flat;
        _loading       = true;
        Presets        = BuildPresets();
        SelectedPreset = Presets.First(p => p.Preset == selected);
        _loading       = false;
        foreach (var band in Bands)
            band.RefreshLabel();
    }
}
