using System.Collections.Generic;
using Izakaya.Rules;

namespace MystiaModManager.Logic;

public sealed class ModUpdateItem
{
    public ModUpdateItem(string id, string? localVersion, string remoteVersion)
    {
        Id = id;
        LocalVersion = localVersion;
        RemoteVersion = remoteVersion;
    }

    public string Id { get; }
    public string? LocalVersion { get; }
    public string RemoteVersion { get; }
}

public static class ModUpdatePlan
{
    public static IReadOnlyList<ModUpdateItem> Select(
        IEnumerable<InstalledMod> installed,
        IEnumerable<MarketMod> remote)
    {
        var latest = new Dictionary<string, string>();
        foreach (var mod in remote)
        {
            if (mod.LatestVersion == null || mod.LatestVersion.Length == 0) continue;
            latest[mod.Id] = mod.LatestVersion;
        }

        var chosen = new List<ModUpdateItem>();
        foreach (var mod in installed)
        {
            if (!latest.TryGetValue(mod.Id, out var remoteVersion)) continue;
            if (VersionOrder.IsNewer(remoteVersion, mod.Version))
                chosen.Add(new ModUpdateItem(mod.Id, mod.Version, remoteVersion));
        }
        return chosen;
    }

    public static string Banner(int count)
        => count <= 0 ? "" : "您有" + count + "个模组更新可用。是否要更新全部？";
}
