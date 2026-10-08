using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;

namespace MystiaModManager.Logic;

public sealed class PackageFile
{
    public PackageFile(string sourcePath, string entryName)
    {
        SourcePath = sourcePath;
        EntryName = entryName;
    }

    public string SourcePath { get; }
    public string EntryName { get; }
}

public static class CrashPackage
{
    public static string FileName(DateTime time)
        => time.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".zip";

    public static string SaveFolder(string userProfile)
        => Path.Combine(userProfile, "AppData", "LocalLow", "Epicomic", "Touhou Mystia Izakaya", "Memory", "Save");

    public static IReadOnlyList<PackageFile> Collect(string profilePath, string userProfile)
    {
        var items = new List<PackageFile>();
        AddFile(items, Path.Combine(profilePath, "BepInEx", "LogOutput.log"), "BepInEx/LogOutput.log");
        AddFile(items, Path.Combine(profilePath, "BepInEx", "ErrorLog.log"), "BepInEx/ErrorLog.log");

        var save = SaveFolder(userProfile);
        if (!Directory.Exists(save)) return items;

        var root = Path.GetFullPath(save).TrimEnd('\\', '/');
        var prefix = root + Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var full = Path.GetFullPath(file);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            items.Add(new PackageFile(full, "Save/" + full.Substring(prefix.Length).Replace('\\', '/')));
        }
        return items;
    }

    public static void Create(string profilePath, string userProfile, string zipPath)
    {
        var items = Collect(profilePath, userProfile);
        var directory = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using (var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var item in items)
            {
                var entry = zip.CreateEntry(item.EntryName, CompressionLevel.Optimal);
                using (var input = new FileStream(item.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var output = entry.Open())
                    input.CopyTo(output);
            }
        }
    }

    private static void AddFile(List<PackageFile> items, string source, string entryName)
    {
        if (File.Exists(source))
            items.Add(new PackageFile(source, entryName));
    }
}
