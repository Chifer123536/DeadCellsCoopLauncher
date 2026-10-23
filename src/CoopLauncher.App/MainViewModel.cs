using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopLauncher.Core;
using CoopLauncher.Core.Chooser;
using CoopLauncher.Core.Install;
using CoopLauncher.Core.Logs;
using CoopLauncher.Core.Manifest;
using CoopLauncher.Core.Steam;
using CoopLauncher.Core.Update;

namespace CoopLauncher.App;

public sealed record StepTile(string Number, string Text);

public sealed record StatusLine(string Title, string Value, bool Ok)
{
    public string Label => Title.ToUpperInvariant();

    static readonly IBrush Good = new SolidColorBrush(Color.Parse("#6CC070"));
    static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#D9534F"));

    public IBrush Dot => Ok ? Good : Bad;
}

enum PrimaryAction
{
    ChooseFolder,
    CloseGame,
    Install,
    Update,
    Repair,
    Play
}

public sealed partial class MainViewModel : ObservableObject
{
    static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#8FD18F"));
    static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#E06C64"));
    static HttpClient Http => AppInfo.Http;

    LauncherSettings _settings = LauncherSettings.Default;
    GameInstall? _game;
    InstallLayout? _layout;
    InstallInspection? _inspection;
    LauncherManifest? _manifest;
    bool _manifestFailed;
    PrimaryAction _action;
    DateTime _uninstallArmedUntil;
    bool _suppressModeWrite;
    readonly DispatcherTimer _timer;

    [ObservableProperty] Strings _t = Strings.En;
    [ObservableProperty] string _primaryText = "";
    [ObservableProperty] bool _primaryEnabled;
    [ObservableProperty] bool _busy;
    [ObservableProperty] double _progress;
    [ObservableProperty] bool _progressIndeterminate = true;
    [ObservableProperty] string _message = "";
    [ObservableProperty] IBrush _messageBrush = InfoBrush;
    [ObservableProperty] string _warning = "";
    [ObservableProperty] int _launchModeIndex;
    [ObservableProperty] bool _modeEnabled;
    [ObservableProperty] string _uninstallText = "";
    [ObservableProperty] bool _canUninstall;
    [ObservableProperty] string _launcherUpdateText = "";
    [ObservableProperty] bool _launcherUpdateVisible;
    [ObservableProperty] string _gamePath = "";
    [ObservableProperty] string[] _launchModes = [];
    [ObservableProperty] StepTile[] _steps = [];
    [ObservableProperty] bool _isRu;
    [ObservableProperty] bool _playVisible;

    public ObservableCollection<StatusLine> Statuses { get; } = [];
    public string Version => AppInfo.Version;
    public Func<Task<string?>>? PickFolder { get; set; }
    public Action? Shutdown { get; set; }
    public bool HasWarning => Warning.Length > 0;
    public Avalonia.Media.Imaging.Bitmap? Hero { get; } = SteamArt.Load("library_hero.jpg");
    public Avalonia.Media.Imaging.Bitmap? Logo { get; } = SteamArt.Load("logo.png");
    public bool HasLogo => Logo != null;
    public bool NoLogo => Logo == null;

