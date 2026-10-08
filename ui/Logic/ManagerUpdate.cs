using Izakaya.Rules;
using Newtonsoft.Json.Linq;

namespace MystiaModManager.Logic;

public static class ManagerUpdate
{
    public const string VersionUrl = "http://47.116.214.184/manager/version";

    public static string? ReadVersion(string json)
    {
        var token = JObject.Parse(json)["version"];
        if (token == null || token.Type == JTokenType.Null)
            return null;
        var text = token.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    public static bool ShouldPrompt(string? remote, string? local)
        => VersionOrder.IsNewer(remote, local);
}
