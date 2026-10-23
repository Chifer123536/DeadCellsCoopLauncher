using System.Diagnostics;

namespace CoopLauncher.Core.Install;

public static class GameProcess
{
    static readonly string[] Names = ["deadcells", "deadcells_gl", "deadcells.vanilla", "DeadCellsModding"];

    public static bool IsRunning(InstallLayout layout)
    {
        foreach (var name in Names)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    if (process.Id != Environment.ProcessId && BelongsTo(process, layout.GameDir))
                        return true;
                }
            }
        }

        return false;
    }

    static bool BelongsTo(Process process, string gameDir)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(path))
                return true;
            var full = Path.GetFullPath(path);
            var root = Path.TrimEndingDirectorySeparator(gameDir) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  GAME PROCESS — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Игра запущена, если есть процесс deadcells / deadcells_gl / deadcells.vanilla / DeadCellsModding
 *  из папки этой игры, кроме своего процесса (окно выбора само работает как deadcells.exe).
 *  Путь не прочитать (другой пользователь, защита) → считаем, что запущена:
 *  лучше попросить закрыть игру, чем менять файлы под живым процессом.
 * ═══════════════════════════════════════
*/
