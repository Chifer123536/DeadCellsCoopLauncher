using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CoopLauncher.Core.Install;
using CoopLauncher.Core.Manifest;
using Xunit;

namespace CoopLauncher.Core.Tests;

public sealed class SafeZipTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "coop-zip-" + Guid.NewGuid().ToString("N"));

    public SafeZipTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void RejectsPathTraversal()
    {
        var zip = Path.Combine(_dir, "evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open());
            writer.Write("x");
        }

        Assert.Throws<InvalidDataException>(() => SafeZip.ExtractTo(zip, Path.Combine(_dir, "out")));
        Assert.False(File.Exists(Path.Combine(_dir, "escape.txt")));
    }

    [Fact]
    public void DescendsIntoSingleTopFolder()
    {
        var root = Path.Combine(_dir, "root");
        Directory.CreateDirectory(Path.Combine(root, "Mod"));
        File.WriteAllText(Path.Combine(root, "Mod", "a.dll"), "x");
        Assert.Equal(Path.Combine(root, "Mod"), SafeZip.DescendSingleFolder(root));

        File.WriteAllText(Path.Combine(root, "b.txt"), "x");
        Assert.Equal(root, SafeZip.DescendSingleFolder(root));
    }
}

public sealed class FolderSwapTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "coop-swap-" + Guid.NewGuid().ToString("N"));

    public FolderSwapTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    string Dir(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "f.txt"), content);
        return path;
    }

    [Fact]
    public void CommitReplacesAndRemovesBackups()
    {
        var live = Dir("live", "old");
        var staged = Dir("staged", "new");
        var swap = new FolderSwap();
        swap.Replace(staged, live);
        swap.Commit();

        Assert.Equal("new", File.ReadAllText(Path.Combine(live, "f.txt")));
        Assert.False(Directory.Exists(live + FolderSwap.BackupSuffix));
        Assert.False(Directory.Exists(staged));
    }

    [Fact]
    public void RollbackRestoresEverything()
    {
        var liveA = Dir("a", "oldA");
        var liveB = Path.Combine(_dir, "b");
        var stagedA = Dir("sa", "newA");
        var stagedB = Dir("sb", "newB");
        var swap = new FolderSwap();
        swap.Replace(stagedA, liveA);
        swap.Replace(stagedB, liveB);
        swap.Rollback();

        Assert.Equal("oldA", File.ReadAllText(Path.Combine(liveA, "f.txt")));
        Assert.False(Directory.Exists(liveB));
        Assert.False(Directory.Exists(liveA + FolderSwap.BackupSuffix));
    }

    [Fact]
    public void MissingSourceThrowsWithoutTouchingLive()
    {
        var live = Dir("live", "old");
        var swap = new FolderSwap();
        Assert.Throws<FileNotFoundException>(() => swap.Replace(Path.Combine(_dir, "nope"), live));
        Assert.Equal("old", File.ReadAllText(Path.Combine(live, "f.txt")));
    }
}

