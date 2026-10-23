using System.IO.Compression;

namespace CoopLauncher.Core.Install;

public static class SafeZip
{
    public static void ExtractTo(string zipPath, string destination, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);

        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = entries[i];
            var relative = entry.FullName.Replace('\\', '/');
            if (relative.Length == 0)
                continue;

            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry escapes the target folder: {entry.FullName}");

            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            progress?.Report((i + 1) / (double)entries.Count);
        }
    }

    public static string DescendSingleFolder(string dir)
    {
        var current = dir;
        for (var depth = 0; depth < 2; depth++)
        {
            if (Directory.EnumerateFiles(current).Any())
                return current;
            var dirs = Directory.GetDirectories(current);
            if (dirs.Length != 1)
                return current;
            current = dirs[0];
        }
        return current;
    }
}

/*
 * ═══════════════════════════════════════
 *  SAFE ZIP — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Распаковка с защитой от zip-slip: путь каждой записи обязан лежать внутри папки назначения.
 *  DescendSingleFolder: если в корне архива ровно одна папка и нет файлов — заходим в неё
 *  (архив «DeadCellsMultiplayerMod/…» и архив «…» ставятся одинаково).
 * ═══════════════════════════════════════
*/
