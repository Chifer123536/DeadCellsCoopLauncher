using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace CoopLauncher.Core.Install;

public readonly record struct DownloadProgress(long Received, long? Total, double BytesPerSecond);

public sealed class Downloader
{
    const int BufferSize = 1 << 16;
    const int MaxAttempts = 4;

    readonly HttpClient _http;
    readonly TimeSpan _retryDelay;

    public Downloader(HttpClient http, TimeSpan? retryDelay = null)
    {
        _http = http;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
    }

    public async Task<string> DownloadVerifiedAsync(
        string url,
        string expectedSha256,
        string destinationPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        if (File.Exists(destinationPath) && await HashFileAsync(destinationPath, ct).ConfigureAwait(false) == Normalize(expectedSha256))
            return destinationPath;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var partial = destinationPath + ".part";
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await DownloadWithResumeAsync(url, partial, progress, ct).ConfigureAwait(false);
                var actual = await HashFileAsync(partial, ct).ConfigureAwait(false);
                if (actual != Normalize(expectedSha256))
                {
                    File.Delete(partial);
                    throw new InvalidDataException($"Checksum mismatch for {Path.GetFileName(destinationPath)}: expected {Normalize(expectedSha256)}, got {actual}");
                }

                File.Move(partial, destinationPath, overwrite: true);
                return destinationPath;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxAttempts && ex is HttpRequestException or IOException)
            {
                last = ex;
                await Task.Delay(_retryDelay * attempt, ct).ConfigureAwait(false);
            }
        }

        throw new IOException($"Download failed after {MaxAttempts} attempts: {url}", last);
    }

    async Task DownloadWithResumeAsync(string url, string partial, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0L;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (existing > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
            existing = 0;
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength is { } length ? length + existing : null;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(partial, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        var buffer = new byte[BufferSize];
        var received = existing;
        var started = DateTime.UtcNow;
        var sessionBytes = 0L;
        var lastReport = DateTime.MinValue;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            sessionBytes += read;
            var now = DateTime.UtcNow;
            if (progress != null && (now - lastReport).TotalMilliseconds >= 100)
            {
                var seconds = Math.Max(0.001, (now - started).TotalSeconds);
                progress.Report(new DownloadProgress(received, total, sessionBytes / seconds));
                lastReport = now;
            }
        }

        progress?.Report(new DownloadProgress(received, total ?? received, 0));
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    static string Normalize(string sha) => sha.Trim().ToLowerInvariant();
}

/*
 * ═══════════════════════════════════════
 *  DOWNLOADER — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Качает в <файл>.part с докачкой (Range), до 4 попыток с паузой 2/4/6 с.
 *  sha256 обязателен: не совпал → .part удаляется, исключение; совпал → атомарный rename.
 *  Уже скачанный файл с верным хэшем не перекачивается.
 *  Прогресс — не чаще раза в 100 мс (байты, всего, скорость).
 * ═══════════════════════════════════════
*/
