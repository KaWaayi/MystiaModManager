using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MystiaModManager.Services;

internal sealed class CfgSetting
{
    public string Section { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Original { get; set; } = "";
    public string Description { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public string Hint { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public int LineIndex { get; set; }
}

internal sealed class CfgDocument
{
    public string Path { get; set; } = "";
    public string Newline { get; set; } = "\r\n";
    public bool WriteBom { get; set; }
    public List<string> Lines { get; set; } = new();
    public List<string> Endings { get; set; } = new();
    public List<CfgSetting> Settings { get; set; } = new();

    public static CfgDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text;
        if (bom)
        {
            var payload = new byte[bytes.Length - 3];
            Buffer.BlockCopy(bytes, 3, payload, 0, payload.Length);
            text = new UTF8Encoding(false).GetString(payload);
        }
        else
        {
            text = new UTF8Encoding(false).GetString(bytes);
        }
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = new List<string>();
        var endings = new List<string>();
        var cursor = 0;
        while (cursor <= text.Length)
        {
            var next = text.IndexOf('\n', cursor);
            if (next < 0)
            {
                lines.Add(text.Substring(cursor).TrimEnd('\r'));
                endings.Add("");
                break;
            }

            var content = text.Substring(cursor, next - cursor);
            var ending = "\n";
            if (content.EndsWith("\r", StringComparison.Ordinal))
            {
                content = content.Substring(0, content.Length - 1);
                ending = "\r\n";
            }
            lines.Add(content);
            endings.Add(ending);
            cursor = next + 1;
            if (cursor == text.Length)
            {
                lines.Add("");
                endings.Add("");
                break;
            }
        }

        var doc = new CfgDocument
        {
            Path = path,
            Newline = newline,
            WriteBom = bom,
            Lines = lines,
            Endings = endings
        };

        var section = "";
        var description = new StringBuilder();
        var typeName = "";
        var defaultValue = "";
        var hint = "";
        var options = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]") && trimmed.Length > 2)
            {
                section = trimmed.Substring(1, trimmed.Length - 2);
                ClearPending(description, ref typeName, ref defaultValue, ref hint, options);
                continue;
            }

            if (trimmed.StartsWith("##"))
            {
                var note = trimmed.Substring(2).Trim();
                if (note.Length > 0)
                {
                    if (description.Length > 0) description.Append(' ');
                    description.Append(note);
                }
                continue;
            }

            if (trimmed.StartsWith("#"))
            {
                var note = trimmed.Substring(1).Trim();
                if (note.StartsWith("Setting type:", StringComparison.OrdinalIgnoreCase))
                    typeName = note.Substring("Setting type:".Length).Trim();
                else if (note.StartsWith("Default value:", StringComparison.OrdinalIgnoreCase))
                    defaultValue = note.Substring("Default value:".Length).Trim();
                else if (note.StartsWith("Acceptable value range:", StringComparison.OrdinalIgnoreCase))
                    hint = note.Substring("Acceptable value range:".Length).Trim();
                else if (note.StartsWith("Acceptable values:", StringComparison.OrdinalIgnoreCase))
                {
                    var rawOptions = note.Substring("Acceptable values:".Length).Trim();
                    if (rawOptions.Length > 0 && rawOptions.Length <= 180)
                    {
                        foreach (var part in rawOptions.Split(','))
                        {
                            var item = part.Trim();
                            if (item.Length > 0) options.Add(item);
                        }
                    }
                    else if (rawOptions.Length > 180)
                    {
                        hint = "可选值很多，直接填写";
                    }
                }
                continue;
            }

            var eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            var key = raw.Substring(0, eq).Trim();
            if (key.Length == 0 || key.StartsWith("#")) continue;
            doc.Settings.Add(new CfgSetting
            {
                Section = section,
                Key = key,
                Value = raw.Substring(eq + 1).Trim(),
                Original = raw.Substring(eq + 1).Trim(),
                Description = description.ToString(),
                TypeName = typeName,
                DefaultValue = defaultValue,
                Hint = hint,
                Options = new List<string>(options),
                LineIndex = i
            });
            ClearPending(description, ref typeName, ref defaultValue, ref hint, options);
        }

        return doc;
    }

    public void Save()
    {
        var changed = false;
        foreach (var setting in Settings)
        {
            if (string.Equals(setting.Value, setting.Original, StringComparison.Ordinal)) continue;
            if (setting.LineIndex < 0 || setting.LineIndex >= Lines.Count) continue;
            var line = Lines[setting.LineIndex];
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var left = line.Substring(0, eq).TrimEnd();
            Lines[setting.LineIndex] = left + " = " + setting.Value;
            changed = true;
        }

        if (!changed) return;

        var text = new StringBuilder();
        for (var i = 0; i < Lines.Count; i++)
        {
            text.Append(Lines[i]);
            if (i < Endings.Count) text.Append(Endings[i]);
        }

        var encoding = new UTF8Encoding(WriteBom);
        File.WriteAllText(Path, text.ToString(), encoding);
    }

    private static void ClearPending(
        StringBuilder description,
        ref string typeName,
        ref string defaultValue,
        ref string hint,
        List<string> options)
    {
        description.Clear();
        typeName = "";
        defaultValue = "";
        hint = "";
        options.Clear();
    }
}
