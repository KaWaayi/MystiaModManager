using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MystiaModManager.Services;

internal sealed class UpdateOffer
{
    public string Tag { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Source { get; set; } = "";
}

internal static class AppUpdater
{
    private const int SlowTimeoutSeconds = 15;

    private static string GitHubRepo =>
        Environment.GetEnvironmentVariable("MYSTIA_GITHUB_REPO") ?? "KaWaayi/MystiaModManager";

    private static string GiteeRepo =>
        Environment.GetEnvironmentVariable("MYSTIA_GITEE_REPO") ?? "wjjnb666/MystiaModManager";

    public static string LocalVersionText
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
        }
    }

    public static bool IsNewer(string tag)
    {
        var remote = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(remote, out var rv)) return true;
        if (!Version.TryParse(LocalVersionText, out var local)) return true;
        return rv > local;
    }

    public static async Task<UpdateOffer> CheckAsync(Action<string>? progress = null)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MystiaModManager/" + LocalVersionText);
        http.Timeout = TimeSpan.FromMinutes(10);

        try
        {
            progress?.Invoke("正在从 GitHub 检查更新…");
            var release = await FetchAsync(
                http,
                $"https://api.github.com/repos/{GitHubRepo}/releases/latest",
                SlowTimeoutSeconds);
            var offer = PickZip(release, "GitHub");
            return offer;
        }
        catch (Exception ex)
        {
            progress?.Invoke($"GitHub 不可用，改查 Gitee…（{ex.Message}）");
        }

        progress?.Invoke("正在从 Gitee 检查更新…");
        var gitee = await FetchAsync(
            http,
            $"https://gitee.com/api/v5/repos/{GiteeRepo}/releases/latest",
            60);
        return PickZip(gitee, "Gitee");
    }

    public static async Task DownloadAsync(string url, string destPath, Action<string>? progress)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MystiaModManager/" + LocalVersionText);
        http.Timeout = TimeSpan.FromMinutes(10);
        progress?.Invoke("正在下载更新…");
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        using (var network = await resp.Content.ReadAsStreamAsync())
        using (var file = File.Create(destPath))
        {
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                int read = await network.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0) break;
                await file.WriteAsync(buffer, 0, read);
                total += read;
                if (total % (512 * 1024) < read)
                    progress?.Invoke($"正在下载更新… {total / 1024} KB");
            }
            if (total == 0) throw new InvalidOperationException("下载内容为空");
        }
    }

    public static void ScheduleReplaceAndRestart(string zipPath, string managerDir)
    {
        var ps1 = Path.Combine(Path.GetTempPath(), "MystiaModManager-update.ps1");
        var script = """
            $ErrorActionPreference = 'Stop'
            Start-Sleep -Seconds 2
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zip = [System.IO.Compression.ZipFile]::OpenRead($env:MYSTIA_UPDATE_ZIP)
            try {
              foreach ($entry in $zip.Entries) {
                if ([string]::IsNullOrEmpty($entry.Name)) { continue }
                $rel = $entry.FullName.Replace('/', '\')
                if ($rel -eq 'bootstrap.json') { continue }
                $dest = Join-Path $env:MYSTIA_UPDATE_DIR $rel
                $parent = Split-Path $dest -Parent
                if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dest, $true)
              }
            } finally {
              $zip.Dispose()
            }
            $exe = Join-Path $env:MYSTIA_UPDATE_DIR 'MystiaModManager.exe'
            Start-Process -FilePath $exe -WorkingDirectory $env:MYSTIA_UPDATE_DIR
            """;
        File.WriteAllText(ps1, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        psi.EnvironmentVariables["MYSTIA_UPDATE_ZIP"] = zipPath;
        psi.EnvironmentVariables["MYSTIA_UPDATE_DIR"] = managerDir;
        if (Process.Start(psi) == null)
            throw new InvalidOperationException("无法启动更新进程");
    }

    private static async Task<JObject> FetchAsync(HttpClient http, string url, int timeoutSeconds)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using var resp = await http.GetAsync(url, cts.Token);
            resp.EnsureSuccessStatusCode();
            return JObject.Parse(await resp.Content.ReadAsStringAsync());
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"超过 {timeoutSeconds} 秒未响应");
        }
    }

    private static UpdateOffer PickZip(JObject release, string source)
    {
        var assets = release["assets"] as JArray
            ?? throw new InvalidOperationException($"{source} Release 没有附件");
        var asset = assets.FirstOrDefault(a =>
        {
            var name = a.Value<string>("name") ?? "";
            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                   && name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) < 0;
        });
        if (asset == null)
            throw new InvalidOperationException($"{source} Release 中没有管理器 zip");
        return new UpdateOffer
        {
            Tag = release.Value<string>("tag_name") ?? "",
            Name = asset.Value<string>("name") ?? "MystiaModManager.zip",
            Url = asset.Value<string>("browser_download_url")
                ?? throw new InvalidOperationException($"{source} 缺少下载地址"),
            Source = source
        };
    }
}
