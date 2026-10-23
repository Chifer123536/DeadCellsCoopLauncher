namespace CoopLauncher.Core;

public static class LauncherPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeadCellsCoop");

    public static string CacheDir => Path.Combine(DataDir, "cache");
    public static string LogFile => Path.Combine(DataDir, "launcher.log");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
}

public static class LauncherLog
{
    const long MaxBytes = 1 << 20;
    static readonly object Sync = new();

    public static void Info(string message) => Write("INF", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERR", ex == null ? message : $"{message}: {ex}");

    static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LauncherPaths.DataDir);
                var path = LauncherPaths.LogFile;
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  LAUNCHER PATHS / LOG — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  %LOCALAPPDATA%\DeadCellsCoop: cache (архивы), launcher.log (≤ 1 МБ, затем .1), settings.json.
 *  Лог никогда не бросает исключений.
 * ═══════════════════════════════════════
*/
