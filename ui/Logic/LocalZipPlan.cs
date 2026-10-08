using System;
using System.Collections.Generic;
using System.IO;
using Izakaya.Rules;

namespace MystiaModManager.Logic;

public static class LocalZipPlan
{
    public static IReadOnlyList<ModUpdateItem> NewerThanInstalled(string configRoot, IReadOnlyList<InstalledMod> installed)
    {
        var dir = PackageFiles.DirectoryOf(configRoot);
        var chosen = new List<ModUpdateItem>();
        foreach (var mod in installed)
        {
            var latest = LatestVersion(dir, mod.Id);
            if (latest != null && VersionOrder.IsNewer(latest, mod.Version))
                chosen.Add(new ModUpdateItem(mod.Id, mod.Version, latest));
        }
        return chosen;
    }

    public static string? ZipPath(string configRoot, string id, string version)
    {
        var path = Path.Combine(PackageFiles.DirectoryOf(configRoot), id + "_" + version + ".zip");
        return File.Exists(path) ? path : null;
    }

    static string? LatestVersion(string directory, string id)
    {
        if (!Directory.Exists(directory)) return null;
        var prefix = id + "_";
        string? best = null;
        foreach (var file in Directory.GetFiles(directory, prefix + "*.zip"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var version = stem.Substring(prefix.Length);
            if (best == null || VersionOrder.Compare(version, best) > 0)
                best = version;
        }
        return best;
    }
}
