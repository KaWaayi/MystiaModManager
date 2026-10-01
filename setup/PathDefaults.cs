using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MystiaModManager.Setup;

internal static class PathDefaults
{
    private const string AppId = "1584090";
    private const string GameFolder = "Touhou Mystia Izakaya";
    private const string GameExe = "Touhou Mystia Izakaya.exe";

    public static string? DetectGamePath()
    {
        var steam = SteamPath();
        if (steam == null) return null;
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return null;
        var text = File.ReadAllText(vdf);
        foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
        {
            var lib = m.Groups[1].Value.Replace("\\\\", "\\");
            var candidate = Path.Combine(lib, "steamapps", "common", GameFolder);
            if (File.Exists(Path.Combine(candidate, GameExe)))
                return candidate;
            var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{AppId}.acf");
            if (File.Exists(manifest) && File.Exists(Path.Combine(candidate, GameExe)))
                return candidate;
        }
        return null;
    }

    private static string? SteamPath()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
        {
            var p = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(p))
            {
                var normalized = p!.Replace('/', '\\');
                if (Directory.Exists(normalized))
                    return normalized;
            }
        }
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
        {
            var p = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                return p;
        }
        return null;
    }

    public static (string Manager, string Config) DefaultsForGame(string gamePath)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(gamePath)) ?? "C:\\";
        var isC = root.StartsWith("C:", StringComparison.OrdinalIgnoreCase);
        if (isC)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return (
                Path.Combine(appData, "MystiaModManager"),
                Path.Combine(appData, "MystiaModManagerConfig"));
        }
        return (
            Path.Combine(root, "MystiaModManager"),
            Path.Combine(root, "MystiaModManagerConfig"));
    }
}
