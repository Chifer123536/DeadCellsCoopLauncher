using CoopLauncher.Core.Chooser;
using CoopLauncher.Core.Manifest;
using CoopLauncher.Core.Steam;
using CoopLauncher.Core.Update;
using Xunit;

namespace CoopLauncher.Core.Tests;

public sealed class VdfTests
{
    [Fact]
    public void ParsesNewLibraryFoldersFormat()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"      "C:\\Program Files (x86)\\Steam"
                    "apps" { "228980" "1" }
                }
                "1"
                {
                    "path"      "E:\\Steam"
                }
            }
            """;
        var folders = VdfReader.Parse(vdf).Child("libraryfolders")!;
        Assert.Equal(@"C:\Program Files (x86)\Steam", folders.Child("0")!.Value("path"));
        Assert.Equal(@"E:\Steam", folders.Child("1")!.Value("path"));
        Assert.Equal("1", folders.Child("0")!.Child("apps")!.Value("228980"));
    }

    [Fact]
    public void SkipsCommentsConditionalsAndIsCaseInsensitive()
    {
        const string vdf = """
            // comment
            "AppState"
            {
                "appid" "588650" [$WIN]
                "BuildId" "23762174"
                "escaped" "a\"b\\c"
            }
            """;
        var state = VdfReader.Parse(vdf).Child("appstate")!;
        Assert.Equal("23762174", state.Value("buildid"));
        Assert.Equal("a\"b\\c", state.Value("escaped"));
    }

    [Theory]
    [InlineData("\"a\" {")]
    [InlineData("}")]
    [InlineData("\"a\" \"unterminated")]
    [InlineData("\"key\"")]
    public void MalformedThrows(string vdf)
    {
        Assert.Throws<FormatException>(() => VdfReader.Parse(vdf));
    }
}

public sealed class GameLocatorTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "coop-locator-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void FindsGameInSecondLibrary()
    {
        var lib1 = Path.Combine(_root, "lib1");
        var lib2 = Path.Combine(_root, "lib2");
        Directory.CreateDirectory(Path.Combine(lib1, "steamapps"));
        var game = Path.Combine(lib2, "steamapps", "common", "Dead Cells");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "deadcells.exe"), "x");
        File.WriteAllText(Path.Combine(lib2, "steamapps", "appmanifest_588650.acf"),
            "\"AppState\" { \"installdir\" \"Dead Cells\" \"buildid\" \"123\" \"StateFlags\" \"4\" }");

        var found = GameLocator.FindInLibraries([lib1, lib2]);

        Assert.NotNull(found);
        Assert.Equal(game, found!.GameDir);
        Assert.Equal("123", found.BuildId);
        Assert.True(found.FullyInstalled);
    }

    [Fact]
    public void ManifestWithoutExeIsIgnored()
    {
        var lib = Path.Combine(_root, "lib");
        Directory.CreateDirectory(Path.Combine(lib, "steamapps", "common", "Dead Cells"));
        File.WriteAllText(Path.Combine(lib, "steamapps", "appmanifest_588650.acf"),
            "\"AppState\" { \"installdir\" \"Dead Cells\" \"buildid\" \"1\" }");

        Assert.Null(GameLocator.FindInLibraries([lib]));
    }

    [Fact]
    public void ReadsLibrariesFromVdf()
    {
        var steam = Path.Combine(_root, "steam");
        var other = Path.Combine(_root, "other");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"1\" {{ \"path\" \"{other.Replace("\\", "\\\\")}\" }} }}");

        var libraries = GameLocator.ReadLibraries(steam);

        Assert.Equal([steam, Path.GetFullPath(other)], libraries);
    }
}

public sealed class ManifestTests
{
    const string Sha = "6cb81871c6f74d0b876cc9dedb5ac7738b2a19de88a1e8877908d9af7d7d9904";

    static string Json(string modUrl = "https://example.com/mod.zip", string sha = Sha, int schema = 1) => $$"""
        {
          // comment
          "schema": {{schema}},
          "mod": { "version": "0.8.91-coop.2", "url": "{{modUrl}}", "sha256": "{{sha}}", "size": 10 },
          "dccm": { "version": "35.14.1", "url": "https://example.com/dccm.zip", "sha256": "{{Sha}}", "size": 20 },
          "launcher": null,
          "gameBuildIds": ["23762174"],
        }
        """;

    [Fact]
    public void ParsesValidManifest()
    {
        var manifest = ManifestLoader.Parse(Json());
        Assert.Equal("0.8.91-coop.2", manifest.Mod.Version);
        Assert.Equal("35.14.1", manifest.Dccm.Version);
        Assert.Null(manifest.Launcher);
        Assert.Equal(["23762174"], manifest.GameBuildIds!);
    }

    [Theory]
    [InlineData("http://example.com/mod.zip", Sha, 1)]
    [InlineData("not a url", Sha, 1)]
    [InlineData("https://example.com/mod.zip", "abc", 1)]
    [InlineData("https://example.com/mod.zip", "zz81871c6f74d0b876cc9dedb5ac7738b2a19de88a1e8877908d9af7d7d9904", 1)]
    [InlineData("https://example.com/mod.zip", Sha, 2)]
    public void RejectsInvalidManifest(string url, string sha, int schema)
    {
        Assert.Throws<FormatException>(() => ManifestLoader.Parse(Json(url, sha, schema)));
    }

    [Fact]
    public void RejectsMissingPackage()
    {
        Assert.Throws<FormatException>(() => ManifestLoader.Parse("{ \"schema\": 1 }"));
    }
}

public sealed class ChooserSettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "coop-chooser-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var settings = ChooserSettingsStore.Read(_dir);
        Assert.Equal(LaunchMode.Ask, settings.LaunchMode);
        Assert.Equal("auto", settings.Language);
    }

    [Fact]
    public void RoundTripsAndNormalizes()
    {
        ChooserSettingsStore.Write(_dir, ChooserSettings.Default.WithMode(LaunchMode.Coop).WithLanguage("RU"));
        var settings = ChooserSettingsStore.Read(_dir);
        Assert.Equal(LaunchMode.Coop, settings.LaunchMode);
        Assert.Equal("ru", settings.Language);
    }

    [Fact]
    public void GarbageFallsBack()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ChooserSettingsStore.PathIn(_dir), "{ \"mode\": \"rocket\", \"language\": \"de\" }");
        var settings = ChooserSettingsStore.Read(_dir);
        Assert.Equal(LaunchMode.Ask, settings.LaunchMode);
        Assert.Equal("auto", settings.Language);

        File.WriteAllText(ChooserSettingsStore.PathIn(_dir), "\0\0\0");
        Assert.Equal(ChooserSettings.Default, ChooserSettingsStore.Read(_dir));
    }
}

public sealed class SelfUpdaterTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("v1.1", "1.0.9", true)]
    [InlineData("2.0.0-beta", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("", "1.0.0", false)]
    [InlineData(null, "1.0.0", false)]
    [InlineData("garbage", "1.0.0", false)]
    [InlineData("1.0.0+abc", "1.0.0", false)]
    public void ComparesVersions(string? candidate, string current, bool expected)
    {
        Assert.Equal(expected, SelfUpdater.IsNewer(candidate, current));
    }
}

/*
 * ═══════════════════════════════════════
 *  PARSING TESTS
 * ═══════════════════════════════════════
 *  VDF (новый/старый формат, комментарии, условия, escape, мусор), поиск игры по библиотекам Steam,
 *  строгая валидация манифеста, настройки окна выбора, сравнение версий самообновления.
 * ═══════════════════════════════════════
*/
