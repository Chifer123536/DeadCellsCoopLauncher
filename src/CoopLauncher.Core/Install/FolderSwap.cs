namespace CoopLauncher.Core.Install;

public sealed class FolderSwap
{
    public const string BackupSuffix = ".coop-old";

    readonly List<(string Live, string Backup)> _backedUp = [];
    readonly List<string> _placed = [];

    public void Replace(string source, string live)
    {
        if (!Exists(source))
            throw new FileNotFoundException("Staged item is missing", source);

        var backup = live + BackupSuffix;
        DeletePath(backup);

        if (Exists(live))
        {
            MovePath(live, backup);
            _backedUp.Add((live, backup));
        }

        var parent = Path.GetDirectoryName(live);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
        MovePath(source, live);
        _placed.Add(live);
    }

    public void Rollback()
    {
        for (var i = _placed.Count - 1; i >= 0; i--)
        {
            try { DeletePath(_placed[i]); } catch { }
        }

        for (var i = _backedUp.Count - 1; i >= 0; i--)
        {
            var (live, backup) = _backedUp[i];
            try
            {
                if (Exists(backup) && !Exists(live))
                    MovePath(backup, live);
            }
            catch
            {
            }
        }

        _placed.Clear();
        _backedUp.Clear();
    }

    public void Commit()
    {
        foreach (var (_, backup) in _backedUp)
        {
            try { DeletePath(backup); } catch { }
        }

        _placed.Clear();
        _backedUp.Clear();
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public static void DeletePath(string path)
    {
        if (Directory.Exists(path))
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    static void MovePath(string from, string to)
    {
        if (Directory.Exists(from))
            Directory.Move(from, to);
        else
            File.Move(from, to);
    }
}

/*
 * ═══════════════════════════════════════
 *  FOLDER SWAP — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Замена папок/файлов переименованием (staging лежит на том же диске → rename атомарный):
 *  live → live.coop-old, staged → live. Ошибка на любом шаге → Rollback: поставленное удаляется,
 *  старое возвращается на место в обратном порядке. Commit удаляет .coop-old.
 *  Остаток .coop-old от упавшей прошлой попытки удаляется перед новой заменой.
 * ═══════════════════════════════════════
*/
