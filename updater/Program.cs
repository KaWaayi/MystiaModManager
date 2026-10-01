using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace MystiaModManager.Update;

internal static class Program
{
    private const int SlowTimeoutSeconds = 15;

    [STAThread]
    private static int Main(string[] args)
    {
        string? dir = null;
        var pid = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--pid" && i + 1 < args.Length) pid = int.Parse(args[++i]);
            else if (args[i] == "--dir" && i + 1 < args.Length) dir = args[++i];
        }

        if (string.IsNullOrWhiteSpace(dir))
        {
            MessageBox.Show("更新程序缺少参数。", "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        var targetDir = dir!;

        try
        {
            var local = ReadLocalVersion(targetDir);
            var offer = FindLatest();
            if (!IsNewer(offer.Tag, local))
            {
                MessageBox.Show($"当前已是最新版本 {local}。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            var answer = MessageBox.Show(
                $"发现 {offer.Tag}（当前 {local}）。\n将从 {offer.Source} 下载并重启管理器。配置目录不会被覆盖。",
                "检查更新",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (answer != DialogResult.OK) return 0;

            WaitForManager(pid);
            var zipPath = Path.Combine(Path.GetTempPath(), "MystiaModManager-update.zip");
            Download(offer.Url, zipPath);
            Extract(zipPath, targetDir);
            try { File.Delete(zipPath); } catch { /* ignore */ }
            StartManager(targetDir);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (File.Exists(Path.Combine(targetDir, "MystiaModManager.exe")))
                StartManager(targetDir);
            return 1;
        }
    }

    private static string ReadLocalVersion(string dir)
    {
        var exe = Path.Combine(dir, "MystiaModManager.exe");
        if (!File.Exists(exe)) return "0.0.0";
        var info = FileVersionInfo.GetVersionInfo(exe);
        return $"{info.FileMajorPart}.{info.FileMinorPart}.{Math.Max(info.FileBuildPart, 0)}";
    }

    private static bool IsNewer(string tag, string localText)
    {
        var remote = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(remote, out var rv)) return true;
        if (!Version.TryParse(localText, out var local)) return true;
        return rv > local;
    }

    private static Offer FindLatest()
    {
        var github = Environment.GetEnvironmentVariable("MYSTIA_GITHUB_REPO") ?? "KaWaayi/MystiaModManager";
        var gitee = Environment.GetEnvironmentVariable("MYSTIA_GITEE_REPO") ?? "wjjnb666/MystiaModManager";
        try
        {
            var json = GetString($"https://api.github.com/repos/{github}/releases/latest", SlowTimeoutSeconds);
            return ParseOffer(json, "GitHub");
        }
        catch
        {
            var json = GetString($"https://gitee.com/api/v5/repos/{gitee}/releases/latest", 60);
            return ParseOffer(json, "Gitee");
        }
    }

    private static Offer ParseOffer(string json, string source)
    {
        var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
        if (!tag.Success) throw new InvalidOperationException($"{source} Release 没有版本号");
        foreach (Match match in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
        {
            var url = match.Groups[1].Value.Replace("\\u0026", "&");
            var name = url.Substring(url.LastIndexOf('/') + 1);
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return new Offer(tag.Groups[1].Value, url, source);
            }
        }
        throw new InvalidOperationException($"{source} Release 中没有管理器 zip");
    }

    private static string GetString(string url, int timeoutSeconds)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MystiaModManager.Update/1");
        http.Timeout = TimeSpan.FromSeconds(timeoutSeconds + 5);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var resp = http.GetAsync(url, cts.Token).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static void WaitForManager(int pid)
    {
        if (pid <= 0) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            process.CloseMainWindow();
            if (!process.WaitForExit(8000))
                process.Kill();
            process.WaitForExit();
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        Thread.Sleep(400);
    }

    private static void Download(string url, string destPath)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MystiaModManager.Update/1");
        http.Timeout = TimeSpan.FromMinutes(10);
        using var resp = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        using var network = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var file = File.Create(destPath);
        network.CopyTo(file);
        if (file.Length == 0) throw new InvalidOperationException("下载内容为空");
    }

    private static void Extract(string zipPath, string managerDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var rel = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (string.Equals(rel, "bootstrap.json", StringComparison.OrdinalIgnoreCase)) continue;
            var dest = Path.Combine(managerDir, rel);
            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    private static void StartManager(string dir)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(dir, "MystiaModManager.exe"),
            WorkingDirectory = dir,
            UseShellExecute = true
        });
    }

    private sealed class Offer
    {
        public Offer(string tag, string url, string source)
        {
            Tag = tag;
            Url = url;
            Source = source;
        }

        public string Tag { get; }
        public string Url { get; }
        public string Source { get; }
    }
}
