using System.Windows;
using Wpf.Ui.Appearance;

namespace MystiaModManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Light);
        base.OnStartup(e);
    }
}
