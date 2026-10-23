using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using CoopLauncher.Core.Chooser;
using CoopLauncher.Core.Install;

namespace CoopLauncher.App;

public sealed class ChooserContext(InstallLayout layout, ChooserSettings settings, Strings text)
{
    public InstallLayout Layout { get; } = layout;
    public ChooserSettings Settings { get; } = settings;
    public Strings Text { get; } = text;
    public LaunchMode? Choice { get; set; }
}

static class ChooserLauncher
{
    const uint MB_YESNO = 0x4;
    const uint MB_ICONWARNING = 0x30;
    const uint MB_ICONERROR = 0x10;
    const int IDYES = 6;
    const int VK_SHIFT = 0x10;

    public static ChooserContext? Context { get; private set; }

    public static bool IsChooserProcess(string[] args) =>
        string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "deadcells", StringComparison.OrdinalIgnoreCase) ||
        args.Contains("--chooser", StringComparer.OrdinalIgnoreCase);

    public static int Run()
    {
        var gameDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var layout = new InstallLayout(gameDir);
        var settings = ChooserSettingsStore.Read(layout.ChooserDir);
        var text = Strings.For(settings.Language);
        var args = RawArguments(Environment.CommandLine).Replace("--chooser", "", StringComparison.OrdinalIgnoreCase).Trim();

        LaunchMode? mode = null;
        if (args.Contains("+connect_lobby", StringComparison.OrdinalIgnoreCase))
            mode = LaunchMode.Coop;
        else if (settings.LaunchMode != LaunchMode.Ask && (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0)
            mode = settings.LaunchMode;

        if (mode == null)
        {
            Context = new ChooserContext(layout, settings, text);
            Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
            mode = Context.Choice;
            if (mode == null)
                return 0;
        }

        return Launch(layout, mode.Value, args, text);
    }

    public static void Remember(ChooserContext context, LaunchMode mode, bool always)
    {
        var next = always ? mode : LaunchMode.Ask;
        if (next == context.Settings.LaunchMode)
            return;
        try
        {
            ChooserSettingsStore.Write(context.Layout.ChooserDir, context.Settings.WithMode(next));
        }
        catch
        {
        }
    }

    static int Launch(InstallLayout layout, LaunchMode mode, string args, Strings text)
    {
        var target = mode == LaunchMode.Coop ? layout.ModdingExe : layout.VanillaExe;
        if (mode == LaunchMode.Coop && !File.Exists(target))
        {
            if (MessageBox(IntPtr.Zero, text.ChooserModMissing, "Dead Cells", MB_YESNO | MB_ICONWARNING) != IDYES)
                return 1;
            target = layout.VanillaExe;
        }

        if (!File.Exists(target))
        {
            MessageBox(IntPtr.Zero, text.ErrNoVanilla, "Dead Cells", MB_ICONERROR);
            return 1;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(target)
            {
                Arguments = args,
                UseShellExecute = false,
                WorkingDirectory = layout.GameDir
            });
            if (process == null)
                return 1;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, ex.Message, "Dead Cells", MB_ICONERROR);
            return 1;
        }
    }

    internal static string RawArguments(string commandLine)
    {
        var line = commandLine.TrimStart();
        if (line.Length == 0)
            return string.Empty;

        int end;
        if (line[0] == '"')
        {
            end = line.IndexOf('"', 1);
            end = end < 0 ? line.Length : end + 1;
        }
        else
        {
            end = 0;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
                end++;
        }

        return line[end..].Trim();
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
 *  Режим окна выбора включается, если процесс называется deadcells.exe (или есть --chooser).
 *  +connect_lobby в аргументах (инвайт / «Присоединиться») → сразу кооператив без окна.
 *  Запомненный режим и не зажат Shift → сразу запуск без окна (Avalonia даже не стартует).
 *  Иначе окно в стиле Steam; после выбора Avalonia закрывается, игра запускается с теми же
 *  аргументами из корня игры, процесс ждёт её выхода (Steam держит «в игре» и оверлей).
 *  Ошибки после закрытия окна — системный MessageBox: нет мода → предложить обычную игру.
 * ═══════════════════════════════════════
*/
