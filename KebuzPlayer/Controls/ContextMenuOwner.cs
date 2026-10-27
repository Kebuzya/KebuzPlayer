using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace KebuzPlayer.Controls;

public static class ContextMenuOwner
{
    public static Control? Find(Visual root, object? sender)
    {
        if (sender is not MenuItem { Parent: ContextMenu menu }) return null;
        return root.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => ReferenceEquals(c.ContextMenu, menu));
    }
}
