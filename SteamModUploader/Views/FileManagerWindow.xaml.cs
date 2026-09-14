using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    private readonly LogPanel _log;
    private readonly List<string> _backupPaths = new();

    /// <summary>根目录输入框的防抖定时器（逐字符触发时不能每次都全盘扫描）。</summary>
    private readonly DispatcherTimer _rootDebounce;

    private ModProfile? _current;
    private bool _suppressSelection;
    private bool _busy;
    private int _listVersion;
    private int _fileVersion;

    public FileManagerWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _log = new LogPanel(LogBox);

        RootDirBox.Text = _settings.RootDir;
        AutoBackupCheck.IsChecked = _settings.AutoBackupBeforeUpload;

        _rootDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _rootDebounce.Tick += (_, _) =>
        {
            _rootDebounce.Stop();
            _ = RefreshModListAsync();
        };

        _ = RefreshModListAsync();
    }

    // ---------------- 根目录（全局） ----------------

    private void BrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择 MOD 文件根目录" };
        if (dlg.ShowDialog(this) != true) return;

        RootDirBox.Text = dlg.FolderName;   // 会触发 TextChanged，由防抖统一刷新
    }

    private void RootDirBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _settings.RootDir = RootDirBox.Text.Trim();

        // 目录枚举开销较大：输入过程中先防抖，停止输入 400ms 后再刷新
        _rootDebounce.Stop();
        _rootDebounce.Start();
    }

    private void OpenRoot_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_settings.RootDir)) return;
        try
        {
            Directory.CreateDirectory(_settings.RootDir);
            Process.Start("explorer.exe", $"\"{_settings.RootDir}\"");
        }
        catch (Exception ex) { Warn("打开根目录失败：" + ex.Message); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshModListAsync();

    /// <summary>供主窗口在配置变化后调用，让本窗口立即同步。</summary>
    public void RefreshAll() => _ = RefreshModListAsync();

    /// <summary>忙碌状态：显示进度条并禁用操作区，避免长耗时操作（打包/备份/迁移）期间被重复点击。</summary>
    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        WorkArea.IsEnabled = !busy;
    }

    /// <summary>取当前选中的 MOD（未选中或正在忙时告知并返回 false）。</summary>
    private bool TryGetCurrent(out ModProfile profile)
    {
        profile = _current!;
        if (_busy) return false;
        if (_current != null) return true;

        Warn("请先在左侧选择一个 MOD（可在主窗口用「新建」创建）。");
        return false;
    }

    // ---------------- MOD 列表 ----------------

    /// <summary>重建左侧列表。目录枚举放到后台线程，避免 MOD / 文件较多时界面卡死。</summary>
    private async Task RefreshModListAsync()
    {
        var version = ++_listVersion;
        var previous = _current?.Name;
        var root = _settings.RootDir;
        var profiles = _settings.Profiles.ToList();

        var items = await Task.Run(() =>
        {
            var list = new List<ModListItem>();
            foreach (var p in profiles)
            {
                int count = 0;
                try
                {
                    if (!string.IsNullOrWhiteSpace(root))
                    {
                        var contentDir = FileManager.ContentDir(root, p);
                        if (Directory.Exists(contentDir))
                            count = Directory.EnumerateFiles(contentDir, "*", SearchOption.AllDirectories).Count();
                    }
                }
                catch { /* 单个目录读取失败不影响整体刷新 */ }

                var info = string.IsNullOrWhiteSpace(p.Title)
                    ? $"{count} 个内容文件"
                    : $"{p.Title} · {count} 个内容文件";
                list.Add(new ModListItem { Profile = p, Name = p.Name, Info = info });
            }
            return list;
        });

        if (version != _listVersion) return;   // 已有更新的刷新，丢弃本次结果

        FolderList.ItemsSource = items;
        if (items.Count == 0)
        {
            _current = null;
            RefreshDetails();
            return;
        }

        // 尽量恢复之前的选中项
        int idx = 0;
        if (previous != null)
        {
            var found = items.FindIndex(i => string.Equals(i.Name, previous, StringComparison.OrdinalIgnoreCase));
            if (found >= 0) idx = found;
        }

        _suppressSelection = true;
        try { FolderList.SelectedIndex = idx; }
        finally { _suppressSelection = false; }

        _current = items[idx].Profile;
        RefreshDetails();
    }

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;

        _current = (FolderList.SelectedItem as ModListItem)?.Profile;
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
        _ = RefreshFilesAsync();
        UpdatePreviewImage();
    }

    /// <summary>刷新内容文件 / 备份列表（文件枚举与大小统计放到后台线程）。</summary>
    private async Task RefreshFilesAsync()
    {
        var version = ++_fileVersion;
        var current = _current;
        var root = _settings.RootDir;

        if (current == null || string.IsNullOrWhiteSpace(root))
        {
            _backupPaths.Clear();
            ContentFileList.ItemsSource = null;
            BackupList.ItemsSource = null;
            return;
        }

        var result = await Task.Run(() =>
        {
            var files = new List<string>();
            var backups = new List<(string Path, string Label)>();
            try
            {
                foreach (var f in FileManager.ListContentFiles(root, current))
                {
                    long length = 0;
                    try { length = new FileInfo(f).Length; } catch { }
                    files.Add($"{length:N0} B   {Path.GetFileName(f)}");
                }

                foreach (var b in FileManager.ListBackups(root, current))
                    backups.Add((b, $"{Path.GetFileName(b)}   ({File.GetLastWriteTime(b):yyyy-MM-dd HH:mm})"));
            }
            catch
            {
                // 忽略刷新错误
            }
            return (Files: files, Backups: backups);
        });

        if (version != _fileVersion) return;

        _backupPaths.Clear();
        _backupPaths.AddRange(result.Backups.Select(b => b.Path));
        ContentFileList.ItemsSource = result.Files;
        BackupList.ItemsSource = result.Backups.Select(b => b.Label).ToList();
    }

    private void UpdatePreviewImage()
    {
        if (_current == null || string.IsNullOrWhiteSpace(_settings.RootDir)) return;
        try
        {
            var dir = FileManager.PreviewDir(_settings.RootDir, _current);
            if (Directory.Exists(dir))
            {
                var img = Directory.GetFiles(dir).FirstOrDefault(FileManager.IsImageFile);
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

    // ---------------- 文件操作 ----------------

    private void CreateStructure_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_settings.RootDir))
        { Warn("请先设置 MOD 文件根目录。"); return; }
        if (!TryGetCurrent(out var current)) return;

        // 检查缺失哪些子目录
        var missing = new List<string>();
        foreach (var (key, dir) in new[]
        {
            ("content", FileManager.ContentDir(_settings.RootDir, current)),
            ("preview", FileManager.PreviewDir(_settings.RootDir, current)),
            ("backup", FileManager.BackupDir(_settings.RootDir, current)),
            ("output", FileManager.OutputDir(_settings.RootDir, current)),
        })
        {
            if (!Directory.Exists(dir)) missing.Add(key);
        }

        if (missing.Count == 0)
        {
            MessageBox.Show(this, $"「{current.Name}」的目录结构已完整，无需创建。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        FileManager.EnsureStructure(_settings.RootDir, current);
        Log($"已创建缺失目录：{string.Join("、", missing)}（{FileManager.ModDir(_settings.RootDir, current)}）");
        _ = RefreshFilesAsync();
        _ = RefreshModListAsync();
    }

    private void OpenContent_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCurrent(out var current)) return;
        try
        {
            FileManager.EnsureStructure(_settings.RootDir, current);
            Process.Start("explorer.exe", $"\"{FileManager.ContentDir(_settings.RootDir, current)}\"");
        }
        catch (Exception ex) { Warn("打开内容文件夹失败：" + ex.Message); }
    }

    private async void ImportContent_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCurrent(out var current)) return;

        var dlg = new OpenFileDialog { Title = "选择要导入的内容文件（可多选）", Multiselect = true, CheckFileExists = true };
        if (dlg.ShowDialog(this) != true) return;

        // 重名检查：同名的将覆盖，先让用户确认
        var conflicts = new List<string>();
        var contentDir = FileManager.ContentDir(_settings.RootDir, current);
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

        // 大文件复制比较耗时，放到后台线程并显示进度
        SetBusy(true);
        try
        {
            var files = dlg.FileNames;
            await Task.Run(() =>
            {
                FileManager.EnsureStructure(_settings.RootDir, current);
                FileManager.ImportFiles(_settings.RootDir, current, files);
            });
            Log($"已导入 {files.Length} 个文件到内容文件夹。");
            await RefreshFilesAsync();
            await RefreshModListAsync();
        }
        catch (Exception ex) { Warn("导入失败：" + ex.Message); }
        finally { SetBusy(false); }
    }

    private void ImportPreview_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCurrent(out var current)) return;

        var dlg = new OpenFileDialog
        {
            Title = "选择预览图",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            FileManager.EnsureStructure(_settings.RootDir, current);
            var dest = FileManager.ImportPreview(_settings.RootDir, current, dlg.FileName);
            // 同步写回配置的 PreviewFile 并保存，主窗口下次加载即自动带上
            current.PreviewFile = dest;
            SettingsService.Save(_settings);
            Log($"已导入预览图：{dest}（已同步到配置 PreviewFile）");
            UpdatePreviewImage();
            _ = RefreshModListAsync();
        }
        catch (Exception ex) { Warn("导入失败：" + ex.Message); }
    }

    private async void Migrate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_settings.RootDir)) { Warn("请先设置根目录。"); return; }
        if (!TryGetCurrent(out var current)) return;

        if (FileManager.ContentHasFiles(_settings.RootDir, current))
        {
            if (MessageBox.Show(this, "目标内容文件夹已有文件，迁移将合并/覆盖同名文件，确定继续吗？", "提示",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        var dlg = new MigrateDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        SetBusy(true);
        try
        {
            var source = dlg.SourceFolder;
            var move = dlg.MoveFiles;
            var count = await Task.Run(() => FileManager.MigrateContent(_settings.RootDir, current, source, move));
            Log($"迁移完成：从 {source} 迁移 {count} 个文件。");

            if (!string.IsNullOrEmpty(dlg.PreviewFile))
            {
                var dest = FileManager.ImportPreview(_settings.RootDir, current, dlg.PreviewFile);
                // 同步到配置的 PreviewFile 并保存，否则文件只在磁盘上、主窗口不会使用它
                current.PreviewFile = dest;
                SettingsService.Save(_settings);
                Log($"已导入预览图：{dest}（已同步到配置 PreviewFile）");
            }

            await RefreshFilesAsync();
            UpdatePreviewImage();
            await RefreshModListAsync();
        }
        catch (Exception ex) { Warn("迁移失败：" + ex.Message); }
        finally { SetBusy(false); }
    }

    private async void PackageRelease_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCurrent(out var current)) return;

        // 压缩大文件夹很耗时，放到后台线程并显示进度
        SetBusy(true);
        string zip;
        try { zip = await Task.Run(() => FileManager.CreateReleaseZip(_settings.RootDir, current)); }
        catch (Exception ex) { Warn("打包失败：" + ex.Message); return; }
        finally { SetBusy(false); }

        if (string.IsNullOrEmpty(zip)) { Warn("内容文件夹为空，无法打包。"); return; }
        Log($"已生成发布版 zip：{zip}");

        if (MessageBox.Show(this, "发布版 zip 已生成：\n" + zip + "\n\n是否在资源管理器中定位？", "打包完成",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            try { Process.Start("explorer.exe", $"/select,\"{zip}\""); } catch { }
        }
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCurrent(out var current)) return;

        SetBusy(true);
        try
        {
            var zip = await Task.Run(() => FileManager.CreateBackup(_settings.RootDir, current));
            if (string.IsNullOrEmpty(zip)) { Warn("内容文件夹为空，无法备份。"); return; }
            Log($"已创建备份：{zip}");
            await RefreshFilesAsync();
        }
        catch (Exception ex) { Warn("备份失败：" + ex.Message); }
        finally { SetBusy(false); }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedIndex < 0 || _backupPaths.Count <= BackupList.SelectedIndex) return;
        if (!TryGetCurrent(out var current)) return;

        var zip = _backupPaths[BackupList.SelectedIndex];
        if (MessageBox.Show(this, "恢复将覆盖当前内容文件夹中的全部文件，确定继续吗？", "恢复确认",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true);
        try
        {
            await Task.Run(() => FileManager.RestoreBackup(_settings.RootDir, current, zip));
            Log("已从备份恢复内容文件夹。");
            await RefreshFilesAsync();
            await RefreshModListAsync();
        }
        catch (Exception ex) { Warn("恢复失败：" + ex.Message); }
        finally { SetBusy(false); }
    }

    private void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (BackupList.SelectedIndex < 0 || _backupPaths.Count <= BackupList.SelectedIndex) return;
        if (MessageBox.Show(this, "确定删除该备份文件吗？", "删除确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            File.Delete(_backupPaths[BackupList.SelectedIndex]);
            Log("已删除备份。");
            _ = RefreshFilesAsync();
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
        _rootDebounce.Stop();
        _settings.RootDir = RootDirBox.Text.Trim();
        SettingsService.Save(_settings);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();

    private void Log(string line)
    {
        _log.Append(line);
        Logger.Write(line);
    }

    private void Warn(string message)
        => MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
}

/// <summary>左侧 MOD 列表项。直接持有对应的配置对象，避免用索引反查造成错位。</summary>
public sealed class ModListItem
{
    public ModProfile Profile { get; set; } = new();
    public string Name { get; set; } = "";
    public string Info { get; set; } = "";
}
