using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoopLauncher.Core.Manifest;

public sealed record PackageRef(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("size")] long Size);

public sealed record LauncherManifest(
    [property: JsonPropertyName("schema")] int Schema,
    [property: JsonPropertyName("mod")] PackageRef Mod,
    [property: JsonPropertyName("dccm")] PackageRef Dccm,
    [property: JsonPropertyName("launcher")] PackageRef? Launcher,
    [property: JsonPropertyName("gameBuildIds")] string[]? GameBuildIds,
    [property: JsonPropertyName("notes")] string? Notes);

[JsonSerializable(typeof(LauncherManifest))]
[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, WriteIndented = true)]
partial class ManifestJsonContext : JsonSerializerContext;

public static class ManifestLoader
{
    public const int SupportedSchema = 1;
    public const string DefaultUrl = "https://raw.githubusercontent.com/Chifer123536/DeadCellsCoopLauncher/main/manifest.json";

    public static LauncherManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.LauncherManifest)
                       ?? throw new FormatException("Manifest is empty");
        Validate(manifest);
        return manifest;
    }

    public static async Task<LauncherManifest> FetchAsync(HttpClient http, string url, CancellationToken ct)
    {
        var bust = url.Contains('?') ? "&" : "?";
        var json = await http.GetStringAsync($"{url}{bust}t={DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60}", ct).ConfigureAwait(false);
        return Parse(json);
    }

    static void Validate(LauncherManifest manifest)
    {
        if (manifest.Schema != SupportedSchema)
            throw new FormatException($"Manifest schema {manifest.Schema} is not supported (need {SupportedSchema}); update the launcher");
        ValidatePackage(manifest.Mod, "mod");
        ValidatePackage(manifest.Dccm, "dccm");
        if (manifest.Launcher != null)
            ValidatePackage(manifest.Launcher, "launcher");
    }

    static void ValidatePackage(PackageRef? package, string name)
    {
        if (package == null)
            throw new FormatException($"Manifest has no '{name}' entry");
        if (string.IsNullOrWhiteSpace(package.Version))
            throw new FormatException($"Manifest '{name}' has no version");
        if (!Uri.TryCreate(package.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new FormatException($"Manifest '{name}' url must be https");
        if (package.Sha256 is not { Length: 64 } || !package.Sha256.All(Uri.IsHexDigit))
            throw new FormatException($"Manifest '{name}' sha256 is invalid");
    }
}

/*
 * ═══════════════════════════════════════
 *  LAUNCHER MANIFEST — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  manifest.json в ветке main репозитория лаунчера: версии + https-ссылки + sha256 для мода, DCCM и самого лаунчера.
 *  Схема проверяется строго: чужая схема, http, кривой sha256 → FormatException (ничего не ставим).
 *  Запрос с ?t=<минута> — обход кэша raw.githubusercontent (~5 мин).
 *  Source-generated JSON — совместимо с trimming.
 * ═══════════════════════════════════════
*/
