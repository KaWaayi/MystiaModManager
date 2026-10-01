using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Windows.Forms;

namespace MystiaModManager.Update;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? dir = null;
        string? url = null;
        var pid = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--pid" && i + 1 < args.Length) pid = int.Parse(args[++i]);
            else if (args[i] == "--dir" && i + 1 < args.Length) dir = args[++i];
            else if (args[i] == "--url" && i + 1 < args.Length) url = args[++i];
        }

        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("更新程序缺少参数。", "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        var targetDir = dir!;
        var downloadUrl = url!;

        try
        {
            if (pid > 0)
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.WaitForExit();
                }
                catch (ArgumentException)
                {
                    // The manager has already exited.
                }
            }
            Thread.Sleep(400);

            var zipPath = Path.Combine(Path.GetTempPath(), "MystiaModManager-update.zip");
            Download(downloadUrl, zipPath);
            Extract(zipPath, targetDir);
            try { File.Delete(zipPath); } catch { /* ignore */ }

            var exe = Path.Combine(targetDir, "MystiaModManager.exe");
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = targetDir,
                UseShellExecute = true
            });
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            var exe = Path.Combine(targetDir, "MystiaModManager.exe");
            if (File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = targetDir,
                    UseShellExecute = true
                });
            }
            return 1;
        }
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
}
