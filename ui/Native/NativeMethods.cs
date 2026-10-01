using System;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MystiaModManager.Native;

internal static class NativeMethods
{
    private const string DllName = "mystia_core";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mystia_free_string(IntPtr s);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_detect_default_paths();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_default_paths_for_game(IntPtr gamePath);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_init_manager(IntPtr gamePath, IntPtr managerPath, IntPtr configRoot);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_load_manager(IntPtr configRoot);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_save_manager(IntPtr configRoot, IntPtr json);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_list_profiles(IntPtr configRoot);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_list_bepinex_builds();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_create_profile(IntPtr configRoot, IntPtr name, IntPtr downloadUrl, IntPtr versionLabel);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_update_bepinex(IntPtr profilePath, IntPtr downloadUrl, IntPtr versionLabel);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_rename_profile(IntPtr configRoot, IntPtr oldName, IntPtr newName);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_delete_profile(IntPtr configRoot, IntPtr name);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_list_mods(IntPtr profilePath);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_install_mod(IntPtr profilePath, IntPtr sourcePath);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_set_mod_enabled(IntPtr profilePath, IntPtr modName, [MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_uninstall_mod(IntPtr profilePath, IntPtr modName);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_write_doorstop_hook(IntPtr gamePath, IntPtr profilePath);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_prepare_launch(IntPtr gamePath, IntPtr profilePath);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mystia_is_game_running();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mystia_profile_path(IntPtr configRoot, IntPtr name);

    private static IntPtr AllocUtf8(string? value)
    {
        value ??= "";
        var bytes = Encoding.UTF8.GetBytes(value);
        var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr, bytes.Length, 0);
        return ptr;
    }

    private static void Free(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
    }

    private static string ReadUtf8AndFree(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return string.Empty;
        try
        {
            var length = 0;
            while (Marshal.ReadByte(ptr, length) != 0) length++;
            var buffer = new byte[length];
            Marshal.Copy(ptr, buffer, 0, length);
            return Encoding.UTF8.GetString(buffer);
        }
        finally
        {
            mystia_free_string(ptr);
        }
    }

    public static JToken Call(Func<IntPtr> fn)
    {
        var json = ReadUtf8AndFree(fn());
        var obj = JObject.Parse(json);
        if (obj.Value<bool>("ok") != true)
            throw new InvalidOperationException(obj.Value<string>("error") ?? "未知错误");
        return obj["data"]!;
    }

    public static IntPtr DetectDefaultPaths() => mystia_detect_default_paths();
    public static IntPtr ListBuilds() => mystia_list_bepinex_builds();

    public static IntPtr DefaultPathsForGame(string gamePath)
    {
        var p = AllocUtf8(gamePath);
        try { return mystia_default_paths_for_game(p); }
        finally { Free(p); }
    }

    public static IntPtr InitManager(string gamePath, string managerPath, string configRoot)
    {
        var a = AllocUtf8(gamePath);
        var b = AllocUtf8(managerPath);
        var c = AllocUtf8(configRoot);
        try { return mystia_init_manager(a, b, c); }
        finally { Free(a); Free(b); Free(c); }
    }

    public static IntPtr LoadManager(string configRoot)
    {
        var p = AllocUtf8(configRoot);
        try { return mystia_load_manager(p); }
        finally { Free(p); }
    }

    public static IntPtr SaveManager(string configRoot, string json)
    {
        var a = AllocUtf8(configRoot);
        var b = AllocUtf8(json);
        try { return mystia_save_manager(a, b); }
        finally { Free(a); Free(b); }
    }

    public static IntPtr ListProfiles(string configRoot)
    {
        var p = AllocUtf8(configRoot);
        try { return mystia_list_profiles(p); }
        finally { Free(p); }
    }

    public static IntPtr CreateProfile(string configRoot, string name, string url, string version)
    {
        var a = AllocUtf8(configRoot);
        var b = AllocUtf8(name);
        var c = AllocUtf8(url);
        var d = AllocUtf8(version);
        try { return mystia_create_profile(a, b, c, d); }
        finally { Free(a); Free(b); Free(c); Free(d); }
    }

    public static IntPtr UpdateBepInEx(string profilePath, string url, string version)
    {
        var a = AllocUtf8(profilePath);
        var b = AllocUtf8(url);
        var c = AllocUtf8(version);
        try { return mystia_update_bepinex(a, b, c); }
        finally { Free(a); Free(b); Free(c); }
    }

    public static IntPtr RenameProfile(string configRoot, string oldName, string newName)
    {
        var a = AllocUtf8(configRoot);
        var b = AllocUtf8(oldName);
        var c = AllocUtf8(newName);
        try { return mystia_rename_profile(a, b, c); }
        finally { Free(a); Free(b); Free(c); }
    }

    public static IntPtr DeleteProfile(string configRoot, string name)
    {
        var a = AllocUtf8(configRoot);
        var b = AllocUtf8(name);
        try { return mystia_delete_profile(a, b); }
        finally { Free(a); Free(b); }
    }

    public static IntPtr ListMods(string profilePath)
    {
        var p = AllocUtf8(profilePath);
        try { return mystia_list_mods(p); }
        finally { Free(p); }
    }

    public static IntPtr InstallMod(string profilePath, string sourcePath)
    {
        var a = AllocUtf8(profilePath);
        var b = AllocUtf8(sourcePath);
        try { return mystia_install_mod(a, b); }
        finally { Free(a); Free(b); }
    }

    public static IntPtr SetModEnabled(string profilePath, string modName, bool enabled)
    {
        var a = AllocUtf8(profilePath);
        var b = AllocUtf8(modName);
        try { return mystia_set_mod_enabled(a, b, enabled); }
        finally { Free(a); Free(b); }
    }

    public static IntPtr UninstallMod(string profilePath, string modName)
    {
        var a = AllocUtf8(profilePath);
        var b = AllocUtf8(modName);
        try { return mystia_uninstall_mod(a, b); }
        finally { Free(a); Free(b); }
    }

    public static IntPtr WriteDoorstopHook(string gamePath, string profilePath)
    {
        var a = AllocUtf8(gamePath);
        var b = AllocUtf8(profilePath);
        try { return mystia_write_doorstop_hook(a, b); }
        finally { Free(a); Free(b); }
    }

    public static IntPtr PrepareLaunch(string gamePath, string profilePath)
    {
        var a = AllocUtf8(gamePath);
        var b = AllocUtf8(profilePath);
        try { return mystia_prepare_launch(a, b); }
        finally { Free(a); Free(b); }
    }

    public static IntPtr ProfilePath(string configRoot, string name)
    {
        var a = AllocUtf8(configRoot);
        var b = AllocUtf8(name);
        try { return mystia_profile_path(a, b); }
        finally { Free(a); Free(b); }
    }
}
