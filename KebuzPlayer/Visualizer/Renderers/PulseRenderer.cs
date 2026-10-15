using System;
using Avalonia;
using Avalonia.Media;
using KebuzPlayer.Services;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class PulseRenderer : IVizRenderer
{
    public bool IsAnimated => false;

    public void Reset() { }

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        var   smooth = frame.Processor.Smooth;
        float amp    = frame.Amp;

        float bass = 0f, mid = 0f, treble = 0f;
        for (int i = 0; i < 8; i++)   bass   += smooth[i];
        for (int i = 8; i < 32; i++)  mid    += smooth[i];
        for (int i = 32; i < 64; i++) treble += smooth[i];
        bass   = Math.Min(1f, bass / 8f * amp);
        mid    = Math.Min(1f, mid / 24f * amp);
        treble = Math.Min(1f, treble / 32f * amp);

        var    center  = bounds.Center;
        double minHalf = Math.Min(bounds.Width, bounds.Height) / 2;

        double coreR = minHalf * (0.10 + 0.34 * bass);
        using (ctx.PushOpacity(0.55))
            ctx.DrawEllipse(frame.Palette.LevelBrush(bass), null, center, coreR, coreR);
        ctx.DrawEllipse(null, frame.Palette.LinePen, center, coreR, coreR);

        double midR = minHalf * (0.48 + 0.22 * mid);
        ctx.DrawEllipse(null, frame.Palette.LinePen, center, midR, midR);

        double trebleR = minHalf * (0.62 + 0.30 * treble);
        using (ctx.PushOpacity(0.45))
            ctx.DrawEllipse(null, frame.Palette.LinePen, center, trebleR, trebleR);
    }
}
