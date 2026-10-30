using Microsoft.Win32;

namespace WorkingCenter.Core;

/// <summary>开机自启动，通过 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 注册表项
/// 实现（用户级，无需管理员权限）。注册表本身即状态持久化：存在即开启。
/// 值指向当前 exe 的完整路径（带引号，兼容含空格路径）。</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HiveStation";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) != null;
        }
        catch { return false; }
    }

    /// <summary>开启自启：写入当前 exe 路径。失败返回 false 并由调用方提示。</summary>
    public static bool Enable()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key?.SetValue(ValueName, $"\"{exe}\"");
            Logger.Info($"AutoStart enabled: {exe}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Info($"AutoStart enable failed: {ex.Message}");
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            Logger.Info("AutoStart disabled");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Info($"AutoStart disable failed: {ex.Message}");
            return false;
        }
    }
}
