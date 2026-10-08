using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace MystiaModManager.Setup;

internal static class ReleaseInstaller
{
        private const string PackageUrl = "http://47.116.214.184/manager/file";

        public static async Task InstallLatestAsync(string managerDir, Action<string>? progress = null)
        {
            Directory.CreateDirectory(managerDir);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MystiaModManager.Setup/0.1");
            http.Timeout = TimeSpan.FromMinutes(10);

            var zipPath = Path.Combine(Path.GetTempPath(), "MystiaModManager-release.zip");
            progress?.Invoke("正在从服务器下载管理器…");
            await DownloadFullyAsync(http, PackageUrl, zipPath);
            progress?.Invoke("下载完成，正在解压…");
            ExtractZip(zipPath, managerDir);
            TryDelete(zipPath);
            progress?.Invoke("安装完成");
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
}
