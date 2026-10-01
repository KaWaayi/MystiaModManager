using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using MystiaModManager.Models;
using MystiaModManager.Services;

namespace MystiaModManager;

public partial class FirstRunWindow : Window
{
    public BepInExBuild? SelectedBuild { get; private set; }

    public FirstRunWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadBuildsAsync();
    }

    private async Task LoadBuildsAsync()
    {
        try
        {
            BuildList.IsEnabled = false;
            var builds = await Task.Run(CoreApi.ListBepInExBuilds);
            BuildList.ItemsSource = builds;
            if (builds.Count > 0)
                BuildList.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "加载构建列表失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BuildList.IsEnabled = true;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadBuildsAsync();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (BuildList.SelectedItem is not BepInExBuild build)
        {
            MessageBox.Show("请选择一个构建", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedBuild = build;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
