using System.Drawing;
using System.Drawing.Drawing2D;

namespace MouseBatteryTray.UI;

internal static class Gfx
{
    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Fakes a neon glow by stroking the same rounded outline several times with growing width
    /// and falling opacity. GDI+ has no real blur, but layering translucent strokes reads as a
    /// soft glow at UI sizes.
    /// </summary>
    public static void DrawGlowOutline(Graphics g, RectangleF rect, float radius, Color color, int layers = 4, float maxWidth = 8f)
    {
        for (int i = layers; i >= 1; i--)
        {
            float t = i / (float)layers;
            using var pen = new Pen(Color.FromArgb((int)(50 * (1 - t) + 10), color), maxWidth * t);
            using var path = RoundedRect(rect, radius);
            g.DrawPath(pen, path);
        }
    }

    public static void FillRoundedRect(Graphics g, RectangleF rect, float radius, Color color)
    {
        using var path = RoundedRect(rect, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedRect(Graphics g, RectangleF rect, float radius, Color color, float width)
    {
        using var path = RoundedRect(rect, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }
}
