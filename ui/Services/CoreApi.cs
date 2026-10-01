using System;
using System.Collections.Generic;
using MystiaModManager.Models;
using MystiaModManager.Native;
using Newtonsoft.Json;

namespace MystiaModManager.Services;

public static class CoreApi
{
    public static DefaultPaths DetectDefaultPaths()
        => NativeMethods.Call(NativeMethods.DetectDefaultPaths).ToObject<DefaultPaths>()!;

    public static DefaultPaths DefaultPathsForGame(string gamePath)
        => NativeMethods.Call(() => NativeMethods.DefaultPathsForGame(gamePath)).ToObject<DefaultPaths>()!;

    public static ManagerSettings InitManager(string gamePath, string managerPath, string configRoot)
        => NativeMethods.Call(() => NativeMethods.InitManager(gamePath, managerPath, configRoot))
            .ToObject<ManagerSettings>()!;

    public static ManagerSettings LoadManager(string configRoot)
        => NativeMethods.Call(() => NativeMethods.LoadManager(configRoot)).ToObject<ManagerSettings>()!;

    public static void SaveManager(string configRoot, ManagerSettings settings)
    {
        var json = JsonConvert.SerializeObject(settings);
        NativeMethods.Call(() => NativeMethods.SaveManager(configRoot, json));
    }

    public static List<ProfileInfo> ListProfiles(string configRoot)
        => NativeMethods.Call(() => NativeMethods.ListProfiles(configRoot)).ToObject<List<ProfileInfo>>()!;

    public static List<BepInExBuild> ListBepInExBuilds()
        => NativeMethods.Call(NativeMethods.ListBuilds).ToObject<List<BepInExBuild>>()!;

    public static string CreateProfile(string configRoot, string name, string url, string version)
        => NativeMethods.Call(() => NativeMethods.CreateProfile(configRoot, name, url, version)).ToObject<string>()!;

    public static void UpdateBepInEx(string profilePath, string url, string version)
        => NativeMethods.Call(() => NativeMethods.UpdateBepInEx(profilePath, url, version));

    public static string RenameProfile(string configRoot, string oldName, string newName)
        => NativeMethods.Call(() => NativeMethods.RenameProfile(configRoot, oldName, newName)).ToObject<string>()!;

    public static void DeleteProfile(string configRoot, string name)
        => NativeMethods.Call(() => NativeMethods.DeleteProfile(configRoot, name));

    public static List<ModInfo> ListMods(string profilePath)
        => NativeMethods.Call(() => NativeMethods.ListMods(profilePath)).ToObject<List<ModInfo>>()!;

    public static string InstallMod(string profilePath, string sourcePath)
        => NativeMethods.Call(() => NativeMethods.InstallMod(profilePath, sourcePath)).ToObject<string>()!;

    public static void SetModEnabled(string profilePath, string modName, bool enabled)
        => NativeMethods.Call(() => NativeMethods.SetModEnabled(profilePath, modName, enabled));

    public static void UninstallMod(string profilePath, string modName)
        => NativeMethods.Call(() => NativeMethods.UninstallMod(profilePath, modName));

    public static void WriteDoorstopHook(string gamePath, string profilePath)
        => NativeMethods.Call(() => NativeMethods.WriteDoorstopHook(gamePath, profilePath));

    public static LaunchInfo PrepareLaunch(string gamePath, string profilePath)
        => NativeMethods.Call(() => NativeMethods.PrepareLaunch(gamePath, profilePath)).ToObject<LaunchInfo>()!;

    public static bool IsGameRunning() => NativeMethods.mystia_is_game_running();

    public static string ProfilePath(string configRoot, string name)
        => NativeMethods.Call(() => NativeMethods.ProfilePath(configRoot, name)).ToObject<string>()!;
}
