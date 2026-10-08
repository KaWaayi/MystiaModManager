using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Microsoft.Win32;
using MystiaModManager.Logic;
using MystiaModManager.Services;
using Newtonsoft.Json;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace MystiaModManager;

public partial class MarketWindow : UserControl
{
    private readonly string _configRoot;
    private readonly HttpClient _http;
    private string? _token;
    private bool _busy;
    private bool _suppress;
    private int _listGeneration;
    private int _detailGeneration;

    public event EventHandler<int>? CatalogCountChanged;
    public event EventHandler? ModsInstalled;
    public event EventHandler? SessionChanged;
    public Func<(string ProfilePath, string GamePath)?>? InstallTarget { get; set; }

    public MarketWindow(string configRoot)
    {
        if (configRoot == null) throw new ArgumentNullException(nameof(configRoot));
        _configRoot = configRoot;
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        _http = new HttpClient
        {
            BaseAddress = new Uri(MarketClient.BaseUrl + "/"),
            Timeout = TimeSpan.FromMinutes(10)
        };
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                _token = ReadStoredToken();
                AccountText.Text = string.IsNullOrEmpty(_token)
                    ? "未登录。可以查看列表。订阅、上传和下载请先在左下角登录。"
                    : "已登录。";
                await LoadListAsync();
            }
            catch (Exception ex)
            {
                Show(ex.Message, "模组市场", MessageBoxImage.Error);
            }
        };
        Unloaded += (_, _) => _http.Dispose();
    }

    public void ApplySession()
    {
        _token = ReadStoredToken();
        AccountText.Text = string.IsNullOrEmpty(_token)
            ? "未登录。可以查看列表。订阅、上传和下载请先在左下角登录。"
            : "已登录。";
        if (IsLoaded)
            _ = LoadListAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await Run(LoadListAsync);
    }

    private async void Subscribe_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var mod = SelectedMod();
        if (mod == null) return;
        var subscribed = mod.Subscribed == true;
        if (!RequireLogin(subscribed ? "取消订阅" : "订阅")) return;
        await Run(() => ChangeSubscriptionAsync(subscribed ? "unsubscribe" : "subscribe", mod, !subscribed));
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var mod = SelectedMod();
        if (mod == null) return;
        if (!RequireLogin("下载")) return;
        var version = VersionBox.SelectedItem as string;
        if (version == null || version.Trim().Length == 0)
        {
            Show("请选择版本。", "下载", MessageBoxImage.Information);
            return;
        }

        var modId = mod.Id;
        await Run(() => DownloadAsync(modId, version));
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!RequireLogin("上传")) return;
        var dialog = new OpenFileDialog
        {
            Filter = "ZIP|*.zip",
            Title = "选择要上传的模组"
        };
        if (dialog.ShowDialog() != true) return;
        var source = dialog.FileName;
        await Run(() => UploadAsync(source));
    }

    private async void ModList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (ModList.SelectedItem is not MarketRow row)
        {
            PaintSubscribe(false);
            return;
        }
        PaintSubscribe(row.Mod.Subscribed == true);
        var id = row.Mod.Id;
        var generation = ++_detailGeneration;
        try
        {
            var body = await GetStringAsync("mods/" + Uri.EscapeDataString(id), true);
            if (!IsLoaded || generation != _detailGeneration) return;
            ShowDetail(row, MarketClient.ParseDetail(body));
        }
        catch (Exception ex)
        {
            if (!IsLoaded || generation != _detailGeneration) return;
            Show(ex.Message, "模组市场", MessageBoxImage.Error);
        }
    }

    private async Task LoadListAsync()
    {
        var generation = ++_listGeneration;
        var selected = (ModList.SelectedItem as MarketRow)?.Mod.Id;
        var body = await GetStringAsync("mods", true);
        if (!IsLoaded || generation != _listGeneration) return;

        var catalog = MarketClient.ParseList(body);
        UploadButton.Visibility = catalog.ShowUpload ? Visibility.Visible : Visibility.Collapsed;
        var rows = new List<MarketRow>();
        foreach (var mod in catalog.Mods)
            rows.Add(new MarketRow(mod));

        MarketRow? pick = null;
        foreach (var row in rows)
        {
            if (selected != null && row.Mod.Id == selected)
            {
                pick = row;
                break;
            }
        }
        if (pick == null && rows.Count > 0)
            pick = rows[0];

        _suppress = true;
        ModList.ItemsSource = rows;
        _suppress = false;
        if (pick == null)
        {
            ClearDetail();
            StatusText.Text = "市场上还没有模组";
            CatalogCountChanged?.Invoke(this, 0);
            return;
        }

        ModList.SelectedItem = pick;
        StatusText.Text = "共 " + rows.Count + " 个模组";
        CatalogCountChanged?.Invoke(this, rows.Count);
    }

    private async Task ChangeSubscriptionAsync(string path, MarketMod mod, bool subscribe)
    {
        if (subscribe)
        {
            var blocked = ModWriteGuard.Reject(GameProcessQuery.AnyRunning(), "安装");
            if (blocked != null)
            {
                Show(blocked, "订阅", MessageBoxImage.Information);
                return;
            }
            if (InstallTarget?.Invoke() == null)
            {
                Show("还没有配置档，不能把模组装进去。", "订阅", MessageBoxImage.Information);
                return;
            }
            if (!await ConfirmHardDependenciesAsync(mod)) return;
        }
        else
        {
            var blocked = ModWriteGuard.Reject(GameProcessQuery.AnyRunning(), "卸载");
            if (blocked != null)
            {
                Show(blocked, "取消订阅", MessageBoxImage.Information);
                return;
            }
        }

        var result = await PostJsonAsync(path, new { modId = mod.Id }, true);
        if (subscribe)
        {
            var decision = MarketClient.ReadSubscribe(result.Status, result.Body);
            if (decision.Missing.Count > 0)
            {
                var head = string.IsNullOrEmpty(decision.Error) ? "前置尚未上架" : decision.Error;
                Show(head + "：\n" + string.Join("\n", decision.Missing), "订阅", MessageBoxImage.Information);
                return;
            }

            if (!decision.Succeeded)
            {
                if (result.Status == 401) ForgetToken();
                Show(decision.Error ?? "订阅失败", "订阅", MessageBoxImage.Information);
                return;
            }

            StatusText.Text = "正在安装 " + mod.Name;
            await InstallSubscribedAsync(mod.Id);
            StatusText.Text = "已安装到当前配置档";
            ModsInstalled?.Invoke(this, EventArgs.Empty);
            await LoadListAsync();
            return;
        }

        if (result.Status == 401)
        {
            ForgetToken();
            Show(MarketClient.ReadError(result.Body) ?? "请先登录。", "取消订阅", MessageBoxImage.Information);
            return;
        }

        if (result.Status < 200 || result.Status >= 300)
        {
            Show(MarketClient.ReadError(result.Body) ?? "取消订阅失败", "取消订阅", MessageBoxImage.Information);
            return;
        }

        StatusText.Text = "已取消订阅 " + mod.Name;
        RemoveFromProfiles(mod.Id);
        await LoadListAsync();
        ModsInstalled?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> ConfirmHardDependenciesAsync(MarketMod mod)
    {
        var detail = MarketClient.ParseDetail(await GetStringAsync("mods/" + Uri.EscapeDataString(mod.Id), true));
        var catalog = MarketClient.ParseList(await GetStringAsync("mods", true));
        var subscribed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in catalog.Mods)
        {
            if (item.Subscribed == true) subscribed.Add(item.Id);
        }
        var names = new List<string>();
        foreach (var dep in detail.Dependencies)
        {
            if (!dep.Hard || subscribed.Contains(dep.Id) || dep.Id == mod.Id) continue;
            string? name = null;
            foreach (var item in catalog.Mods)
            {
                if (item.Id == dep.Id)
                {
                    name = string.IsNullOrEmpty(item.Name) ? item.Id : item.Name;
                    break;
                }
            }
            names.Add(name ?? dep.Id);
        }
        if (names.Count == 0) return true;
        var text = "还要一并订阅这些前置：\n" + string.Join("\n", names);
        return System.Windows.MessageBox.Show(text, "订阅", MessageBoxButton.OKCancel, MessageBoxImage.Information)
            == MessageBoxResult.OK;
    }

    private void RemoveFromProfiles(string modId)
    {
        foreach (var profile in CoreApi.ListProfiles(_configRoot))
        {
            var nodes = InstalledMods.ReadNodes(profile.Path);
            ModNode? target = null;
            foreach (var node in nodes)
            {
                if (string.Equals(node.Id, modId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(node.Folder, modId, StringComparison.OrdinalIgnoreCase))
                {
                    target = node;
                    break;
                }
            }
            if (target == null) continue;
            var disable = ModActionPlan.FoldersToDisable(new[] { target.Folder }, nodes);
            foreach (var folder in disable)
            {
                if (string.Equals(folder, target.Folder, StringComparison.OrdinalIgnoreCase)) continue;
                CoreApi.SetModEnabled(profile.Path, folder, false);
            }
            var dir = Path.Combine(profile.Path, "BepInEx", "plugins", target.Folder);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private async Task InstallSubscribedAsync(string rootId)
    {
        var target = InstallTarget?.Invoke();
        if (target == null)
            throw new InvalidOperationException("还没有配置档，不能把模组装进去。");
        var profilePath = target.Value.ProfilePath;
        var gamePath = target.Value.GamePath;
        var pending = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var install = new List<(string Id, string Version)>();
        pending.Enqueue(rootId);
        seen.Add(rootId);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            var detail = MarketClient.ParseDetail(await GetStringAsync("mods/" + Uri.EscapeDataString(id), true));
            var version = detail.LatestVersion ?? "";
            if (version.Length == 0)
                throw new InvalidOperationException(id + " 没有可安装的版本");
            foreach (var dep in detail.Dependencies)
            {
                if (!dep.Hard || !seen.Add(dep.Id)) continue;
                var sub = await PostJsonAsync("subscribe", new { modId = dep.Id }, true);
                var decision = MarketClient.ReadSubscribe(sub.Status, sub.Body);
                if (!decision.Succeeded)
                {
                    var message = decision.Missing.Count > 0
                        ? (decision.Error ?? "前置尚未上架") + "：\n" + string.Join("\n", decision.Missing)
                        : decision.Error ?? dep.Id + " 订阅失败";
                    throw new InvalidOperationException(message);
                }
                pending.Enqueue(dep.Id);
            }
            install.Add((id, version));
        }

        install.Reverse();
        foreach (var item in install)
        {
            var zip = await SaveZipAsync(item.Id, item.Version);
            var id = item.Id;
            var version = item.Version;
            await Task.Run(() => ZipLayout.Extract(zip, profilePath, gamePath));
            StatusText.Text = "已安装 " + id + " " + version;
        }
    }

    private async Task<string> SaveZipAsync(string modId, string version)
    {
        var path = "mods/" + Uri.EscapeDataString(modId) + "/file?version=" + Uri.EscapeDataString(version);
        using (var request = new HttpRequestMessage(HttpMethod.Get, path))
        {
            ApplyBearer(request);
            using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    if ((int)response.StatusCode == 401) ForgetToken();
                    throw new InvalidOperationException(MarketClient.ReadError(errorBody) ?? (modId + " 下载失败"));
                }

                var dest = PackageFiles.SavePath(_configRoot, modId + "_" + version + ".zip");
                var temp = dest + ".part";
                try
                {
                    using (var job = DownloadHub.Begin(modId + " " + version))
                    using (var input = await response.Content.ReadAsStreamAsync())
                    using (var output = File.Create(temp))
                        await DownloadHub.CopyAsync(input, output, response.Content.Headers.ContentLength, job.Report);
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(temp, dest);
                }
                catch
                {
                    if (File.Exists(temp)) File.Delete(temp);
                    throw;
                }

                return dest;
            }
        }
    }

    private async Task DownloadAsync(string modId, string version)
    {
        var path = "mods/" + Uri.EscapeDataString(modId) + "/file?version=" + Uri.EscapeDataString(version);
        using (var request = new HttpRequestMessage(HttpMethod.Get, path))
        {
            ApplyBearer(request);
            using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    if ((int)response.StatusCode == 401) ForgetToken();
                    Show(MarketClient.ReadError(errorBody) ?? "下载失败", "下载", MessageBoxImage.Information);
                    return;
                }

                var fileName = MarketClient.DownloadFileName(DispositionHeader(response), modId, version);
                var dest = PackageFiles.SavePath(_configRoot, fileName);
                var temp = dest + ".part";
                try
                {
                    using (var job = DownloadHub.Begin(modId + " " + version))
                    using (var input = await response.Content.ReadAsStreamAsync())
                    using (var output = File.Create(temp))
                        await DownloadHub.CopyAsync(input, output, response.Content.Headers.ContentLength, job.Report);
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(temp, dest);
                }
                catch
                {
                    if (File.Exists(temp)) File.Delete(temp);
                    throw;
                }

                StatusText.Text = "已保存 " + fileName;
                Show("已保存到\n" + dest, "下载", MessageBoxImage.Information);
            }
        }
    }

    private async Task UploadAsync(string source)
    {
        using (var stream = File.OpenRead(source))
        using (var request = new HttpRequestMessage(HttpMethod.Post, "mods/upload"))
        {
            ApplyBearer(request);
            var form = new MultipartFormDataContent();
            var file = new StreamContent(stream);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(file, "file", Path.GetFileName(source));
            request.Content = form;
            using (var response = await _http.SendAsync(request))
            {
                var body = await response.Content.ReadAsStringAsync();
                var status = (int)response.StatusCode;
                if (status == 403)
                {
                    UploadButton.Visibility = Visibility.Collapsed;
                    Show("没有上传权限。", "上传", MessageBoxImage.Information);
                    return;
                }

                if (status == 401)
                {
                    ForgetToken();
                    Show(MarketClient.ReadError(body) ?? "请先登录。", "上传", MessageBoxImage.Information);
                    return;
                }

                if (status < 200 || status >= 300)
                {
                    Show(MarketClient.ReadError(body) ?? "上传失败", "上传", MessageBoxImage.Information);
                    return;
                }
            }
        }

        StatusText.Text = "上传成功";
        await LoadListAsync();
    }

    private async Task<string> GetStringAsync(string path, bool anonymousOn401)
    {
        var result = await SendTextAsync(HttpMethod.Get, path, null, _token != null);
        if (result.Status == 401 && anonymousOn401 && _token != null)
        {
            ForgetToken();
            result = await SendTextAsync(HttpMethod.Get, path, null, false);
        }

        if (result.Status < 200 || result.Status >= 300)
            throw new InvalidOperationException(MarketClient.ReadError(result.Body) ?? "无法获取数据");
        return result.Body;
    }

    private Task<(int Status, string Body)> PostJsonAsync(string path, object payload, bool bearer)
    {
        var json = JsonConvert.SerializeObject(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return SendTextAsync(HttpMethod.Post, path, content, bearer);
    }

    private async Task<(int Status, string Body)> SendTextAsync(HttpMethod method, string path, HttpContent? content, bool bearer)
    {
        using (var request = new HttpRequestMessage(method, path))
        {
            request.Content = content;
            if (bearer) ApplyBearer(request);
            using (var response = await _http.SendAsync(request))
            {
                var body = await response.Content.ReadAsStringAsync();
                return ((int)response.StatusCode, body);
            }
        }
    }

    private void ApplyBearer(HttpRequestMessage request)
    {
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException("请先登录。");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    private string? ReadStoredToken()
    {
        try
        {
            return SessionStore.ReadToken(_configRoot);
        }
        catch (Exception)
        {
            var path = SessionStore.FilePath(_configRoot);
            if (File.Exists(path)) File.Delete(path);
            return null;
        }
    }

    private void ForgetToken()
    {
        _token = null;
        var path = SessionStore.FilePath(_configRoot);
        if (File.Exists(path)) File.Delete(path);
        AccountText.Text = "未登录。可以查看列表。订阅、上传和下载请先在左下角登录。";
        UploadButton.Visibility = Visibility.Collapsed;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool RequireLogin(string action)
    {
        if (!string.IsNullOrEmpty(_token)) return true;
        Show("请先在左下角登录。", action, MessageBoxImage.Information);
        return false;
    }

    private MarketMod? SelectedMod()
    {
        if (ModList.SelectedItem is MarketRow row) return row.Mod;
        Show("请先选择模组。", "模组市场", MessageBoxImage.Information);
        return null;
    }

    private MarketDetail? _shownDetail;
    private MarketMod? _shownMod;
    private ZipNotes? _shownNotes;

    private void ShowDetail(MarketRow row, MarketDetail detail)
    {
        var title = string.IsNullOrEmpty(detail.Name) ? row.Mod.Id : detail.Name;
        if (row.Mod.Subscribed == true)
            title += "（已订阅）";
        DetailTitle.Text = title;

        var text = new StringBuilder();
        if (!string.IsNullOrEmpty(detail.Author))
            text.AppendLine("作者：" + detail.Author);
        text.AppendLine("下载次数：" + detail.DownloadCount);
        text.AppendLine();
        text.AppendLine(string.IsNullOrEmpty(detail.Description) ? "没有简介。" : detail.Description);
        text.AppendLine();
        text.AppendLine("依赖：");
        if (detail.Dependencies.Count == 0)
        {
            text.AppendLine("无");
        }
        else
        {
            foreach (var dep in detail.Dependencies)
                text.AppendLine((dep.Hard ? "必须  " : "可选  ") + dep.Id);
        }

        DetailBody.Text = text.ToString().TrimEnd();
        _shownDetail = detail;
        _shownMod = row.Mod;
        _shownNotes = LoadNotes(row.Mod.Id, detail.LatestVersion);
        ShowIcon(_shownNotes);
        IntroTab_Click(this, new RoutedEventArgs());
        var versions = new List<string>(detail.Versions);
        var latest = detail.LatestVersion;
        if (latest != null && latest.Length > 0 && !versions.Contains(latest))
            versions.Insert(0, latest);
        VersionBox.ItemsSource = versions;
        if (latest != null && latest.Length > 0 && versions.Contains(latest))
            VersionBox.SelectedItem = latest;
        else if (versions.Count > 0)
            VersionBox.SelectedIndex = 0;
        else
            VersionBox.SelectedIndex = -1;
    }

    private void ClearDetail()
    {
        DetailTitle.Text = "市场上还没有模组";
        DetailBody.Text = "";
        DetailBody.Visibility = Visibility.Visible;
        DepList.Visibility = Visibility.Collapsed;
        DepList.ItemsSource = null;
        IconImage.Visibility = Visibility.Collapsed;
        IconImage.Source = null;
        VersionBox.ItemsSource = null;
        _shownDetail = null;
        _shownMod = null;
        _shownNotes = null;
        PaintSubscribe(false);
    }

    private void PaintSubscribe(bool subscribed)
    {
        SubscribeLabel.Text = subscribed ? "取消订阅" : "订阅";
        if (subscribed)
        {
            SubscribeButton.Appearance = ControlAppearance.Secondary;
            SubscribeButton.Background = Brushes.White;
            SubscribeButton.Foreground = new SolidColorBrush(Color.FromRgb(0xD1, 0x24, 0x2F));
            SubscribeButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0x24, 0x2F));
            return;
        }

        SubscribeButton.Appearance = ControlAppearance.Primary;
        SubscribeButton.ClearValue(Control.BackgroundProperty);
        SubscribeButton.ClearValue(Control.ForegroundProperty);
        SubscribeButton.ClearValue(Control.BorderBrushProperty);
    }

    private void IntroTab_Click(object sender, RoutedEventArgs e)
    {
        DetailBody.Visibility = Visibility.Visible;
        DepList.Visibility = Visibility.Collapsed;
        if (_shownNotes != null && _shownNotes.Readme.Length > 0)
            DetailBody.Text = _shownNotes.Readme;
        else if (_shownDetail != null)
            DetailBody.Text = string.IsNullOrEmpty(_shownDetail.Description) ? "没有介绍。" : _shownDetail.Description;
    }

    private void DepTab_Click(object sender, RoutedEventArgs e)
    {
        DetailBody.Visibility = Visibility.Collapsed;
        DepList.Visibility = Visibility.Visible;
        var rows = new List<DepPick>();
        if (_shownDetail != null)
        {
            foreach (var dep in _shownDetail.Dependencies)
                rows.Add(new DepPick(dep.Id, (dep.Hard ? "必须  " : "可选  ") + dep.Id));
        }
        DepList.ItemsSource = rows;
    }

    private void TreeTab_Click(object sender, RoutedEventArgs e)
    {
        DetailBody.Visibility = Visibility.Visible;
        DepList.Visibility = Visibility.Collapsed;
        if (_shownDetail != null && _shownDetail.Files.Count > 0)
            DetailBody.Text = Izakaya.Rules.ZipTree.Format(_shownDetail.Files);
        else if (_shownNotes != null && _shownNotes.Tree.Length > 0)
            DetailBody.Text = _shownNotes.Tree;
        else
            DetailBody.Text = "服务器还没有这个模组的文件夹层级。";
    }

    private void DepList_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DepList.SelectedItem is not DepPick pick) return;
        if (ModList.ItemsSource is not IEnumerable<MarketRow> rows) return;
        foreach (var row in rows)
        {
            if (row.Mod.Id != pick.Id) continue;
            ModList.SelectedItem = row;
            return;
        }
        Show("市场上没有 " + pick.Id, "依赖", MessageBoxImage.Information);
    }

    private ZipNotes? LoadNotes(string id, string? version)
    {
        var zip = ZipNotes.FindZip(_configRoot, id, version);
        if (zip == null) return null;
        try
        {
            return ZipNotes.Read(zip);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ShowIcon(ZipNotes? notes)
    {
        IconImage.Source = null;
        IconImage.Visibility = Visibility.Collapsed;
        if (notes?.Icon == null || notes.Icon.Length == 0) return;
        try
        {
            using (var stream = new MemoryStream(notes.Icon))
            {
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) return;
                IconImage.Source = decoder.Frames[0];
                IconImage.Visibility = Visibility.Visible;
            }
        }
        catch (Exception)
        {
            IconImage.Visibility = Visibility.Collapsed;
        }
    }

    sealed class DepPick
    {
        public DepPick(string id, string title)
        {
            Id = id;
            Title = title;
        }

        public string Id { get; }
        public string Title { get; }
        public override string ToString() => Title;
    }

    private static string? DispositionHeader(HttpResponseMessage response)
    {
        if (response.Content.Headers.TryGetValues("Content-Disposition", out var values))
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }

        return response.Content.Headers.ContentDisposition?.ToString();
    }

    private async Task Run(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Show(ex.Message, "模组市场", MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Show(string message, string title, MessageBoxImage image)
    {
        if (!IsLoaded) return;
        var owner = Window.GetWindow(this);
        if (owner == null)
            System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, image);
        else
            System.Windows.MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
    }

    sealed class MarketRow
    {
        public MarketRow(MarketMod mod)
        {
            Mod = mod;
            var name = string.IsNullOrEmpty(mod.Name) ? mod.Id : mod.Name;
            var version = string.IsNullOrEmpty(mod.LatestVersion) ? "" : "  " + mod.LatestVersion;
            var subscribed = mod.Subscribed == true ? "  已订阅" : "";
            Title = name + version + subscribed;
        }

        public MarketMod Mod { get; }
        public string Title { get; }
    }
}
