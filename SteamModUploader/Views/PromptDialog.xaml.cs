using System.Windows;

namespace SteamModUploader;

/// <summary>通用单行输入对话框（也用于 Steam Guard 验证码等输入场景）。</summary>
public partial class PromptDialog : Window
{
    public string Value => InputBox.Text.Trim();

    public PromptDialog(string title, string prompt, string defaultValue = "", double inputFontSize = 14)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;
        InputBox.FontSize = inputFontSize;
        if (!string.IsNullOrEmpty(defaultValue)) InputBox.SelectAll();
        Loaded += (_, _) => InputBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InputBox.Text))
        {
            MessageBox.Show(this, "请输入内容。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
