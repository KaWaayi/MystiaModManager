using System;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using MystiaModManager.Models;
using MystiaModManager.Services;

namespace MystiaModManager;

public partial class SetupPathsWindow : Window
{
    public ManagerSettings? Result { get; private set; }

    public SetupPathsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Prefill();
    }

    private void Prefill()
    {
        try
        {
            var paths = CoreApi.DetectDefaultPaths();
            GamePathBox.Text = paths.GamePath;
            ManagerPathBox.Text = paths.ManagerPath;
            ConfigPathBox.Text = paths.ConfigRoot;
        }
        catch
        {
            // leave empty for manual selection
        }
    }

    private void BrowseGame_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "选择夜雀食堂游戏目录" };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            GamePathBox.Text = dlg.SelectedPath;
            try
            {
                var paths = CoreApi.DefaultPathsForGame(dlg.SelectedPath);
                ManagerPathBox.Text = paths.ManagerPath;
                ConfigPathBox.Text = paths.ConfigRoot;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "路径无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void BrowseManager_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "选择管理器安装目录" };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ManagerPathBox.Text = dlg.SelectedPath;
    }

    private void BrowseConfig_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "选择配置父目录" };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ConfigPathBox.Text = dlg.SelectedPath;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var game = GamePathBox.Text.Trim();
        var manager = ManagerPathBox.Text.Trim();
        var config = ConfigPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(game) || string.IsNullOrWhiteSpace(manager) || string.IsNullOrWhiteSpace(config))
        {
            System.Windows.MessageBox.Show("请填写全部路径", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.Equals(Path.GetFullPath(manager), Path.GetFullPath(config), StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show("管理器目录与配置目录不能相同", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!File.Exists(Path.Combine(game, "Touhou Mystia Izakaya.exe")))
        {
            System.Windows.MessageBox.Show("游戏目录中找不到 Touhou Mystia Izakaya.exe", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Directory.CreateDirectory(manager);
        Directory.CreateDirectory(config);
        Result = new ManagerSettings
        {
            GamePath = game,
            ManagerPath = manager,
            ConfigRoot = config
        };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
