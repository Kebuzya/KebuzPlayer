using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using KebuzPlayer.Services;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class WaterfallRenderer : IVizRenderer, IDisposable
{
    private const int Cols = SpectrumProcessor.BarCount;
    private const int Rows = 128;
    private const int LutSize = 32;

    private readonly int[] _rowBuf = new int[Cols];
    private WriteableBitmap? _bitmap;
    private VizPalette? _cachedPalette;
    private int[]? _lut;
    private int _row;
    private float _rowAcc;
    private int _fade;

    public bool IsAnimated => _fade > 0;

    public void Reset()
    {
        _fade = 0;
        _row = 0;
        _rowAcc = 0f;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    public void Dispose() => Reset();

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        _bitmap ??= new WriteableBitmap(new PixelSize(Cols, Rows), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);

        if (!ReferenceEquals(_cachedPalette, frame.Palette) || _lut == null)
        {
            _cachedPalette = frame.Palette;
            _lut = new int[LutSize];
            for (int i = 1; i < LutSize; i++)
            {
                var c = frame.Palette.LevelBrush(i / (LutSize - 1f)).Color;
                _lut[i] = (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
            }
        }

        var   smooth = frame.Processor.Smooth;
        float amp    = frame.Amp;
        bool  hasContent = false;
        for (int i = 0; i < Cols; i++)
        {
            float level = Math.Min(1f, smooth[i] * amp);
            if (level > 0.01f) hasContent = true;
            _rowBuf[i] = _lut[(int)(level * (LutSize - 1))];
        }
        _rowAcc += frame.Steps;
        int scroll = (int)_rowAcc;
        _rowAcc -= scroll;
        if (scroll > Rows) scroll = Rows;

        _fade = hasContent ? Rows : Math.Max(_fade - Math.Max(scroll, 1), 0);

        using (var fb = _bitmap.Lock())
        {
            if (scroll == 0)
                Marshal.Copy(_rowBuf, 0, fb.Address + ((_row - 1 + Rows) % Rows) * fb.RowBytes, Cols);
            else
                for (int n = 0; n < scroll; n++)
                {
                    Marshal.Copy(_rowBuf, 0, fb.Address + _row * fb.RowBytes, Cols);
                    _row = (_row + 1) % Rows;
                }
        }

        int oldRows = Rows - _row;
        double rowH = bounds.Height / Rows;
        if (oldRows > 0)
            ctx.DrawImage(_bitmap,
                new Rect(0, _row, Cols, oldRows),
                new Rect(0, 0, bounds.Width, rowH * oldRows));
        if (_row > 0)
            ctx.DrawImage(_bitmap,
                new Rect(0, 0, Cols, _row),
                new Rect(0, rowH * oldRows, bounds.Width, rowH * _row));
    }
}
