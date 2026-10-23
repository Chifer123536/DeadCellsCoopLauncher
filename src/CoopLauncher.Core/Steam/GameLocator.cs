using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CoopLauncher.Core.Steam;

public sealed record GameInstall(string GameDir, string LibraryDir, string BuildId, bool FullyInstalled);

public static class GameLocator
{
    public const uint AppId = 588650;
    public const string GameExeName = "deadcells.exe";

    [SupportedOSPlatform("windows")]
    public static string? FindSteamDir()
    {
        foreach (var (hive, path, value) in new[]
                 {
                     (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath")
                 })
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key?.GetValue(value) is string dir && Directory.Exists(dir))
                    return Path.GetFullPath(dir.Replace('/', Path.DirectorySeparatorChar));
            }
            catch
            {
            }
        }

        return null;
    }

    public static IReadOnlyList<string> ReadLibraries(string steamDir)
    {
        var libraries = new List<string> { steamDir };
        var vdf = Path.Combine(steamDir, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            return libraries;

        VdfNode root;
        try
        {
            root = VdfReader.ParseFile(vdf);
        }
        catch
        {
            return libraries;
        }

        var folders = root.Child("libraryfolders") ?? root.Child("LibraryFolders");
        if (folders == null)
            return libraries;

        foreach (var (_, node) in folders.Children)
        {
            var path = node.Value("path");
            if (string.IsNullOrWhiteSpace(path))
                continue;
            var full = Path.GetFullPath(path);
            if (!libraries.Contains(full, StringComparer.OrdinalIgnoreCase))
                libraries.Add(full);
        }

        foreach (var (key, value) in folders.Values)
        {
            if (int.TryParse(key, out _) && Directory.Exists(value))
            {
                var full = Path.GetFullPath(value);
                if (!libraries.Contains(full, StringComparer.OrdinalIgnoreCase))
                    libraries.Add(full);
            }
        }

        return libraries;
    }

    public static GameInstall? FindInLibraries(IEnumerable<string> libraries)
    {
        foreach (var library in libraries)
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
            if (!File.Exists(manifest))
                continue;

            VdfNode state;
            try
            {
                state = VdfReader.ParseFile(manifest).Child("AppState") ?? new VdfNode();
            }
            catch
            {
                continue;
            }

            var installDir = state.Value("installdir");
            if (string.IsNullOrWhiteSpace(installDir))
                continue;

            var gameDir = Path.Combine(library, "steamapps", "common", installDir);
            if (!File.Exists(Path.Combine(gameDir, GameExeName)))
                continue;

            var flags = int.TryParse(state.Value("StateFlags"), out var f) ? f : 0;
            return new GameInstall(gameDir, library, state.Value("buildid") ?? string.Empty, (flags & 4) != 0);
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    public static GameInstall? Locate()
    {
        var steam = FindSteamDir();
        return steam == null ? null : FindInLibraries(ReadLibraries(steam));
    }

    public static GameInstall? FromManualPath(string gameDir)
    {
        if (!File.Exists(Path.Combine(gameDir, GameExeName)))
            return null;

        var common = Directory.GetParent(gameDir);
        var steamapps = common?.Parent;
        var library = steamapps?.Parent?.FullName ?? gameDir;
        var buildId = string.Empty;
        if (steamapps != null)
        {
            var manifest = Path.Combine(steamapps.FullName, $"appmanifest_{AppId}.acf");
            try
            {
                if (File.Exists(manifest))
                    buildId = VdfReader.ParseFile(manifest).Child("AppState")?.Value("buildid") ?? string.Empty;
            }
            catch
            {
            }
        }

        return new GameInstall(Path.GetFullPath(gameDir), library, buildId, true);
    }
}

/*
 * ═══════════════════════════════════════
 *  GAME LOCATOR — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Steam: реестр HKCU SteamPath → HKLM InstallPath. Библиотеки: steamapps/libraryfolders.vdf
 *  (новый формат "N" { "path" } и старый "N" "path").
 *  Игра: appmanifest_588650.acf → installdir + buildid + StateFlags (бит 4 = установлена полностью),
 *  папка steamapps/common/<installdir> обязана содержать deadcells.exe.
 *  Ручной путь: проверка deadcells.exe, buildid из соседнего манифеста если есть.
 * ═══════════════════════════════════════
*/
