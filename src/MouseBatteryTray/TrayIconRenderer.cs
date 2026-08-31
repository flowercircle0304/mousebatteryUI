using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using MouseBatteryTray.UI;

namespace MouseBatteryTray;

internal static class TrayIconRenderer
{
    public static Icon Render(int? percent)
    {
        var size = SystemInformation.SmallIconSize;
        int w = Math.Max(size.Width, 16);
        int h = Math.Max(size.Height, 16);

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            Color level = Theme.LevelColor(percent);
            float radius = w * 0.28f;
            var outer = new RectangleF(0.5f, 0.5f, w - 1, h - 1);

            Gfx.FillRoundedRect(g, outer, radius, Theme.CardBackground);
            Gfx.DrawRoundedRect(g, outer, radius, Color.FromArgb(220, level), Math.Max(1f, w / 16f));

            // Thin HUD readout bar along the bottom, filled proportionally to charge.
            float barMargin = w * 0.16f;
            float barHeight = Math.Max(1.5f, h * 0.10f);
            var barTrack = new RectangleF(barMargin, h - barHeight - 1.5f, w - barMargin * 2, barHeight);
            using (var track = new SolidBrush(Color.FromArgb(90, Theme.TextMuted)))
                g.FillRectangle(track, barTrack);
            if (percent is int p)
            {
                float fillW = barTrack.Width * Math.Clamp(p, 0, 100) / 100f;
                using var fill = new SolidBrush(level);
                g.FillRectangle(fill, barTrack.X, barTrack.Y, fillW, barTrack.Height);
            }

            string text = percent?.ToString() ?? "?";
            float fontSize = w <= 16 ? 8.5f : (w <= 24 ? 12.5f : 16.5f);
            using var font = new Font("Segoe UI Semibold", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            var textSize = g.MeasureString(text, font);
            float tx = (w - textSize.Width) / 2f;
            float ty = (h - barHeight - textSize.Height) / 2f - 0.5f;
            using var textBrush = new SolidBrush(Theme.TextPrimary);
            g.DrawString(text, font, textBrush, tx, ty);
        }

        // Bitmap.GetHicon() collapses alpha into a 1-bit mask and often renders as a solid blob
        // in the Windows 11 tray. Embedding a real 32bpp PNG frame in the .ico container avoids
        // that entirely and is well supported for small icon sizes since Vista.
        using var pngStream = new MemoryStream();
        bmp.Save(pngStream, ImageFormat.Png);
        byte[] png = pngStream.ToArray();

        using var icoStream = new MemoryStream();
        using (var bw = new BinaryWriter(icoStream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            bw.Write((short)0);                       // reserved
            bw.Write((short)1);                        // type = icon
            bw.Write((short)1);                        // image count
            bw.Write((byte)(w >= 256 ? 0 : w));
            bw.Write((byte)(h >= 256 ? 0 : h));
            bw.Write((byte)0);                          // color count
            bw.Write((byte)0);                          // reserved
            bw.Write((short)1);                         // planes
            bw.Write((short)32);                        // bits per pixel
            bw.Write(png.Length);                       // size of image data
            bw.Write(22);                                // offset to image data (6 + 16)
            bw.Write(png);
        }
        icoStream.Position = 0;
        return new Icon(icoStream);
    }
}
