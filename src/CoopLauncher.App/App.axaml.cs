using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace CoopLauncher.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (ChooserLauncher.Session is { } chooser)
            {
                desktop.MainWindow = new ChooserWindow(new ChooserViewModel(chooser));
            }
            else
            {
                var vm = new MainViewModel();
                vm.Shutdown = () => desktop.Shutdown();
                desktop.MainWindow = new MainWindow { DataContext = vm };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}

/*
 * ═══════════════════════════════════════
 *  APP — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Fluent, тёмная тема. Режим окна выбора (exe запущен как deadcells.exe) → ChooserWindow + ChooserViewModel,
 *  иначе лаунчер MainWindow с MainViewModel; Shutdown нужен самообновлению.
 * ═══════════════════════════════════════
*/
