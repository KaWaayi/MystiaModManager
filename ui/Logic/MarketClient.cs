using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MystiaModManager.Logic;

public static class MarketClient
{
    public const string BaseUrl = "http://47.116.214.184";

    public static MarketCatalog ParseList(string json)
    {
        var token = ParseToken(json, "模组列表无法解析");
        if (token is JArray array)
            return new MarketCatalog(null, ReadMods(array));
        if (token is JObject obj)
            return new MarketCatalog(ReadOptionalBool(obj, "isAdmin"), ReadMods(FindModArray(obj)));
        throw new InvalidOperationException("模组列表无法解析");
    }

    public static MarketDetail ParseDetail(string json)
    {
        var token = ParseToken(json, "模组详情无法解析");
        if (token is not JObject obj)
            throw new InvalidOperationException("模组详情无法解析");

        var dependencies = new List<MarketDependency>();
        if (Child(obj, "dependencies") is JArray deps)
        {
            foreach (var item in deps)
            {
                if (item is not JObject dep) continue;
                var id = TextOrNull(Child(dep, "id"));
                if (id == null) continue;
                dependencies.Add(new MarketDependency(id, ReadHard(dep)));
            }
        }

        var versions = new List<string>();
        if (Child(obj, "versions") is JArray vers)
        {
            foreach (var item in vers)
            {
                if (item.Type != JTokenType.String) continue;
                var text = item.ToString().Trim();
                if (text.Length > 0) versions.Add(text);
            }
        }

        return new MarketDetail(
            TextOrNull(Child(obj, "name")) ?? "",
            TextOrNull(Child(obj, "description")) ?? "",
            TextOrNull(Child(obj, "author")) ?? "",
            TextOrNull(Child(obj, "latestVersion")),
            ReadCount(Child(obj, "downloadCount")),
            dependencies,
            versions,
            ReadStrings(Child(obj, "files")));
    }

    static List<string> ReadStrings(JToken? token)
    {
        var values = new List<string>();
        if (token is not JArray array) return values;
        foreach (var item in array)
        {
            if (item.Type != JTokenType.String) continue;
            var text = item.ToString().Trim();
            if (text.Length > 0) values.Add(text);
        }
        return values;
    }

    public static SubscribeDecision ReadSubscribe(int statusCode, string? body)
    {
        var missing = ReadMissing(body);
        if (statusCode == 409 && missing.Count > 0)
            return new SubscribeDecision(false, missing, ReadError(body));
        if (statusCode >= 200 && statusCode < 300)
            return new SubscribeDecision(true, Array.Empty<string>(), null);
        return new SubscribeDecision(false, Array.Empty<string>(), ReadError(body));
    }

