using System.IO.Compression;
using System.Text;
using CoopLauncher.Core.Install;

namespace CoopLauncher.Core.Logs;

public static class LogCollector
{
    const int MaxGameLogs = 6;

    public static string Collect(InstallLayout? layout, string launcherVersion, string? gameBuildId, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var zipPath = Path.Combine(outputDir, $"DeadCellsCoop-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var temp = zipPath + ".tmp";

        using (var stream = File.Create(temp))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            AddText(zip, "info.txt", BuildInfo(layout, launcherVersion, gameBuildId));
            AddFile(zip, LauncherPaths.LogFile, "launcher/launcher.log");
            AddFile(zip, LauncherPaths.LogFile + ".1", "launcher/launcher.log.1");

            if (layout != null)
            {
                if (Directory.Exists(layout.LogsDir))
                {
                    var logs = new DirectoryInfo(layout.LogsDir)
                        .EnumerateFiles("*.log")
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .Take(MaxGameLogs);
                    foreach (var log in logs)
                        AddFile(zip, log.FullName, "game/" + log.Name);
                }

                AddFile(zip, layout.StateFile, "install/coop-launcher.json");
                AddFile(zip, layout.ModCoreVersionFile, "install/ModCoreVersion.txt");
                AddFile(zip, Path.Combine(layout.ModDir, "modinfo.json"), "install/modinfo.json");
                AddFile(zip, Path.Combine(layout.ChooserDir, "settings.json"), "install/chooser-settings.json");
            }
        }

        File.Move(temp, zipPath, overwrite: true);
        return zipPath;
    }

    static string BuildInfo(InstallLayout? layout, string launcherVersion, string? gameBuildId)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Launcher: {launcherVersion}");
        sb.AppendLine($"OS: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})");
        sb.AppendLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Collected: {DateTimeOffset.Now:O}");
        if (layout != null)
        {
            sb.AppendLine($"Game build: {gameBuildId ?? "?"}");
            sb.AppendLine($"deadcells.exe: {ExeInspector.Classify(layout.GameExe)}");
            sb.AppendLine($"deadcells.vanilla.exe: {ExeInspector.Classify(layout.VanillaExe)}");
            sb.AppendLine($"DCCM: {ModInstaller.ReadDccmVersion(layout) ?? "-"}");
            sb.AppendLine($"Mod: {ModInstaller.ReadModVersion(layout.ModDir) ?? "-"}");
        }
        return sb.ToString();
    }

    static void AddText(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    static void AddFile(ZipArchive zip, string path, string name)
    {
        try
        {
            if (!File.Exists(path))
                return;
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var target = entry.Open();
            source.CopyTo(target);
        }
        catch
        {
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  LOG COLLECTOR — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Один zip «DeadCellsCoop-logs-<дата>.zip»: info.txt (версии, ОС, билд игры, что в deadcells.exe),
 *  лог лаунчера, 6 свежих логов coremod/logs, файлы состояния установки и настроек окна выбора.
 *  Логи читаются с FileShare.ReadWrite — можно собирать при запущенной игре.
 * ═══════════════════════════════════════
*/
