namespace MystiaModManager.Logic;

public static class ModWriteGuard
{
    public const string ExitFirst = "请先退出游戏。";

    public static string? Reject(bool gameRunning, string action)
    {
        if (!gameRunning) return null;
        switch (action)
        {
            case "启用":
            case "禁用":
            case "卸载":
            case "安装":
            case "更新模组":
            case "保存配置":
                return ExitFirst;
            default:
                return null;
        }
    }
}