    public MainViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) =>
        {
            if (!Busy)
                await RefreshAsync(fetchManifest: false);
        };
        _ = InitializeAsync();
    }

    partial void OnWarningChanged(string value) => OnPropertyChanged(nameof(HasWarning));

    async Task InitializeAsync()
    {
        SelfUpdater.CleanupOldBinary();
        _settings = LauncherSettingsStore.Read();
        ApplyLanguage(Strings.For(_settings.Language));
        LauncherLog.Info($"Launcher {Version} started");
        await RefreshAsync(fetchManifest: true);
        await RefreshChooserCopyAsync();
        _timer.Start();
    }

    async Task RefreshChooserCopyAsync()
    {
        var layout = _layout;
        var inspection = _inspection;
        if (layout == null || inspection == null || inspection.GameRunning || inspection.Exe != ExeKind.Chooser)
            return;
        if (ExeInspector.ChooserVersion(layout.GameExe) == Version)
            return;
        try
        {
            await Task.Run(() => ModInstaller.EnsureChooser(layout, OpenChooser));
            LauncherLog.Info($"Steam chooser refreshed to {Version}");
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Steam chooser refresh failed", ex);
        }
    }

    void ApplyLanguage(Strings strings)
    {
        _suppressModeWrite = true;
        T = strings;
        LaunchModes = [strings.ModeAsk, strings.ModeCoop, strings.ModeVanilla];
        Steps = [new("1", strings.How1), new("2", strings.How2), new("3", strings.How3), new("4", strings.How4)];
        IsRu = strings.Code == "ru";
        _suppressModeWrite = false;
    }

    async Task RefreshAsync(bool fetchManifest)
    {
        LocateGame();
        if (fetchManifest)
            await FetchManifestAsync();
        var layout = _layout;
        _inspection = layout == null ? null : await Task.Run(() => ModInstaller.Inspect(layout));
        RefreshView();
    }

    void LocateGame()
    {
        _game = null;
        if (!string.IsNullOrWhiteSpace(_settings.GameDir))
            _game = GameLocator.FromManualPath(_settings.GameDir);
        _game ??= GameLocator.Locate();
        _layout = _game == null ? null : new InstallLayout(_game.GameDir);
    }

    async Task FetchManifestAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _manifest = await ManifestLoader.FetchAsync(Http, ManifestLoader.DefaultUrl, cts.Token);
            _manifestFailed = false;
        }
        catch (Exception ex)
        {
            _manifestFailed = true;
            LauncherLog.Error("Manifest fetch failed", ex);
        }
    }

    void RefreshView()
    {
        var t = T;
        var dotnet = Prerequisites.CheckDotNet();
        var vc = Prerequisites.CheckVcRedist();
        var inspection = _inspection;

        Statuses.Clear();
        Statuses.Add(_game == null
            ? new StatusLine(t.Game, t.NotFound, false)
            : new StatusLine(t.Game, string.IsNullOrEmpty(_game.BuildId) ? t.Installed : _game.BuildId, true));
        Statuses.Add(new StatusLine(t.DotNet, dotnet.Installed ? dotnet.Detail : t.Missing, dotnet.Installed));
        Statuses.Add(new StatusLine(t.VcRedist, vc.Installed ? ShortVersion(vc.Detail) : t.Missing, vc.Installed));

        var needsMod = inspection != null && _manifest != null && (inspection.NeedsDccm(_manifest) || inspection.NeedsMod(_manifest));
        if (inspection?.IsInstalled == true)
            Statuses.Add(needsMod
                ? new StatusLine(t.Mod, string.Format(t.UpdateTo, _manifest!.Mod.Version), false)
                : new StatusLine(t.Mod, inspection.ModVersion!, !inspection.ExeNeedsRepair));
        else
            Statuses.Add(new StatusLine(t.Mod, t.NotInstalled, false));

        GamePath = _game?.GameDir ?? "";
        Warning = _manifestFailed
            ? t.Offline
            : _game != null && _manifest?.GameBuildIds is { Length: > 0 } ids && !ids.Contains(_game.BuildId)
                ? string.Format(t.BuildWarning, _game.BuildId)
                : "";

        var prereqsOk = dotnet.Installed && vc.Installed;
        _action = _game == null ? PrimaryAction.ChooseFolder
            : inspection?.GameRunning == true ? PrimaryAction.CloseGame
            : inspection?.IsInstalled != true ? PrimaryAction.Install
            : needsMod ? PrimaryAction.Update
            : !prereqsOk || inspection.ExeNeedsRepair ? PrimaryAction.Repair
            : PrimaryAction.Play;

        PrimaryText = _action switch
        {
            PrimaryAction.ChooseFolder => t.ChooseFolder,
            PrimaryAction.CloseGame => t.CloseGame,
            PrimaryAction.Install => t.Install,
            PrimaryAction.Update => t.Update,
            PrimaryAction.Repair => t.Repair,
            _ => t.Play
        };
        if (Busy)
            PrimaryText = t.Working;
        PlayVisible = _action == PrimaryAction.Play;
        PrimaryEnabled = !Busy && _action switch
        {
            PrimaryAction.CloseGame => false,
            PrimaryAction.Install or PrimaryAction.Update => _manifest != null,
            _ => true
        };

        ModeEnabled = !Busy && inspection?.IsInstalled == true;
        if (_layout != null)
        {
            _suppressModeWrite = true;
            LaunchModeIndex = (int)ChooserSettingsStore.Read(_layout.ChooserDir).LaunchMode;
            _suppressModeWrite = false;
        }

        if (DateTime.UtcNow > _uninstallArmedUntil)
            UninstallText = t.Uninstall;
        CanUninstall = !Busy && _layout != null && Directory.Exists(_layout.CoreMod);

        var launcher = _manifest?.Launcher;
        LauncherUpdateVisible = !Busy && launcher != null && SelfUpdater.IsNewer(launcher.Version, Version);
        LauncherUpdateText = launcher == null ? "" : string.Format(t.LauncherUpdate, launcher.Version);
    }

    partial void OnLaunchModeIndexChanged(int value)
    {
        if (_suppressModeWrite || value < 0 || _layout == null || _inspection?.IsInstalled != true)
            return;
        try
        {
            var current = ChooserSettingsStore.Read(_layout.ChooserDir);
            ChooserSettingsStore.Write(_layout.ChooserDir, current.WithMode((LaunchMode)value).WithLanguage(T.Code));
        }
        catch (Exception ex)
        {
            ShowError(string.Format(T.ErrGeneric, ex.Message), ex);
        }
    }

    static string ShortVersion(string value)
    {
        var parts = value.TrimStart('v', 'V').Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : value;
    }

    [RelayCommand]
    void SetLanguage(string code)
    {
        if (code == T.Code)
            return;
        var next = code == "ru" ? Strings.Ru : Strings.En;
        ApplyLanguage(next);
        _settings = _settings with { Language = next.Code };
        LauncherSettingsStore.Write(_settings);
        if (_layout != null && _inspection?.IsInstalled == true)
        {
            try
            {
                var current = ChooserSettingsStore.Read(_layout.ChooserDir);
                ChooserSettingsStore.Write(_layout.ChooserDir, current.WithLanguage(next.Code));
            }
            catch
            {
            }
        }
        Message = "";
        RefreshView();
    }

    [RelayCommand]
    async Task PrimaryAsync()
    {
        switch (_action)
        {
            case PrimaryAction.ChooseFolder:
                await ChooseFolderAsync();
                break;
            case PrimaryAction.Play:
                LaunchViaSteam(LaunchMode.Coop);
                break;
            case PrimaryAction.Install:
            case PrimaryAction.Update:
            case PrimaryAction.Repair:
                await RunInstallAsync();
                break;
        }
    }

    [RelayCommand]
    void PlayCoop() => LaunchViaSteam(LaunchMode.Coop);

    [RelayCommand]
    void PlayVanilla() => LaunchViaSteam(LaunchMode.Vanilla);

    void LaunchViaSteam(LaunchMode mode)
    {
        var layout = _layout;
        if (layout == null || Busy || _action != PrimaryAction.Play)
            return;
        try
        {
            LaunchRequestStore.Write(layout.ChooserDir, mode, DateTimeOffset.UtcNow);
            Process.Start(new ProcessStartInfo("steam://rungameid/" + GameLocator.AppId) { UseShellExecute = true });
            LauncherLog.Info($"Launch {mode} via Steam");
            ShowInfo(T.LaunchingSteam);
        }
        catch (Exception ex)
        {
            ShowError(string.Format(T.ErrGeneric, ex.Message), ex);
        }
    }

    [RelayCommand]
    async Task ChooseFolderAsync()
    {
        if (PickFolder == null)
            return;
        var path = await PickFolder();
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (GameLocator.FromManualPath(path) == null)
        {
            ShowError(T.ErrWrongFolder);
            return;
        }
        _settings = _settings with { GameDir = path };
        LauncherSettingsStore.Write(_settings);
        Message = "";
        await RefreshAsync(fetchManifest: false);
    }

    async Task RunInstallAsync()
    {
        var layout = _layout;
        if (layout == null)
            return;

        SetBusy(true);
        Message = "";
        var t = T;
        try
        {
            var downloadProgress = new Progress<DownloadProgress>(d => ReportDownload(d));
            if (!Prerequisites.CheckDotNet().Installed)
            {
                Stage(t.StageDotNet, null);
                await Prerequisites.InstallAsync(PrerequisiteKind.DotNetRuntime, Http, LauncherPaths.CacheDir, downloadProgress, CancellationToken.None);
            }
            if (!Prerequisites.CheckVcRedist().Installed)
            {
                Stage(t.StageVc, null);
                await Prerequisites.InstallAsync(PrerequisiteKind.VcRedist, Http, LauncherPaths.CacheDir, downloadProgress, CancellationToken.None);
            }

            var manifest = _manifest;
            if (manifest != null)
            {
                var progress = new Progress<InstallProgress>(ReportInstall);
                var downloader = new Downloader(Http);
                await Task.Run(() => ModInstaller.InstallAsync(layout, manifest, downloader, LauncherPaths.CacheDir, OpenChooser, progress, CancellationToken.None));
            }
            else
            {
                Stage(t.StageExe, null);
                await Task.Run(() => ModInstaller.EnsureChooser(layout, OpenChooser));
            }

            LauncherLog.Info("Install/repair finished");
            ShowInfo(t.Done);
        }
        catch (GameRunningException)
        {
            ShowError(t.ErrGameRunning);
        }
        catch (InstallException ex) when (ex.Code == "no-vanilla")
        {
            ShowError(t.ErrNoVanilla, ex);
        }
        catch (InstallException ex) when (ex.Code == "game-files")
        {
            ShowError(t.ErrGameFiles, ex);
        }
        catch (OperationCanceledException ex)
        {
            ShowError(t.ErrUac, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex.InnerException is HttpRequestException || ex is TaskCanceledException)
        {
            ShowError(t.ErrNetwork, ex);
        }
        catch (Exception ex)
        {
            ShowError(string.Format(t.ErrGeneric, ex.Message), ex);
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync(fetchManifest: false);
        }
    }

    [RelayCommand]
    async Task CollectLogsAsync()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var layout = _layout;
            var build = _game?.BuildId;
            var zip = await Task.Run(() => LogCollector.Collect(layout, Version, build, desktop));
            ShowInfo(string.Format(T.LogsSaved, Path.GetFileName(zip)));
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            ShowError(string.Format(T.ErrGeneric, ex.Message), ex);
        }
    }

    [RelayCommand]
    async Task UninstallAsync()
    {
        var layout = _layout;
        if (layout == null)
            return;

        if (DateTime.UtcNow > _uninstallArmedUntil)
        {
            _uninstallArmedUntil = DateTime.UtcNow.AddSeconds(6);
            UninstallText = T.UninstallConfirm;
            if (_inspection?.OtherMods is { Count: > 0 } others)
                Warning = string.Format(T.OtherModsWarning, string.Join(", ", others));
            return;
        }

        _uninstallArmedUntil = DateTime.MinValue;
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => ModInstaller.Uninstall(layout));
            LauncherLog.Info($"Uninstalled, original exe restored: {result.OriginalExeRestored}");
            ShowInfo(result.OriginalExeRestored ? T.Uninstalled : T.UninstalledVerify);
        }
        catch (GameRunningException)
        {
            ShowError(T.ErrGameRunning);
        }
        catch (Exception ex)
        {
            ShowError(string.Format(T.ErrGeneric, ex.Message), ex);
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync(fetchManifest: false);
        }
    }

    [RelayCommand]
    async Task UpdateLauncherAsync()
    {
        var launcher = _manifest?.Launcher;
        if (launcher == null)
            return;
        SetBusy(true);
        try
        {
            Stage(T.LauncherUpdateButton, null);
            var file = await SelfUpdater.DownloadAsync(launcher, new Downloader(Http), new Progress<DownloadProgress>(ReportDownload), CancellationToken.None);
            SelfUpdater.ApplyAndRestart(file);
            Shutdown?.Invoke();
        }
        catch (Exception ex)
        {
            ShowError(ex is HttpRequestException ? T.ErrNetwork : string.Format(T.ErrGeneric, ex.Message), ex);
            SetBusy(false);
            RefreshView();
        }
    }

    void SetBusy(bool busy)
    {
        Busy = busy;
        Progress = 0;
        ProgressIndeterminate = true;
        RefreshView();
    }

    void Stage(string text, double? fraction)
    {
        Message = text;
        MessageBrush = InfoBrush;
        ProgressIndeterminate = fraction == null;
        Progress = (fraction ?? 0) * 100;
    }

    void ReportDownload(DownloadProgress d)
    {
        var current = Message;
        var cut = current.IndexOf("  ", StringComparison.Ordinal);
        var head = cut >= 0 ? current[..cut] : current;
        var total = d.Total is > 0 ? $" / {d.Total.Value / 1048576.0:0.0}" : "";
        Message = $"{head}  {d.Received / 1048576.0:0.0}{total} MB";
        ProgressIndeterminate = d.Total is not > 0;
        Progress = d.Total is > 0 ? d.Received * 100.0 / d.Total.Value : 0;
    }

    void ReportInstall(InstallProgress p)
    {
        var t = T;
        var text = p.Stage switch
        {
            InstallStage.Checking => t.StageChecking,
            InstallStage.DownloadingDccm => t.StageDccm,
            InstallStage.DownloadingMod => t.StageMod,
            InstallStage.Extracting => t.StageExtract,
            InstallStage.Replacing => t.StageReplace,
            InstallStage.ConfiguringExe => t.StageExe,
            _ => t.Done
        };
        Stage(text, p.Fraction);
        if (p.Download is { } download)
            ReportDownload(download);
    }

    void ShowInfo(string text)
    {
        Message = text;
        MessageBrush = InfoBrush;
    }

    void ShowError(string text, Exception? ex = null)
    {
        Message = text;
        MessageBrush = ErrorBrush;
        LauncherLog.Error(text, ex);
    }

    static Stream OpenChooser()
    {
        var exe = Environment.ProcessPath;
        if (exe == null || File.Exists(Path.ChangeExtension(exe, ".dll")))
            throw new InvalidOperationException("Run the published single-file launcher to install the Steam chooser");
        return new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }
}

/*
 * ═══════════════════════════════════════
 *  MAIN VIEW MODEL — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Старт: чистка .old после самообновления, язык (настройки → язык Windows), поиск игры
 *  (ручной путь → Steam), манифест (15 с), осмотр установки. Дальше переосмотр раз в 3 с, пока не занято.
 *  Главная кнопка по состоянию: нет игры → выбрать папку; игра запущена → «Закройте игру»;
 *  мода нет → Установить; манифест новее → Обновить; нет .NET/VC++ или exe сброшен Steam → Починить;
 *  иначе две кнопки «Играть в кооп» / «Обычная игра»: выбор пишется в next-launch.json и игра стартует через
 *  steam://rungameid (первый вариант запуска Steam = deadcells.exe), окно выбора забирает его и не спрашивает снова.
 *  Установка: .NET и VC++ (UAC), затем ModInstaller в фоне; без манифеста — только починка exe.
 *  Режим запуска и язык пишутся в coremod/coop-chooser/settings.json (их читает окно выбора в Steam).
 *  Удаление — двойным нажатием за 6 с, с предупреждением о чужих модах.
 *  Ошибки → понятный текст в окне + полный стек в launcher.log.
 * ═══════════════════════════════════════
*/
