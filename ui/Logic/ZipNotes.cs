using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MystiaModManager.Logic;

public sealed class ZipNotes
{
    public ZipNotes(string readme, string tree, byte[]? icon)
    {
        Readme = readme;
        Tree = tree;
        Icon = icon;
    }

    public string Readme { get; }
    public string Tree { get; }
    public byte[]? Icon { get; }

    public static ZipNotes Read(string zipPath)
    {
        var readme = new StringBuilder();
        var tree = new StringBuilder();
        byte[]? icon = null;
        using (var stream = File.OpenRead(zipPath))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith("/")) continue;
                if (IsRootFile(name, "README.md"))
                {
                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
                    readme.Append(reader.ReadToEnd());
                    continue;
                }
                if (IsRootFile(name, "icon.ico"))
                {
                    using var input = entry.Open();
                    using var memory = new MemoryStream();
                    input.CopyTo(memory);
                    icon = memory.ToArray();
                    continue;
                }
                if (InPack(name))
                    tree.AppendLine(name);
            }
        }
        return new ZipNotes(readme.ToString().Trim(), tree.ToString().Trim(), icon);
    }

    public static string? FindZip(string configRoot, string id, string? version)
    {
        if (string.IsNullOrEmpty(version)) return null;
        return LocalZipPlan.ZipPath(configRoot, id, version!);
    }

    static bool IsRootFile(string name, string file)
    {
        return name.Equals(file, StringComparison.OrdinalIgnoreCase);
    }

    static bool InPack(string name)
    {
        return name.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ResourceEx/", StringComparison.OrdinalIgnoreCase);
    }
}

public static class CfgRange
{
    public static bool TryParse(string? hint, out double min, out double max)
    {
        min = 0;
        max = 0;
        if (string.IsNullOrWhiteSpace(hint)) return false;
        var text = hint!.Trim();
        if (text.StartsWith("From ", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(5).Trim();
        var split = text.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
        if (split <= 0) return false;
        if (!double.TryParse(text.Substring(0, split).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out min))
            return false;
        if (!double.TryParse(text.Substring(split + 4).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out max))
            return false;
        return max > min;
    }
}
