using System;
using System.Windows;
using System.Windows.Forms;
using MystiaModManager.Models;

namespace MystiaModManager;

public partial class SettingsWindow : System.Windows.Controls.UserControl
{
    private readonly ManagerSettings _settings;

    public event EventHandler? Saved;

    public SettingsWindow(ManagerSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        GamePathBox.Text = settings.GamePath;
        ManagerPathBox.Text = settings.ManagerPath;
        ConfigRootBox.Text = settings.ConfigRoot;
    }

    private void BrowseGame_Click(object sender, RoutedEventArgs e)
        => PickFolder(GamePathBox, "选择游戏目录");

    private void BrowseManager_Click(object sender, RoutedEventArgs e)
        => PickFolder(ManagerPathBox, "选择管理器安装目录");

    private void BrowseConfig_Click(object sender, RoutedEventArgs e)
        => PickFolder(ConfigRootBox, "选择配置父目录");

    private static void PickFolder(System.Windows.Controls.TextBox box, string description)
    {
        using (var dialog = new FolderBrowserDialog())
        {
            dialog.Description = description;
            dialog.SelectedPath = box.Text;
            if (dialog.ShowDialog() != DialogResult.OK) return;
            box.Text = dialog.SelectedPath;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.GamePath = GamePathBox.Text.Trim();
        _settings.ManagerPath = ManagerPathBox.Text.Trim();
        _settings.ConfigRoot = ConfigRootBox.Text.Trim();
        Saved?.Invoke(this, EventArgs.Empty);
    }
}
