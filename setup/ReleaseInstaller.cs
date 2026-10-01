using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MystiaModManager.Setup;

internal static class ReleaseInstaller
{
    // Primary: GitHub. Backup: Gitee. Override with env vars if needed.
    private static string GitHubRepo =>
        Environment.GetEnvironmentVariable("MYSTIA_GITHUB_REPO") ?? "wjjnb666/MystiaModManager";

    private static string GiteeRepo =>
        Environment.GetEnvironmentVariable("MYSTIA_GITEE_REPO") ?? "wjjnb666/MystiaModManager";

    /// <summary>Seconds before treating a GitHub request as too slow and falling back.</summary>
    private const int SlowTimeoutSeconds = 15;

    public static async Task InstallLatestAsync(string managerDir, Action<string>? progress = null)
    {
        Directory.CreateDirectory(managerDir);
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MystiaModManager.Setup/0.1");
        http.Timeout = TimeSpan.FromMinutes(10);

        var zipPath = Path.Combine(Path.GetTempPath(), "MystiaModManager-release.zip");
        Exception? lastError = null;

        // 1) Prefer GitHub (with speed/connect timeout)
        try
        {
            progress?.Invoke("正在从 GitHub 获取最新 Release…");
            var release = await FetchGitHubLatestAsync(http, SlowTimeoutSeconds, progress);
            var (name, url) = PickZipAsset(release);
            progress?.Invoke($"正在从 GitHub 下载 {name}…");
            await DownloadWithTimeoutAsync(http, url, zipPath, SlowTimeoutSeconds, progress);
            progress?.Invoke("GitHub 下载完成，正在解压…");
            ExtractZip(zipPath, managerDir);
            TryDelete(zipPath);
            progress?.Invoke("安装完成（来源：GitHub）");
            return;
        }
        catch (Exception ex)
        {
            lastError = ex;
            progress?.Invoke($"GitHub 不可用或过慢，切换到 Gitee…\n（{Short(ex)}）");
        }

        // 2) Fallback: Gitee
        try
        {
            progress?.Invoke("正在从 Gitee 获取最新 Release…");
            var release = await FetchGiteeLatestAsync(http);
            var (name, url) = PickZipAsset(release);
            progress?.Invoke($"正在从 Gitee 下载 {name}…");
            await DownloadFullyAsync(http, url, zipPath);
            progress?.Invoke("Gitee 下载完成，正在解压…");
            ExtractZip(zipPath, managerDir);
            TryDelete(zipPath);
            progress?.Invoke("安装完成（来源：Gitee）");
        }
        catch (Exception giteeEx)
        {
            throw new InvalidOperationException(
                "GitHub 与 Gitee 均无法下载 Release。\n" +
                $"GitHub：{Short(lastError)}\n" +
                $"Gitee：{Short(giteeEx)}\n" +
                $"仓库 GitHub={GitHubRepo}，Gitee={GiteeRepo}",
                giteeEx);
        }
    }

    private static async Task<JObject> FetchGitHubLatestAsync(
        HttpClient http, int timeoutSeconds, Action<string>? progress)
    {
        var api = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using var resp = await http.GetAsync(api, cts.Token);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }
        catch (OperationCanceledException)
        {
            progress?.Invoke($"GitHub API 超过 {timeoutSeconds}s，视为过慢");
            throw new TimeoutException($"GitHub API 超过 {timeoutSeconds} 秒未响应");
        }
    }

    private static async Task<JObject> FetchGiteeLatestAsync(HttpClient http)
    {
        // Gitee OpenAPI: GET /repos/{owner}/{repo}/releases/latest
        var api = $"https://gitee.com/api/v5/repos/{GiteeRepo}/releases/latest";
        using var resp = await http.GetAsync(api);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JObject.Parse(json);
    }

    private static (string Name, string Url) PickZipAsset(JObject release)
    {
        var assets = release["assets"] as JArray
            ?? throw new InvalidOperationException("Release 没有附件");

        var asset = assets.FirstOrDefault(a =>
        {
            var name = a.Value<string>("name") ?? "";
            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                   && name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) < 0;
        }) ?? assets.FirstOrDefault(a =>
            (a.Value<string>("name") ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (asset == null)
            throw new InvalidOperationException("Release 中没有 zip 附件");

        var name = asset.Value<string>("name") ?? "package.zip";
        // GitHub: browser_download_url
        // Gitee: browser_download_url (same field in API v5)
        var url = asset.Value<string>("browser_download_url")
            ?? throw new InvalidOperationException("缺少下载地址");
        return (name, url);
    }

    /// <summary>
    /// Download with a "time to first byte / early progress" timeout.
    /// If no bytes arrive within <paramref name="slowTimeoutSeconds"/>, cancel and throw.
    /// Once streaming starts, allow the full HttpClient.Timeout for the rest.
    /// </summary>
    private static async Task DownloadWithTimeoutAsync(
        HttpClient http,
        string url,
        string destPath,
        int slowTimeoutSeconds,
        Action<string>? progress)
    {
        using var cts = new CancellationTokenSource();
        var slowWatch = Stopwatch.StartNew();
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resp.EnsureSuccessStatusCode();

        using (var network = await resp.Content.ReadAsStreamAsync())
        using (var file = File.Create(destPath))
        {
            var buffer = new byte[81920];
            long total = 0;
            var gotFirstByte = false;

            while (true)
            {
                // If still waiting for first byte and too slow, abort
                if (!gotFirstByte && slowWatch.Elapsed.TotalSeconds > slowTimeoutSeconds)
                {
                    cts.Cancel();
                    throw new TimeoutException($"GitHub 下载超过 {slowTimeoutSeconds} 秒仍无数据，切换备用源");
                }

                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                if (!gotFirstByte)
                {
                    var remain = TimeSpan.FromSeconds(slowTimeoutSeconds) - slowWatch.Elapsed;
                    if (remain <= TimeSpan.Zero)
                        throw new TimeoutException($"GitHub 下载超过 {slowTimeoutSeconds} 秒仍无数据，切换备用源");
                    readCts.CancelAfter(remain);
                }

                int read;
                try
                {
                    read = await network.ReadAsync(buffer, 0, buffer.Length, readCts.Token);
                }
                catch (OperationCanceledException) when (!gotFirstByte)
                {
                    throw new TimeoutException($"GitHub 下载超过 {slowTimeoutSeconds} 秒仍无数据，切换备用源");
                }

                if (read <= 0) break;
                gotFirstByte = true;
                await file.WriteAsync(buffer, 0, read);
                total += read;
                if (total % (512 * 1024) < read)
                    progress?.Invoke($"正在从 GitHub 下载… {total / 1024} KB");
            }

            if (total == 0)
                throw new InvalidOperationException("下载内容为空");
        }
    }

    private static async Task DownloadFullyAsync(HttpClient http, string url, string destPath)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        using var network = await resp.Content.ReadAsStreamAsync();
        using var file = File.Create(destPath);
        await network.CopyToAsync(file);
    }

    private static void ExtractZip(string zipPath, string managerDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var dest = Path.Combine(managerDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }

    private static string Short(Exception? ex)
    {
        if (ex == null) return "未知错误";
        return ex.Message.Length > 120 ? ex.Message.Substring(0, 120) + "…" : ex.Message;
    }
}
