using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace KebuzPlayer.Controls;

public class KebuzComboBox : ComboBox
{
    public static bool WheelSelectionEnabled { get; set; }

    protected override Type StyleKeyOverride => typeof(ComboBox);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!WheelSelectionEnabled || IsDropDownOpen)
        {
            base.OnPointerWheelChanged(e);
            return;
        }

        if (ItemCount > 0)
        {
            int next = SelectedIndex + (e.Delta.Y > 0 ? -1 : 1);
            SelectedIndex = Math.Clamp(next, 0, ItemCount - 1);
        }
        e.Handled = true;
    }
}
