using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlexPad.Core;

namespace FlexPad.App.Services;

public sealed class UpdateInfo
{
    public string CurrentVersion { get; init; } = "";
    public string LatestTag { get; set; } = "";
    public bool UpdateAvailable { get; set; }
    public string ReleaseUrl { get; set; } = $"https://github.com/{UpdateService.Repo}/releases/latest";
    public string? AssetUrl { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// In-app updater ported from the LP-100A project: checks the GitHub latest release, downloads
/// the build for this platform, and (since a running executable can't overwrite itself) stages
/// a helper that waits for exit, swaps the exe, and relaunches. Cross-platform (win/linux/Pi).
/// </summary>
public static class UpdateService
{
    // The cross-platform port's own repo (separate from the PowerShell gsa700/flexpad).
    // Note: CheckAsync hits /releases/latest, which excludes pre-releases — the in-app update
    // check only surfaces full (non-prerelease) releases.
    public const string Repo = "gsa700/flexpad";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }

    /// <summary>Runtime identifier used in the release asset name, e.g. "win-x64", "linux-arm64".</summary>
    public static string Rid()
    {
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        if (OperatingSystem.IsWindows()) return $"win-{arch}";
        if (OperatingSystem.IsMacOS()) return $"osx-{arch}";
        return $"linux-{arch}";
    }

    public static async Task<UpdateInfo> CheckAsync()
    {
        var info = new UpdateInfo { CurrentVersion = CurrentVersion };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("FlexPad-UpdateCheck", "1.0"));
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await Http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            info.LatestTag = root.GetProperty("tag_name").GetString() ?? "";
            if (root.TryGetProperty("html_url", out var hu) && hu.GetString() is { } url) info.ReleaseUrl = url;

            var assetName = $"FlexPad-{Rid()}.zip";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    if (a.GetProperty("name").GetString() == assetName)
                    {
                        info.AssetUrl = a.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            // Ordering lives in FlexPad.Core so pre-release suffixes are covered by tests rather than by
            // hope: the comparison this replaced truncated at the dash, which made 1.0.0-beta1,
            // 1.0.0-beta2 and 1.0.0 indistinguishable.
            info.UpdateAvailable = VersionOrder.IsNewer(info.LatestTag, CurrentVersion);
        }
        catch (Exception ex)
        {
            info.Error = ex.Message;
        }
        return info;
    }

    /// <summary>
    /// Temp directory the update is downloaded and unpacked into. The relaunched app must never have
    /// this as its working directory: a directory in use as one cannot be deleted, so the clean-up
    /// below would throw and updating twice without a restart would fail.
    /// </summary>
    private static string StageRoot => Path.Combine(Path.GetTempPath(), "FlexPad-update");

    /// <summary>Download the asset zip, extract it, and return the path to the staged executable.</summary>
    public static async Task<string> DownloadAndStageAsync(string assetUrl)
    {
        var tmp = StageRoot;
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);

        var zip = Path.Combine(tmp, "update.zip");
        using (var req = new HttpRequestMessage(HttpMethod.Get, assetUrl))
        {
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("FlexPad-UpdateInstall", "1.0"));
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zip);
            await resp.Content.CopyToAsync(fs);
        }

        var ex = Path.Combine(tmp, "ex");
        ZipFile.ExtractToDirectory(zip, ex, overwriteFiles: true);

        var exeName = OperatingSystem.IsWindows() ? "FlexPad.exe" : "FlexPad";
        var staged = Directory.GetFiles(ex, exeName, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new FileNotFoundException($"{exeName} not found in the downloaded package.");
        return staged;
    }

    /// <summary>
    /// Launch a detached helper that waits for this process to exit, replaces the current
    /// executable with the staged one, and relaunches it. The caller must then exit the app.
    /// </summary>
    public static void ApplyAndRestart(string stagedExe)
    {
        var target = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the current executable path.");
        var pid = Environment.ProcessId;
        var targetDir = Path.GetDirectoryName(target)!;
        var marker = UpdateFailedMarkerPath(target);

        // The helper lives in the temp root, not in the staging directory — it deletes that
        // directory, and a script cannot sit in the folder it is removing.
        if (OperatingSystem.IsWindows())
        {
            var ps1 = Path.Combine(Path.GetTempPath(), "flexpad-apply-update.ps1");
            File.WriteAllText(ps1, UpdateApplyScript.Windows(pid, stagedExe, target, marker, targetDir, StageRoot, ps1));
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps1}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        else
        {
            var sh = Path.Combine(Path.GetTempPath(), "flexpad-apply-update.sh");
            File.WriteAllText(sh, UpdateApplyScript.Unix(pid, stagedExe, target, marker, targetDir, StageRoot, sh));
            Process.Start(new ProcessStartInfo { FileName = "/bin/sh", Arguments = $"\"{sh}\"", UseShellExecute = false });
        }
    }

    /// <summary>Path of the marker the apply helper drops next to the exe when the file swap fails.</summary>
    private static string UpdateFailedMarkerPath(string targetExe) =>
        Path.Combine(Path.GetDirectoryName(targetExe) ?? ".", ".flexpad-update-failed");

    /// <summary>
    /// True once if the previous apply helper reported a failed copy (it relaunched the old exe).
    /// Clears the marker so the warning shows only on the next start, not every start.
    /// </summary>
    public static bool ConsumeUpdateFailed()
    {
        try
        {
            var p = UpdateFailedMarkerPath(Environment.ProcessPath ?? "");
            if (File.Exists(p)) { File.Delete(p); return true; }
        }
        catch { /* ignore */ }
        return false;
    }

}
