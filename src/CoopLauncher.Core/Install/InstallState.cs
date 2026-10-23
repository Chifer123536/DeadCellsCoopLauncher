using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoopLauncher.Core.Install;

public sealed record InstallState(
    [property: JsonPropertyName("modVersion")] string ModVersion,
    [property: JsonPropertyName("modSha256")] string ModSha256,
    [property: JsonPropertyName("dccmVersion")] string DccmVersion,
    [property: JsonPropertyName("dccmSha256")] string DccmSha256,
    [property: JsonPropertyName("vanillaExeSha256")] string VanillaExeSha256,
    [property: JsonPropertyName("chooserSha256")] string ChooserSha256,
    [property: JsonPropertyName("installedAt")] DateTimeOffset InstalledAt);

[JsonSerializable(typeof(InstallState))]
[JsonSourceGenerationOptions(WriteIndented = true)]
partial class InstallStateJsonContext : JsonSerializerContext;

public static class InstallStateStore
{
    public static InstallState? Read(InstallLayout layout)
    {
        try
        {
            if (!File.Exists(layout.StateFile))
                return null;
            return JsonSerializer.Deserialize(File.ReadAllText(layout.StateFile), InstallStateJsonContext.Default.InstallState);
        }
        catch
        {
            return null;
        }
    }

    public static void Write(InstallLayout layout, InstallState state)
    {
        Directory.CreateDirectory(layout.CoreMod);
        var temp = layout.StateFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, InstallStateJsonContext.Default.InstallState));
        File.Move(temp, layout.StateFile, overwrite: true);
    }
}

/*
 * ═══════════════════════════════════════
 *  INSTALL STATE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  coremod/coop-launcher.json: какие версии мода/DCCM стоят (+sha256 архивов), хэш оригинального
 *  deadcells.exe и окна выбора. Пишется атомарно (tmp → rename). Битый/нет файла → null (= не установлено).
 * ═══════════════════════════════════════
*/
