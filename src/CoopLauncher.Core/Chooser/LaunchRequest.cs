using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoopLauncher.Core.Chooser;

public sealed record LaunchRequest(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("utc")] long UtcSeconds);

[JsonSerializable(typeof(LaunchRequest))]
partial class LaunchRequestJsonContext : JsonSerializerContext;

public static class LaunchRequestStore
{
    public const string FileName = "next-launch.json";
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);
    static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(5);

    public static string PathIn(string chooserDir) => Path.Combine(chooserDir, FileName);

    public static void Write(string chooserDir, LaunchMode mode, DateTimeOffset now)
    {
        if (mode == LaunchMode.Ask)
            throw new ArgumentOutOfRangeException(nameof(mode), "A launch request needs a concrete mode");
        Directory.CreateDirectory(chooserDir);
        var path = PathIn(chooserDir);
        var temp = path + ".tmp";
        var request = new LaunchRequest(ChooserSettings.ModeToString(mode), now.ToUnixTimeSeconds());
        File.WriteAllText(temp, JsonSerializer.Serialize(request, LaunchRequestJsonContext.Default.LaunchRequest));
        File.Move(temp, path, overwrite: true);
    }

    public static LaunchMode? Take(string chooserDir, DateTimeOffset now)
    {
        var path = PathIn(chooserDir);
        string text;
        try
        {
            if (!File.Exists(path))
                return null;
            text = File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }

        try
        {
            var request = JsonSerializer.Deserialize(text, LaunchRequestJsonContext.Default.LaunchRequest);
            if (request == null)
                return null;
            var age = now - DateTimeOffset.FromUnixTimeSeconds(request.UtcSeconds);
            if (age < -ClockSkew || age > MaxAge)
                return null;
            var mode = ChooserSettings.ParseMode(request.Mode);
            return mode == LaunchMode.Ask ? null : mode;
        }
        catch
        {
            return null;
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  LAUNCH REQUEST — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  coremod/coop-chooser/next-launch.json — одноразовая передача выбора «кооп / обычная» из лаунчера
 *  в окно Steam-запуска: лаунчер пишет файл и запускает игру через Steam, deadcells.exe забирает и удаляет его.
 *  Файл удаляется при любом чтении (даже битый). Старше 2 мин или «из будущего» > 5 с → игнор.
 *  Запись атомарная (tmp → rename).
 * ═══════════════════════════════════════
*/
