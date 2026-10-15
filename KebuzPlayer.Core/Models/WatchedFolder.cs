namespace KebuzPlayer.Models;

public class WatchedFolder
{
    public string Path { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime? LastScanned { get; set; }
}
