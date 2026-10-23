using Avalonia;

namespace CoopLauncher.App;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (ChooserLauncher.IsChooserProcess(args))
            return ChooserLauncher.Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

/*
 * ═══════════════════════════════════════
 *  PROGRAM — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Запуск как deadcells.exe → окно выбора режима (ChooserLauncher), иначе лаунчер. BuildAvaloniaApp нужен ещё и дизайнеру.
 * ═══════════════════════════════════════
*/
