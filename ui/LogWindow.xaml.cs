using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MystiaModManager.Logic;

namespace MystiaModManager;

public partial class LogWindow : UserControl
{
    private readonly List<LogLine> _lines = new();

    public LogWindow()
    {
        InitializeComponent();
        LevelFilter.SelectedIndex = 0;
    }

    public void LoadProfile(string profilePath)
    {
        var path = Path.Combine(profilePath, "BepInEx", "LogOutput.log");
        PathText.Text = path;
        _lines.Clear();
        try
        {
            if (!File.Exists(path))
            {
                _lines.Add(new LogLine("还没有日志。运行一次游戏后才会生成。", "info"));
            }
            else
            {
                foreach (var line in LogFile.ReadLines(path))
                    _lines.Add(new LogLine(line, LogLine.KindOf(line)));
            }
        }
        catch (Exception ex)
        {
            _lines.Add(new LogLine("日志暂时读不了：" + ex.Message, "error"));
        }
        ApplyFilter();
    }

    private void LevelFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var kind = LevelFilter.SelectedIndex == 1 ? "warning" : LevelFilter.SelectedIndex == 2 ? "error" : "all";
        Lines.Items.Clear();
        foreach (var line in _lines)
        {
            if (kind != "all" && line.Kind != kind) continue;
            Lines.Items.Add(line);
        }
        if (Lines.Items.Count == 0)
            Lines.Items.Add(new LogLine(kind == "warning" ? "没有警告。" : "没有报错。", "info"));
        Dispatcher.BeginInvoke(new Action(() => LogScroll.ScrollToEnd()), DispatcherPriority.Loaded);
    }
}

public sealed class LogLine
{
    public LogLine(string text, string kind)
    {
        Text = text;
        Kind = kind;
        Ink = kind == "error" ? new SolidColorBrush(Color.FromRgb(209, 36, 47))
            : kind == "warning" ? new SolidColorBrush(Color.FromRgb(184, 122, 0))
            : new SolidColorBrush(Color.FromRgb(26, 26, 26));
    }

    public string Text { get; }
    public string Kind { get; }
    public Brush Ink { get; }

    public static string KindOf(string line)
    {
        if (line.IndexOf("[Error", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("[Fatal", StringComparison.OrdinalIgnoreCase) >= 0)
            return "error";
        if (line.IndexOf("[Warning", StringComparison.OrdinalIgnoreCase) >= 0)
            return "warning";
        return "info";
    }
}
