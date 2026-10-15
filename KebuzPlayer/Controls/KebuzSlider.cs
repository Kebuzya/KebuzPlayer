using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace KebuzPlayer.Controls;

public class KebuzSlider : Slider
{
    protected override Type StyleKeyOverride => typeof(Slider);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        double range = Maximum - Minimum;
        if (range <= 0) { e.Handled = true; return; }

        double step = range * 0.02;
        double delta = e.Delta.Y > 0 ? step : -step;
        Value = Math.Clamp(Value + delta, Minimum, Maximum);
        e.Handled = true;
    }
}
