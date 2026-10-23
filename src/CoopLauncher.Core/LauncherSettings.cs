using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoopLauncher.Core;

public sealed record LauncherSettings(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("gameDir")] string? GameDir)
{
    public static LauncherSettings Default { get; } = new("auto", null);
}

[JsonSerializable(typeof(LauncherSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
partial class LauncherSettingsJsonContext : JsonSerializerContext;

public static class LauncherSettingsStore
{
    public static LauncherSettings Read()
    {
        try
        {
            if (!File.Exists(LauncherPaths.SettingsFile))
                return LauncherSettings.Default;
            return JsonSerializer.Deserialize(File.ReadAllText(LauncherPaths.SettingsFile), LauncherSettingsJsonContext.Default.LauncherSettings)
                   ?? LauncherSettings.Default;
        }
        catch
        {
            return LauncherSettings.Default;
        }
    }

    public static void Write(LauncherSettings settings)
    {
        try
        {
            Directory.CreateDirectory(LauncherPaths.DataDir);
            var temp = LauncherPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, LauncherSettingsJsonContext.Default.LauncherSettings));
            File.Move(temp, LauncherPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to save launcher settings", ex);
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  LAUNCHER SETTINGS — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  %LOCALAPPDATA%\DeadCellsCoop\settings.json: язык лаунчера (auto | ru | en) и путь к игре,
 *  если пользователь выбрал его вручную. Битый файл → значения по умолчанию.
 * ═══════════════════════════════════════
*/
