using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using MystiaModManager.Logic;
using Newtonsoft.Json;

namespace MystiaModManager;

public partial class LoginWindow : Window
{
    private readonly string _configRoot;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public bool SessionChanged { get; private set; }

    public LoginWindow(string configRoot)
    {
        InitializeComponent();
        _configRoot = configRoot;
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var loggedIn = SessionStore.ReadToken(configRoot) != null;
        FormPanel.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        LoggedPanel.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        Closed += (_, _) => _http.Dispose();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (!TryAccount(out var username, out var password)) return;
        try
        {
            var result = await PostAsync("login", username, password);
            if (result.Status < 200 || result.Status >= 300)
            {
                Show(MarketClient.ReadError(result.Body) ?? "登录失败");
                return;
            }
            var token = MarketClient.ReadLoginToken(result.Body);
            if (string.IsNullOrEmpty(token))
            {
                Show("登录失败");
                return;
            }
            SessionStore.SaveToken(_configRoot, token!);
            SessionChanged = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Show(ex.Message);
        }
    }

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        if (!TryAccount(out var username, out var password)) return;
        try
        {
            var result = await PostAsync("register", username, password);
            if (result.Status < 200 || result.Status >= 300)
            {
                Show(MarketClient.ReadError(result.Body) ?? "注册失败");
                return;
            }
            PasswordBox.Clear();
            Show("注册成功，请登录。");
        }
        catch (Exception ex)
        {
            Show(ex.Message);
        }
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        SessionStore.Clear(_configRoot);
        SessionChanged = true;
        DialogResult = true;
    }

    private bool TryAccount(out string username, out string password)
    {
        username = UserBox.Text.Trim();
        password = PasswordBox.Password;
        if (username.Length == 0 || password.Length == 0)
        {
            Show("请填写用户名和密码。");
            return false;
        }
        return true;
    }

    private async System.Threading.Tasks.Task<(int Status, string Body)> PostAsync(string path, string username, string password)
    {
        var json = JsonConvert.SerializeObject(new { username, password });
        using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
        using (var response = await _http.PostAsync(MarketClient.BaseUrl + "/" + path, content))
        {
            var body = await response.Content.ReadAsStringAsync();
            return ((int)response.StatusCode, body);
        }
    }

    private void Show(string message)
    {
        System.Windows.MessageBox.Show(this, message, "登录", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
