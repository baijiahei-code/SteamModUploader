using System.Windows;

namespace SteamModUploader;

public partial class GuardCodeDialog : Window
{
    public string Code => CodeBox.Text.Trim();

    public GuardCodeDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CodeBox.Text))
        {
            MessageBox.Show(this, "请输入验证码。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
