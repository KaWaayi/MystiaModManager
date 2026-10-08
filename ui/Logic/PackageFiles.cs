using System;
using System.Diagnostics;
using System.IO;

namespace MystiaModManager.Logic;

public static class PackageFiles
{
    public static string DirectoryOf(string configRoot)
    {
        if (configRoot == null) throw new ArgumentNullException(nameof(configRoot));
        var dir = Path.Combine(configRoot, "download");
        Directory.CreateDirectory(dir);
        if (!Directory.Exists(configRoot)) return dir;
        foreach (var zip in Directory.GetFiles(configRoot, "*.zip"))
        {
            var dest = Path.Combine(dir, Path.GetFileName(zip));
            if (string.Equals(Path.GetFullPath(zip), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.Exists(dest))
            {
                if (File.GetLastWriteTimeUtc(zip) > File.GetLastWriteTimeUtc(dest))
                {
                    File.Delete(dest);
                    File.Move(zip, dest);
                }
                else
                {
                    File.Delete(zip);
                }
            }
            else
            {
                File.Move(zip, dest);
            }
        }
        return dir;
    }

    public static string SavePath(string configRoot, string fileName)
        => Path.Combine(DirectoryOf(configRoot), Path.GetFileName(fileName));
}

public static class ResourcePacks
{
    public static void Sync(string configRoot, string profilePath)
    {
        var dir = PackageFiles.DirectoryOf(configRoot);
        foreach (var zip in Directory.GetFiles(dir, "*.zip"))
            ZipLayout.ExtractResources(zip, profilePath);
    }
}

public static class ResourceExLink
{
    public static void Attach(string gamePath, string profilePath)
    {
        var source = Path.GetFullPath(Path.Combine(profilePath, "ResourceEx"));
        Directory.CreateDirectory(source);
        var link = Path.GetFullPath(Path.Combine(gamePath, "ResourceEx"));
        if (Directory.Exists(link))
        {
            if (IsReparse(link))
            {
                Directory.Delete(link);
            }
            else if (HasEntries(link))
            {
                throw new InvalidOperationException("游戏目录的 ResourceEx 里已有文件，没有改成配置档链接。");
            }
            else
            {
                Directory.Delete(link);
            }
        }
        var start = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c mklink /J \"" + link + "\" \"" + source + "\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using (var process = Process.Start(start))
        {
            if (process == null) throw new InvalidOperationException("无法创建 ResourceEx 链接");
            process.WaitForExit();
            if (process.ExitCode != 0 || !Directory.Exists(link))
                throw new InvalidOperationException("无法把游戏目录的 ResourceEx 指到配置档");
        }
    }

    public static void Detach(string gamePath)
    {
        var link = Path.Combine(gamePath, "ResourceEx");
        if (Directory.Exists(link) && IsReparse(link))
            Directory.Delete(link);
    }

    static bool IsReparse(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    static bool HasEntries(string path)
    {
        foreach (var unused in Directory.EnumerateFileSystemEntries(path))
        {
            if (unused != null) return true;
        }
        return false;
    }
}
