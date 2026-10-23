using System.Text.Json;
using CoopLauncher.Core.Manifest;

namespace CoopLauncher.Core.Install;

public enum InstallStage
{
    Checking,
    DownloadingDccm,
    DownloadingMod,
    Extracting,
    Replacing,
    ConfiguringExe,
    Done
}

public readonly record struct InstallProgress(InstallStage Stage, double? Fraction, DownloadProgress? Download = null);

public sealed record InstallInspection(
    bool GameRunning,
    string? DccmVersion,
    string? ModVersion,
    ExeKind Exe,
    bool HasVanillaBackup,
    IReadOnlyList<string> OtherMods,
    InstallState? State)
{
    public bool IsInstalled => DccmVersion != null && ModVersion != null;

    public bool ExeNeedsRepair => IsInstalled && Exe != ExeKind.Chooser;

    public bool NeedsDccm(LauncherManifest manifest) =>
        !string.Equals(DccmVersion, manifest.Dccm.Version, StringComparison.Ordinal);

    public bool NeedsMod(LauncherManifest manifest) =>
        !string.Equals(ModVersion, manifest.Mod.Version, StringComparison.Ordinal) ||
        !string.Equals(State?.ModSha256, manifest.Mod.Sha256, StringComparison.OrdinalIgnoreCase);

    public bool NeedsUpdate(LauncherManifest manifest) => NeedsDccm(manifest) || NeedsMod(manifest) || ExeNeedsRepair;
}

public sealed record UninstallResult(bool OriginalExeRestored);

public sealed class GameRunningException() : InvalidOperationException("Dead Cells is running");

public sealed class InstallException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class ModInstaller
{
    const string StagingName = ".coop-staging";

    public static InstallInspection Inspect(InstallLayout layout)
    {
        return new InstallInspection(
            GameProcess.IsRunning(layout),
            ReadDccmVersion(layout),
            ReadModVersion(layout.ModDir),
            ExeInspector.Classify(layout.GameExe),
            File.Exists(layout.VanillaExe),
            ListOtherMods(layout),
            InstallStateStore.Read(layout));
    }

    public static string? ReadDccmVersion(InstallLayout layout)
    {
        try
        {
            if (!File.Exists(layout.ModCoreVersionFile) || !File.Exists(layout.ModdingExe))
                return null;
            var text = File.ReadAllText(layout.ModCoreVersionFile).Trim();
            return text.Length == 0 ? null : text;
        }
        catch
        {
            return null;
        }
    }

