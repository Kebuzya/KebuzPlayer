using System.ComponentModel;

namespace KebuzPlayer.Models;

public class Track : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public int DurationSeconds { get; set; }
    public float? ReplayGainTarget { get; set; }

    private float _userGainOffset;
    public float UserGainOffset
    {
        get => _userGainOffset;
        set
        {
            if (_userGainOffset == value) return;
            _userGainOffset = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UserGainOffset)));
        }
    }

    public bool IsFavorite { get; set; }
    public int PlayCount { get; set; }
    public DateTime DateAdded { get; set; }
    public bool IsHidden { get; set; }
    public bool TagsRead { get; set; }

    public string DurationFormatted => TimeSpan.FromSeconds(DurationSeconds)
        .ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    public event PropertyChangedEventHandler? PropertyChanged;
}
