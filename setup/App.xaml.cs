using System.Windows;
using Wpf.Ui.Appearance;

namespace MystiaModManager.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (UpdateMode.IsUpdate(e.Args))
        {
            UpdateMode.Run(e.Args);
            Shutdown();
            return;
        }
        ApplicationThemeManager.Apply(ApplicationTheme.Light);
        new MainWindow().Show();
    }
}
