using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MystiaModManager.Logic;

public static class ZipLayout
{
    public static string? Destination(string entryName, string profilePath, string gamePath)
    {
        var name = entryName.Replace('\\', '/');
        if (name.EndsWith("/")) return null;
        var slash = name.IndexOf('/');
        if (slash <= 0) return null;
        var top = name.Substring(0, slash);
        var rest = name.Substring(slash + 1);
        if (rest.Length == 0 || rest.EndsWith("/")) return null;
        string root;
        if (top.Equals("plugins", StringComparison.OrdinalIgnoreCase))
            root = Path.Combine(profilePath, "BepInEx", "plugins");
        else if (top.Equals("patchers", StringComparison.OrdinalIgnoreCase))
            root = Path.Combine(profilePath, "BepInEx", "patchers");
        else if (top.Equals("ResourceEx", StringComparison.OrdinalIgnoreCase))
            root = Path.Combine(profilePath, "ResourceEx");
        else
            return null;
        return Path.Combine(root, rest.Replace('/', Path.DirectorySeparatorChar));
    }

    public static void Extract(string zipPath, string profilePath, string gamePath)
    {
        using (var stream = File.OpenRead(zipPath))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            ZipArchiveEntry? manifest = null;
            var pluginRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pluginsRoot = Path.Combine(profilePath, "BepInEx", "plugins");
            foreach (var entry in zip.Entries)
            {
                if (IsRootManifest(entry.FullName))
                {
                    manifest = entry;
                    continue;
                }
                var dest = Destination(entry.FullName, profilePath, gamePath);
                if (dest == null) continue;
                RememberPluginRoot(dest, pluginsRoot, pluginRoots);
                if (File.Exists(dest + ".off"))
                    dest = dest + ".off";
                var dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                entry.ExtractToFile(dest, true);
            }
            if (manifest == null) return;
            foreach (var dir in pluginRoots)
            {
                Directory.CreateDirectory(dir);
                manifest.ExtractToFile(Path.Combine(dir, "Manifest.json"), true);
            }
        }
    }

    public static void ExtractResources(string zipPath, string profilePath)
    {
        var root = Path.GetFullPath(Path.Combine(profilePath, "ResourceEx"));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using (var stream = File.OpenRead(zipPath))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                var dest = Destination(entry.FullName, profilePath, "");
                if (dest == null) continue;
                var full = Path.GetFullPath(dest);
                if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(full) && File.GetLastWriteTimeUtc(full) >= File.GetLastWriteTimeUtc(zipPath))
                    continue;
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                entry.ExtractToFile(full, true);
            }
        }
    }

    static bool IsRootManifest(string entryName)
    {
        var name = entryName.Replace('\\', '/');
        return name.Equals("Manifest.json", StringComparison.OrdinalIgnoreCase);
    }

    static void RememberPluginRoot(string dest, string pluginsRoot, HashSet<string> pluginRoots)
    {
        var root = Path.GetFullPath(pluginsRoot);
        var full = Path.GetFullPath(dest);
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
        var rest = full.Substring(prefix.Length);
        var slash = rest.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        if (slash <= 0) return;
        pluginRoots.Add(Path.Combine(root, rest.Substring(0, slash)));
    }
}
