using System;
using System.Diagnostics;
using System.IO;

namespace MystiaModManager.Services;

internal static class AppUpdater
{
    public static void Start(string managerDir)
    {
        var updater = Path.Combine(managerDir, "MystiaModManager.Update.exe");
        if (!File.Exists(updater))
            throw new InvalidOperationException("缺少 MystiaModManager.Update.exe，请重新运行安装器。");
        var temp = Path.Combine(Path.GetTempPath(), "MystiaModManager.Update.exe");
        File.Copy(updater, temp, true);
        var psi = new ProcessStartInfo
        {
            FileName = temp,
            Arguments = "--pid " + Process.GetCurrentProcess().Id
                + " --dir \"" + managerDir.TrimEnd('\\') + "\"",
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath()
        };
        if (Process.Start(psi) == null)
            throw new InvalidOperationException("无法启动更新程序");
    }
}
