using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SteamModUploader.Models;
using SteamModUploader.Services;

namespace SteamModUploader;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ModProfile> _profiles = new();
    private readonly SteamCmdRunner _runner = new();
    private AppSettings _settings = new();
    private ModProfile? _current;
    private bool _suppressEvents;
    private CancellationTokenSource? _uploadCts;

    public MainWindow()
    {
        InitializeComponent();
        _runner.OutputReceived += (_, line) => Dispatcher.BeginInvoke(() => OnRunnerOutput(line));
        _runner.InputProvider = PromptForGuardCode;
    }

    // ---------------- 生命周期 ----------------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsService.Load();

        foreach (var p in _settings.Profiles)
            _profiles.Add(p);

        SteamCmdPathBox.Text = _settings.SteamCmdPath;
        SteamUserBox.Text = _settings.SteamUsername;
        SteamPassBox.Password = _settings.SteamPassword;

        if (_profiles.Count == 0)
        {
            var sample = new ModProfile { Name = "示例 MOD", Title = "我的第一个 MOD" };
            _profiles.Add(sample);
        }

        ProfileList.ItemsSource = _profiles;

        var sel = _profiles.FirstOrDefault(p => p.Name == _settings.LastProfileName) ?? _profiles[0];
        _current = sel;
        // 抑制初始化时 SelectedItem 触发的 SelectionChanged，
        // 避免其内部的 SaveFormToProfile 用尚未加载的空表单覆盖默认选中的 MOD
        _suppressEvents = true;
        ProfileList.SelectedItem = sel;
        _suppressEvents = false;
        LoadProfileToForm(sel);

        // 环境体检：启动时提示关键路径缺失，避免填写半天才发现
        if (string.IsNullOrWhiteSpace(_settings.SteamCmdPath) || !File.Exists(_settings.SteamCmdPath))
            Log("体检：未找到 steamcmd.exe，请在下方设置栏点击「浏览…」选择正确路径。");
        if (string.IsNullOrWhiteSpace(_settings.RootDir))
            Log("体检：未设置 MOD 文件根目录，可点击右上角「文件管理（全局）」设置。");
        else if (!Directory.Exists(_settings.RootDir))
            Log($"体检：MOD 根目录不存在：{_settings.RootDir}（可点击「修复路径」重新指定）。");
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveFormToProfile();
        ReadSettingsFromUi();
        _settings.LastProfileName = _current?.Name ?? "";

        try
        {
            var disk = SettingsService.Load();
            bool hasValid = _profiles.Any(p => !string.IsNullOrWhiteSpace(p.Name));
            bool hasEmptyOnly = _profiles.Count > 0 && !hasValid;
            bool diskHasValid = disk.Profiles.Any(p => !string.IsNullOrWhiteSpace(p.Name));

            // 保护：若当前列表只剩空项（如误建未填写），但磁盘已有有效配置，则保留磁盘配置，防止误清空
            if (hasEmptyOnly && diskHasValid)
            {
                _settings.Profiles = disk.Profiles;
                SettingsService.Save(_settings);
                return;
            }

            // 合并磁盘上已存在、但主窗口当前未加载的配置（例如文件管理窗口新建的 MOD），避免覆盖丢失
            foreach (var dp in disk.Profiles)
            {
                if (!_profiles.Any(p => string.Equals(p.Name, dp.Name, StringComparison.OrdinalIgnoreCase)))
                    _profiles.Add(dp);
            }
        }
        catch { }

        _settings.Profiles = _profiles.ToList();
        SettingsService.Save(_settings);
    }

    // ---------------- 表单读写 ----------------

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        SaveFormToProfile();
        _current = ProfileList.SelectedItem as ModProfile;
        LoadProfileToForm(_current);
    }

    private void LoadProfileToForm(ModProfile? p)
    {
        _suppressEvents = true;
        try
        {
            NameBox.Text = p?.Name ?? "";
            TitleBox.Text = p?.Title ?? "";
            AppIdBox.Text = p?.AppId ?? "";
            VersionBox.Text = p?.ChangeNote ?? "";
            VisibilityBox.SelectedIndex = p == null ? 0 : Math.Clamp(p.Visibility, 0, 2);
            ContentFolderBox.Text = p?.ContentFolder ?? "";
            PreviewBox.Text = p?.PreviewFile ?? "";
            PublishedIdBox.Text = p?.PublishedFileId ?? "";
            VdfPathBox.Text = p?.VdfPath ?? "";
            UpdatePublishedHint();
        }
        finally
        {
            _suppressEvents = false;
        }
        UpdatePreviewImage();
    }

    private void SaveFormToProfile()
    {
        if (_current == null) return;
        _current.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? TitleBox.Text.Trim() : NameBox.Text.Trim();
        _current.Title = TitleBox.Text.Trim();
        _current.AppId = AppIdBox.Text.Trim();
        _current.ChangeNote = VersionBox.Text.Trim();
        _current.Visibility = VisibilityBox.SelectedIndex < 0 ? 0 : VisibilityBox.SelectedIndex;
        _current.ContentFolder = ContentFolderBox.Text.Trim();
        _current.PreviewFile = PreviewBox.Text.Trim();
        _current.PublishedFileId = PublishedIdBox.Text.Trim();
        _current.VdfPath = VdfPathBox.Text.Trim();
    }

    private void UpdatePublishedHint()
    {
        bool isUpdate = !string.IsNullOrWhiteSpace(PublishedIdBox.Text);
        HintText.Text = isUpdate
            ? "当前为「更新」模式：将使用已填写的 PublishedFileID 更新已有创意工坊项目。"
            : "提示：首次上传请留空 PublishedFileID；上传成功后软件会自动识别并填入该 ID，之后即可用于更新。";
    }

    private void ReadSettingsFromUi()
    {
        _settings.SteamCmdPath = SteamCmdPathBox.Text.Trim();
        _settings.SteamUsername = SteamUserBox.Text.Trim();
        _settings.SteamPassword = SteamPassBox.Password;
    }

    // ---------------- 浏览对话框 ----------------

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder();
        if (path != null) SetTargetText((Button)sender, path);
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "选择文件" };
        if (dlg.ShowDialog(this) == true)
            SetTargetText((Button)sender, dlg.FileName);
    }

    private void BrowseSaveFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "选择 VDF 保存位置", Filter = "VDF 文件 (*.vdf)|*.vdf", FileName = "workshopitem.vdf" };
        if (dlg.ShowDialog(this) == true)
            SetTargetText((Button)sender, dlg.FileName);
    }

    private void SetTargetText(Button btn, string path)
    {
        var target = btn.Tag as string;
        var box = FindName(target) as TextBox;
        if (box != null) box.Text = path;
    }

    private string? PickFolder()
    {
        // 用 WPF 实现文件夹选择（避免额外依赖）
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择文件夹",
            Multiselect = false
        };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    // ---------------- MOD 列表操作 ----------------

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        SaveFormToProfile();

        // 统一的新建入口：弹窗输入名称，创建配置项；
        // 若已设置 MOD 文件根目录，则同时建立标准目录结构并自动填充内容文件夹。
        var dlg = new PromptDialog("新建 MOD",
            "输入 MOD 名称（将创建配置项；若已设置 MOD 文件根目录，会同时建立标准目录结构 content / preview / backup / output）：",
            $"新 MOD {_profiles.Count + 1}")
        { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var safe = FileManager.Sanitize(dlg.Value);
        if (_profiles.Any(p => string.Equals(p.Name, safe, StringComparison.OrdinalIgnoreCase)))
        { Warn($"已存在名为「{safe}」的 MOD。"); return; }

        var p = new ModProfile { Name = safe, Title = "" };
        _profiles.Add(p);
        _current = p;

        // 已设置根目录时：一并创建标准目录结构并填充内容文件夹
        if (!string.IsNullOrWhiteSpace(_settings.RootDir))
        {
            if (Directory.Exists(FileManager.ModDir(_settings.RootDir, p)))
            {
                Log($"提示：根目录下已存在文件夹「{safe}」，未重复创建目录结构。");
            }
            else
            {
                FileManager.EnsureStructure(_settings.RootDir, p);
                p.ContentFolder = FileManager.ContentDir(_settings.RootDir, p);
                Log($"已创建标准目录结构：{FileManager.ModDir(_settings.RootDir, p)}");
            }
        }
        else
        {
            Log("提示：未设置 MOD 文件根目录，暂未创建目录结构。可在「文件管理」中设置根目录后使用「创建标准目录结构」。");
        }

        // 同步到共享设置并保存，确保文件管理窗口立即能看到新 MOD
        _settings.Profiles = _profiles.ToList();
        SettingsService.Save(_settings);

        _suppressEvents = true;
        ProfileList.SelectedItem = p;
        _suppressEvents = false;
        LoadProfileToForm(p);
        TitleBox.Focus();
    }

    private void DuplicateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        SaveFormToProfile();
        var copy = new ModProfile
        {
            Name = _current.Name + " - 副本",
            Title = _current.Title,
            AppId = _current.AppId,
            // 不复用原 MOD 的内容文件夹/预览图/PublishedFileID/VDF 路径，避免副本误用原 MOD 的内容上传；
            // 若设置了根目录，上传时会自动为其建立独立的 content 文件夹
            ContentFolder = "",
            PreviewFile = "",
            Visibility = _current.Visibility,
            ChangeNote = _current.ChangeNote,
            PublishedFileId = "",
            VdfPath = ""
        };
        _profiles.Add(copy);
        ProfileList.SelectedItem = copy;
        _current = copy;
        LoadProfileToForm(copy);
        Log($"已复制配置「{copy.Name}」。注意：副本未复用原 MOD 的内容文件夹/预览图，请按需重新指定（避免两个 MOD 上传同一内容）。");
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;

        var name = _current.Name;
        bool deleteFiles = false;

        // 若设置了根目录且该 MOD 存在磁盘文件夹，询问是否一并删除文件夹（统一彻底删除）
        if (!string.IsNullOrWhiteSpace(_settings.RootDir)
            && Directory.Exists(FileManager.ModDir(_settings.RootDir, _current)))
        {
            var r = MessageBox.Show(this,
                $"确定删除配置「{name}」吗？\n\n" +
                $"检测到磁盘上存在该 MOD 的文件夹：\n{FileManager.ModDir(_settings.RootDir, _current)}\n\n" +
                "是否同时删除该文件夹（含 content / preview / backup / output 全部内容）？\n" +
                "· 是    → 删除配置并删除文件夹\n" +
                "· 否    → 仅删除配置，保留文件夹\n" +
                "· 取消  → 不删除",
                "删除确认", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Cancel) return;
            deleteFiles = r == MessageBoxResult.Yes;
        }
        else
        {
            if (MessageBox.Show(this, $"确定删除配置「{name}」吗？", "删除确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }

        var idx = _profiles.IndexOf(_current);
        _profiles.Remove(_current);
        _current = null;

        // 选择一并删除时，安全删除磁盘上的 MOD 文件夹
        if (deleteFiles)
        {
            var probe = new ModProfile { Name = name };
            if (FileManager.DeleteModDir(_settings.RootDir, probe))
                Log($"已删除 MOD 文件夹：{FileManager.ModDir(_settings.RootDir, probe)}");
            else
                Warn("删除文件夹失败或路径不安全，请手动检查。");
        }

        // 同步保存，确保文件管理窗口同步
        _settings.Profiles = _profiles.ToList();
        SettingsService.Save(_settings);

        if (_profiles.Count == 0)
        {
            var p = new ModProfile { Name = "示例 MOD", Title = "" };
            _profiles.Add(p);
            idx = 0;
        }

        idx = Math.Clamp(idx, 0, _profiles.Count - 1);
        _current = _profiles[idx];
        // 抑制 SelectedItem 触发的事件，避免用被删 MOD 残留的表单内容覆盖新选中项
        _suppressEvents = true;
        ProfileList.SelectedItem = _current;
        _suppressEvents = false;
        LoadProfileToForm(_current);
    }

    private void ImportVdf_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择已有的 mod.vdf",
            Filter = "VDF 文件 (*.vdf)|*.vdf|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var p = VdfParser.Parse(File.ReadAllText(dlg.FileName));
            SaveFormToProfile();
            _profiles.Add(p);
            ProfileList.SelectedItem = p;
            _current = p;
            LoadProfileToForm(p);
            Log($"已从 {dlg.FileName} 导入 MOD 配置。");
            if (string.IsNullOrWhiteSpace(p.PublishedFileId))
                Log("提示：该 VDF 未包含 publishedfileid，可作首次上传；若需更新请填写 PublishedFileID。");
        }
        catch (Exception ex)
        {
            Warn("导入失败：" + ex.Message);
        }
    }

    // ---------------- VDF 预览 / 打开文件夹 ----------------

    private void PreviewVdf_Click(object sender, RoutedEventArgs e)
    {
        SaveFormToProfile();
        if (_current == null) return;
        Log("—— 生成的 mod.vdf 内容 ——");
        foreach (var line in VdfGenerator.Generate(_current).Split('\n'))
            Log(line.TrimEnd('\r'));
        Log("—— 预览结束 ——");
    }

    private void OpenContentFolder_Click(object sender, RoutedEventArgs e)
    {
        SaveFormToProfile();
        if (_current == null || string.IsNullOrWhiteSpace(_current.ContentFolder)) return;
        if (!Directory.Exists(_current.ContentFolder))
        {
            MessageBox.Show(this, "内容文件夹不存在：" + _current.ContentFolder, "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Process.Start("explorer.exe", $"\"{_current.ContentFolder}\"");
    }

    // ---------------- 文件管理（全局窗口入口） ----------------

    private void OpenFileManager_Click(object sender, RoutedEventArgs e)
    {
        var w = new FileManagerWindow(_settings) { Owner = this };
        w.Show();
    }

    private void PreviewBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;
        UpdatePreviewImage();
    }

    private void UpdatePreviewImage()
    {
        var path = PreviewBox.Text.Trim();
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                PreviewImage.Source = bmp;
                PreviewImage.Visibility = Visibility.Visible;
                NoPreviewText.Visibility = Visibility.Collapsed;
                return;
            }
            catch
            {
                // 图片无效则回退到占位提示
            }
        }
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        NoPreviewText.Visibility = Visibility.Visible;
    }

    // ---------------- 上传 ----------------

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFormToProfile();
        ReadSettingsFromUi();
        var p = _current;
        if (p == null) return;

        if (string.IsNullOrWhiteSpace(p.Title)) { Warn("请填写「创意工坊标题」。"); return; }
        if (string.IsNullOrWhiteSpace(p.AppId)) { Warn("请填写「游戏 AppID」后再上传（AppID 新建时留空，可在上传前填写）。"); return; }

        // 统一目录管理：若设置了根目录，确保标准结构存在并自动填充内容文件夹
        if (!string.IsNullOrWhiteSpace(_settings.RootDir))
        {
            FileManager.EnsureStructure(_settings.RootDir, p);
            if (string.IsNullOrWhiteSpace(p.ContentFolder))
            {
                p.ContentFolder = FileManager.ContentDir(_settings.RootDir, p);
                ContentFolderBox.Text = p.ContentFolder;
            }
        }
        if (string.IsNullOrWhiteSpace(p.ContentFolder) || !Directory.Exists(p.ContentFolder))
        { Warn("「内容文件夹」无效或不存在，请选择正确的文件夹。"); return; }
        var contentFiles = Directory.EnumerateFiles(p.ContentFolder, "*", SearchOption.AllDirectories).ToList();
        if (contentFiles.Count == 0)
        { Warn("「内容文件夹」为空，请先导入或放入 MOD 文件。"); return; }
        if (string.IsNullOrWhiteSpace(_settings.SteamCmdPath) || !File.Exists(_settings.SteamCmdPath))
        { Warn("未找到 steamcmd.exe，请在下方设置正确的路径。"); return; }
        if (string.IsNullOrWhiteSpace(_settings.SteamUsername))
        { Warn("请填写 Steam 用户名。"); return; }
        if (string.IsNullOrEmpty(_settings.SteamPassword))
        { Warn("请填写 Steam 密码。"); return; }

        // 上传内容统计
        long totalBytes = 0;
        foreach (var f in contentFiles) { try { totalBytes += new FileInfo(f).Length; } catch { } }
        Log($"上传内容：{contentFiles.Count} 个文件，共 {totalBytes / 1024.0 / 1024.0:F2} MB。");

        // 首次上传（无 PublishedFileID）必须填写更新说明，否则 steamcmd 会报错
        if (string.IsNullOrWhiteSpace(p.PublishedFileId) && string.IsNullOrWhiteSpace(p.ChangeNote))
        {
            Warn("首次上传必须填写「更新说明」（changenote），否则 steamcmd 会报错。请先填写更新说明。");
            return;
        }

        // 预览图校验：不存在 / 格式不符（需 jpg、png）/ 超过 1MB 时跳过 previewfile 字段
        bool skipPreview = false;
        if (!string.IsNullOrWhiteSpace(p.PreviewFile))
        {
            if (!File.Exists(p.PreviewFile))
            {
                Log("警告：预览图文件不存在，将跳过 previewfile 字段。");
                skipPreview = true;
            }
            else
            {
                var ext = Path.GetExtension(p.PreviewFile).ToLowerInvariant();
                long size = 0;
                try { size = new FileInfo(p.PreviewFile).Length; } catch { }
                if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
                {
                    Log($"警告：预览图格式 {ext} 不符合 Steam 要求（jpg/png），将跳过 previewfile 字段。");
                    skipPreview = true;
                }
                else if (size > 1 * 1024 * 1024)
                {
                    Log($"警告：预览图大小 {size / 1024.0 / 1024.0:F2} MB 超过 Steam 限制（1MB），将跳过 previewfile 字段。");
                    skipPreview = true;
                }
            }
        }

        // 上传前自动备份（可选，全局设置）
        if (!string.IsNullOrWhiteSpace(_settings.RootDir) && _settings.AutoBackupBeforeUpload)
        {
            var bk = FileManager.CreateBackup(_settings.RootDir, p);
            if (!string.IsNullOrEmpty(bk)) Log($"已自动备份到：{bk}");
        }

        // 生成 VDF（预览图不满足要求时，临时跳过 previewfile 字段，不改动用户数据）
        var originalPreview = p.PreviewFile;
        if (skipPreview) p.PreviewFile = "";
        var vdfText = VdfGenerator.Generate(p);
        if (skipPreview) p.PreviewFile = originalPreview;
        var useTempVdf = string.IsNullOrWhiteSpace(p.VdfPath);
        var vdfPath = useTempVdf
            ? Path.Combine(Path.GetTempPath(), $"workshopitem_{Guid.NewGuid():N}.vdf")
            : p.VdfPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(vdfPath) ?? ".");
            File.WriteAllText(vdfPath, vdfText, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Warn("写入 VDF 失败：" + ex.Message);
            return;
        }
        Log($"已生成 VDF：{vdfPath}");

        // 构造 steamcmd 参数（密码不放入命令行，改为通过标准输入传递，避免被其他进程读取）
        var args = new List<string> { "+login", _settings.SteamUsername };
        args.Add("+workshop_build_item");
        args.Add(vdfPath);
        args.Add("+quit");
        _runner.InitialInput = _settings.SteamPassword;

        SetUploading(true);
        try
        {
            _uploadCts = new CancellationTokenSource();
            Log("开始上传，请稍候…（密码通过标准输入安全传递，不会出现在命令行；若需 Steam Guard 验证码会弹出输入框）");
            var code = await _runner.RunAsync(_settings.SteamCmdPath, args.ToArray(), _uploadCts.Token);
            Log($"--- steamcmd 退出码：{code} ---");
            Log(code == 0 ? "上传命令执行完成。" : "上传过程出现异常，请查看上方日志。");
        }
        catch (OperationCanceledException)
        {
            Log("上传已取消。");
        }
        catch (Exception ex)
        {
            Log("发生错误：" + ex.Message);
        }
        finally
        {
            SetUploading(false);
            // 使用默认临时路径时，上传结束后清理临时 VDF
            if (useTempVdf)
            {
                try { File.Delete(vdfPath); } catch { }
            }
        }
    }

    private void OnRunnerOutput(string line)
    {
        Log(line);

        // 尝试解析上传成功后的 publishedfileid
        if (_current != null
            && line.Contains("publishedfileid", StringComparison.OrdinalIgnoreCase))
        {
            var m = Regex.Match(line, @"publishedfileid[:=]?\s*(\d{9,})", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(line, @"itemid[:=]?\s*(\d{9,})", RegexOptions.IgnoreCase);

            if (m.Success)
            {
                _current.PublishedFileId = m.Groups[1].Value;
                PublishedIdBox.Text = _current.PublishedFileId;
                UpdatePublishedHint();
                Log($"✓ 已识别 PublishedFileID：{_current.PublishedFileId}（下次上传将自动用于更新，请记得保存配置）");
            }
        }
    }

    private void CancelUpload_Click(object sender, RoutedEventArgs e)
    {
        _uploadCts?.Cancel();
        Log("正在取消上传…");
    }

    private void SetUploading(bool uploading)
    {
        UploadButton.IsEnabled = !uploading;
        CancelButton.Visibility = uploading ? Visibility.Visible : Visibility.Collapsed;
        BusyBar.Visibility = uploading ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = uploading;
    }

    private string PromptForGuardCode()
    {
        string? code = null;
        Dispatcher.Invoke(() =>
        {
            var dlg = new GuardCodeDialog { Owner = this };
            if (dlg.ShowDialog() == true) code = dlg.Code;
        });
        return code ?? "";
    }

    // ---------------- 设置 / 日志 ----------------

    private void ClearSteamCmdCache_Click(object sender, RoutedEventArgs e)
    {
        // 上传进行中不允许清除
        if (!UploadButton.IsEnabled)
        {
            Warn("上传进行中，请先完成或取消上传。");
            return;
        }

        var steamCmdPath = SteamCmdPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(steamCmdPath) || !File.Exists(steamCmdPath))
        {
            Warn("未找到 steamcmd.exe，请先设置正确的路径。");
            return;
        }

        // steamcmd 正在运行时不要删除（文件可能被占用）
        if (Process.GetProcessesByName("steamcmd").Length > 0)
        {
            Warn("steamcmd 正在运行，请先关闭它再清除缓存。");
            return;
        }

        var configVdf = Path.Combine(Path.GetDirectoryName(steamCmdPath) ?? "", "config", "config.vdf");
        if (!File.Exists(configVdf))
        {
            MessageBox.Show(this, "未找到缓存文件 config.vdf，无需清除。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this,
                "将清除 steamcmd 的缓存登录凭据（config.vdf）。\n\n" +
                "■ 适用场景：\n" +
                "  修改 Steam 密码后，旧缓存失效会导致上传报 “Access Denied”。\n\n" +
                "■ 清除后：\n" +
                "  下次上传会要求重新登录，可能需要输入 Steam Guard 令牌码。\n\n" +
                "■ 安全：\n" +
                "  清除前会自动备份到 config.vdf.bak，可随时恢复。\n\n" +
                "确定继续吗？",
                "清除 steamcmd 缓存", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var bak = configVdf + ".bak";
            if (File.Exists(bak)) File.Delete(bak);
            File.Copy(configVdf, bak, true);
            File.Delete(configVdf);
            Log($"已清除 steamcmd 缓存：{configVdf}（备份：{bak}）");

            var deleteBak = MessageBox.Show(this,
                "已清除 steamcmd 缓存登录凭据。\n" +
                "下次上传将重新登录，可能需要输入 Steam Guard 验证码。\n\n" +
                "注意：备份文件 config.vdf.bak 中仍包含旧登录凭据。\n" +
                "是否立即删除该备份，以彻底清除凭据？\n" +
                "（选择“否”可保留备份用于恢复，但请注意其含敏感信息）",
                "完成", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;

            if (deleteBak)
            {
                try
                {
                    File.Delete(bak);
                    Log($"已删除含凭据的备份：{bak}");
                }
                catch (Exception ex) { Warn("删除备份失败：" + ex.Message); }
            }
        }
        catch (Exception ex)
        {
            Warn("清除缓存失败：" + ex.Message);
        }
    }

    private void RepairPaths_Click(object sender, RoutedEventArgs e)
    {
        // 1. 检测失效路径
        var broken = new List<string>();
        if (!string.IsNullOrWhiteSpace(_settings.RootDir) && !Directory.Exists(_settings.RootDir))
            broken.Add("根目录：" + _settings.RootDir);

        foreach (var p in _settings.Profiles)
        {
            if (!string.IsNullOrWhiteSpace(p.ContentFolder) && !Directory.Exists(p.ContentFolder))
                broken.Add($"{p.Name} 内容文件夹：{p.ContentFolder}");
            if (!string.IsNullOrWhiteSpace(p.PreviewFile) && !File.Exists(p.PreviewFile))
                broken.Add($"{p.Name} 预览图：{p.PreviewFile}");
            if (!string.IsNullOrWhiteSpace(p.VdfPath) && !Directory.Exists(Path.GetDirectoryName(p.VdfPath)))
                broken.Add($"{p.Name} VDF 路径：{p.VdfPath}");
        }

        if (broken.Count == 0)
        {
            MessageBox.Show(this, "未检测到失效路径，无需修复。", "修复路径",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var msg = "检测到以下路径已失效（可能是文件夹被移动）：\n\n" +
                  string.Join("\n", broken.Take(8)) +
                  (broken.Count > 8 ? $"\n… 共 {broken.Count} 项" : "") +
                  "\n\n是否选择「新的 MOD 根目录」来批量修复？\n" +
                  "（软件会把旧的根目录路径批量替换为新根目录路径）";
        if (MessageBox.Show(this, msg, "修复路径",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        // 2. 选择新的 MOD 根目录
        var dlg = new OpenFolderDialog { Title = "选择新的 MOD 根目录（即新的 mods 文件夹位置）" };
        if (dlg.ShowDialog(this) != true) return;
        var newRoot = dlg.FolderName.TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(newRoot)) return;

        var oldRoot = (_settings.RootDir ?? "").TrimEnd('\\', '/');
        int fixedCount = 0;

        // 3. 批量替换各 MOD 的路径
        foreach (var p in _settings.Profiles)
        {
            var cf = RepairPath(p.ContentFolder, oldRoot, newRoot);
            if (cf != p.ContentFolder) { p.ContentFolder = cf; fixedCount++; }

            var pf = RepairPath(p.PreviewFile, oldRoot, newRoot);
            if (pf != p.PreviewFile) { p.PreviewFile = pf; fixedCount++; }

            var vf = RepairPath(p.VdfPath, oldRoot, newRoot);
            if (vf != p.VdfPath) { p.VdfPath = vf; fixedCount++; }
        }

        if (!string.Equals(_settings.RootDir, newRoot, StringComparison.OrdinalIgnoreCase))
        {
            _settings.RootDir = newRoot;
            fixedCount++;
        }

        // 4. 顺带更新 VDF 文件里的 contentfolder / previewfile 路径
        foreach (var p in _settings.Profiles)
        {
            if (!string.IsNullOrWhiteSpace(p.VdfPath) && File.Exists(p.VdfPath))
            {
                try
                {
                    var c = File.ReadAllText(p.VdfPath);
                    var c2 = ReplaceVdfField(c, "contentfolder", p.ContentFolder);
                    c2 = ReplaceVdfField(c2, "previewfile", p.PreviewFile);
                    if (c2 != c) File.WriteAllText(p.VdfPath, c2, new UTF8Encoding(false));
                }
                catch { /* 忽略 VDF 更新失败 */ }
            }
        }

        _settings.Profiles = _profiles.ToList();
        SettingsService.Save(_settings);

        // 刷新表单显示
        if (_current != null) LoadProfileToForm(_current);
        Log($"已修复 {fixedCount} 处路径，新根目录：{newRoot}");
        MessageBox.Show(this,
            $"已修复 {fixedCount} 处路径。\n新根目录：{newRoot}\n\n请确认各 MOD 的内容文件夹、预览图、VDF 路径已更新。",
            "修复完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>把位于旧根目录下（含旧根目录本身）的路径替换为新根目录。</summary>
    private static string RepairPath(string? value, string oldRoot, string newRoot)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? "";
        var v = value.TrimEnd('\\', '/');
        var root = (oldRoot ?? "").TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(root)) return value;

        // 仅当路径等于旧根目录，或旧根目录后紧跟路径分隔符时才算“位于旧根目录下”，
        // 避免把 D:\SteamMOD2 误当作 D:\SteamMOD 的子路径
        if (v.Equals(root, StringComparison.OrdinalIgnoreCase))
            return newRoot;
        if (v.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            return newRoot + v.Substring(root.Length);

        return value;
    }

    /// <summary>替换 VDF 文本中指定键的值为新值（保持 Tab 分隔格式）。</summary>
    private static string ReplaceVdfField(string vdfText, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return vdfText;
        var pattern = $"\"{key}\"\\s+\"[^\"]*\"";
        var replacement = $"\"{key}\"\t\t\"{value}\"";
        return Regex.Replace(vdfText, pattern, replacement, RegexOptions.IgnoreCase);
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveFormToProfile();
        ReadSettingsFromUi();
        _settings.Profiles = _profiles.ToList();
        SettingsService.Save(_settings);
        Log("设置已保存。");
    }

    private void ExportLog_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出上传日志",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"SteamModUploader-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, LogBox.Text, Encoding.UTF8);
            Log($"已导出日志到：{dlg.FileName}");
        }
        catch (Exception ex) { Warn("导出失败：" + ex.Message); }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    private void Log(string line)
    {
        // 防御性脱敏：日志中不出现密码
        line = MaskPassword(line, _settings.SteamPassword);
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        LogBox.ScrollToEnd();
        Logger.Write(line);
    }

    /// <summary>
    /// 在日志文本中隐藏密码：
    /// 1) 优先按“独立词边界”脱敏，避免短密码（如 "1"）把正常文本中的子串误替换为 ***；
    /// 2) 兜底做精确替换，覆盖密码出现在连续字母数字中间（如 URL 编码）的情况。
    /// </summary>
    private static string MaskPassword(string line, string password)
    {
        if (string.IsNullOrWhiteSpace(password) || !line.Contains(password, StringComparison.Ordinal)) return line;

        // 带边界的脱敏：密码两侧不得是字母/数字/下划线
        var boundary = $"(?<![\\w]){Regex.Escape(password)}(?![\\w])";
        line = Regex.Replace(line, boundary, "***", RegexOptions.IgnoreCase);
        // 兜底：精确替换
        line = line.Replace(password, "***", StringComparison.Ordinal);
        return line;
    }

    private void Warn(string message)
        => MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
}
