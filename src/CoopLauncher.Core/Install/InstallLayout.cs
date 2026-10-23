namespace CoopLauncher.Core.Install;

public sealed class InstallLayout
{
    public const string ModFolderName = "DeadCellsMultiplayerMod";
    public const string VanillaExeName = "deadcells.vanilla.exe";

    public InstallLayout(string gameDir)
    {
        GameDir = Path.GetFullPath(gameDir);
    }

    public string GameDir { get; }
    public string GameExe => Path.Combine(GameDir, "deadcells.exe");
    public string GlExe => Path.Combine(GameDir, "deadcells_gl.exe");
    public string VanillaExe => Path.Combine(GameDir, VanillaExeName);
    public string CoreMod => Path.Combine(GameDir, "coremod");
    public string CoreDir => Path.Combine(CoreMod, "core");
    public string PluginsDir => Path.Combine(CoreMod, "plugins");
    public string ModsDir => Path.Combine(CoreMod, "mods");
    public string ModDir => Path.Combine(ModsDir, ModFolderName);
    public string LogsDir => Path.Combine(CoreMod, "logs");
    public string ModCoreVersionFile => Path.Combine(CoreMod, "ModCoreVersion.txt");
    public string StateFile => Path.Combine(CoreMod, "coop-launcher.json");
    public string ModdingExe => Path.Combine(CoreDir, "host", "startup", "DeadCellsModding.exe");
    public string DccmSteamStub => Path.Combine(CoreDir, "host", "startup", "steam", "deadcells.exe");
    public string ChooserStub => Path.Combine(CoreMod, "coop-chooser", "deadcells.exe");
    public string ChooserDir => Path.Combine(CoreMod, "coop-chooser");
}

/*
 * ═══════════════════════════════════════
 *  INSTALL LAYOUT — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Все пути установки в одном месте.
 *  coremod/{core,plugins,ModCoreVersion.txt} — DCCM; coremod/mods/DeadCellsMultiplayerMod — мод.
 *  coremod/{cache,config,data,logs} — данные DCCM/мода, при обновлении не трогаются.
 *  deadcells.exe в корне игры → окно выбора режима (coop-chooser), оригинал — deadcells.vanilla.exe.
 *  DCCM берёт байткод игры из deadcells_gl.exe, поэтому подмена deadcells.exe ему не мешает.
 * ═══════════════════════════════════════
*/
