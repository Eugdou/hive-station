using System.IO;
using System.Text.Json;
using System.Windows;
using WorkingCenter.Core;
using Application = System.Windows.Application;

namespace WorkingCenter.Views;

/// <summary>Applies and persists the UI theme (Light/Dark). The active theme is a
/// ResourceDictionary merged into Application.Resources; swapping it restyles every
/// control bound via DynamicResource. Persisted next to edge in data/settings.json.</summary>
internal static class ThemeService
{
    public enum Theme { Light, Dark }

    public static Theme Current { get; private set; } = Theme.Light;

    /// <summary>Apply a theme at runtime (re-merges the token dictionary).</summary>
    public static void Apply(Theme theme)
    {
        Current = theme;
        var merged = Application.Current.Resources.MergedDictionaries;
        // the theme dictionary is the SECOND merged dictionary (Icons.xaml is first)
        var src = new System.Uri($"Views/Themes/{theme}.xaml", System.UriKind.Relative);
        // remove any existing theme dict, then add
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            if (merged[i].Source != null && merged[i].Source.OriginalString.Contains("/Themes/"))
                merged.RemoveAt(i);
        }
        merged.Add(new ResourceDictionary { Source = src });
        Logger.Info($"Theme applied: {theme}");
    }

    /// <summary>Persist the theme choice alongside the edge setting.</summary>
    public static void Save(Theme theme)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "data", "settings.json");
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var edge = "";
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("edge", out var e) && e.ValueKind == JsonValueKind.String)
                    edge = e.GetString() ?? "";
            }
            File.WriteAllText(path, $"{{\"edge\":\"{edge}\",\"theme\":\"{theme}\"}}");
        }
        catch { /* best-effort */ }
    }

    /// <summary>Load the persisted theme (default Light).</summary>
    public static Theme Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "data", "settings.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("theme", out var t) && t.ValueKind == JsonValueKind.String
                    && Enum.TryParse<Theme>(t.GetString(), true, out var parsed))
                    return parsed;
            }
        }
        catch { }
        return Theme.Light;
    }
}
