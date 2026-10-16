using System.Drawing;
using System.Drawing.Drawing2D;

namespace WorkingCenter.Core;

/// <summary>
/// Draws a small bee icon at runtime so the tray (and later the app icon) is
/// on-brand without shipping a separate .ico file yet.
/// </summary>
internal static class IconGenerator
{
    public static Icon CreateBeeIcon()
    {
        const int size = 32;
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // wings (drawn first, behind the body): translucent white ellipses
            using (var wing = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
            {
                g.FillEllipse(wing, 6, 5, 9, 8);
                g.FillEllipse(wing, 17, 5, 9, 8);
            }

            // body: amber horizontal ellipse
            var bodyRect = new Rectangle(6, 10, 20, 15);
            using (var body = new SolidBrush(Color.FromArgb(245, 166, 35)))
                g.FillEllipse(body, bodyRect);

            // black stripes across the body
            using (var stripe = new SolidBrush(Color.FromArgb(31, 36, 48)))
            {
                g.FillEllipse(stripe, new Rectangle(11, 10, 3, 15));
                g.FillEllipse(stripe, new Rectangle(18, 10, 3, 15));
            }

            // body outline
            using (var pen = new Pen(Color.FromArgb(60, 31, 36, 48), 1f))
                g.DrawEllipse(pen, bodyRect);
        }

        IntPtr h = bmp.GetHicon();
        bmp.Dispose();
        return Icon.FromHandle(h);
    }
}
