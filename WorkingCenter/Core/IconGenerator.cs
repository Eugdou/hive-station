using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows;
using System.Windows.Resources;

namespace WorkingCenter.Core;

/// <summary>
/// Builds the tray icon from the embedded pixel-bee image (bee.png), so the
/// tray matches the header logo and the exe icon (bee.ico — regenerated from
/// the same png). One source of truth for the bee, no GDI hand-drawing.
/// </summary>
internal static class IconGenerator
{
    public static Icon CreateBeeIcon()
    {
        // 48px base so HiDPI (150–200%) taskbars scale down cleanly
        const int size = 48;
        using var bmp = LoadBeePng();
        using var scaled = new Bitmap(size, size);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(bmp, 0, 0, size, size);
        }
        IntPtr h = scaled.GetHicon();
        return Icon.FromHandle(h);
    }

    private static Bitmap LoadBeePng()
    {
        StreamResourceInfo sri = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/bee.png"));
        using var ms = new MemoryStream();
        sri.Stream.CopyTo(ms);
        return new Bitmap(ms);
    }
}
