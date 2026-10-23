using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoopLauncher.Core.Chooser;

public enum LaunchMode
{
    Ask,
    Coop,
    Vanilla
}

public sealed record ChooserSettings(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("language")] string Language)
{
    public static ChooserSettings Default { get; } = new("ask", "auto");

    [JsonIgnore]
    public LaunchMode LaunchMode => ParseMode(Mode);

    public ChooserSettings WithMode(LaunchMode mode) => this with { Mode = ModeToString(mode) };

    public ChooserSettings WithLanguage(string language) => this with { Language = NormalizeLanguage(language) };

    public static LaunchMode ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "coop" => LaunchMode.Coop,
        "vanilla" => LaunchMode.Vanilla,
        _ => LaunchMode.Ask
    };

    public static string ModeToString(LaunchMode mode) => mode switch
    {
        LaunchMode.Coop => "coop",
        LaunchMode.Vanilla => "vanilla",
        _ => "ask"
    };

    public static string NormalizeLanguage(string? value)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        return trimmed is "ru" or "en" ? trimmed : "auto";
    }
}

[JsonSerializable(typeof(ChooserSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
partial class ChooserSettingsJsonContext : JsonSerializerContext;

public static class ChooserSettingsStore
{
    public const string FileName = "settings.json";

    public static string PathIn(string chooserDir) => System.IO.Path.Combine(chooserDir, FileName);

    public static ChooserSettings Read(string chooserDir)
    {
        try
        {
            var path = PathIn(chooserDir);
            if (!File.Exists(path))
                return ChooserSettings.Default;
            var settings = JsonSerializer.Deserialize(File.ReadAllText(path), ChooserSettingsJsonContext.Default.ChooserSettings);
            if (settings == null)
                return ChooserSettings.Default;
            return new ChooserSettings(
                ChooserSettings.ModeToString(ChooserSettings.ParseMode(settings.Mode)),
                ChooserSettings.NormalizeLanguage(settings.Language));
        }
        catch
        {
            return ChooserSettings.Default;
        }
    }

    public static void Write(string chooserDir, ChooserSettings settings)
    {
        Directory.CreateDirectory(chooserDir);
        var path = PathIn(chooserDir);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, ChooserSettingsJsonContext.Default.ChooserSettings));
        File.Move(temp, path, overwrite: true);
    }
}

/*
 * ═══════════════════════════════════════
 *  CHOOSER SETTINGS — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  coremod/coop-chooser/settings.json — общий файл окна выбора и лаунчера:
 *  mode = ask | coop | vanilla (что запускать из Steam без вопроса), language = auto | ru | en.
 *  Битый/нет файла → ask + auto. Запись атомарная (tmp → rename). Мусорные значения нормализуются.
 * ═══════════════════════════════════════
*/
