using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MystiaModManager.Logic;

public static class SessionStore
{
    public static string FilePath(string configRoot)
        => Path.Combine(configRoot, "session.json");

    public static void SaveToken(string configRoot, string token)
    {
        if (configRoot == null) throw new ArgumentNullException(nameof(configRoot));
        if (token == null) throw new ArgumentNullException(nameof(token));
        Directory.CreateDirectory(configRoot);
        var body = JsonConvert.SerializeObject(new Dictionary<string, string>
        {
            ["token"] = token
        });
        File.WriteAllText(FilePath(configRoot), body);
    }

    public static string? ReadToken(string configRoot)
    {
        var path = FilePath(configRoot);
        if (!File.Exists(path)) return null;
        var token = JObject.Parse(File.ReadAllText(path))["token"]?.ToString();
        return string.IsNullOrEmpty(token) ? null : token;
    }

    public static void Clear(string configRoot)
    {
        var path = FilePath(configRoot);
        if (File.Exists(path)) File.Delete(path);
    }
}
