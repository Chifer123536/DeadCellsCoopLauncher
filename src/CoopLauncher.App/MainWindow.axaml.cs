using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace CoopLauncher.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel vm)
            vm.PickFolder = PickFolderAsync;
    }

    async Task<string?> PickFolderAsync()
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }
}

/*
 * ═══════════════════════════════════════
 *  MAIN WINDOW — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Разметка в MainWindow.axaml (compiled bindings). Код окна отдаёт модели выбор папки через StorageProvider.
 * ═══════════════════════════════════════
*/
