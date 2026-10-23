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
    [InlineData("", LaunchMode.Ask, false, LaunchMode.Coop, ChooserStart.Coop)]
    [InlineData("", LaunchMode.Coop, true, LaunchMode.Vanilla, ChooserStart.Vanilla)]
    [InlineData("+connect_lobby 1", LaunchMode.Ask, false, LaunchMode.Vanilla, ChooserStart.Coop)]
    public void LauncherRequestSkipsWindow(string args, LaunchMode remembered, bool shift, LaunchMode requested, ChooserStart expected)
    {
        Assert.Equal(expected, ChooserPlan.Decide(args, remembered, shift, requested));
    }

    [Fact]
    public void LaunchRequestIsOneShotAndExpires()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coop-request-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = DateTimeOffset.UtcNow;
            Assert.Null(LaunchRequestStore.Take(dir, now));

            LaunchRequestStore.Write(dir, LaunchMode.Vanilla, now);
            Assert.Equal(LaunchMode.Vanilla, LaunchRequestStore.Take(dir, now.AddSeconds(30)));
            Assert.Null(LaunchRequestStore.Take(dir, now.AddSeconds(31)));

            LaunchRequestStore.Write(dir, LaunchMode.Coop, now);
            Assert.Null(LaunchRequestStore.Take(dir, now + LaunchRequestStore.MaxAge + TimeSpan.FromSeconds(1)));
            Assert.False(File.Exists(LaunchRequestStore.PathIn(dir)));

            LaunchRequestStore.Write(dir, LaunchMode.Coop, now.AddMinutes(10));
            Assert.Null(LaunchRequestStore.Take(dir, now));

            File.WriteAllText(LaunchRequestStore.PathIn(dir), "{ broken");
            Assert.Null(LaunchRequestStore.Take(dir, now));
            Assert.False(File.Exists(LaunchRequestStore.PathIn(dir)));

            Assert.Throws<ArgumentOutOfRangeException>(() => LaunchRequestStore.Write(dir, LaunchMode.Ask, now));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
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
 *  Таблица решений запуска из Steam (инвайт сильнее выбора из лаунчера, тот сильнее Shift и запомненного режима),
 *  одноразовый next-launch.json (забирается один раз, протухает через 2 мин, битый/из будущего — игнор) и
 *  выделение аргументов Steam из командной строки: имя exe и ведущий --chooser срезаются, остальное как есть.
 * ═══════════════════════════════════════
*/
