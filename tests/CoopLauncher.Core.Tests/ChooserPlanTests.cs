using CoopLauncher.Core.Chooser;
using Xunit;

namespace CoopLauncher.Core.Tests;

public sealed class ChooserPlanTests
{
    [Theory]
    [InlineData("", LaunchMode.Ask, false, ChooserStart.Window)]
    [InlineData("", LaunchMode.Coop, false, ChooserStart.Coop)]
    [InlineData("", LaunchMode.Vanilla, false, ChooserStart.Vanilla)]
    [InlineData("", LaunchMode.Coop, true, ChooserStart.Window)]
    [InlineData("", LaunchMode.Vanilla, true, ChooserStart.Window)]
    [InlineData("+connect_lobby 109775241000000000", LaunchMode.Vanilla, false, ChooserStart.Coop)]
    [InlineData("-foo +CONNECT_LOBBY 1", LaunchMode.Ask, true, ChooserStart.Coop)]
    public void Decides(string args, LaunchMode remembered, bool shift, ChooserStart expected)
    {
        Assert.Equal(expected, ChooserPlan.Decide(args, remembered, shift));
    }

    [Theory]
    [InlineData("\"E:\\Steam\\Dead Cells\\deadcells.exe\" +connect_lobby 1", "+connect_lobby 1")]
    [InlineData("deadcells.exe   -a \"b c\"  ", "-a \"b c\"")]
    [InlineData("\"C:\\x\\deadcells.exe\"", "")]
    [InlineData("deadcells.exe --chooser +connect_lobby 7", "+connect_lobby 7")]
    [InlineData("deadcells.exe --CHOOSER", "")]
    [InlineData("deadcells.exe --chooserx", "--chooserx")]
    [InlineData("deadcells.exe -a --chooser", "-a --chooser")]
    public void KeepsSteamArgumentsVerbatim(string commandLine, string expected)
    {
        Assert.Equal(expected, ChooserPlan.GameArguments(commandLine));
    }
}

/*
 * ═══════════════════════════════════════
 *  CHOOSER PLAN TESTS — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Таблица решений запуска из Steam (инвайт сильнее Shift и запомненного режима) и
 *  выделение аргументов Steam из командной строки: имя exe и ведущий --chooser срезаются, остальное как есть.
 * ═══════════════════════════════════════
*/
