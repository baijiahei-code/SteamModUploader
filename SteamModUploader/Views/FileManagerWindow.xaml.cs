using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SteamModUploader.Models;
using SteamModUploader.Services;

namespace SteamModUploader;

/// <summary>
/// 全局文件管理窗口：以根目录为视角，统一管理所有 MOD 的文件
/// （content / preview / backup / output）。
/// </summary>
public partial class FileManagerWindow : Window
{
    private readonly AppSettings _settings;
    private ModProfile? _current;
    private readonly List<string> _backupPaths = new();

    public FileManagerWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        RootDirBox.Text = _settings.RootDir;
        AutoBackupCheck.IsChecked = _settings.AutoBackupBeforeUpload;
        RefreshModList();
    }

    // ---------------- 根目录（全局） ----------------

    private void BrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择 MOD 文件根目录" };
        if (dlg.ShowDialog(this) == true)
        {
            RootDirBox.Text = dlg.FolderName;
            ApplyRoot();
        }
    }

    private void RootDirBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyRoot();

    private void ApplyRoot()
    {
        _settings.RootDir = RootDirBox.Text.Trim();
        RefreshModList();
    }

    private void OpenRoot_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_settings.RootDir)) return;
        Directory.CreateDirectory(_settings.RootDir);
        Process.Start("explorer.exe", $"\"{_settings.RootDir}\"");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshModList();

    // ---------------- MOD 列表 ----------------

    private void RefreshModList()
    {
        var previous = _current?.Name;
        var root = _settings.RootDir;

        var items = new List<FolderItem>();
        foreach (var p in _settings.Profiles)
        {
            int count = 0;
            if (!string.IsNullOrWhiteSpace(root))
            {
                var contentDir = FileManager.ContentDir(root, p);
                if (Directory.Exists(contentDir))
                    count = Directory.EnumerateFiles(contentDir, "*", SearchOption.AllDirectories).Count();
            }
            var info = string.IsNullOrWhiteSpace(p.Title)
                ? $"{count} 个内容文件"
                : $"{p.Title} · {count} 个内容文件";
            items.Add(new FolderItem { Name = p.Name, Info = info });
        }

        FolderList.ItemsSource = items;
        if (FolderList.Items.Count > 0)
        {
            // 尽量恢复之前的选中项（与主窗口相同的配置顺序）
            int idx = 0;
            if (previous != null)
            {
                for (int i = 0; i < _settings.Profiles.Count; i++)
                {
                    if (string.Equals(_settings.Profiles[i].Name, previous, StringComparison.OrdinalIgnoreCase))
                    { idx = i; break; }
                }
            }
            FolderList.SelectedIndex = idx;
        }
        else
        {
            _current = null;
            RefreshDetails();
        }
    }

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderList.SelectedIndex < 0 || FolderList.SelectedIndex >= _settings.Profiles.Count)
        {
            _current = null;
            RefreshDetails();
            return;
        }
        _current = _settings.Profiles[FolderList.SelectedIndex];
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        if (_current == null)
        {
            ModTitleText.Text = "（请在左侧选择 MOD）";
            ContentFileList.ItemsSource = null;
            BackupList.ItemsSource = null;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            NoPreviewText.Visibility = Visibility.Visible;
            return;
        }
        ModTitleText.Text = $"MOD：{_current.Name}（{FileManager.ModDir(_settings.RootDir, _current)}）";
        RefreshFiles();
        UpdatePreviewImage();
    }

    private void RefreshFiles()
    {
        _backupPaths.Clear();
        if (_current == null || string.IsNullOrWhiteSpace(_settings.RootDir))
        {
            ContentFileList.ItemsSource = null;
            BackupList.ItemsSource = null;
            return;
        }

        try
        {
            ContentFileList.ItemsSource = FileManager.ListContentFiles(_settings.RootDir, _current)
                .Select(f => $"{new FileInfo(f).Length:N0} B   {Path.GetFileName(f)}").ToList();

            _backupPaths.AddRange(FileManager.ListBackups(_settings.RootDir, _current));
            BackupList.ItemsSource = _backupPaths
                .Select(f => $"{Path.GetFileName(f)}   ({new FileInfo(f).LastWriteTime:yyyy-MM-dd HH:mm})").ToList();
        }
        catch
        {
            // 忽略刷新错误
        }
    }

    private void UpdatePreviewImage()
    {
        if (_current == null || string.IsNullOrWhiteSpace(_settings.RootDir)) return;
        try
        {
            var dir = FileManager.PreviewDir(_settings.RootDir, _current);
            if (Directory.Exists(dir))
            {
                var img = Directory.GetFiles(dir).FirstOrDefault(IsImage);
                if (img != null)
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(img);
                    bmp.EndInit();
                    bmp.Freeze();
                    PreviewImage.Source = bmp;
                    PreviewImage.Visibility = Visibility.Visible;
                    NoPreviewText.Visibility = Visibility.Collapsed;
                    return;
                }
            }
        }
        catch
        {
            // 图片无效
        }
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        NoPreviewText.Visibility = Visibility.Visible;
    }

    private static bool IsImage(string path)
        => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

    // ---------------- 文件操作 ----------------

    private void CreateStructure_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null)
        { Warn("请先在左侧选择一个 MOD，或点击「新建 MOD…」创建。"); return; }
        if (string.IsNullOrWhiteSpace(_settings.RootDir))
        { Warn("请先设置 MOD 文件根目录。"); return; }

        // 检查缺失哪些子目录
        var missing = new List<string>();
        foreach (var (key, dir) in new[]
        {
            ("content", FileManager.ContentDir(_settings.RootDir, _current)),
            ("preview", FileManager.PreviewDir(_settings.RootDir, _current)),
            ("backup", FileManager.BackupDir(_settings.RootDir, _current)),
            ("output", FileManager.OutputDir(_settings.RootDir, _current)),
        })
        {
            if (!Directory.Exists(dir)) missing.Add(key);
        }

        if (missing.Count == 0)
        {
            MessageBox.Show(this, $"「{_current.Name}」的目录结构已完整，无需创建。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        FileManager.EnsureStructure(_settings.RootDir, _current);
        Log($"已创建缺失目录：{string.Join("、", missing)}（{FileManager.ModDir(_settings.RootDir, _current)}）");
        RefreshFiles();
        RefreshModList();
    }

    private void OpenContent_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        FileManager.EnsureStructure(_settings.RootDir, _current);
        Process.Start("explorer.exe", $"\"{FileManager.ContentDir(_settings.RootDir, _current)}\"");
    }

    private void ImportContent_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var dlg = new OpenFileDialog { Title = "选择要导入的内容文件（可多选）", Multiselect = true, CheckFileExists = true };
        if (dlg.ShowDialog(this) != true) return;

        // 重名检查：同名的将覆盖，先让用户确认
        var conflicts = new List<string>();
        var contentDir = FileManager.ContentDir(_settings.RootDir, _current);
        foreach (var f in dlg.FileNames)
        {
            if (File.Exists(Path.Combine(contentDir, Path.GetFileName(f))))
                conflicts.Add(Path.GetFileName(f));
        }
        if (conflicts.Count > 0)
        {
            var msg = "以下文件在内容文件夹中已存在，导入将覆盖同名文件：\n"
                      + string.Join("\n", conflicts.Take(12))
                      + (conflicts.Count > 12 ? $"\n… 共 {conflicts.Count} 个" : "")
                      + "\n\n确定继续吗？";
            if (MessageBox.Show(this, msg, "覆盖确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        try
        {
            FileManager.EnsureStructure(_settings.RootDir, _current);
            FileManager.ImportFiles(_settings.RootDir, _current, dlg.FileNames);
            Log($"已导入 {dlg.FileNames.Length} 个文件到内容文件夹。");
            RefreshFiles();
        }
        catch (Exception ex) { Warn("导入失败：" + ex.Message); }
    }

    private void ImportPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var dlg = new OpenFileDialog
        {
            Title = "选择预览图",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            FileManager.EnsureStructure(_settings.RootDir, _current);
            var dest = FileManager.ImportPreview(_settings.RootDir, _current, dlg.FileName);
            // 同步写回配置的 PreviewFile 并保存，主窗口下次加载即自动带上
            _current.PreviewFile = dest;
            SettingsService.Save(_settings);
            Log($"已导入预览图：{dest}（已同步到配置 PreviewFile）");
            UpdatePreviewImage();
        }
        catch (Exception ex) { Warn("导入失败：" + ex.Message); }
    }

    private void Migrate_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        if (string.IsNullOrWhiteSpace(_settings.RootDir)) { Warn("请先设置根目录。"); return; }

        var contentDir = FileManager.ContentDir(_settings.RootDir, _current);
        if (Directory.Exists(contentDir)
            && Directory.EnumerateFiles(contentDir, "*", SearchOption.AllDirectories).Any())
        {
            if (MessageBox.Show(this, "目标内容文件夹已有文件，迁移将合并/覆盖同名文件，确定继续吗？", "提示",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        var dlg = new MigrateDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        try
        {
            FileManager.EnsureStructure(_settings.RootDir, _current);
            var count = FileManager.MigrateContent(_settings.RootDir, _current, dlg.SourceFolder, dlg.MoveFiles);
            Log($"迁移完成：从 {dlg.SourceFolder} 迁移 {count} 个文件。");
            if (!string.IsNullOrEmpty(dlg.PreviewFile))
            {
                var dest = FileManager.ImportPreview(_settings.RootDir, _current, dlg.PreviewFile);
                Log($"已导入预览图：{dest}");
            }
            RefreshFiles();
            UpdatePreviewImage();
            RefreshModList();
        }
        catch (Exception ex) { Warn("迁移失败：" + ex.Message); }
    }

    private void PackageRelease_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var zip = FileManager.CreateReleaseZip(_settings.RootDir, _current);
        if (string.IsNullOrEmpty(zip)) { Warn("内容文件夹为空，无法打包。"); return; }
        Log($"已生成发布版 zip：{zip}");

        if (MessageBox.Show(this, "发布版 zip 已生成：\n" + zip + "\n\n是否在资源管理器中定位？", "打包完成",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            try { Process.Start("explorer.exe", $"/select,\"{zip}\""); } catch { }
        }
    }

    private void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var zip = FileManager.CreateBackup(_settings.RootDir, _current);
        if (string.IsNullOrEmpty(zip)) { Warn("内容文件夹为空，无法备份。"); return; }
        Log($"已创建备份：{zip}");
        RefreshFiles();
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null || BackupList.SelectedIndex < 0 || _backupPaths.Count <= BackupList.SelectedIndex) return;
        if (MessageBox.Show(this, "恢复将覆盖当前内容文件夹中的全部文件，确定继续吗？", "恢复确认",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            FileManager.RestoreBackup(_settings.RootDir, _current, _backupPaths[BackupList.SelectedIndex]);
            Log("已从备份恢复内容文件夹。");
            RefreshFiles();
        }
        catch (Exception ex) { Warn("恢复失败：" + ex.Message); }
    }

    private void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedIndex < 0 || _backupPaths.Count <= BackupList.SelectedIndex) return;
        if (MessageBox.Show(this, "确定删除该备份文件吗？", "删除确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            File.Delete(_backupPaths[BackupList.SelectedIndex]);
            Log("已删除备份。");
            RefreshFiles();
        }
        catch (Exception ex) { Warn("删除失败：" + ex.Message); }
    }

    private void AutoBackup_Changed(object sender, RoutedEventArgs e)
    {
        _settings.AutoBackupBeforeUpload = AutoBackupCheck.IsChecked == true;
        SettingsService.Save(_settings);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settings.RootDir = RootDirBox.Text.Trim();
        SettingsService.Save(_settings);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    private void Log(string line)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        LogBox.ScrollToEnd();
        Logger.Write(line);
    }

    private void Warn(string message)
        => MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
}

/// <summary>左侧 MOD 列表项。</summary>
public class FolderItem
{
    public string Name { get; set; } = "";
    public string Info { get; set; } = "";
}
