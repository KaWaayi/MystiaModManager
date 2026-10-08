using System;
using System.Collections.Generic;

namespace MystiaModManager.Logic;

public sealed class ModNode
{
    public ModNode(string folder, string? id, bool enabled, IReadOnlyList<string> hardDependencies)
    {
        Folder = folder;
        Id = id;
        Enabled = enabled;
        HardDependencies = hardDependencies;
    }

    public string Folder { get; }
    public string? Id { get; }
    public bool Enabled { get; }
    public IReadOnlyList<string> HardDependencies { get; }
}

public static class ModActionPlan
{
    public static IReadOnlyList<string> FoldersToEnable(IReadOnlyList<string> selected, IReadOnlyList<ModNode> mods)
    {
        var byFolder = Index(mods);
        var byId = IndexById(mods);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in selected)
        {
            if (!byFolder.ContainsKey(folder)) continue;
            AddEnable(folder, byFolder, byId, result, seen);
        }
        return result;
    }

    public static IReadOnlyList<string> ExtraStateChanges(
        IReadOnlyList<string> plan,
        IReadOnlyList<string> selected,
        IReadOnlyList<ModNode> mods,
        bool enabling)
    {
        var selectedSet = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        var byFolder = Index(mods);
        var extra = new List<string>();
        foreach (var folder in plan)
        {
            if (selectedSet.Contains(folder)) continue;
            if (!byFolder.TryGetValue(folder, out var node)) continue;
            if (enabling)
            {
                if (!node.Enabled) extra.Add(folder);
            }
            else if (node.Enabled)
            {
                extra.Add(folder);
            }
        }
        return extra;
    }

    public static IReadOnlyList<string> FoldersToDisable(IReadOnlyList<string> selected, IReadOnlyList<ModNode> mods)
    {
        var byFolder = Index(mods);
        var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in selected)
        {
            if (byFolder.TryGetValue(folder, out var node) && node.Id != null)
                selectedIds.Add(node.Id);
        }
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in selected)
            Add(result, seen, folder);
        foreach (var mod in mods)
        {
            if (mod.Id == null) continue;
            foreach (var dep in mod.HardDependencies)
            {
                if (selectedIds.Contains(dep))
                    Add(result, seen, mod.Folder);
            }
        }
        return result;
    }

    static void AddEnable(
        string folder,
        Dictionary<string, ModNode> byFolder,
        Dictionary<string, ModNode> byId,
        List<string> result,
        HashSet<string> seen)
    {
        if (!byFolder.TryGetValue(folder, out var node)) return;
        if (node.Id != null)
        {
            foreach (var dep in node.HardDependencies)
            {
                if (byId.TryGetValue(dep, out var target))
                    AddEnable(target.Folder, byFolder, byId, result, seen);
            }
        }
        Add(result, seen, folder);
    }

    static void Add(List<string> result, HashSet<string> seen, string folder)
    {
        if (seen.Add(folder)) result.Add(folder);
    }

    static Dictionary<string, ModNode> Index(IReadOnlyList<ModNode> mods)
    {
        var map = new Dictionary<string, ModNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
            map[mod.Folder] = mod;
        return map;
    }

    static Dictionary<string, ModNode> IndexById(IReadOnlyList<ModNode> mods)
    {
        var map = new Dictionary<string, ModNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            if (mod.Id != null) map[mod.Id] = mod;
        }
        return map;
    }
}
