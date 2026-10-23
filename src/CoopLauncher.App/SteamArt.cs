using Avalonia.Media.Imaging;
using CoopLauncher.Core;
using CoopLauncher.Core.Steam;

namespace CoopLauncher.App;

static class SteamArt
{
    public static Bitmap? Load(string name)
    {
        try
        {
            var steam = GameLocator.FindSteamDir();
            if (steam == null)
                return null;
            var cache = Path.Combine(steam, "appcache", "librarycache");
            foreach (var path in new[]
                     {
                         Path.Combine(cache, GameLocator.AppId.ToString(), name),
                         Path.Combine(cache, $"{GameLocator.AppId}_{name}")
                     })
            {
                if (File.Exists(path))
                    return new Bitmap(path);
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Steam art {name} failed", ex);
        }
        return null;
    }
}

/*
 * ═══════════════════════════════════════
 *  STEAM ART — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Арт и логотип игры берутся из кэша библиотеки Steam на этом ПК
 *  (appcache/librarycache/588650/<файл> или старый формат 588650_<файл>) — в лаунчер ничего не вшито.
 *  Нет файла → null, окно рисует запасной фон и текстовый логотип.
 * ═══════════════════════════════════════
*/
