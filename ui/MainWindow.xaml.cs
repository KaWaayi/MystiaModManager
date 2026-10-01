using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MystiaModManager.Models;
using MystiaModManager.Services;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace MystiaModManager;

public partial class MainWindow : FluentWindow
{
    private ManagerSettings? _settings;
    private readonly ObservableCollection<ModRow> _mods = new();
    private string? _configRoot;

    public MainWindow()
    {
        InitializeComponent();
        ModsGrid.ItemsSource = _mods;
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            SetBusy(true, "正在初始化…");
            var bootstrap = BootstrapPaths.TryLoad();
            if (bootstrap == null)
            {
                var dlg = new SetupPathsWindow();
                if (dlg.ShowDialog() != true)
                {
                    Close();
                    return;
                }
                bootstrap = dlg.Result!;
                BootstrapPaths.Save(bootstrap);
            }

            _configRoot = bootstrap.ConfigRoot;
            _settings = await Task.Run(() =>
                CoreApi.InitManager(bootstrap.GamePath, bootstrap.ManagerPath, bootstrap.ConfigRoot));

            PathHint.Text = $"游戏：{_settings.GamePath}\n配置：{_settings.ConfigRoot}";

            var profiles = await Task.Run(() => CoreApi.ListProfiles(_configRoot));
            if (profiles.Count == 0)
            {
                SetBusy(false);
                var created = await EnsureFirstProfileAsync();
                if (!created)
                {
                    Close();
                    return;
                }
            }

            await ReloadProfilesAsync();
            SetStatus("就绪");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "初始化失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> EnsureFirstProfileAsync()
    {
        var win = new FirstRunWindow();
        if (win.ShowDialog() != true || win.SelectedBuild == null)
            return false;

        SetBusy(true, "正在下载并安装 BepInEx…");
        try
        {
            var build = win.SelectedBuild;
            var name = await Task.Run(() =>
                CoreApi.CreateProfile(_configRoot!, "Default", build.Url, build.Version));
            _settings!.CurrentProfile = name;
            _settings.BepInExBuildId = build.BuildId;
            _settings.BepInExVersion = build.Version;
            CoreApi.SaveManager(_configRoot!, _settings);

            var profilePath = CoreApi.ProfilePath(_configRoot!, name);
            await Task.Run(() => CoreApi.WriteDoorstopHook(_settings.GamePath, profilePath));
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "创建配置失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ReloadProfilesAsync()
    {
        if (_configRoot == null || _settings == null) return;
        var profiles = await Task.Run(() => CoreApi.ListProfiles(_configRoot));
        ProfileCombo.ItemsSource = profiles;
        var current = profiles.FirstOrDefault(p => p.Name == _settings.CurrentProfile)
                      ?? profiles.FirstOrDefault();
        if (current != null)
        {
            ProfileCombo.SelectedItem = current;
            _settings.CurrentProfile = current.Name;
            CoreApi.SaveManager(_configRoot, _settings);
            await ReloadModsAsync(current.Path);
        }
        else
        {
            _mods.Clear();
        }
    }

    private async Task ReloadModsAsync(string profilePath)
    {
        var mods = await Task.Run(() => CoreApi.ListMods(profilePath));
        _mods.Clear();
        foreach (var m in mods)
        {
            _mods.Add(new ModRow(m));
        }
    }

    private ProfileInfo? CurrentProfile => ProfileCombo.SelectedItem as ProfileInfo;

    private async void ProfileCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null || _configRoot == null) return;
        if (CurrentProfile == null) return;
        _settings.CurrentProfile = CurrentProfile.Name;
        CoreApi.SaveManager(_configRoot, _settings);
        await ReloadModsAsync(CurrentProfile.Path);
    }

    private async void CreateProfile_Click(object sender, RoutedEventArgs e)
    {
        var nameWin = new InputDialog("新建配置", "配置名称：", "NewProfile");
        if (nameWin.ShowDialog() != true) return;
        var buildWin = new FirstRunWindow { Title = "选择 BepInEx 版本" };
        if (buildWin.ShowDialog() != true || buildWin.SelectedBuild == null) return;

        SetBusy(true, "正在下载并创建配置…");
        try
        {
            var build = buildWin.SelectedBuild;
            var name = await Task.Run(() =>
                CoreApi.CreateProfile(_configRoot!, nameWin.Value, build.Url, build.Version));
            _settings!.CurrentProfile = name;
            CoreApi.SaveManager(_configRoot!, _settings);
            var profilePath = CoreApi.ProfilePath(_configRoot!, name);
            await Task.Run(() => CoreApi.WriteDoorstopHook(_settings.GamePath, profilePath));
            await ReloadProfilesAsync();
            SetStatus($"已创建配置 {name}");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void UpdateFramework_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProfile == null) return;
        var buildWin = new FirstRunWindow { Title = "更新 BepInEx 框架" };
        if (buildWin.ShowDialog() != true || buildWin.SelectedBuild == null) return;
        SetBusy(true, "正在更新框架（保留模组）…");
        try
        {
            var build = buildWin.SelectedBuild;
            await Task.Run(() =>
                CoreApi.UpdateBepInEx(CurrentProfile.Path, build.Url, build.Version));
            await Task.Run(() => CoreApi.WriteDoorstopHook(_settings!.GamePath, CurrentProfile.Path));
            await ReloadProfilesAsync();
            SetStatus($"框架已更新到 {build.Version}");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProfile == null) return;
        var dlg = new InputDialog("重命名配置", "新名称：", CurrentProfile.Name);
        if (dlg.ShowDialog() != true) return;
        try
        {
            var newName = await Task.Run(() =>
                CoreApi.RenameProfile(_configRoot!, CurrentProfile.Name, dlg.Value));
            _settings!.CurrentProfile = newName;
            CoreApi.SaveManager(_configRoot!, _settings);
            await ReloadProfilesAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProfile == null) return;
        if (System.Windows.MessageBox.Show($"确定删除配置「{CurrentProfile.Name}」？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            await Task.Run(() => CoreApi.DeleteProfile(_configRoot!, CurrentProfile.Name));
            _settings!.CurrentProfile = "";
            CoreApi.SaveManager(_configRoot!, _settings);
            await ReloadProfilesAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void InstallMod_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProfile == null) return;
        var ofd = new OpenFileDialog
        {
            Filter = "模组包|*.zip;*.dll|ZIP|*.zip|DLL|*.dll|所有文件|*.*",
            Title = "选择本地模组"
        };
        if (ofd.ShowDialog() != true) return;
        SetBusy(true, "正在安装模组…");
        try
        {
            var name = await Task.Run(() => CoreApi.InstallMod(CurrentProfile.Path, ofd.FileName));
            await ReloadModsAsync(CurrentProfile.Path);
            SetStatus($"已安装 {name}");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProfile == null) return;
        await ReloadModsAsync(CurrentProfile.Path);
        SetStatus("已刷新");
    }

    private async void EnableMod_Click(object sender, RoutedEventArgs e)
        => await SetSelectedModEnabled(true);

    private async void DisableMod_Click(object sender, RoutedEventArgs e)
        => await SetSelectedModEnabled(false);

    private async Task SetSelectedModEnabled(bool enabled)
    {
        if (CurrentProfile == null) return;
        if (ModsGrid.SelectedItem is not ModRow row) return;
        try
        {
            await Task.Run(() => CoreApi.SetModEnabled(CurrentProfile.Path, row.Name, enabled));
            await ReloadModsAsync(CurrentProfile.Path);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void UninstallMod_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProfile == null) return;
        if (ModsGrid.SelectedItem is not ModRow row) return;
        if (System.Windows.MessageBox.Show($"卸载模组「{row.Name}」？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            await Task.Run(() => CoreApi.UninstallMod(CurrentProfile.Path, row.Name));
            await ReloadModsAsync(CurrentProfile.Path);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProfile == null || _settings == null) return;
        SetBusy(true, "正在准备启动…");
        try
        {
            var info = await Task.Run(() =>
                CoreApi.PrepareLaunch(_settings.GamePath, CurrentProfile.Path));
            var args = string.Join(" ", info.Arguments.Select(QuoteArg));
            var psi = new ProcessStartInfo
            {
                FileName = info.ExePath,
                WorkingDirectory = info.WorkingDirectory,
                Arguments = args,
                UseShellExecute = false
            };
            Process.Start(psi);
            SetStatus("游戏已启动");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string QuoteArg(string arg)
        => arg.Contains(' ') ? $"\"{arg}\"" : arg;

    private void SetBusy(bool busy, string? text = null)
    {
        BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (text != null) BusyText.Text = text;
    }

    private void SetStatus(string text) => StatusText.Text = text;
}

public sealed class ModRow
{
    public ModRow(ModInfo info)
    {
        Name = info.Name;
        Enabled = info.Enabled;
        DllCount = info.DllCount;
        StatusText = info.Enabled ? "启用" : "停用";
    }

    public string Name { get; }
    public bool Enabled { get; }
    public int DllCount { get; }
    public string StatusText { get; }
}

public static class BootstrapPaths
{
    private static string FilePath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bootstrap.json");

    public static ManagerSettings? TryLoad()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<ManagerSettings>(File.ReadAllText(FilePath));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(ManagerSettings s)
    {
        File.WriteAllText(FilePath, Newtonsoft.Json.JsonConvert.SerializeObject(s, Newtonsoft.Json.Formatting.Indented));
    }
}
