using System;
using System.IO;
using Microsoft.Win32;

namespace MystiaModManager.Logic;

public static class SteamLaunch
{
    public const string Arguments = "-applaunch 1584090";

    public static string ExeIn(string installDir)
    {
        var dir = installDir.Replace('/', '\\').Trim().TrimEnd('\\');
        return Path.Combine(dir, "Steam.exe");
    }

    public static string? Choose(string? hkcuSteamPath, string? hklmSteamPath, Func<string, bool> fileExists)
    {
        var hkcu = ExistingExe(hkcuSteamPath, fileExists);
        if (hkcu != null) return hkcu;
        return ExistingExe(hklmSteamPath, fileExists);
    }

    public static string? FindInstalled()
    {
        try
        {
            string? hkcu = null;
            string? hklm = null;
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                hkcu = key?.GetValue("SteamPath") as string;
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
            {
                if (key != null)
                {
                    hklm = key.GetValue("SteamPath") as string;
                    if (string.IsNullOrWhiteSpace(hklm))
                        hklm = key.GetValue("InstallPath") as string;
                }
            }
            return Choose(hkcu, hklm, File.Exists);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ExistingExe(string? installDir, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(installDir)) return null;
        var exe = ExeIn(installDir!);
        return fileExists(exe) ? exe : null;
    }
}