    public static string? ReadModVersion(string modDir)
    {
        try
        {
            var info = Path.Combine(modDir, "modinfo.json");
            if (!File.Exists(info) || !File.Exists(Path.Combine(modDir, InstallLayout.ModFolderName + ".dll")))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(info));
            return doc.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    static IReadOnlyList<string> ListOtherMods(InstallLayout layout)
    {
        try
        {
            if (!Directory.Exists(layout.ModsDir))
                return [];
            return Directory.GetDirectories(layout.ModsDir)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name) &&
                               !string.Equals(name, InstallLayout.ModFolderName, StringComparison.OrdinalIgnoreCase) &&
                               !name.EndsWith(FolderSwap.BackupSuffix, StringComparison.OrdinalIgnoreCase))
                .Select(name => name!)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static async Task<InstallState> InstallAsync(
        InstallLayout layout,
        LauncherManifest manifest,
        Downloader downloader,
        string cacheDir,
        Func<Stream>? chooserSource,
        IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        progress?.Report(new InstallProgress(InstallStage.Checking, null));
        if (GameProcess.IsRunning(layout))
            throw new GameRunningException();
        if (!File.Exists(layout.GlExe))
            throw new InstallException("game-files", "deadcells_gl.exe is missing; verify the game files in Steam");
        if (chooserSource == null && ExeInspector.Classify(layout.VanillaExe) != ExeKind.Vanilla)
            throw new InstallException("no-vanilla", "The original deadcells.exe is missing; verify the game files in Steam");

        var before = Inspect(layout);
        var needDccm = before.NeedsDccm(manifest);
        var needMod = before.NeedsMod(manifest);
        Directory.CreateDirectory(cacheDir);

        string? dccmZip = null;
        if (needDccm)
        {
            dccmZip = await downloader.DownloadVerifiedAsync(
                manifest.Dccm.Url,
                manifest.Dccm.Sha256,
                Path.Combine(cacheDir, $"dccm-{SafeName(manifest.Dccm.Version)}.zip"),
                Map(progress, InstallStage.DownloadingDccm),
                ct).ConfigureAwait(false);
        }

        string? modZip = null;
        if (needMod)
        {
            modZip = await downloader.DownloadVerifiedAsync(
                manifest.Mod.Url,
                manifest.Mod.Sha256,
                Path.Combine(cacheDir, $"mod-{SafeName(manifest.Mod.Version)}.zip"),
                Map(progress, InstallStage.DownloadingMod),
                ct).ConfigureAwait(false);
        }

        var staging = Path.Combine(layout.CoreMod, StagingName);
        FolderSwap.DeletePath(staging);
        Directory.CreateDirectory(staging);
        try
        {
            progress?.Report(new InstallProgress(InstallStage.Extracting, 0));
            string? dccmRoot = null;
            if (dccmZip != null)
            {
                dccmRoot = Path.Combine(staging, "dccm");
                SafeZip.ExtractTo(dccmZip, dccmRoot, Fraction(progress, InstallStage.Extracting, 0, needMod ? 0.7 : 1), ct);
                dccmRoot = SafeZip.DescendSingleFolder(dccmRoot);
                if (!File.Exists(Path.Combine(dccmRoot, "core", "host", "startup", "DeadCellsModding.exe")) ||
                    !File.Exists(Path.Combine(dccmRoot, "ModCoreVersion.txt")))
                    throw new InstallException("bad-dccm", "The DCCM archive has an unexpected layout");
            }

            string? modRoot = null;
            if (modZip != null)
            {
                var raw = Path.Combine(staging, "mod");
                SafeZip.ExtractTo(modZip, raw, Fraction(progress, InstallStage.Extracting, needDccm ? 0.7 : 0, 1), ct);
                modRoot = SafeZip.DescendSingleFolder(raw);
                if (!string.Equals(ReadModVersion(modRoot), manifest.Mod.Version, StringComparison.Ordinal))
                    throw new InstallException("bad-mod", "The mod archive does not match the manifest version");
            }

            ct.ThrowIfCancellationRequested();
            if (GameProcess.IsRunning(layout))
                throw new GameRunningException();

            progress?.Report(new InstallProgress(InstallStage.Replacing, null));
            var swap = new FolderSwap();
            try
            {
                if (dccmRoot != null)
                {
                    swap.Replace(Path.Combine(dccmRoot, "core"), layout.CoreDir);
                    var plugins = Path.Combine(dccmRoot, "plugins");
                    if (Directory.Exists(plugins))
                        swap.Replace(plugins, layout.PluginsDir);
                    swap.Replace(Path.Combine(dccmRoot, "ModCoreVersion.txt"), layout.ModCoreVersionFile);
                }

                if (modRoot != null)
                    swap.Replace(modRoot, layout.ModDir);

                if (chooserSource != null)
                {
                    progress?.Report(new InstallProgress(InstallStage.ConfiguringExe, null));
                    EnsureChooser(layout, chooserSource);
                }
                swap.Commit();
            }
            catch
            {
                swap.Rollback();
                throw;
            }
        }
        finally
        {
            try { FolderSwap.DeletePath(staging); } catch { }
        }

        var state = new InstallState(
            manifest.Mod.Version,
            manifest.Mod.Sha256.ToLowerInvariant(),
            manifest.Dccm.Version,
            needDccm ? manifest.Dccm.Sha256.ToLowerInvariant() : before.State?.DccmSha256 ?? manifest.Dccm.Sha256.ToLowerInvariant(),
            await Downloader.HashFileAsync(layout.VanillaExe, ct).ConfigureAwait(false),
            await Downloader.HashFileAsync(File.Exists(layout.ChooserStub) ? layout.ChooserStub : layout.GameExe, ct).ConfigureAwait(false),
            DateTimeOffset.UtcNow);
        InstallStateStore.Write(layout, state);
        PruneCache(cacheDir, dccmZip ?? Path.Combine(cacheDir, $"dccm-{SafeName(manifest.Dccm.Version)}.zip"), modZip ?? Path.Combine(cacheDir, $"mod-{SafeName(manifest.Mod.Version)}.zip"));
        progress?.Report(new InstallProgress(InstallStage.Done, 1));
        return state;
    }

    public static void EnsureChooser(InstallLayout layout, Func<Stream> chooserSource)
    {
        switch (ExeInspector.Classify(layout.GameExe))
        {
            case ExeKind.Vanilla:
                CopyAtomic(layout.GameExe, layout.VanillaExe);
                break;
            case ExeKind.Missing:
            case ExeKind.DccmStub:
            case ExeKind.Chooser:
                if (ExeInspector.Classify(layout.VanillaExe) != ExeKind.Vanilla)
                    throw new InstallException("no-vanilla", "The original deadcells.exe is missing; verify the game files in Steam");
                break;
        }

        Directory.CreateDirectory(layout.ChooserDir);
        var temp = layout.ChooserStub + ".tmp";
        using (var source = chooserSource())
        using (var target = File.Create(temp))
            source.CopyTo(target);
        if (ExeInspector.Classify(temp) != ExeKind.Chooser)
        {
            File.Delete(temp);
            throw new InstallException("bad-chooser", "The bundled launch chooser is damaged");
        }
        File.Move(temp, layout.ChooserStub, overwrite: true);
        CopyAtomic(layout.ChooserStub, layout.GameExe);
    }

    public static UninstallResult Uninstall(InstallLayout layout)
    {
        if (GameProcess.IsRunning(layout))
            throw new GameRunningException();

        var restored = false;
        if (ExeInspector.Classify(layout.VanillaExe) == ExeKind.Vanilla)
        {
            File.Move(layout.VanillaExe, layout.GameExe, overwrite: true);
            restored = true;
        }
        else if (ExeInspector.Classify(layout.GameExe) == ExeKind.Vanilla)
        {
            restored = true;
        }

        if (Directory.Exists(layout.CoreMod))
            FolderSwap.DeletePath(layout.CoreMod);
        return new UninstallResult(restored);
    }

    static void CopyAtomic(string from, string to)
    {
        var temp = to + ".coop-tmp";
        File.Copy(from, temp, overwrite: true);
        File.Move(temp, to, overwrite: true);
    }

    static void PruneCache(string cacheDir, params string[] keep)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(cacheDir, "*.zip"))
            {
                if (!keep.Any(k => string.Equals(Path.GetFullPath(k), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)))
                    File.Delete(file);
            }
        }
        catch
        {
        }
    }

    static string SafeName(string version)
    {
        var chars = version.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }

    static IProgress<DownloadProgress>? Map(IProgress<InstallProgress>? progress, InstallStage stage)
    {
        if (progress == null)
            return null;
        return new SyncProgress<DownloadProgress>(d =>
            progress.Report(new InstallProgress(stage, d.Total is > 0 ? d.Received / (double)d.Total.Value : null, d)));
    }

    static IProgress<double>? Fraction(IProgress<InstallProgress>? progress, InstallStage stage, double from, double to)
    {
        if (progress == null)
            return null;
        var last = -1.0;
        return new SyncProgress<double>(f =>
        {
            var value = from + (to - from) * f;
            if (value - last < 0.01 && f < 1)
                return;
            last = value;
            progress.Report(new InstallProgress(stage, value));
        });
    }
}

sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

/*
 * ═══════════════════════════════════════
 *  MOD INSTALLER — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Inspect: версии DCCM (ModCoreVersion.txt + DeadCellsModding.exe) и мода (modinfo.json + dll),
 *  что лежит в deadcells.exe, есть ли бэкап оригинала, чужие моды в coremod/mods, запущена ли игра.
 *  Install (= обновление и починка):
 *    1. Игра должна быть закрыта; качаем только то, что отличается от манифеста (версия, у мода ещё sha).
 *    2. Архивы → coremod/.coop-staging (тот же диск), проверка раскладки и версии мода.
 *    3. FolderSwap: core, plugins, ModCoreVersion.txt, mods/DeadCellsMultiplayerMod. cache/config/data/logs
 *       и чужие моды не трогаются. Ошибка → откат всего.
 *    4. EnsureChooser: оригинальный exe (в т.ч. новый после апдейта игры/проверки Steam) → deadcells.vanilla.exe,
 *       окно выбора → coremod/coop-chooser/deadcells.exe и копией → deadcells.exe. Без оригинала не ставим.
 *       chooserSource = null (обновление из окна выбора, которое само работает как deadcells.exe) →
 *       exe не трогается, но оригинал в deadcells.vanilla.exe обязан быть.
 *    5. coop-launcher.json, чистка старых архивов в кэше.
 *  Uninstall: оригинал → deadcells.exe, удаление coremod. Оригинала нет → OriginalExeRestored=false
 *  (нужна «Проверка целостности» в Steam). Сохранения лежат в Steam userdata и не удаляются.
 * ═══════════════════════════════════════
*/
