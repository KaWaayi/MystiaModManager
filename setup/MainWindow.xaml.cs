using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Newtonsoft.Json;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace MystiaModManager.Setup;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Prefill();
    }

    private void Prefill()
    {
        var game = PathDefaults.DetectGamePath();
        if (game != null)
        {
            GamePathBox.Text = game;
            var (manager, config) = PathDefaults.DefaultsForGame(game);
            ManagerPathBox.Text = manager;
            ConfigPathBox.Text = config;
        }
    }

    private void BrowseGame_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "选择夜雀食堂游戏目录" };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        GamePathBox.Text = dlg.SelectedPath;
        try
        {
            var (manager, config) = PathDefaults.DefaultsForGame(dlg.SelectedPath);
            ManagerPathBox.Text = manager;
            ConfigPathBox.Text = config;
        }
        catch { /* ignore */ }
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

    private async void Install_Click(object sender, RoutedEventArgs e)
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

        InstallButton.IsEnabled = false;
        try
        {
            Directory.CreateDirectory(manager);
            Directory.CreateDirectory(config);

            // Prefer local build if present next to setup (dev / offline)
            var localUi = FindLocalUiPackage();
            if (localUi != null)
            {
                StatusText.Text = "检测到本地构建，正在复制…";
                CopyDirectory(localUi, manager);
            }
            else
            {
                await ReleaseInstaller.InstallLatestAsync(manager, s => StatusText.Text = s);
            }

            var bootstrap = new
            {
                game_path = game,
                manager_path = manager,
                config_root = config,
                current_profile = "",
                bepinex_build_id = (int?)null,
                bepinex_version = (string?)null
            };
            File.WriteAllText(
                Path.Combine(manager, "bootstrap.json"),
                JsonConvert.SerializeObject(bootstrap, Formatting.Indented));

            var exe = Path.Combine(manager, "MystiaModManager.exe");
            if (!File.Exists(exe))
                throw new InvalidOperationException("安装后未找到 MystiaModManager.exe");

            StatusText.Text = "正在启动管理器…";
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = manager,
                UseShellExecute = true
            });
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = ex.Message;
        }
        finally
        {
            InstallButton.IsEnabled = true;
        }
    }

    private static string? FindLocalUiPackage()
    {
        // Dev: ../ui/bin/Release next to setup bin
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "ui", "bin", "Release")),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "package")),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(Path.Combine(c, "MystiaModManager.exe")) &&
                File.Exists(Path.Combine(c, "mystia_core.dll")))
                return c;
        }
        return null;
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(src))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(dest, name), overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(src))
        {
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }
}
