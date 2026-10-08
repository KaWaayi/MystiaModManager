using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MystiaModManager.Logic;
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
    private readonly DispatcherTimer _gameTimer;
    private readonly GameLaunchWatch _watch = new();
    private Process? _owned;
    private bool _exportPrompt;
    private string? _configRoot;
    private List<ModUpdateItem> _updatePlan = new();
    private readonly List<ModRow> _allMods = new();
    private MarketWindow? _market;
    private SettingsWindow? _settingsPage;
    private ConfigEditorWindow? _configPage;
    private LogWindow? _logPage;
    private string _page = "installed";
    private string? _viewProfile;
    private bool _profileReady;
    private bool _gameRunning;
    private readonly ObservableCollection<DownloadRowVm> _downloads = new();

    public MainWindow()
    {
        InitializeComponent();
        ModsGrid.ItemsSource = _mods;
        DownloadList.ItemsSource = _downloads;
        DownloadHub.Changed += OnDownloadsChanged;
        _gameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _gameTimer.Tick += (_, _) => PollGame();
        _gameTimer.Start();
        PollGame();
        Loaded += async (_, _) => await InitializeAsync();
        HighlightNav("installed");
        Closing += (_, _) =>
        {
            _gameTimer.Stop();
            DetachOwned();
        };
    }

    private async Task InitializeAsync()
    {
        _ = CheckForManagerUpdateAsync();
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
            EnsureShell();
            ShowPage("installed");
            UpdateLoginCaption();
            TryAttachResources();
            _profileReady = true;
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
            string name = "";
            await TrackFrameworkAsync("BepInEx " + build.Version, "bepinex_download.zip", build.Url, () =>
            {
                name = CoreApi.CreateProfile(_configRoot!, "Default", build.Url, build.Version);
            });
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
            _allMods.Clear();
            _mods.Clear();
            InstalledCount.Text = "0";
        }
    }

    private async Task ReloadModsAsync(string profilePath)
    {
        var mods = await Task.Run(() => CoreApi.ListMods(profilePath));
        _allMods.Clear();
        foreach (var m in mods)
        {
            var label = InstalledMods.ReadLabel(m.Path, m.Name, _configRoot);
            _allMods.Add(new ModRow(m, label));
        }
        InstalledCount.Text = _allMods.Count.ToString();
        ApplyModFilter();
        if (_configRoot != null)
            await Task.Run(() => ResourcePacks.Sync(_configRoot, profilePath));
        SyncProfileViews(profilePath);
        _ = RefreshUpdateBannerAsync(profilePath);
    }

    private ProfileInfo? CurrentProfile => ProfileCombo.SelectedItem as ProfileInfo;

    private async void ProfileCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null || _configRoot == null) return;
        if (CurrentProfile == null) return;
        _settings.CurrentProfile = CurrentProfile.Name;
        CoreApi.SaveManager(_configRoot, _settings);
        await ReloadModsAsync(CurrentProfile.Path);
        if (_profileReady)
            await OfferLocalZipUpdateAsync(CurrentProfile.Path);
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
            var profileName = nameWin.Value;
            string name = "";
            await TrackFrameworkAsync("BepInEx " + build.Version, "bepinex_download.zip", build.Url, () =>
            {
                name = CoreApi.CreateProfile(_configRoot!, profileName, build.Url, build.Version);
            });
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
            var profilePath = CurrentProfile.Path;
            var gamePath = _settings!.GamePath;
            await TrackFrameworkAsync("BepInEx " + build.Version, "bepinex_update.zip", build.Url, () =>
            {
                CoreApi.UpdateBepInEx(profilePath, build.Url, build.Version);
            });
            await Task.Run(() => CoreApi.WriteDoorstopHook(gamePath, profilePath));
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
            var oldName = CurrentProfile.Name;
            var requested = dlg.Value;
            var newName = await Task.Run(() =>
                CoreApi.RenameProfile(_configRoot!, oldName, requested));
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
            var profileName = CurrentProfile.Name;
            await Task.Run(() => CoreApi.DeleteProfile(_configRoot!, profileName));
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
        if (BlockGameWrite("安装")) return;
        var ofd = new OpenFileDialog
        {
            Filter = "模组包|*.zip;*.dll|ZIP|*.zip|DLL|*.dll|所有文件|*.*",
            Title = "选择本地模组"
        };
        if (ofd.ShowDialog() != true) return;
        var profilePath = CurrentProfile.Path;
        var sourcePath = ofd.FileName;
        SetBusy(true, "正在安装模组…");
        try
        {
            var name = await Task.Run(() => CoreApi.InstallMod(profilePath, sourcePath));
            await ReloadModsAsync(profilePath);
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
        if (BlockGameWrite(enabled ? "启用" : "禁用")) return;
        var selected = new List<string>();
        foreach (var item in ModsGrid.SelectedItems)
        {
            if (item is ModRow row) selected.Add(row.Folder);
        }
        if (selected.Count == 0) return;
        var profilePath = CurrentProfile.Path;
        var nodes = InstalledMods.ReadNodes(profilePath);
        var folders = enabled
            ? ModActionPlan.FoldersToEnable(selected, nodes)
            : ModActionPlan.FoldersToDisable(selected, nodes);
        var extra = ModActionPlan.ExtraStateChanges(folders, selected, nodes, enabled);
        if (extra.Count > 0)
        {
            var lines = new List<string>();
            foreach (var folder in extra)
                lines.Add(DisplayName(folder));
            var verb = enabled ? "将一并启用这些模组" : "将一并禁用这些模组";
            if (System.Windows.MessageBox.Show(verb + "：\n" + string.Join("\n", lines), "模组",
                    MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
                return;
        }
        try
        {
            foreach (var folder in folders)
                await Task.Run(() => CoreApi.SetModEnabled(profilePath, folder, enabled));
            await ReloadModsAsync(profilePath);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void UninstallMod_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProfile == null) return;
        if (BlockGameWrite("卸载")) return;
        if (ModsGrid.SelectedItem is not ModRow row) return;
        if (System.Windows.MessageBox.Show($"卸载模组「{row.Name}」？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        var profilePath = CurrentProfile.Path;
        var modName = row.Folder;
        try
        {
            await Task.Run(() => CoreApi.UninstallMod(profilePath, modName));
            await ReloadModsAsync(profilePath);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppUpdater.Start(AppDomain.CurrentDomain.BaseDirectory);
            SetStatus("已交给更新程序");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("更新失败");
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string page)
            ShowPage(page);
    }

    private async void ModToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle || toggle.DataContext is not ModRow row) return;
        var want = toggle.IsChecked == true;
        if (want == row.Enabled)
            return;
        if (BlockGameWrite(want ? "启用" : "禁用"))
        {
            toggle.IsChecked = row.Enabled;
            return;
        }
        ModsGrid.SelectedItem = row;
        await SetSelectedModEnabled(want);
        if (toggle.DataContext is ModRow still)
            toggle.IsChecked = still.Enabled;
    }

    private void ModSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyModFilter();

    private void ModSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyModFilter();
    }

    private void ApplyModFilter()
    {
        var query = ModSearch.Text.Trim();
        var rows = new List<ModRow>();
        foreach (var row in _allMods)
        {
            if (query.Length == 0
                || row.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || row.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                rows.Add(row);
        }
        var key = ModSort.SelectedIndex;
        var ascending = ModOrder.SelectedIndex != 1;
        rows.Sort((a, b) => ModListSort.Compare(key, ascending, a.SortKey, b.SortKey));
        _mods.Clear();
        foreach (var row in rows)
            _mods.Add(row);
        if (_allMods.Count == 0)
            EmptyMods.Text = "当前配置还没有模组。可以在左侧打开在线页面，或安装本地模组。";
        else if (_mods.Count == 0)
            EmptyMods.Text = "没有名称包含这段文字的模组。";
        EmptyMods.Visibility = _mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPage(string page)
    {
        _page = page;
        InstalledPage.Visibility = page == "installed" ? Visibility.Visible : Visibility.Collapsed;
        MarketHost.Visibility = page == "online" ? Visibility.Visible : Visibility.Collapsed;
        ConfigHost.Visibility = page == "config" ? Visibility.Visible : Visibility.Collapsed;
        LogHost.Visibility = page == "log" ? Visibility.Visible : Visibility.Collapsed;
        SettingsHost.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        EnsureShell();
        if (page == "config" && CurrentProfile != null)
        {
            if (_configPage == null)
            {
                _configPage = new ConfigEditorWindow(CurrentProfile.Path);
                ConfigHost.Content = _configPage;
            }
        }
        if (page == "log" && CurrentProfile != null)
        {
            if (_logPage == null)
            {
                _logPage = new LogWindow();
                LogHost.Content = _logPage;
            }
            _logPage.LoadProfile(CurrentProfile.Path);
        }
        HighlightNav(page);
    }

    private void SyncProfileViews(string profilePath)
    {
        if (string.Equals(_viewProfile, profilePath, StringComparison.OrdinalIgnoreCase))
            return;
        _viewProfile = profilePath;
        _configPage?.LoadProfile(profilePath);
        if (_page == "log")
            _logPage?.LoadProfile(profilePath);
    }

    private void EnsureShell()
    {
        if (_configRoot == null || _settings == null) return;
        if (_market == null)
        {
            _market = new MarketWindow(_configRoot);
            AttachMarket(_market);
            MarketHost.Content = _market;
        }
        if (_settingsPage == null)
        {
            _settingsPage = new SettingsWindow(_settings);
            _settingsPage.Saved += (_, _) => PersistSettings();
            SettingsHost.Content = _settingsPage;
        }
    }

    private void AttachMarket(MarketWindow market)
    {
        market.CatalogCountChanged += (_, count) => OnlineCount.Text = count.ToString();
        market.SessionChanged += (_, _) => UpdateLoginCaption();
        market.ModsInstalled += async (_, _) =>
        {
            if (CurrentProfile == null) return;
            await ReloadModsAsync(CurrentProfile.Path);
            SetStatus("模组已安装到当前配置档");
        };
        market.InstallTarget = () =>
        {
            if (CurrentProfile == null || _settings == null) return null;
            if (string.IsNullOrWhiteSpace(CurrentProfile.Path) || string.IsNullOrWhiteSpace(_settings.GamePath))
                return null;
            return (CurrentProfile.Path, _settings.GamePath);
        };
    }

    private void PersistSettings()
    {
        if (_settings == null) return;
        CoreApi.SaveManager(_settings.ConfigRoot, _settings);
        BootstrapPaths.Save(_settings);
        var rootChanged = !string.Equals(_configRoot, _settings.ConfigRoot, StringComparison.OrdinalIgnoreCase);
        _configRoot = _settings.ConfigRoot;
        PathHint.Text = $"游戏：{_settings.GamePath}\n配置：{_settings.ConfigRoot}";
        if (rootChanged)
        {
            _market = new MarketWindow(_configRoot);
            AttachMarket(_market);
            MarketHost.Content = _market;
            OnlineCount.Text = "—";
        }
        SetStatus("路径已保存");
    }

    private void HighlightNav(string page)
    {
        var selected = (Brush)FindResource("NavSelected");
        PaintNav(NavInstalled, page == "installed", selected);
        PaintNav(NavOnline, page == "online", selected);
        PaintNav(NavConfig, page == "config", selected);
        PaintNav(NavLog, page == "log", selected);
        PaintNav(NavSettings, page == "settings", selected);
    }

    private static void PaintNav(System.Windows.Controls.Button button, bool on, Brush selected)
    {
        if (on)
        {
            button.Background = selected;
            button.Foreground = Brushes.White;
        }
        else
        {
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
        }
    }

    private async void VanillaLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (!LaunchState.AcceptsClick(GameProcessQuery.AnyRunning()))
        {
            ShowGameRunning();
            return;
        }
        if (_settings == null) return;
        var gamePath = _settings.GamePath;
        try
        {
            await Task.Run(() =>
            {
                ParkDoorstop(gamePath);
                ResourceExLink.Detach(gamePath);
            });
            StartSteam();
            SetStatus("已通过 Steam 启动原版");
            ShowLaunchNotice("通过 Steam 启动原版");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void ParkDoorstop(string gamePath)
    {
        ParkFile(Path.Combine(gamePath, "winhttp.dll"));
        ParkFile(Path.Combine(gamePath, "doorstop_config.ini"));
    }

    private static void ParkFile(string path)
    {
        if (!File.Exists(path)) return;
        var parked = path + ".off";
        if (File.Exists(parked)) File.Delete(parked);
        File.Move(path, parked);
    }

    private void StartSteam()
    {
        var steamExe = SteamLaunch.FindInstalled();
        if (steamExe == null)
            throw new InvalidOperationException("找不到 Steam.exe");
        var started = Process.Start(new ProcessStartInfo
        {
            FileName = steamExe,
            Arguments = SteamLaunch.Arguments,
            WorkingDirectory = Path.GetDirectoryName(steamExe) ?? "",
            UseShellExecute = false
        });
        if (started == null)
            throw new InvalidOperationException("无法启动 Steam");
        started.Dispose();
        DetachOwned();
        _watch.Arm();
    }

    private async Task RefreshUpdateBannerAsync(string profilePath)
    {
        try
        {
            string body;
            using (var http = new HttpClient { Timeout = System.TimeSpan.FromSeconds(8) })
                body = await http.GetStringAsync(MarketClient.BaseUrl + "/mods");
            if (!IsLoaded) return;
            var catalog = MarketClient.ParseList(body);
            var installed = InstalledMods.Read(profilePath);
            var plan = ModUpdatePlan.Select(installed, catalog.Mods);
            _updatePlan = new List<ModUpdateItem>(plan);
            UpdateBanner.Text = ModUpdatePlan.Banner(plan.Count);
            UpdateBanner.Visibility = plan.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (System.Exception)
        {
            if (!IsLoaded) return;
            UpdateBanner.Visibility = Visibility.Collapsed;
        }
    }

    private async Task OfferLocalZipUpdateAsync(string profilePath)
    {
        try
        {
            await OfferLocalZipUpdateCoreAsync(profilePath);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            System.Windows.MessageBox.Show(ex.Message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OfferLocalZipUpdateCoreAsync(string profilePath)
    {
        if (_configRoot == null || _settings == null) return;
        var installed = InstalledMods.Read(profilePath);
        var plan = LocalZipPlan.NewerThanInstalled(_configRoot, installed);
        if (plan.Count == 0) return;
        if (BlockGameWrite("更新模组")) return;
        var lines = new System.Text.StringBuilder();
        lines.AppendLine("这个配置档还没用已下载的新版本。确定后直接解压，不再重新下载：");
        foreach (var item in plan)
            lines.AppendLine(item.Id + "  " + item.LocalVersion + " → " + item.RemoteVersion);
        if (System.Windows.MessageBox.Show(lines.ToString(), "更新当前配置档",
                MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return;
        var gamePath = _settings.GamePath;
        SetBusy(true, "正在解压已下载的模组…");
        try
        {
            foreach (var item in plan)
            {
                var zip = LocalZipPlan.ZipPath(_configRoot, item.Id, item.RemoteVersion);
                if (zip == null) continue;
                await Task.Run(() => ZipLayout.Extract(zip, profilePath, gamePath));
            }
            await ReloadModsAsync(profilePath);
            SetStatus("已用本地压缩包更新");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void UpdateBanner_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CurrentProfile == null || _settings == null || _configRoot == null) return;
        if (BlockGameWrite("更新模组")) return;
        var token = SessionStore.ReadToken(_configRoot);
        if (token == null)
        {
            System.Windows.MessageBox.Show("请先在模组市场登录。", "更新", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var plan = _updatePlan;
        if (plan.Count == 0) return;
        var lines = new System.Text.StringBuilder();
        lines.AppendLine("以下模组将下载并安装：");
        foreach (var item in plan)
            lines.AppendLine(item.Id + "  " + item.LocalVersion + " → " + item.RemoteVersion);
        if (System.Windows.MessageBox.Show(lines.ToString(), "更新所有已安装模组",
                MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return;
        var profilePath = CurrentProfile.Path;
        var gamePath = _settings.GamePath;
        try
        {
            SetBusy(true, "正在更新模组…");
            foreach (var item in plan)
            {
                var zip = await DownloadModZipAsync(token, item.Id, item.RemoteVersion);
                await Task.Run(() => ZipLayout.Extract(zip, profilePath, gamePath));
            }
            await ReloadModsAsync(profilePath);
            SetStatus("模组已更新");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<string> DownloadModZipAsync(string token, string id, string version)
    {
        var url = MarketClient.BaseUrl + "/mods/" + Uri.EscapeDataString(id) + "/file?version=" + Uri.EscapeDataString(version);
        using (var http = new HttpClient { Timeout = System.TimeSpan.FromMinutes(5) })
        using (var request = new HttpRequestMessage(HttpMethod.Get, url))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(id + " 下载失败");
                string? disposition = response.Content.Headers.ContentDisposition?.ToString();
                var name = MarketClient.DownloadFileName(disposition, id, version);
                var dest = PackageFiles.SavePath(_configRoot!, name);
                var temp = dest + ".part";
                using (var job = DownloadHub.Begin(id + " " + version))
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = File.Create(temp))
                    await DownloadHub.CopyAsync(input, output, response.Content.Headers.ContentLength, job.Report);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(temp, dest);
                return dest;
            }
        }
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (!LaunchState.AcceptsClick(GameProcessQuery.AnyRunning()))
        {
            ShowGameRunning();
            return;
        }
        if (CurrentProfile == null || _settings == null) return;
        var gamePath = _settings.GamePath;
        var profilePath = CurrentProfile.Path;
        try
        {
            await Task.Run(() =>
            {
                CoreApi.PrepareLaunch(gamePath, profilePath);
                ResourceExLink.Attach(gamePath, profilePath);
            });
            StartSteam();
            SetStatus("已通过 Steam 启动");
            ShowLaunchNotice("通过 Steam 启动东方夜雀食堂");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowLaunchNotice(string heading)
    {
        var notice = new LaunchNoticeWindow(heading) { Owner = this };
        notice.ShowDialog();
    }

    private string DisplayName(string folder)
    {
        foreach (var row in _allMods)
        {
            if (string.Equals(row.Folder, folder, StringComparison.OrdinalIgnoreCase) && row.Name.Length > 0)
                return row.Name;
        }
        return folder;
    }

    private async Task CheckForManagerUpdateAsync()
    {
        string? remote;
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string body;
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
                body = await http.GetStringAsync(ManagerUpdate.VersionUrl);
            if (!IsLoaded) return;
            remote = ManagerUpdate.ReadVersion(body);
        }
        catch (Exception)
        {
            return;
        }

        var local = typeof(MainWindow).Assembly.GetName().Version?.ToString();
        if (!ManagerUpdate.ShouldPrompt(remote, local)) return;
        var answer = System.Windows.MessageBox.Show(
            "发现新版本 " + remote + "，是否现在更新？",
            "更新",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            AppUpdater.Start(AppDomain.CurrentDomain.BaseDirectory);
            SetStatus("已交给更新程序");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("更新失败");
        }
    }

    private void PollGame()
    {
        int[] ids;
        try
        {
            ids = GameProcessQuery.Ids();
        }
        catch (Exception)
        {
            return;
        }

        var running = ids.Length > 0;
        if (LaunchState.JustStopped(_gameRunning, running))
            SetStatus("就绪");
        _gameRunning = running;
        LaunchButton.Content = LaunchState.Caption(running);
        var export = _watch.Tick(ids, ReadOwnedExitCode);
        AttachOwnedIfNeeded();
        if (!_watch.Armed)
            DetachOwned();
        if (export == true)
            _ = PromptCrashExportAsync();
    }

    private void AttachOwnedIfNeeded()
    {
        var pid = _watch.OwnedPid;
        if (pid == null) return;
        if (_owned != null && _owned.Id == pid.Value) return;
        DetachOwned();
        try
        {
            _owned = Process.GetProcessById(pid.Value);
        }
        catch (Exception)
        {
            _owned = null;
        }
    }

    private int? ReadOwnedExitCode(int pid)
    {
        if (_owned == null || _owned.Id != pid)
        {
            try
            {
                using (var process = Process.GetProcessById(pid))
                {
                    if (!process.HasExited) return null;
                    return process.ExitCode;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        try
        {
            if (!_owned.HasExited) return null;
            return _owned.ExitCode;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void DetachOwned()
    {
        if (_owned == null) return;
        _owned.Dispose();
        _owned = null;
    }

    private void ShowGameRunning()
    {
        LaunchButton.Content = LaunchState.Caption(true);
    }

    private bool BlockGameWrite(string action)
    {
        var running = GameProcessQuery.AnyRunning();
        LaunchButton.Content = LaunchState.Caption(running);
        var message = ModWriteGuard.Reject(running, action);
        if (message == null) return false;
        System.Windows.MessageBox.Show(message, "请先退出游戏", MessageBoxButton.OK, MessageBoxImage.Information);
        return true;
    }

    private async Task PromptCrashExportAsync()
    {
        if (_exportPrompt) return;
        var profile = CurrentProfile;
        if (profile == null) return;
        _exportPrompt = true;
        try
        {
            var answer = System.Windows.MessageBox.Show(
                "游戏异常退出，是否导出日志和存档？",
                "导出",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择导出目录";
                dialog.ShowNewFolderButton = true;
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                var folder = dialog.SelectedPath;
                if (string.IsNullOrWhiteSpace(folder)) return;

                var user = Environment.GetEnvironmentVariable("USERPROFILE");
                if (string.IsNullOrWhiteSpace(user))
                    user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(user))
                    user = "";
                var dest = Path.Combine(folder, CrashPackage.FileName(DateTime.Now));
                var profilePath = profile.Path;
                if (CrashPackage.Collect(profilePath, user).Count == 0)
                {
                    System.Windows.MessageBox.Show("没有可导出的日志或存档。", "导出", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                SetBusy(true, "正在导出…");
                try
                {
                    await Task.Run(() => CrashPackage.Create(profilePath, user, dest));
                    SetStatus("已导出 " + Path.GetFileName(dest));
                }
                finally
                {
                    SetBusy(false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _exportPrompt = false;
        }
    }

    private void TryAttachResources()
    {
        if (_settings == null || CurrentProfile == null) return;
        if (GameProcessQuery.AnyRunning()) return;
        try
        {
            ResourceExLink.Attach(_settings.GamePath, CurrentProfile.Path);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_configRoot == null) return;
        var win = new LoginWindow(_configRoot) { Owner = this };
        win.ShowDialog();
        if (!win.SessionChanged) return;
        UpdateLoginCaption();
        _market?.ApplySession();
    }

    private void UpdateLoginCaption()
    {
        var loggedIn = _configRoot != null && SessionStore.ReadToken(_configRoot) != null;
        LoginButton.Content = loggedIn ? "已登录" : "登录";
    }

    private void SetBusy(bool busy, string? text = null)
    {
        BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SideTop.IsEnabled = !busy;
        if (text != null) BusyText.Text = text;
    }

    private void OnDownloadsChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnDownloadsChanged));
            return;
        }

        var jobs = DownloadHub.Snapshot();
        while (_downloads.Count > jobs.Length)
            _downloads.RemoveAt(_downloads.Count - 1);
        for (var i = 0; i < jobs.Length; i++)
        {
            if (i == _downloads.Count)
                _downloads.Add(new DownloadRowVm());
            _downloads[i].Apply(jobs[i]);
        }

        DownloadHost.Visibility = jobs.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task TrackFrameworkAsync(string title, string zipName, string url, Action work)
    {
        var zipPath = Path.Combine(_configRoot!, "cache", zipName);
        using var job = DownloadHub.Begin(title);
        using var cts = new CancellationTokenSource();
        var poll = PollZipAsync(zipPath, job, cts.Token);
        var length = ApplyLengthAsync(url, job, cts.Token);
        try
        {
            await Task.Run(work);
        }
        finally
        {
            cts.Cancel();
            try { await Task.WhenAll(poll, length); }
            catch (Exception) { }
        }
    }

    private static async Task PollZipAsync(string path, DownloadHub.Job job, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            long len = 0;
            try
            {
                if (File.Exists(path))
                    len = new FileInfo(path).Length;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            job.Report(len, null);
            try { await Task.Delay(200, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static async Task ApplyLengthAsync(string url, DownloadHub.Job job, CancellationToken token)
    {
        var total = await TryLengthAsync(url, token);
        if (token.IsCancellationRequested || total is not > 0) return;
        job.Report(job.Received, total);
    }

    private static async Task<long?> TryLengthAsync(string url, CancellationToken token)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await http.SendAsync(request, token).ConfigureAwait(false);
            var length = response.Content.Headers.ContentLength;
            return length is > 0 ? length : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;
}

public sealed class DownloadRowVm : INotifyPropertyChanged
{
    string _title = "";
    string _detail = "";
    double _percent;
    bool _unknown = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value, nameof(Title));
    }

    public string Detail
    {
        get => _detail;
        private set => Set(ref _detail, value, nameof(Detail));
    }

    public double Percent
    {
        get => _percent;
        private set => Set(ref _percent, value, nameof(Percent));
    }

    public bool Unknown
    {
        get => _unknown;
        private set => Set(ref _unknown, value, nameof(Unknown));
    }

    public void Apply(DownloadSnapshot snap)
    {
        Title = snap.Title;
        Detail = snap.Detail;
        Percent = snap.Percent;
        Unknown = snap.Unknown;
    }

    void Set<T>(ref T field, T value, string name)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class ModRow
{
    public ModRow(ModInfo info, ModLabel label)
    {
        Folder = info.Name;
        Name = label.Name;
        VersionText = label.Version;
        Description = label.Description;
        Enabled = info.Enabled;
        SortKey = new ModSortKeyset(label.Name, label.Version, label.Author, label.DownloadedAt, label.UpdatedAt);
    }

    public string Folder { get; }
    public string Name { get; }
    public string VersionText { get; }
    public string Description { get; }
    public bool Enabled { get; }
    public ModSortKeyset SortKey { get; }
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
