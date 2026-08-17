using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace SteamModUploader;

public partial class MigrateDialog : Window
{
    public string SourceFolder => SourceBox.Text.Trim();
    public string PreviewFile => PreviewBox.Text.Trim();
    public bool MoveFiles => MoveRadio.IsChecked == true;

    public MigrateDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => SourceBox.Focus();
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择已有 MOD 内容文件夹" };
        if (dlg.ShowDialog(this) == true) SourceBox.Text = dlg.FolderName;
    }

    private void BrowsePreview_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择预览图",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) == true) PreviewBox.Text = dlg.FileName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceBox.Text) || !Directory.Exists(SourceBox.Text))
        {
            MessageBox.Show(this, "请选择有效的来源文件夹。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
