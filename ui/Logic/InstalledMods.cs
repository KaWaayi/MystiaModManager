using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Izakaya.Rules;
using Newtonsoft.Json.Linq;

namespace MystiaModManager.Logic;

public sealed class InstalledMod
{
    public InstalledMod(string id, string version)
    {
        Id = id;
        Version = version;
    }

    public string Id { get; }
    public string Version { get; }
}

public sealed class ModLabel
{
    public ModLabel(
        string name,
        string version,
        string description,
        string author,
        string id,
        DateTime downloadedAt,
        DateTime updatedAt)
    {
        Name = name;
        Version = version;
        Description = description;
        Author = author;
        Id = id;
        DownloadedAt = downloadedAt;
        UpdatedAt = updatedAt;
    }

    public string Name { get; }
    public string Version { get; }
    public string Description { get; }
    public string Author { get; }
    public string Id { get; }
    public DateTime DownloadedAt { get; }
    public DateTime UpdatedAt { get; }

    public ModLabel WithTimes(DateTime downloadedAt, DateTime updatedAt)
        => new(Name, Version, Description, Author, Id, downloadedAt, updatedAt);
}

public static class InstalledMods
{
    public static IReadOnlyList<InstalledMod> Read(string profilePath)
    {
        var plugins = Path.Combine(profilePath, "BepInEx", "plugins");
        var found = new List<InstalledMod>();
        if (!Directory.Exists(plugins)) return found;
        foreach (var manifest in Directory.GetFiles(plugins, "Manifest.json", SearchOption.AllDirectories))
        {
            var mod = ReadManifest(File.ReadAllText(manifest));
            if (mod != null) found.Add(mod);
        }
        return found;
    }

    public static IReadOnlyList<ModNode> ReadNodes(string profilePath)
    {
        var plugins = Path.Combine(profilePath, "BepInEx", "plugins");
        var nodes = new List<ModNode>();
        if (!Directory.Exists(plugins)) return nodes;
        foreach (var dir in Directory.GetDirectories(plugins))
        {
            var folder = Path.GetFileName(dir);
            string? id = null;
            var hard = new List<string>();
            var manifest = Directory.GetFiles(dir, "Manifest.json", SearchOption.AllDirectories);
            if (manifest.Length > 0)
            {
                var obj = JObject.Parse(File.ReadAllText(manifest[0]));
                id = obj["id"]?.ToString()?.Trim();
                if (obj["dependencies"] is JArray deps)
                {
                    foreach (var item in deps)
                    {
                        var dep = item.Type == JTokenType.String ? item.ToString().Trim() : null;
                        if (!string.IsNullOrEmpty(dep)) hard.Add(dep!);
                    }
                }
            }
            nodes.Add(new ModNode(folder, string.IsNullOrEmpty(id) ? null : id, FolderEnabled(dir), hard));
        }
        return nodes;
    }

    static bool FolderEnabled(string dir)
    {
        var hasOn = false;
        var hasOff = false;
        foreach (var file in Directory.GetFiles(dir, "*.dll*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".dll.off", StringComparison.OrdinalIgnoreCase)) hasOff = true;
            else if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) hasOn = true;
        }
        return hasOn || !hasOff;
    }

    public static InstalledMod? ReadManifest(string json)
    {
        var obj = JObject.Parse(json);
        var idText = obj["id"]?.ToString()?.Trim();
        var versionText = obj["version"]?.ToString()?.Trim();
        if (idText == null || idText.Length == 0 || versionText == null || versionText.Length == 0)
            return null;
        return new InstalledMod(idText, versionText);
    }

    public static ModLabel ReadLabel(string folderPath, string folderName, string? configRoot)
    {
        var label = ReadLabelJson(FirstManifest(folderPath))
                    ?? ReadZipLabel(configRoot, folderName)
                    ?? new ModLabel(folderName, "", "", "", folderName, DateTime.MinValue, DateTime.MinValue);
        var id = string.IsNullOrEmpty(label.Id) ? folderName : label.Id;
        var times = PackageTimes(configRoot, folderPath, id, folderName);
        return label.WithTimes(times.Download, times.Update);
    }

    public static (DateTime Download, DateTime Update) PackageTimes(
        string? configRoot,
        string folderPath,
        string id,
        string folderName)
    {
        DateTime? first = null;
        DateTime? last = null;
        if (!string.IsNullOrEmpty(configRoot))
        {
            var dir = PackageFiles.DirectoryOf(configRoot!);
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "*.zip"))
                {
                    var stem = Path.GetFileNameWithoutExtension(file);
                    if (!BelongsTo(stem, id) && !BelongsTo(stem, folderName)) continue;
                    var created = File.GetCreationTimeUtc(file);
                    var written = File.GetLastWriteTimeUtc(file);
                    if (first == null || created < first) first = created;
                    if (last == null || written > last) last = written;
                }
            }
        }

        if (Directory.Exists(folderPath))
        {
            if (first == null) first = Directory.GetCreationTimeUtc(folderPath);
            if (last == null) last = Directory.GetLastWriteTimeUtc(folderPath);
        }

        return (first ?? DateTime.MinValue, last ?? DateTime.MinValue);
    }

    static bool BelongsTo(string stem, string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        return stem.StartsWith(key + "_", StringComparison.OrdinalIgnoreCase);
    }

    static string? FirstManifest(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return null;
        var files = Directory.GetFiles(folderPath, "Manifest.json", SearchOption.AllDirectories);
        return files.Length == 0 ? null : files[0];
    }

    static ModLabel? ReadZipLabel(string? configRoot, string folderName)
    {
        if (string.IsNullOrEmpty(configRoot)) return null;
        var dir = PackageFiles.DirectoryOf(configRoot!);
        var prefix = folderName + "_";
        string? bestVersion = null;
        string? bestPath = null;
        foreach (var file in Directory.GetFiles(dir, prefix + "*.zip"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var version = stem.Substring(prefix.Length);
            if (bestVersion == null || VersionOrder.Compare(version, bestVersion) > 0)
            {
                bestVersion = version;
                bestPath = file;
            }
        }
        if (bestPath == null) return null;
        using (var stream = File.OpenRead(bestPath))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.Equals("Manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                using (var reader = new StreamReader(entry.Open()))
                    return ReadLabelJson(reader.ReadToEnd());
            }
        }
        return null;
    }

    static ModLabel? ReadLabelJson(string? jsonOrPath)
    {
        if (string.IsNullOrEmpty(jsonOrPath)) return null;
        var json = jsonOrPath!;
        if (json.IndexOf('{') < 0 && File.Exists(json))
            json = File.ReadAllText(json);
        if (json.IndexOf('{') < 0) return null;
        try
        {
            var obj = JObject.Parse(json);
            var name = obj["name"]?.ToString()?.Trim();
            if (name == null || name.Length == 0) return null;
            var version = obj["version"]?.ToString()?.Trim() ?? "";
            var description = obj["description"]?.ToString()?.Trim() ?? "";
            var author = obj["author"]?.ToString()?.Trim() ?? "";
            var id = obj["id"]?.ToString()?.Trim() ?? "";
            return new ModLabel(name, version, description, author, id, DateTime.MinValue, DateTime.MinValue);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }
}
