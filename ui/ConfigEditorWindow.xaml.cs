using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using MystiaModManager.Models;
using MystiaModManager.Services;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace MystiaModManager;

public partial class ConfigEditorWindow : Window
{
    private readonly string _profilePath;
    private List<CfgSetting>? _settings;
    private bool _loading;
    private string? _loadedFile;

    public ConfigEditorWindow(string profilePath)
    {
        InitializeComponent();
        _profilePath = profilePath;
        Loaded += (_, _) => LoadFileList();
    }

    private void LoadFileList()
    {
        FileList.Items.Clear();
        try
        {
            var files = CoreApi.ListCfgFiles(_profilePath);
            foreach (var file in files)
                FileList.Items.Add(file);
            if (FileList.Items.Count > 0)
                FileList.SelectedIndex = 0;
            else
                FileTitle.Text = "这个配置还没有 cfg 文件";
        }
        catch (Exception ex)
        {
            FileTitle.Text = "这个配置还没有 cfg 文件";
            System.Windows.MessageBox.Show(ex.Message, "无法读取配置", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FileList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (FileList.SelectedItem is not string name) return;
        if (_settings != null && _loadedFile != null && !string.Equals(_loadedFile, name, StringComparison.OrdinalIgnoreCase))
        {
            if (HasUnsavedChanges() && !ConfirmDiscard())
            {
                _loading = true;
                FileList.SelectedItem = _loadedFile;
                _loading = false;
                return;
            }
        }

        LoadDocument(name);
    }

    private void LoadDocument(string fileName)
    {
        try
        {
            _settings = CoreApi.LoadCfg(_profilePath, fileName);
            _loadedFile = fileName;
            FileTitle.Text = fileName;
            BuildEditors(_settings);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "无法读取配置", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BuildEditors(List<CfgSetting> settings)
    {
        EditorPanel.Children.Clear();
        var shownSection = "\0";
        foreach (var setting in settings)
        {
            if (!string.Equals(shownSection, setting.Section, StringComparison.Ordinal))
            {
                shownSection = setting.Section;
                EditorPanel.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(setting.Section) ? "未分组" : setting.Section,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 12, 0, 8)
                });
            }

            if (!string.IsNullOrWhiteSpace(setting.Description))
            {
                EditorPanel.Children.Add(new TextBlock
                {
                    Text = setting.Description,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75,
                    Margin = new Thickness(0, 4, 0, 4)
                });
            }

            var hint = BuildHint(setting);
            EditorPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(hint) ? setting.Key : $"{setting.Key}    {hint}",
                Margin = new Thickness(0, 0, 0, 4)
            });

            EditorPanel.Children.Add(CreateEditor(setting));
        }

        if (settings.Count == 0)
        {
            EditorPanel.Children.Add(new TextBlock { Text = "这个文件里没有可编辑的键值。", Opacity = 0.7 });
        }
    }

    private static string BuildHint(CfgSetting setting)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(setting.TypeName)) parts.Add(setting.TypeName);
        if (!string.IsNullOrEmpty(setting.DefaultValue)) parts.Add("默认 " + setting.DefaultValue);
        if (!string.IsNullOrEmpty(setting.Hint)) parts.Add(setting.Hint);
        return string.Join("，", parts);
    }

    private static FrameworkElement CreateEditor(CfgSetting setting)
    {
        if (string.Equals(setting.TypeName, "Boolean", StringComparison.OrdinalIgnoreCase))
        {
            return new CheckBox
            {
                Content = "启用",
                IsChecked = string.Equals(setting.Value, "true", StringComparison.OrdinalIgnoreCase),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = setting
            };
        }

        if (setting.Options.Count > 0)
        {
            var combo = new ComboBox
            {
                ItemsSource = setting.Options,
                IsEditable = true,
                Text = setting.Value,
                Margin = new Thickness(0, 0, 0, 8),
                Tag = setting
            };
            combo.SelectedItem = setting.Value;
            return combo;
        }

        return new TextBox
        {
            Text = setting.Value,
            Margin = new Thickness(0, 0, 0, 8),
            Tag = setting
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _loadedFile == null) return;
        if (CoreApi.IsGameRunning())
        {
            System.Windows.MessageBox.Show("游戏正在运行，请先退出游戏后再保存。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            ReadEditors();
            CoreApi.SaveCfg(_profilePath, _loadedFile, _settings);
            foreach (var setting in _settings)
                setting.Original = setting.Value;
            System.Windows.MessageBox.Show("已保存。下次启动游戏时生效。", "编辑配置", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReadEditors()
    {
        foreach (var child in EditorPanel.Children)
        {
            if (child is not FrameworkElement element || element.Tag is not CfgSetting setting) continue;
            setting.Value = element switch
            {
                CheckBox box => box.IsChecked == true ? "true" : "false",
                ComboBox combo => combo.Text.Trim(),
                TextBox text => text.Text.Trim(),
                _ => setting.Value
            };
        }
    }

    private bool HasUnsavedChanges()
    {
        if (_settings == null) return false;
        ReadEditors();
        foreach (var setting in _settings)
        {
            if (!string.Equals(setting.Value, setting.Original, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private bool ConfirmDiscard()
    {
        return System.Windows.MessageBox.Show(
                   "切换文件会丢掉还没保存的修改。继续吗？",
                   "编辑配置",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
