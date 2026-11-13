using System.IO;
using System.Text.Json.Nodes;
using WorkingCenter.Core;

namespace WorkingCenter.Data;

/// <summary>Updates individual settings without discarding other saved fields.</summary>
internal static class SettingsStore
{
    private static readonly object Gate = new();
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "data", "settings.json");

    public static string? GetValue(string name)
    {
        lock (Gate)
        {
            try { return ReadSettings()[name]?.GetValue<string>(); }
            catch (Exception ex)
            {
                Logger.Info($"Settings read failed: {ex.Message}");
                return null;
            }
        }
    }

    public static void SetValue(string name, string value)
    {
        lock (Gate)
        {
            try
            {
                var settings = ReadSettings();
                settings[name] = value;
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                var temporaryPath = SettingsPath + ".tmp";
                File.WriteAllText(temporaryPath, settings.ToJsonString());
                File.Move(temporaryPath, SettingsPath, overwrite: true);
            }
            catch (Exception ex) { Logger.Info($"Settings save failed: {ex.Message}"); }
        }
    }

    private static JsonObject ReadSettings()
        => File.Exists(SettingsPath)
            ? JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject
                ?? throw new FormatException("设置文件必须是 JSON 对象。")
            : new JsonObject();
}
