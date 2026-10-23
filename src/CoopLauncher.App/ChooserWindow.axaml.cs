using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CoopLauncher.Core.Chooser;

namespace CoopLauncher.App;

public partial class ChooserWindow : Window
{
    static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#1A9FFF"));
    static readonly IBrush Ring = new SolidColorBrush(Color.Parse("#C6CBD1"));

    readonly ChooserContext? _context;
    LaunchMode _selected = LaunchMode.Coop;

    public ChooserWindow()
    {
        InitializeComponent();
    }

    public ChooserWindow(ChooserContext context) : this()
    {
        _context = context;
        var t = context.Text;
        HeaderText.Text = t.ChooserHeader;
        CoopText.Text = t.ChooserCoop;
        VanillaText.Text = t.ChooserVanilla;
        AlwaysBox.Content = t.ChooserAlways;
        HintText.Text = t.ChooserHint;
        PlayButton.Content = t.ChooserPlay;
        CancelButton.Content = t.ChooserCancel;

        _selected = context.Settings.LaunchMode == LaunchMode.Vanilla ? LaunchMode.Vanilla : LaunchMode.Coop;
        Select(_selected);

        CoopRow.PointerPressed += (_, e) => OnRow(LaunchMode.Coop, e);
        VanillaRow.PointerPressed += (_, e) => OnRow(LaunchMode.Vanilla, e);
        PlayButton.Click += (_, _) => Play();
        CancelButton.Click += (_, _) => Close();
        CloseButton.Click += (_, _) => Close();
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button)
                BeginMoveDrag(e);
        };
        KeyDown += OnKey;
        Opened += (_, _) => PlayButton.Focus();
    }

    void OnRow(LaunchMode mode, PointerPressedEventArgs e)
    {
        Select(mode);
        if (e.ClickCount >= 2)
            Play();
        e.Handled = true;
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
                Select(_selected == LaunchMode.Coop ? LaunchMode.Vanilla : LaunchMode.Coop);
                e.Handled = true;
                break;
            case Key.Enter:
                Play();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    void Select(LaunchMode mode)
    {
        _selected = mode;
        var coop = mode == LaunchMode.Coop;
        CoopRow.Classes.Set("selected", coop);
        VanillaRow.Classes.Set("selected", !coop);
        Paint(CoopRing, CoopDot, coop);
        Paint(VanillaRing, VanillaDot, !coop);
    }

    static void Paint(Avalonia.Controls.Shapes.Ellipse ring, Avalonia.Controls.Shapes.Ellipse dot, bool on)
    {
        ring.Fill = on ? Accent : Brushes.Transparent;
        ring.Stroke = on ? Accent : Ring;
        ring.StrokeThickness = 1.5;
        dot.IsVisible = on;
    }

    void Play()
    {
        if (_context == null)
            return;
        _context.Choice = _selected;
        ChooserLauncher.Remember(_context, _selected, AlwaysBox.IsChecked == true);
        Close();
    }
}

/*
 * ═══════════════════════════════════════
 *  CHOOSER WINDOW — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Копия Steam-окна «Выберите вариант запуска»: два варианта (кооператив / обычная), «Всегда использовать»,
 *  Играть / Отмена. Выбор по умолчанию — кооператив (или обычная, если она была запомнена до Shift).
 *  Мышь: клик — выбрать, двойной клик — играть. Клавиатура: ↑/↓, Enter, Esc. Окно без рамки, тянется за фон.
 *  Результат кладётся в ChooserContext.Choice; запуск игры делает ChooserLauncher после закрытия окна.
 * ═══════════════════════════════════════
*/
