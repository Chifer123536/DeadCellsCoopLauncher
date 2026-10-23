using Avalonia.Controls;
using Avalonia.Input;

namespace CoopLauncher.App;

public partial class ChooserWindow : Window
{
    public ChooserWindow()
    {
        InitializeComponent();
    }

    public ChooserWindow(ChooserViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseWindow = Close;
        Opened += (_, _) =>
        {
            (vm.PrefersVanilla || !vm.CanPlayCoop ? VanillaButton : CoopButton).Focus();
            _ = vm.StartAsync();
        };
        Closing += (_, e) =>
        {
            if (!vm.RequestClose())
                e.Cancel = true;
        };
        KeyDown += OnKey;
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
                var next = CoopButton.IsFocused ? VanillaButton : CoopButton;
                if (next.IsEffectivelyEnabled)
                    next.Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  CHOOSER WINDOW — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Окно Steam-запуска в стиле лаунчера: арт и логотип из кэша Steam, две большие кнопки режима.
 *  Фокус при открытии — на запомненном режиме (кооп, если он доступен). ←/→ — между кнопками,
 *  Enter/Space — нажать, Esc — закрыть без запуска. Закрытие во время обновления откладывает модель.
 * ═══════════════════════════════════════
*/
