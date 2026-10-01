using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MystiaModManager.Models;

public sealed class DefaultPaths
{
    [JsonProperty("game_path")] public string GamePath { get; set; } = "";
    [JsonProperty("manager_path")] public string ManagerPath { get; set; } = "";
    [JsonProperty("config_root")] public string ConfigRoot { get; set; } = "";
}

public sealed class ManagerSettings
{
    [JsonProperty("game_path")] public string GamePath { get; set; } = "";
    [JsonProperty("manager_path")] public string ManagerPath { get; set; } = "";
    [JsonProperty("config_root")] public string ConfigRoot { get; set; } = "";
    [JsonProperty("current_profile")] public string CurrentProfile { get; set; } = "";
    [JsonProperty("bepinex_build_id")] public int? BepInExBuildId { get; set; }
    [JsonProperty("bepinex_version")] public string? BepInExVersion { get; set; }
}

public sealed class ProfileInfo
{
    [JsonProperty("name")] public string Name { get; set; } = "";
    [JsonProperty("path")] public string Path { get; set; } = "";
    [JsonProperty("bepinex_version")] public string? BepInExVersion { get; set; }
    [JsonProperty("mod_count")] public int ModCount { get; set; }
}

public sealed class BepInExBuild
{
    [JsonProperty("build_id")] public int BuildId { get; set; }
    [JsonProperty("version")] public string Version { get; set; } = "";
    [JsonProperty("file_name")] public string FileName { get; set; } = "";
    [JsonProperty("url")] public string Url { get; set; } = "";

    public string Display => $"#{BuildId}  {Version}";
}

public sealed class ModInfo
{
    [JsonProperty("name")] public string Name { get; set; } = "";
    [JsonProperty("path")] public string Path { get; set; } = "";
    [JsonProperty("enabled")] public bool Enabled { get; set; }
    [JsonProperty("dll_count")] public int DllCount { get; set; }
}

public sealed class LaunchInfo
{
    [JsonProperty("exe_path")] public string ExePath { get; set; } = "";
    [JsonProperty("working_directory")] public string WorkingDirectory { get; set; } = "";
    [JsonProperty("arguments")] public List<string> Arguments { get; set; } = new();
}
