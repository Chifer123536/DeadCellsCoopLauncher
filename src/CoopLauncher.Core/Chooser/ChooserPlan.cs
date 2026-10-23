namespace CoopLauncher.Core.Chooser;

public enum ChooserStart
{
    Window,
    Coop,
    Vanilla
}

public static class ChooserPlan
{
    public const string ConnectLobbyArg = "+connect_lobby";
    public const string ChooserFlag = "--chooser";

    public static ChooserStart Decide(string args, LaunchMode remembered, bool shiftDown, LaunchMode? requested = null)
    {
        if (args.Contains(ConnectLobbyArg, StringComparison.OrdinalIgnoreCase))
            return ChooserStart.Coop;
        if (requested == LaunchMode.Coop)
            return ChooserStart.Coop;
        if (requested == LaunchMode.Vanilla)
            return ChooserStart.Vanilla;
        if (shiftDown)
            return ChooserStart.Window;
        return remembered switch
        {
            LaunchMode.Coop => ChooserStart.Coop,
            LaunchMode.Vanilla => ChooserStart.Vanilla,
            _ => ChooserStart.Window
        };
    }

    public static string GameArguments(string commandLine)
    {
        var args = RawArguments(commandLine);
        if (args.StartsWith(ChooserFlag, StringComparison.OrdinalIgnoreCase) &&
            (args.Length == ChooserFlag.Length || char.IsWhiteSpace(args[ChooserFlag.Length])))
            args = args[ChooserFlag.Length..].TrimStart();
        return args;
    }

    public static string RawArguments(string commandLine)
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
}

/*
 * ═══════════════════════════════════════
 *  CHOOSER PLAN — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Что делать при запуске deadcells.exe из Steam:
 *    +connect_lobby (инвайт / «Присоединиться») → кооп без окна, даже с Shift;
 *    выбор, переданный из лаунчера (next-launch.json) → он без окна;
 *    Shift → окно; запомнен кооп / обычная → сразу он; иначе окно.
 *  GameArguments: хвост командной строки как есть (без имени exe и без ведущего --chooser) —
 *  игра получает аргументы Steam байт в байт, без пересборки кавычек.
 * ═══════════════════════════════════════
*/
