using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MystiaModManager.Setup;

internal static class UpdateMode
{
    public static bool IsUpdate(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--dir") return true;
        }
        return false;
    }

    public static int Run(string[] args)
    {
        return Task.Run(() => RunCore(args)).GetAwaiter().GetResult();
    }

    static int RunCore(string[] args)
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
            if (!IsNewer(offer.Version, local))
            {
                MessageBox.Show("当前已是最新版本 " + local + "。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            var answer = MessageBox.Show(
                "发现 " + offer.Version + "（当前 " + local + "）。\n将从服务器下载并重启管理器。配置目录不会被覆盖。",
                "检查更新",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (answer != DialogResult.OK) return 0;
            WaitForManager(pid);
            var zipPath = Path.Combine(Path.GetTempPath(), "MystiaModManager-update.zip");
            Download(offer.Url, zipPath);
            Extract(zipPath, targetDir);
            try { File.Delete(zipPath); } catch { }
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

    static string ReadLocalVersion(string dir)
    {
        var exe = Path.Combine(dir, "MystiaModManager.exe");
        if (!File.Exists(exe)) return "0.0.0";
        var info = FileVersionInfo.GetVersionInfo(exe);
        return info.FileMajorPart + "." + info.FileMinorPart + "." + Math.Max(info.FileBuildPart, 0);
    }

    static bool IsNewer(string tag, string localText)
    {
        var remote = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(remote, out var rv)) return true;
        if (!Version.TryParse(localText, out var local)) return true;
        return rv > local;
    }

    static Offer FindLatest()
    {
        var json = GetString("http://47.116.214.184/manager/version");
        var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
        if (!match.Success) throw new InvalidOperationException("服务器没有返回管理器版本");
        return new Offer(match.Groups[1].Value, "http://47.116.214.184/manager/file");
    }

    static string GetString(string url)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(20);
        using var resp = http.GetAsync(url).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    static void WaitForManager(int pid)
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
        }
        Thread.Sleep(400);
    }

    static void Download(string url, string destPath)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(10);
        using var resp = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        using var network = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var file = File.Create(destPath);
        network.CopyTo(file);
        if (file.Length == 0) throw new InvalidOperationException("下载内容为空");
    }

    static void Extract(string zipPath, string managerDir)
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

    static void StartManager(string dir)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(dir, "MystiaModManager.exe"),
            WorkingDirectory = dir,
            UseShellExecute = true
        });
    }

    sealed class Offer
    {
        public Offer(string version, string url)
        {
            Version = version;
            Url = url;
        }

        public string Version { get; }
        public string Url { get; }
    }
}
