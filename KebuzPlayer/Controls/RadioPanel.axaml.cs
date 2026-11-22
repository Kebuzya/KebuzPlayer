using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using KebuzPlayer.Lang;
using KebuzPlayer.Models;
using KebuzPlayer.ViewModels;

namespace KebuzPlayer.Controls;

public partial class RadioPanel : UserControl
{
    private RadioStation? _pendingRemoval;

    public event EventHandler? DismissRequested;

    public RadioPanel() => InitializeComponent();

    public void FocusAddress()
    {
        if (DataContext is PlayerViewModel player)
        {
            AddressBox.Text = player.LastRadioAddress.Length > 0
                ? player.LastRadioAddress
                : player.RecentRadioStations.FirstOrDefault()?.Address;
        }
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    private async Task PlayAsync(string? address)
    {
        if (DataContext is not PlayerViewModel player || string.IsNullOrWhiteSpace(address)) return;
        DismissRequested?.Invoke(this, EventArgs.Empty);
        await player.OpenRadioAsync(address.Trim());
    }

    private async void PlayAddress_Click(object? sender, RoutedEventArgs e) =>
        await PlayAsync(AddressBox.Text);

    private async void AddressBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await PlayAsync(AddressBox.Text);
    }

    private async void PlayRecent_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RadioStation station }) await PlayAsync(station.Address);
    }

    private async void CopyAddress_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RadioStation station }) return;
        bool copied = false;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(station.Address);
                copied = true;
            }
        }
        catch (ExternalException)
        {
        }
        CopyStatus.Text = Loc.T(copied ? "Radio.AddressCopied" : "Radio.AddressCopyFailed");
    }

    private async void StationThumbnail_Attached(object? sender, VisualTreeAttachmentEventArgs e) =>
        await LoadStationThumbnailAsync(sender);

    private async void StationThumbnail_Changed(object? sender, EventArgs e) =>
        await LoadStationThumbnailAsync(sender);

    private async Task LoadStationThumbnailAsync(object? sender)
    {
        if (sender is not Image image || !image.IsAttachedToVisualTree()) return;
        SetThumbnail(image, null);
        if (image.DataContext is not RadioStation station || DataContext is not PlayerViewModel player) return;

        var thumbnail = await player.LoadRadioThumbnailAsync(station.Address);
        if (image.IsAttachedToVisualTree() && ReferenceEquals(image.DataContext, station))
            SetThumbnail(image, thumbnail);
        else
            thumbnail?.Dispose();
    }

    private void StationThumbnail_Detached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Image image) SetThumbnail(image, null);
    }

    private static void SetThumbnail(Image image, Bitmap? thumbnail)
    {
        var previous = image.Source as Bitmap;
        image.Source = thumbnail;
        previous?.Dispose();
    }

    private void RequestRemoval_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RadioStation station }) return;
        _pendingRemoval = station;
        CopyStatus.Text = null;
        RemovalQuestion.Text = Loc.F("Radio.ConfirmRemoval", station.Name);
        StationsPanel.IsVisible = false;
        RemovalPanel.IsVisible = true;
        CancelRemovalButton.Focus();
    }

    private void CancelRemoval_Click(object? sender, RoutedEventArgs e) => CloseConfirmation();

    private void ConfirmRemoval_Click(object? sender, RoutedEventArgs e)
    {
        if (_pendingRemoval is { } station && DataContext is PlayerViewModel player
            && player.RemoveRecentRadioStation(station))
        {
            if (RadioStation.ParseAddress(AddressBox.Text)?.GetLeftPart(UriPartial.Query) == station.Address)
                AddressBox.Clear();
        }
        CloseConfirmation();
    }

    private void CloseConfirmation()
    {
        _pendingRemoval = null;
        RemovalPanel.IsVisible = false;
        StationsPanel.IsVisible = true;
        AddressBox.Focus();
    }
}
