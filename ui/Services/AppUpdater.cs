using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
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

    public static void StartUpdater(string downloadUrl, string managerDir)
    {
        var updater = Path.Combine(managerDir, "MystiaModManager.Update.exe");
        if (!File.Exists(updater))
            throw new InvalidOperationException("缺少 MystiaModManager.Update.exe，请重新运行安装器。");
        var temp = Path.Combine(Path.GetTempPath(), "MystiaModManager.Update.exe");
        File.Copy(updater, temp, true);
        var psi = new ProcessStartInfo
        {
            FileName = temp,
            Arguments = "--pid " + Process.GetCurrentProcess().Id
                + " --dir \"" + managerDir.TrimEnd('\\') + "\""
                + " --url \"" + downloadUrl + "\"",
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath()
        };
        if (Process.Start(psi) == null)
            throw new InvalidOperationException("无法启动更新程序");
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
