using System.Collections.Generic;
using System.IO;

namespace MystiaModManager.Logic;

public static class LogFile
{
    public static List<string> ReadLines(string path)
    {
        var lines = new List<string>();
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
                lines.Add(line);
        }
        return lines;
    }
}
