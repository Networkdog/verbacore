using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using VerbaCore.Models;

namespace VerbaCore.Services;

/// <summary>
/// Auto-update service. Pulls a manifest (latest.json) from the public GitHub Release,
/// compares with the running assembly version, optionally downloads & verifies the
/// installer, and launches it silently. Inno Setup's CloseApplications=force terminates
/// the running instance and RestartApplications=yes restores it after install.
/// </summary>
public sealed class UpdateService
{
    // Public manifest URL — GitHub redirects /releases/latest/download/<asset> to the
    // current latest release's asset. No auth required (repo is public).
    private const string ManifestUrl =
        "https://github.com/Networkdog/verbacore/releases/latest/download/latest.json";

    private readonly HttpClient _http;
    private readonly SettingsService _settingsService;

    public UpdateService(HttpClient http, SettingsService settingsService)
    {
        _http = http;
        _settingsService = settingsService;
    }

    /// <summary>The version reported by the running assembly (e.g. "0.2.0").</summary>
    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            // Drop trailing .0 revision so it compares cleanly with manifest "0.2.0".
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>
    /// Fetch latest.json. Returns null on network/parse error or if no update is needed.
    /// Sets <paramref name="info"/> to the parsed manifest even when no update is needed
    /// (so callers can show "you are up to date").
    /// </summary>
    public async Task<(UpdateInfo? Manifest, bool UpdateAvailable)> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
            req.Headers.UserAgent.ParseAdd("VerbaCore-Updater/" + CurrentVersion);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var info = await JsonSerializer.DeserializeAsync(
                stream, UpdateJsonContext.Default.UpdateInfo, ct).ConfigureAwait(false);

            // Persist last-check timestamp regardless of result.
            _settingsService.Current.LastUpdateCheckUtc = DateTime.UtcNow;
            _ = _settingsService.SaveAsync();

            if (info is null || string.IsNullOrWhiteSpace(info.Version))
                return (null, false);

            var available = IsNewer(info.Version, CurrentVersion);
            return (info, available);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            Debug.WriteLine($"[VerbaCore] Update check failed: {ex.Message}");
            return (null, false);
        }
    }

    /// <summary>
    /// Download installer to %TEMP% and verify SHA-256. Returns the local path.
    /// </summary>
    public async Task<string> DownloadAsync(
        UpdateInfo info,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(info.Url))
            throw new InvalidOperationException("Manifest has no download URL.");

        var tempDir = Path.Combine(Path.GetTempPath(), "VerbaCore");
        Directory.CreateDirectory(tempDir);
        var dest = Path.Combine(tempDir, $"VerbaCore-Setup-{info.Version}.exe");

        // If an already-verified file exists, skip download.
        if (File.Exists(dest) && !string.IsNullOrEmpty(info.Sha256) &&
            string.Equals(await ComputeSha256Async(dest, ct).ConfigureAwait(false),
                          info.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(1.0);
            return dest;
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, info.Url);
        req.Headers.UserAgent.ParseAdd("VerbaCore-Updater/" + CurrentVersion);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                if (total > 0) progress?.Report((double)read / total);
            }
        }

        // Verify integrity if manifest provides a hash.
        if (!string.IsNullOrEmpty(info.Sha256))
        {
            var actual = await ComputeSha256Async(dest, ct).ConfigureAwait(false);
            if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(dest); } catch { /* best effort */ }
                throw new InvalidOperationException(
                    $"SHA-256 mismatch. expected={info.Sha256} actual={actual}");
            }
        }

        progress?.Report(1.0);
        return dest;
    }

    /// <summary>
    /// Launch installer silently. Inno Setup will close the running app
    /// (CloseApplications=force) and restart it after install (RestartApplications=yes).
    /// </summary>
    public static void LaunchInstaller(string installerPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = installerPath,
            // /SILENT shows progress only; /VERYSILENT hides everything.
            // CLOSEAPPLICATIONS + RESTARTAPPLICATIONS are also defined in installer.iss.
            Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /NORESTART /SUPPRESSMSGBOXES",
            UseShellExecute = true,
        };
        Process.Start(psi);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Compare semver-ish strings ("1.2.3"). Returns true if <paramref name="candidate"/>
    /// is strictly greater than <paramref name="current"/>.
    /// </summary>
    private static bool IsNewer(string candidate, string current)
    {
        static Version Parse(string s)
        {
            s = s.TrimStart('v', 'V');
            // strip pre-release suffixes ("0.3.0-beta1")
            var dash = s.IndexOf('-');
            if (dash >= 0) s = s[..dash];
            return Version.TryParse(s, out var v) ? v : new Version(0, 0, 0);
        }
        return Parse(candidate) > Parse(current);
    }
}
