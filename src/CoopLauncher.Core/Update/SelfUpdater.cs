using System.Diagnostics;
using CoopLauncher.Core.Install;
using CoopLauncher.Core.Manifest;

namespace CoopLauncher.Core.Update;

public static class SelfUpdater
{
    public const string OldSuffix = ".old";

    public static bool IsNewer(string? candidate, string current)
    {
        if (!TryParse(candidate, out var next) || !TryParse(current, out var now))
            return false;
        return next > now;
    }

    static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var core = value.Trim().TrimStart('v', 'V');
        var cut = core.IndexOfAny(['-', '+']);
        if (cut >= 0)
            core = core[..cut];
        if (!Version.TryParse(core, out var parsed))
            return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }

    public static void CleanupOldBinary()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe != null && File.Exists(exe + OldSuffix))
                File.Delete(exe + OldSuffix);
        }
        catch
        {
        }
    }

    public static async Task<string> DownloadAsync(PackageRef launcher, Downloader downloader, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var target = Path.Combine(LauncherPaths.CacheDir, $"launcher-{launcher.Version}.exe");
        return await downloader.DownloadVerifiedAsync(launcher.Url, launcher.Sha256, target, progress, ct).ConfigureAwait(false);
    }

    public static void ApplyAndRestart(string downloadedExe)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown launcher path");
        var old = exe + OldSuffix;
        if (File.Exists(old))
            File.Delete(old);

        File.Move(exe, old);
        try
        {
            File.Copy(downloadedExe, exe, overwrite: false);
        }
        catch
        {
            File.Move(old, exe);
            throw;
        }

        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
    }
}

/*
 * ═══════════════════════════════════════
 *  SELF UPDATER — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Версия из манифеста новее своей (сравнение числовой части, «v» и -суффикс отбрасываются) → обновление.
 *  Новый exe качается в кэш с проверкой sha256. Запущенный exe Windows позволяет переименовать:
 *  свой → .old, новый копируется на место, запуск нового, текущий процесс завершает вызывающий.
 *  Ошибка копирования → .old возвращается. Новый процесс при старте удаляет .old.
 * ═══════════════════════════════════════
*/
