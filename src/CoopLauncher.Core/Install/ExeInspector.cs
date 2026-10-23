using System.Diagnostics;

namespace CoopLauncher.Core.Install;

public enum ExeKind
{
    Missing,
    Vanilla,
    DccmStub,
    Chooser
}

public static class ExeInspector
{
    public const string ChooserProductName = "Dead Cells Co-op";
    public const string DccmCompanyName = "Dead Cells Core Modding";

    public static ExeKind Classify(string path)
    {
        if (!File.Exists(path))
            return ExeKind.Missing;

        FileVersionInfo info;
        try
        {
            info = FileVersionInfo.GetVersionInfo(path);
        }
        catch
        {
            return ExeKind.Vanilla;
        }

        if (string.Equals(info.ProductName, ChooserProductName, StringComparison.Ordinal))
            return ExeKind.Chooser;
        if (string.Equals(info.CompanyName, DccmCompanyName, StringComparison.Ordinal))
            return ExeKind.DccmStub;
        return ExeKind.Vanilla;
    }

    public static string? ChooserVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.Equals(info.ProductName, ChooserProductName, StringComparison.Ordinal) ? info.ProductVersion : null;
        }
        catch
        {
            return null;
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  EXE INSPECTOR — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Что сейчас лежит в deadcells.exe — по VersionInfo PE:
 *    ProductName «Dead Cells Co-op» → наш лаунчер в роли окна выбора;
 *    CompanyName «Dead Cells Core Modding» → заглушка DCCM;
 *    иначе → игра (у оригинального exe VersionInfo пустой). Так новый exe после апдейта игры
 *    или «Проверки целостности» Steam распознаётся как оригинал без списка хэшей.
 * ═══════════════════════════════════════
*/