    public static string? ReadLoginToken(string? json)
    {
        if (json == null || json.Trim().Length == 0) return null;
        try
        {
            var token = JObject.Parse(json)["token"];
            if (token == null || token.Type != JTokenType.String) return null;
            var text = token.ToString().Trim();
            return text.Length == 0 ? null : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ReadError(string? body)
    {
        if (body == null || body.Trim().Length == 0) return null;
        try
        {
            if (JToken.Parse(body) is not JObject obj) return null;
            var error = Child(obj, "error");
            if (error == null || error.Type == JTokenType.Null) return null;
            var text = error.ToString().Trim();
            return text.Length == 0 ? null : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string DownloadFileName(string? contentDisposition, string id, string version)
    {
        var named = FileNameFromDisposition(contentDisposition);
        if (named != null && named.Length > 0)
            return named;
        return id + "_" + version + ".zip";
    }

    static IReadOnlyList<string> ReadMissing(string? body)
    {
        if (body == null || body.Trim().Length == 0) return Array.Empty<string>();
        try
        {
            if (JToken.Parse(body) is not JObject obj) return Array.Empty<string>();
            if (Child(obj, "missing") is not JArray array) return Array.Empty<string>();
            var missing = new List<string>();
            foreach (var item in array)
            {
                if (item.Type != JTokenType.String) continue;
                var text = item.ToString().Trim();
                if (text.Length > 0) missing.Add(text);
            }
            return missing;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    static string? FileNameFromDisposition(string? header)
    {
        if (header == null || header.Trim().Length == 0) return null;
        string? star = null;
        string? plain = null;
        foreach (var part in header.Split(';'))
        {
            var text = part.Trim();
            var eq = text.IndexOf('=');
            if (eq <= 0) continue;
            var key = text.Substring(0, eq).Trim();
            var value = text.Substring(eq + 1).Trim();
            if (key.Equals("filename*", StringComparison.OrdinalIgnoreCase))
            {
                if (star == null) star = DecodeStar(value);
            }
            else if (key.Equals("filename", StringComparison.OrdinalIgnoreCase))
            {
                if (plain == null) plain = Unquote(value);
            }
        }

        var fromStar = SafeFileName(star);
        if (!string.IsNullOrEmpty(fromStar)) return fromStar;
        return SafeFileName(plain);
    }

    static string? DecodeStar(string value)
    {
        value = Unquote(value);
        var split = value.IndexOf("''", StringComparison.Ordinal);
        var encoded = split >= 0 ? value.Substring(split + 2) : value;
        try
        {
            return Uri.UnescapeDataString(encoded);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            return value.Substring(1, value.Length - 2);
        return value;
    }

    static string? SafeFileName(string? name)
    {
        if (name == null) return null;
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return null;
        var file = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(file) || file == "." || file == "..") return null;
        if (file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        return file;
    }

    static List<MarketMod> ReadMods(JArray array)
    {
        var mods = new List<MarketMod>();
        foreach (var token in array)
        {
            if (token is not JObject item) continue;
            var id = TextOrNull(Child(item, "id"));
            if (id == null) continue;
            var name = TextOrNull(Child(item, "name")) ?? id;
            mods.Add(new MarketMod(
                id,
                name,
                TextOrNull(Child(item, "latestVersion")),
                ReadOptionalBool(item, "subscribed")));
        }
        return mods;
    }

    static JArray FindModArray(JObject obj)
    {
        if (Child(obj, "mods") is JArray mods) return mods;
        foreach (var prop in obj.Properties())
        {
            if (prop.Value is JArray array) return array;
        }
        return new JArray();
    }

    static JToken ParseToken(string json, string error)
    {
        try
        {
            return JToken.Parse(json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(error);
        }
    }

    static JToken? Child(JObject obj, string name)
    {
        foreach (var prop in obj.Properties())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return prop.Value;
        }
        return null;
    }

    static bool? ReadOptionalBool(JObject obj, string name)
    {
        var token = Child(obj, name);
        if (token == null || token.Type != JTokenType.Boolean) return null;
        return token.Value<bool>();
    }

    static bool ReadHard(JObject obj)
    {
        var token = Child(obj, "hard");
        if (token == null || token.Type == JTokenType.Null) return false;
        if (token.Type == JTokenType.Boolean) return token.Value<bool>();
        if (token.Type == JTokenType.Integer) return token.Value<int>() != 0;
        return false;
    }

    static long ReadCount(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return 0;
        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            return token.Value<long>();
        return 0;
    }

    static string? TextOrNull(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return null;
        var text = token.ToString().Trim();
        return text.Length == 0 ? null : text;
    }
}

public sealed class MarketCatalog
{
    public MarketCatalog(bool? isAdmin, IReadOnlyList<MarketMod> mods)
    {
        IsAdmin = isAdmin;
        Mods = mods;
    }

    public bool? IsAdmin { get; }
    public IReadOnlyList<MarketMod> Mods { get; }
    public bool ShowUpload => IsAdmin == true;
}

public sealed class MarketMod
{
    public MarketMod(string id, string name, string? latestVersion, bool? subscribed)
    {
        Id = id;
        Name = name;
        LatestVersion = latestVersion;
        Subscribed = subscribed;
    }

    public string Id { get; }
    public string Name { get; }
    public string? LatestVersion { get; }
    public bool? Subscribed { get; }
}

public sealed class MarketDependency
{
    public MarketDependency(string id, bool hard)
    {
        Id = id;
        Hard = hard;
    }

    public string Id { get; }
    public bool Hard { get; }
}

public sealed class MarketDetail
{
    public MarketDetail(
        string name,
        string description,
        string author,
        string? latestVersion,
        long downloadCount,
        IReadOnlyList<MarketDependency> dependencies,
        IReadOnlyList<string> versions,
        IReadOnlyList<string> files)
    {
        Name = name;
        Description = description;
        Author = author;
        LatestVersion = latestVersion;
        DownloadCount = downloadCount;
        Dependencies = dependencies;
        Versions = versions;
        Files = files;
    }

    public string Name { get; }
    public string Description { get; }
    public string Author { get; }
    public string? LatestVersion { get; }
    public long DownloadCount { get; }
    public IReadOnlyList<MarketDependency> Dependencies { get; }
    public IReadOnlyList<string> Versions { get; }
    public IReadOnlyList<string> Files { get; }
}

public sealed class SubscribeDecision
{
    public SubscribeDecision(bool succeeded, IReadOnlyList<string> missing, string? error)
    {
        Succeeded = succeeded;
        Missing = missing;
        Error = error;
    }

    public bool Succeeded { get; }
    public IReadOnlyList<string> Missing { get; }
    public string? Error { get; }
}