public sealed class ModInstallerTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "coop-install-" + Guid.NewGuid().ToString("N"));
    readonly string _game;
    readonly string _cache;
    readonly InstallLayout _layout;
    readonly Downloader _downloader = new(new HttpClient(), TimeSpan.Zero);

    public ModInstallerTests()
    {
        _game = Path.Combine(_root, "Dead Cells");
        _cache = Path.Combine(_root, "cache");
        Directory.CreateDirectory(_game);
        Directory.CreateDirectory(_cache);
        File.WriteAllText(Path.Combine(_game, "deadcells.exe"), "vanilla-1");
        File.WriteAllText(Path.Combine(_game, "deadcells_gl.exe"), "gl");
        _layout = new InstallLayout(_game);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    static Stream Chooser() => File.OpenRead(typeof(ModInstallerTests).Assembly.Location);

    static void Add(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    PackageRef DccmZip(string version)
    {
        var path = Path.Combine(_cache, $"dccm-{version}.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Add(zip, "core/host/startup/DeadCellsModding.exe", "modding-" + version);
            Add(zip, "plugins/plugin.dll", "plugin");
            Add(zip, "ModCoreVersion.txt", version);
        }
        return Ref(version, path);
    }

    PackageRef ModZip(string version, string modinfoVersion)
    {
        var path = Path.Combine(_cache, $"mod-{version}.zip");
        if (File.Exists(path))
            File.Delete(path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Add(zip, "DeadCellsMultiplayerMod/DeadCellsMultiplayerMod.dll", "dll-" + version);
            Add(zip, "DeadCellsMultiplayerMod/modinfo.json", $"{{ \"name\": \"DeadCellsMultiplayerMod\", \"version\": \"{modinfoVersion}\" }}");
        }
        return Ref(version, path);
    }

    static PackageRef Ref(string version, string path)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        return new PackageRef(version, "https://invalid.example/" + Path.GetFileName(path), sha, new FileInfo(path).Length);
    }

    static LauncherManifest Manifest(PackageRef mod, PackageRef dccm) => new(1, mod, dccm, null, null, null);

    Task Install(LauncherManifest manifest) =>
        ModInstaller.InstallAsync(_layout, manifest, _downloader, _cache, Chooser, null, CancellationToken.None);

    [Fact]
    public async Task FullLifecycle()
    {
        var dccm = DccmZip("35.14.1");
        var manifest = Manifest(ModZip("1.0.0", "1.0.0"), dccm);

        await Install(manifest);

        var inspection = ModInstaller.Inspect(_layout);
        Assert.Equal("35.14.1", inspection.DccmVersion);
        Assert.Equal("1.0.0", inspection.ModVersion);
        Assert.Equal(ExeKind.Chooser, inspection.Exe);
        Assert.False(inspection.NeedsUpdate(manifest));
        Assert.Equal("vanilla-1", File.ReadAllText(_layout.VanillaExe));
        Assert.True(File.Exists(Path.Combine(_layout.PluginsDir, "plugin.dll")));
        Assert.NotNull(InstallStateStore.Read(_layout));

        Directory.CreateDirectory(Path.Combine(_layout.CoreMod, "config"));
        File.WriteAllText(Path.Combine(_layout.CoreMod, "config", "user.json"), "keep");
        Directory.CreateDirectory(Path.Combine(_layout.ModsDir, "OtherMod"));
        Assert.Equal(["OtherMod"], ModInstaller.Inspect(_layout).OtherMods);

        File.WriteAllText(_layout.GameExe, "vanilla-2");
        Assert.True(ModInstaller.Inspect(_layout).ExeNeedsRepair);
        await Install(manifest);
        Assert.Equal("vanilla-2", File.ReadAllText(_layout.VanillaExe));
        Assert.Equal(ExeKind.Chooser, ExeInspector.Classify(_layout.GameExe));

        var updated = Manifest(ModZip("1.0.1", "1.0.1"), dccm);
        Assert.True(ModInstaller.Inspect(_layout).NeedsUpdate(updated));
        await Install(updated);
        Assert.Equal("1.0.1", ModInstaller.ReadModVersion(_layout.ModDir));
        Assert.Equal("dll-1.0.1", File.ReadAllText(Path.Combine(_layout.ModDir, "DeadCellsMultiplayerMod.dll")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_layout.CoreMod, "config", "user.json")));
        Assert.True(Directory.Exists(Path.Combine(_layout.ModsDir, "OtherMod")));
        Assert.False(Directory.Exists(_layout.ModDir + FolderSwap.BackupSuffix));
        Assert.False(Directory.Exists(Path.Combine(_layout.CoreMod, ".coop-staging")));
        Assert.False(File.Exists(Path.Combine(_cache, "mod-1.0.0.zip")));

        var broken = Manifest(ModZip("1.0.2", "9.9.9"), dccm);
        var error = await Assert.ThrowsAsync<InstallException>(() => Install(broken));
        Assert.Equal("bad-mod", error.Code);
        Assert.Equal("1.0.1", ModInstaller.ReadModVersion(_layout.ModDir));

        var result = ModInstaller.Uninstall(_layout);
        Assert.True(result.OriginalExeRestored);
        Assert.Equal("vanilla-2", File.ReadAllText(_layout.GameExe));
        Assert.False(File.Exists(_layout.VanillaExe));
        Assert.False(Directory.Exists(_layout.CoreMod));
    }

    [Fact]
    public async Task UpdateFromRunningChooserKeepsExe()
    {
        var dccm = DccmZip("35.14.1");
        await Install(Manifest(ModZip("1.0.0", "1.0.0"), dccm));
        var exeBefore = File.ReadAllBytes(_layout.GameExe);
        var stamp = File.GetLastWriteTimeUtc(_layout.GameExe);

        using (File.Open(_layout.GameExe, FileMode.Open, FileAccess.Read, FileShare.Read))
            await ModInstaller.InstallAsync(_layout, Manifest(ModZip("1.0.1", "1.0.1"), dccm), _downloader, _cache, null, null, CancellationToken.None);

        Assert.Equal("1.0.1", ModInstaller.ReadModVersion(_layout.ModDir));
        Assert.Equal(exeBefore, File.ReadAllBytes(_layout.GameExe));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(_layout.GameExe));
        Assert.Equal("vanilla-1", File.ReadAllText(_layout.VanillaExe));

        File.Delete(_layout.VanillaExe);
        var error = await Assert.ThrowsAsync<InstallException>(() =>
            ModInstaller.InstallAsync(_layout, Manifest(ModZip("1.0.2", "1.0.2"), dccm), _downloader, _cache, null, null, CancellationToken.None));
        Assert.Equal("no-vanilla", error.Code);
        Assert.Equal("1.0.1", ModInstaller.ReadModVersion(_layout.ModDir));
    }

    [Fact]
    public async Task WrongChecksumNeverInstalls()
    {
        var dccm = DccmZip("35.14.1");
        var mod = ModZip("1.0.0", "1.0.0");
        var tampered = mod with { Sha256 = new string('0', 64) };
        File.Move(Path.Combine(_cache, "mod-1.0.0.zip"), Path.Combine(_cache, "elsewhere.zip"));

        await Assert.ThrowsAnyAsync<Exception>(() => Install(Manifest(tampered, dccm)));
        Assert.Null(ModInstaller.ReadModVersion(_layout.ModDir));
        Assert.Equal("vanilla-1", File.ReadAllText(_layout.GameExe));
    }

    [Fact]
    public async Task RefusesWithoutOriginalExe()
    {
        File.Delete(_layout.GameExe);
        var error = await Assert.ThrowsAsync<InstallException>(() => Install(Manifest(ModZip("1.0.0", "1.0.0"), DccmZip("35.14.1"))));
        Assert.Equal("no-vanilla", error.Code);
        Assert.False(Directory.Exists(_layout.ModDir));
    }

    [Fact]
    public void UninstallWithoutBackupReportsIt()
    {
        File.Copy(typeof(ModInstallerTests).Assembly.Location, _layout.GameExe, overwrite: true);
        Directory.CreateDirectory(_layout.CoreMod);
        var result = ModInstaller.Uninstall(_layout);
        Assert.False(result.OriginalExeRestored);
        Assert.False(Directory.Exists(_layout.CoreMod));
    }
}

/*
 * ═══════════════════════════════════════
 *  INSTALLER TESTS — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Всё во временной папке, сеть не нужна: архивы заранее лежат в кэше с верным sha256,
 *  Downloader их не перекачивает. «Окно выбора» — сама тестовая сборка (Product = Dead Cells Co-op),
 *  «оригинальный exe» — текстовый файл без VersionInfo.
 *  Цикл: установка → починка после «проверки Steam» → обновление мода (config и чужие моды целы) →
 *  битый архив с откатом → удаление с возвратом оригинала. Плюс: подмена sha, нет оригинала, удаление без бэкапа,
 *  обновление из окна выбора (deadcells.exe открыт и не меняется, без оригинала — отказ до замены файлов).
 * ═══════════════════════════════════════
*/
