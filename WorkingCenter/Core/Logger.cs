using System.IO;

namespace WorkingCenter.Core;

internal static class Logger
{
    private static string _path = "";

    public static void Init()
    {
        _path = Path.Combine(AppContext.BaseDirectory, "hive-station.log");
        Info("--- new session ---");
    }

    public static void Info(string msg)
    {
        try
        {
            File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff}  {msg}{Environment.NewLine}");
        }
        catch
        {
            // best-effort logging; never crash the app here
        }
    }
}
