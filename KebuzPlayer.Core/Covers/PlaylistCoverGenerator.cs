using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace KebuzPlayer.Services;

public static class PlaylistCoverGenerator
{
    private const int Size     = 400;
    private const int Half     = 200;

    public static byte[] Generate(IList<byte[]?> covers)
    {
        using var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using var g   = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(28, 28, 28));

        if (covers.Count == 0 || covers.All(c => c == null || c.Length == 0))
        {
            DrawMusicNote(g, new Rectangle(0, 0, Size, Size));
            return Encode(bmp);
        }

        var slots = new Rectangle[]
        {
            new(0,    0,    Half, Half),
            new(Half, 0,    Half, Half),
            new(0,    Half, Half, Half),
            new(Half, Half, Half, Half),
        };

        for (int i = 0; i < 4; i++)
        {
            var bytes = covers[i % covers.Count];
            if (bytes != null && bytes.Length > 0)
            {
                try
                {
                    using var ms  = new MemoryStream(bytes);
                    using var src = Image.FromStream(ms);

                    DrawCropped(g, src, slots[i]);
                    continue;
                }
                catch { }
            }
            DrawMusicNote(g, slots[i]);
        }

        using var pen = new Pen(Color.FromArgb(50, 0, 0, 0), 1.5f);
        g.DrawLine(pen, Half, 0,    Half, Size);
        g.DrawLine(pen, 0,    Half, Size, Half);

        return Encode(bmp);
    }

    private static void DrawCropped(Graphics g, Image src, Rectangle dest)
    {
        int sw = src.Width, sh = src.Height;
        int side = Math.Min(sw, sh);
        var srcRect = new Rectangle((sw - side) / 2, (sh - side) / 2, side, side);
        g.DrawImage(src, dest, srcRect, GraphicsUnit.Pixel);
    }

    private static void DrawMusicNote(Graphics g, Rectangle rect)
    {
        using var bg = new SolidBrush(Color.FromArgb(38, 38, 38));
        g.FillRectangle(bg, rect);
        using var font = new Font("Segoe UI", rect.Height * 0.35f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var br   = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("♪", font, br, rect, sf);
    }

    private static byte[] Encode(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }
}
