using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using CoopLauncher.Core;
using CoopLauncher.Core.Chooser;
using CoopLauncher.Core.Install;
using CoopLauncher.Core.Manifest;

namespace CoopLauncher.App;

public sealed class ChooserSession(InstallLayout layout, ChooserSettings settings, Strings text, string args)
{
    public InstallLayout Layout { get; } = layout;
    public ChooserSettings Settings { get; } = settings;
    public Strings Text { get; } = text;
    public string Args { get; } = args;
    public LaunchMode? Choice { get; set; }
    public string? Notice { get; set; }
    public LauncherManifest? PendingUpdate { get; set; }

    public string? ModVersion => ModInstaller.ReadModVersion(Layout.ModDir);

    public string? CoopProblem()
    {
        if (ModInstaller.ReadDccmVersion(Layout) == null || ModVersion == null)
            return Text.ChooserModMissing;
        if (!Prerequisites.CheckDotNet().Installed || !Prerequisites.CheckVcRedist().Installed)
            return Text.ChooserNeedsRepair;
        return null;
    }

    public string? VanillaProblem() => File.Exists(Layout.VanillaExe) ? null : Text.ErrNoVanilla;

    public async Task<LauncherManifest?> FindUpdateAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var manifest = await ManifestLoader.FetchAsync(AppInfo.Http, ManifestLoader.DefaultUrl, cts.Token).ConfigureAwait(false);
            var inspection = await Task.Run(() => ModInstaller.Inspect(Layout), ct).ConfigureAwait(false);
            if (!inspection.NeedsDccm(manifest) && !inspection.NeedsMod(manifest))
                return null;
            LauncherLog.Info($"Chooser: update to mod {manifest.Mod.Version} / DCCM {manifest.Dccm.Version}");
            return manifest;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LauncherLog.Error("Chooser: manifest check failed, starting the installed version", ex);
            return null;
        }
    }

    public Task UpdateAsync(LauncherManifest manifest, IProgress<InstallProgress> progress, CancellationToken ct) =>
        Task.Run(() => ModInstaller.InstallAsync(Layout, manifest, new Downloader(AppInfo.Http), LauncherPaths.CacheDir, null, progress, ct));

    public void Remember(LaunchMode mode, bool remember)
    {
        var next = remember ? mode : LaunchMode.Ask;
        if (next == ChooserSettingsStore.Read(Layout.ChooserDir).LaunchMode)
            return;
        try
        {
            ChooserSettingsStore.Write(Layout.ChooserDir, Settings.WithMode(next));
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Chooser: could not save the launch mode", ex);
        }
    }

    public string Describe(Exception ex) => ex switch
    {
        GameRunningException => Text.ErrGameRunning,
        InstallException { Code: "no-vanilla" } => Text.ErrNoVanilla,
        InstallException { Code: "game-files" } => Text.ErrGameFiles,
        HttpRequestException or TaskCanceledException => Text.ErrNetwork,
        { InnerException: HttpRequestException } => Text.ErrNetwork,
        _ => string.Format(Text.ErrGeneric, ex.Message)
    };
}

static class ChooserLauncher
{
    const uint MB_ICONERROR = 0x10;
    const int VK_SHIFT = 0x10;
    static readonly TimeSpan SilentCheckTimeout = TimeSpan.FromSeconds(5);

    public static ChooserSession? Session { get; private set; }

    public static bool IsChooserProcess(string[] args) =>
        string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "deadcells", StringComparison.OrdinalIgnoreCase) ||
        args is [var first, ..] && string.Equals(first, ChooserPlan.ChooserFlag, StringComparison.OrdinalIgnoreCase);

    public static int Run()
    {
        var gameDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var layout = new InstallLayout(gameDir);
        var settings = ChooserSettingsStore.Read(layout.ChooserDir);
        var session = new ChooserSession(layout, settings, Strings.For(settings.Language), ChooserPlan.GameArguments(Environment.CommandLine));
        var start = ChooserPlan.Decide(session.Args, settings.LaunchMode, (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0);
        LauncherLog.Info($"Chooser {AppInfo.Version}: {start}, remembered {settings.LaunchMode}");

        if (start == ChooserStart.Vanilla)
        {
            session.Notice = session.VanillaProblem();
            if (session.Notice == null)
                return Launch(session, LaunchMode.Vanilla);
        }
        else if (start == ChooserStart.Coop)
        {
            session.Notice = session.CoopProblem();
            if (session.Notice == null)
            {
                session.PendingUpdate = session.FindUpdateAsync(SilentCheckTimeout, CancellationToken.None).GetAwaiter().GetResult();
                if (session.PendingUpdate == null)
                    return Launch(session, LaunchMode.Coop);
            }
        }

        Session = session;
        Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        return session.Choice is { } mode ? Launch(session, mode) : 0;
    }

    static int Launch(ChooserSession session, LaunchMode mode)
    {
        var layout = session.Layout;
        var target = mode == LaunchMode.Coop ? layout.ModdingExe : layout.VanillaExe;
        if (!File.Exists(target))
        {
            Fail(mode == LaunchMode.Coop ? session.Text.ChooserModMissing : session.Text.ErrNoVanilla, null);
            return 1;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(target)
            {
                Arguments = session.Args,
                UseShellExecute = false,
                WorkingDirectory = layout.GameDir
            });
            if (process == null)
                return 1;
            LauncherLog.Info($"Chooser: started {mode}, pid {process.Id}");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Fail(string.Format(session.Text.ErrGeneric, ex.Message), ex);
            return 1;
        }
    }

    static void Fail(string text, Exception? ex)
    {
        LauncherLog.Error("Chooser: " + text, ex);
        MessageBox(IntPtr.Zero, text, "Dead Cells", MB_ICONERROR);
    }

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);

    [DllImport("user32.dll", ExactSpelling = true)]
    static extern short GetAsyncKeyState(int key);
}

/*
 * ═══════════════════════════════════════
 *  CHOOSER LAUNCHER — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Тот же exe лаунчера, скопированный вместо deadcells.exe, — его запускает Steam по «Играть».
 *  Режим выбора включается, если процесс называется deadcells.exe (или первый аргумент --chooser).
 *  План (ChooserPlan): +connect_lobby → кооп без окна; Shift → окно; запомненный режим → сразу он; иначе окно.
 *  Обычная без окна: только проверка deadcells.vanilla.exe, без сети и без Avalonia.
 *  Кооп без окна: мод/DCCM/.NET/VC++ на месте → manifest.json (≤ 5 с, ошибка сети = играем как есть);
 *  нужно обновление → окно с прогрессом (PendingUpdate), иначе сразу запуск.
 *  Любая проблема → окно с понятным текстом (Notice). Обновление идёт без замены deadcells.exe
 *  (это наш же работающий процесс). Игра стартует с аргументами Steam как есть из корня игры,
 *  процесс ждёт её выхода (Steam держит «в игре» и оверлей). Сбой самого запуска — MessageBox.
 * ═══════════════════════════════════════
*/
