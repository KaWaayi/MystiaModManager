namespace MystiaModManager.Logic;

public static class LaunchState
{
    public const string ProcessName = "Touhou Mystia Izakaya";
    public const string IdleCaption = "带模组启动";
    public const string RunningCaption = "游戏中";

    public static string Caption(bool gameRunning)
        => gameRunning ? RunningCaption : IdleCaption;

    public static bool AcceptsClick(bool gameRunning) => !gameRunning;

    public static bool JustStopped(bool wasRunning, bool running) => wasRunning && !running;
}
