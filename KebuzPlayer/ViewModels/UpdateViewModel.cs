using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Services;
using KebuzPlayer.Lang;

namespace KebuzPlayer.ViewModels;

public partial class UpdateViewModel : ViewModelBase
{
    private readonly JsonSettingsService _settings;
    private readonly Version _current =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
    private bool _checking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateText))]
    private string? _availableVersion;

    [ObservableProperty]
    private bool _bannerVisible;

    public bool HasUpdate => AvailableVersion != null;

    public string UpdateText => Loc.F("Update.Available", AvailableVersion);

    public void RefreshTexts() => OnPropertyChanged(nameof(UpdateText));

    public UpdateViewModel(JsonSettingsService settings)
    {
        _settings = settings;
        Refresh();
    }

    public async Task CheckAsync()
    {
        var s = _settings.Current;
        if (_checking || !s.CheckForUpdates || !UpdateChecker.ShouldCheck(s.LastUpdateCheckUtc, DateTime.UtcNow))
            return;

        _checking = true;
        try
        {
            var latest = await UpdateChecker.FetchLatestAsync();
            s.LastUpdateCheckUtc = DateTime.UtcNow;
            if (latest != null)
            {
                s.AvailableUpdateVersion = latest.Version.ToString(3);
                s.AvailableUpdateUrl = latest.Url;
            }
            _settings.Save();
            Refresh();
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        catch (System.IO.IOException) { }
        finally
        {
            _checking = false;
        }
    }

    public void Refresh()
    {
        var s = _settings.Current;
        AvailableVersion = s.CheckForUpdates && UpdateChecker.IsAvailable(s.AvailableUpdateVersion, _current)
            ? s.AvailableUpdateVersion
            : null;
        BannerVisible = AvailableVersion != null
            && UpdateChecker.ShouldShowBanner(s.AvailableUpdateVersion, s.DismissedUpdateVersion, _current);
    }

    [RelayCommand]
    private void OpenPage()
    {
        var url = _settings.Current.AvailableUpdateUrl ?? UpdateChecker.ReleasesPage;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
        Dismiss();
    }

    [RelayCommand]
    private void Dismiss()
    {
        if (AvailableVersion == null) return;
        _settings.Current.DismissedUpdateVersion = AvailableVersion;
        _settings.Save();
        Refresh();
    }
}
