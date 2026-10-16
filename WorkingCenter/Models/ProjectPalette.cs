using System.Windows.Media;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace WorkingCenter.Models;

/// <summary>The fixed colour palette used by projects (mirrors the original web
/// app's PROJECTS colours). Projects reference a colour by index so the data
/// stays a plain int; the brush is derived here for the UI.</summary>
internal static class ProjectPalette
{
    public static readonly string[] Colors =
    {
        "#2563eb", // FT   blue
        "#7c3aed", // RF   purple
        "#16a34a", // FS   green
        "#ea580c", // FTMG orange
        "#0891b2", // NFW  cyan
        "#dc2626", // extra red
        "#ca8a04", // extra amber
        "#0f766e", // extra teal
    };

    public static SolidColorBrush BrushFor(int index) => BrushCache[index % BrushCache.Length];

    private static readonly SolidColorBrush[] BrushCache = BuildBrushes();

    private static SolidColorBrush[] BuildBrushes()
    {
        var arr = new SolidColorBrush[Colors.Length];
        for (int i = 0; i < Colors.Length; i++)
        {
            var b = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Colors[i]));
            b.Freeze();
            arr[i] = b;
        }
        return arr;
    }

    /// <summary>The original 5 projects seeded on first run (matches command-center.html).</summary>
    public static (string key, string name, int colorIndex)[] Defaults =>
        new[] { ("FT", "FT", 0), ("RF", "RF", 1), ("FS", "FS", 2), ("FTMG", "FTMG", 3), ("NFW", "NFW", 4) };

    /// <summary>Map a legacy project key (from the old web export) to a colour index.</summary>
    public static int LegacyColorIndex(string key)
    {
        foreach (var (k, _, ci) in Defaults)
            if (k == key) return ci;
        return 5; // anything else -> red
    }
}
