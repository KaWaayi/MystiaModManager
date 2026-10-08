using System;

namespace MystiaModManager.Logic;

public readonly struct ModSortKeyset
{
    public ModSortKeyset(string name, string version, string author, DateTime downloadedAt, DateTime updatedAt)
    {
        Name = name ?? "";
        Version = version ?? "";
        Author = author ?? "";
        DownloadedAt = downloadedAt;
        UpdatedAt = updatedAt;
    }

    public string Name { get; }
    public string Version { get; }
    public string Author { get; }
    public DateTime DownloadedAt { get; }
    public DateTime UpdatedAt { get; }
}

public static class ModListSort
{
    public const int Name = 0;
    public const int Version = 1;
    public const int DownloadTime = 2;
    public const int UpdateTime = 3;
    public const int Author = 4;

    public static int Compare(int key, bool ascending, ModSortKeyset left, ModSortKeyset right)
    {
        var order = key switch
        {
            Version => Izakaya.Rules.VersionOrder.Compare(left.Version, right.Version),
            DownloadTime => left.DownloadedAt.CompareTo(right.DownloadedAt),
            UpdateTime => left.UpdatedAt.CompareTo(right.UpdatedAt),
            Author => string.Compare(left.Author, right.Author, StringComparison.OrdinalIgnoreCase),
            _ => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
        };
        if (order == 0)
            order = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        return ascending ? order : -order;
    }
}
