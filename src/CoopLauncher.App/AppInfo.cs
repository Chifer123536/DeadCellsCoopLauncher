using System.Reflection;

namespace CoopLauncher.App;

static class AppInfo
{
    static readonly Lazy<HttpClient> LazyHttp = new(CreateHttp);

    public static string Version { get; } = ReadVersion();

    public static HttpClient Http => LazyHttp.Value;

    static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DeadCellsCoopLauncher/" + Version);
        return http;
    }

    static string ReadVersion()
    {
        var info = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }
}

/*
 * ═══════════════════════════════════════
 *  APP INFO — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Версия лаунчера (InformationalVersion без +hash) и один HttpClient на процесс
 *  (User-Agent с версией, таймаут 30 мин под большие архивы). Общие для лаунчера и окна выбора.
 * ═══════════════════════════════════════
*/
