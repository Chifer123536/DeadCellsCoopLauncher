using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopLauncher.Core;
using CoopLauncher.Core.Chooser;
using CoopLauncher.Core.Install;
using CoopLauncher.Core.Manifest;

namespace CoopLauncher.App;

public sealed partial class ChooserViewModel : ObservableObject
{
    static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#C9CED6"));
    static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FF8A80"));
    static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(8);

    readonly ChooserSession _session;
    readonly CancellationTokenSource _cts = new();
    bool _skipUpdate;
    bool _closeRequested;
    bool _finished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlayCoop), nameof(Idle))]
    bool _busy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlayCoop))]
    bool _coopAvailable;

    [ObservableProperty] double _progress;
    [ObservableProperty] bool _progressIndeterminate = true;
    [ObservableProperty] string _message = "";
    [ObservableProperty] IBrush _messageBrush = InfoBrush;
    [ObservableProperty] bool _remember;
    [ObservableProperty] string _coopSubtitle = "";

    public ChooserViewModel(ChooserSession session)
    {
        _session = session;
        T = session.Text;
        Remember = session.Settings.LaunchMode != LaunchMode.Ask;
        PrefersVanilla = session.Settings.LaunchMode == LaunchMode.Vanilla;
        RefreshCoop();
        if (session.Notice != null)
            ShowError(session.Notice);
    }

    public Strings T { get; }
    public bool PrefersVanilla { get; }
    public Bitmap? Hero { get; } = SteamArt.Load("library_hero.jpg");
    public Bitmap? Logo { get; } = SteamArt.Load("logo.png");
    public bool HasLogo => Logo != null;
    public bool NoLogo => Logo == null;
    public bool CanPlayCoop => CoopAvailable && !Busy;
    public bool Idle => !Busy;
    public Action? CloseWindow { get; set; }

    public async Task StartAsync()
    {
        if (_session.PendingUpdate is not { } manifest)
            return;
        Busy = true;
        if (await UpdateAsync(manifest))
            Finish(LaunchMode.Coop);
        else
            EndBusy();
    }

    public bool RequestClose()
    {
        if (_finished || !Busy)
            return true;
        _closeRequested = true;
        _cts.Cancel();
        return false;
    }

    [RelayCommand]
    async Task PlayCoopAsync()
    {
        if (Busy)
            return;
        var problem = _session.CoopProblem();
        if (problem != null)
        {
            CoopAvailable = false;
            ShowError(problem);
            return;
        }

        _session.Remember(LaunchMode.Coop, Remember);
        if (!_skipUpdate)
        {
            Busy = true;
            Stage(T.ChooserChecking, null);
            LauncherManifest? manifest;
            try
            {
                manifest = await _session.FindUpdateAsync(CheckTimeout, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                manifest = null;
            }

            if (_closeRequested || (manifest != null && !await UpdateAsync(manifest)))
            {
                EndBusy();
                return;
            }
        }

        Finish(LaunchMode.Coop);
    }

    [RelayCommand]
    void PlayVanilla()
    {
        if (Busy)
            return;
        var problem = _session.VanillaProblem();
        if (problem != null)
        {
            ShowError(problem);
            return;
        }

        _session.Remember(LaunchMode.Vanilla, Remember);
        Finish(LaunchMode.Vanilla);
    }

    async Task<bool> UpdateAsync(LauncherManifest manifest)
    {
        try
        {
            Stage(T.StageChecking, null);
            await _session.UpdateAsync(manifest, new Progress<InstallProgress>(ReportInstall), _cts.Token);
            LauncherLog.Info($"Chooser: mod updated to {manifest.Mod.Version}");
            RefreshCoop();
            return !_closeRequested;
        }
        catch (Exception ex)
        {
            _skipUpdate = true;
            RefreshCoop();
            if (_closeRequested)
                return false;
            var text = string.Format(T.ChooserUpdateFailed, _session.Describe(ex));
            LauncherLog.Error(text, ex);
            ShowError(text);
            return false;
        }
    }

    void RefreshCoop()
    {
        var version = _session.ModVersion;
        CoopSubtitle = version == null ? T.ChooserCoopNone : string.Format(T.ChooserCoopSub, version);
        CoopAvailable = _session.CoopProblem() == null;
    }

    void Finish(LaunchMode mode)
    {
        _session.Choice = mode;
        _finished = true;
        Stage(T.ChooserStarting, null);
        CloseWindow?.Invoke();
    }

    void EndBusy()
    {
        Busy = false;
        if (!_closeRequested)
            return;
        _finished = true;
        CloseWindow?.Invoke();
    }

    void Stage(string text, double? fraction)
    {
        Message = text;
        MessageBrush = InfoBrush;
        ProgressIndeterminate = fraction == null;
        Progress = (fraction ?? 0) * 100;
    }

    void ShowError(string text)
    {
        Message = text;
        MessageBrush = ErrorBrush;
    }

    void ReportInstall(InstallProgress p)
    {
        if (_closeRequested || _finished)
            return;
        var text = p.Stage switch
        {
            InstallStage.Checking => T.StageChecking,
            InstallStage.DownloadingDccm => T.StageDccm,
            InstallStage.DownloadingMod => T.StageMod,
            InstallStage.Extracting => T.StageExtract,
            InstallStage.Replacing => T.StageReplace,
            InstallStage.ConfiguringExe => T.StageExe,
            _ => T.Done
        };
        if (p.Download is { } d)
        {
            var total = d.Total is > 0 ? $" / {d.Total.Value / 1048576.0:0.0}" : "";
            text = $"{text}  {d.Received / 1048576.0:0.0}{total} MB";
        }
        Stage(text, p.Fraction);
    }
}

/*
 * ═══════════════════════════════════════
 *  CHOOSER VIEW MODEL — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Окно Steam-запуска: «Играть в кооп» / «Обычная игра», «Запомнить выбор» (пишется в settings.json
 *  только при выборе: галка → режим, без галки → ask). Notice из ChooserLauncher — ошибка сразу при открытии.
 *  Кооп: мод/DCCM/.NET/VC++ на месте → manifest.json (≤ 8 с, сбой сети = играть как есть) → обновление
 *  с прогрессом → Choice = Coop и закрытие. Обновление не удалось → текст ошибки, повторное нажатие
 *  запускает установленную версию без новой попытки. PendingUpdate (кооп без окна) стартует сразу при открытии.
 *  Закрытие во время работы: отмена токена, окно закроется, когда установщик откатит/закончит замену файлов
 *  (процесс не убивается посреди FolderSwap).
 * ═══════════════════════════════════════
*/
