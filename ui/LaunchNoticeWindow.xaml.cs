using System.Windows;
using System.Windows.Input;

namespace MystiaModManager;

public partial class LaunchNoticeWindow : Window
{
    public LaunchNoticeWindow(string heading)
    {
        InitializeComponent();
        HeadingText.Text = heading;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        DialogResult = true;
        e.Handled = true;
    }
}
