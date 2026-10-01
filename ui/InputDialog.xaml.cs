using System.Windows;

namespace MystiaModManager;

public partial class InputDialog : Window
{
    public string Value => InputBox.Text.Trim();

    public InputDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initial;
        InputBox.SelectAll();
        InputBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            MessageBox.Show("不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
