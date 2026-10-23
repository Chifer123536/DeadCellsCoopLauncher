using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace CoopLauncher.Core.Install;

public enum PrerequisiteKind
{
    DotNetRuntime,
    VcRedist
}

public sealed record PrerequisiteStatus(PrerequisiteKind Kind, bool Installed, string Detail);

[SupportedOSPlatform("windows")]
public static class Prerequisites
{
    public const int RequiredDotNetMajor = 10;
    public const string DotNetRuntimeUrl = "https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe";
    public const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    public static PrerequisiteStatus CheckDotNet()
    {
        foreach (var root in DotNetRoots())
        {
            var shared = Path.Combine(root, "shared", "Microsoft.NETCore.App");
            if (!Directory.Exists(shared))
                continue;

            foreach (var dir in Directory.EnumerateDirectories(shared))
            {
                var name = Path.GetFileName(dir);
                var dash = name.IndexOf('-');
                var core = dash >= 0 ? name[..dash] : name;
                if (Version.TryParse(core, out var version) && version.Major >= RequiredDotNetMajor && dash < 0)
                    return new PrerequisiteStatus(PrerequisiteKind.DotNetRuntime, true, name);
            }
        }

        return new PrerequisiteStatus(PrerequisiteKind.DotNetRuntime, false, ".NET 10 Runtime x64");
    }

    public static PrerequisiteStatus CheckVcRedist()
    {
        foreach (var path in new[]
                 {
                     @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64",
                     @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X64"
                 })
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(path);
                if (key?.GetValue("Installed") is int installed && installed == 1 &&
                    key.GetValue("Major") is int major && major >= 14 &&
                    key.GetValue("Minor") is int minor && (major > 14 || minor >= 30))
                {
                    var version = key.GetValue("Version") as string ?? $"{major}.{minor}";
                    return new PrerequisiteStatus(PrerequisiteKind.VcRedist, true, version);
                }
            }
            catch
            {
            }
        }

        return new PrerequisiteStatus(PrerequisiteKind.VcRedist, false, "VC++ 2015-2022 x64");
    }

    public static async Task InstallAsync(PrerequisiteKind kind, HttpClient http, string cacheDir, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var url = kind == PrerequisiteKind.DotNetRuntime ? DotNetRuntimeUrl : VcRedistUrl;
        var file = Path.Combine(cacheDir, kind == PrerequisiteKind.DotNetRuntime ? "dotnet-runtime-win-x64.exe" : "vc_redist.x64.exe");
        Directory.CreateDirectory(cacheDir);
        await DownloadUnverifiedAsync(http, url, file, progress, ct).ConfigureAwait(false);

        if (!AuthenticodeVerifier.IsTrustedMicrosoftSigned(file, out var reason))
        {
            TryDelete(file);
            throw new InvalidDataException($"Installer signature rejected: {reason}");
        }

        var psi = new ProcessStartInfo(file, "/install /quiet /norestart")
        {
            UseShellExecute = true,
            Verb = "runas"
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Administrator permission was declined", ex);
        }

        if (process == null)
            throw new InvalidOperationException("Installer did not start");

        using (process)
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            if (process.ExitCode is not (0 or 3010 or 1638 or 1641))
                throw new InvalidOperationException($"Installer exited with code {process.ExitCode}");
        }

        TryDelete(file);
    }

    static async Task DownloadUnverifiedAsync(HttpClient http, string url, string file, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        TryDelete(file);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = File.Create(file);
        var buffer = new byte[1 << 16];
        long received = 0;
        int read;
        var started = DateTime.UtcNow;
        var lastReport = DateTime.MinValue;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            var now = DateTime.UtcNow;
            if (progress != null && (now - lastReport).TotalMilliseconds >= 100)
            {
                progress.Report(new DownloadProgress(received, total, received / Math.Max(0.001, (now - started).TotalSeconds)));
                lastReport = now;
            }
        }

        progress?.Report(new DownloadProgress(received, total ?? received, 0));
    }

    static IEnumerable<string> DotNetRoots()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            yield return env;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}

[SupportedOSPlatform("windows")]
public static class AuthenticodeVerifier
{
    static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static bool IsTrustedMicrosoftSigned(string path, out string reason)
    {
        if (!IsSignatureValid(path, out reason))
            return false;

        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            if (!cert.Subject.Contains("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"unexpected signer {cert.Subject}";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    static bool IsSignatureValid(string path, out string reason)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path
        };
        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,
                fdwRevocationChecks = 0,
                dwUnionChoice = 1,
                pFile = fileInfoPtr,
                dwStateAction = 0,
                dwProvFlags = 0
            };
            var action = ActionGenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            reason = result == 0 ? string.Empty : $"WinVerifyTrust 0x{result:X8}";
            return result == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}

/*
 * ═══════════════════════════════════════
 *  PREREQUISITES — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  .NET: папка shared/Microsoft.NETCore.App/N.x.y, N ≥ 10, без preview (DCCM — rollForward Major), в DOTNET_ROOT или Program Files\dotnet.
 *  VC++: HKLM ...\VisualStudio\14.0\VC\Runtimes\X64, Installed=1, версия ≥ 14.30.
 *  Установка: официальные aka.ms ссылки Microsoft → проверка подписи (WinVerifyTrust + подписант
 *  «Microsoft Corporation») → запуск с UAC (/install /quiet /norestart).
 *  Коды выхода 0/3010/1638/1641 = успех (3010/1641 — нужна перезагрузка, 1638 — уже стоит новее).
 *  Отказ в UAC → OperationCanceledException.
 * ═══════════════════════════════════════
*/
