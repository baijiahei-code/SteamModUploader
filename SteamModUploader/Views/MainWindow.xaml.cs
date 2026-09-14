using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SteamModUploader.Models;
using SteamModUploader.Services;

namespace SteamModUploader;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "_uploadCts 只用于取消上传，且在上传流程结束时（UploadButton_Click 的 finally）会被释放；WPF 窗口不适合实现 IDisposable。")]
public partial class MainWindow : Window
{
    private readonly ObservableCollection<ModProfile> _profiles = new();
    private readonly WorkshopUploader _uploader = new();
    private readonly LogPanel _log;
    private AppSettings _settings = new();
    private ModProfile? _current;
    private FileManagerWindow? _fileManagerWindow;

    private bool _suppressEvents;
    private bool _isUploading;
    private CancellationTokenSource? _uploadCts;

    /// <summary>Steam 对创意工坊标题的长度限制。</summary>
    private const int MaxTitleLength = 128;

    /// <summary>Steam 对创意工坊简介的长度限制。</summary>
    private const int MaxDescriptionLength = 8000;

    /// <summary>指定的 VDF 输出目录中生成的文件名。</summary>
    private const string VdfFileName = "workshopitem.vdf";

    public MainWindow()
    {
        InitializeComponent();
        _log = new LogPanel(LogBox);

        _uploader.OutputReceived += (_, line) => Dispatcher.BeginInvoke(() => Log(line));
        _uploader.PublishedFileIdFound += (_, id) => Dispatcher.BeginInvoke(() => OnPublishedFileIdFound(id));
        _uploader.ProgressChanged += (_, percent) => Dispatcher.BeginInvoke(() => OnUploadProgress(percent));
        _uploader.InputProvider = PromptForGuardCode;

        // 版本号直接取程序集版本，避免与 csproj 里的 <Version> 两处维护
        VersionText.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
    }

    /// <summary>
    /// 解析到 PublishedFileID 后写回目标 MOD。
    /// 目标由上传器锁定（上传期间用户切换列表选中项也不会写错配置）。
    /// </summary>
    private void OnPublishedFileIdFound(string id)
    {
        var target = _uploader.Target;
        if (target == null) return;

        // 若用户仍停留在这个 MOD 上，同步刷新表单与提示
        if (ReferenceEquals(_current, target))
        {
            PublishedIdBox.Text = id;
            UpdatePublishedHint();
        }
        Log($"✓ 已识别 PublishedFileID：{id}（下次上传将自动用于更新，请记得保存配置）");
    }

    // ---------------- 生命周期 ----------------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsService.Load();

        foreach (var p in _settings.Profiles)
            _profiles.Add(p);

        // 首次使用（未设置路径）时自动探测 steamcmd 常见安装位置
        if (string.IsNullOrWhiteSpace(_settings.SteamCmdPath))
        {
            var found = SteamCmdLocator.Find();
            if (!string.IsNullOrEmpty(found))
            {
                _settings.SteamCmdPath = found;
                Log($"已自动找到 steamcmd：{found}");
            }
        }

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
        // 统一走 SelectProfile：它在抑制事件的前提下切换选中并加载表单，
        // 避免 SelectedItem 触发的 SelectionChanged 用空表单覆盖选中的 MOD
        SelectProfile(sel, saveCurrent: false);

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
        // 退出前先结束上传，否则 steamcmd 会成为脱离本程序的残留进程
        if (_isUploading)
        {
            _uploadCts?.Cancel();
            _uploader.Kill();
        }

        SaveFormToProfile();
        ReadSettingsFromUi();
        _settings.LastProfileName = _current?.Name ?? "";

        try
        {
            // 保护：若当前列表只剩空项（如误建未填写），但磁盘已有有效配置，则保留磁盘配置，防止误清空
            var disk = SettingsService.Load();
            bool hasValid = _profiles.Any(p => !string.IsNullOrWhiteSpace(p.Name));
            bool hasEmptyOnly = _profiles.Count > 0 && !hasValid;
            bool diskHasValid = disk.Profiles.Any(p => !string.IsNullOrWhiteSpace(p.Name));

            if (hasEmptyOnly && diskHasValid)
            {
                _settings.Profiles = disk.Profiles;
                SettingsService.Save(_settings);
                return;
            }
        }
        catch { }

        // 直接以内存列表为准落盘。
        // 这里刻意不再与磁盘做“按名字合并”：改过名字的配置（内存已是新名、磁盘还是旧名）
        // 会被当成另一条配置重新加回来，导致每次重命名后多出一条重复项。
        SaveSettings();
    }

    // ---------------- 表单读写 ----------------

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        SaveFormToProfile();
        _current = ProfileList.SelectedItem as ModProfile;
        LoadProfileToForm(_current);
    }

    /// <summary>
    /// 切换当前 MOD 的唯一入口：先存旧表单，再在抑制事件的前提下切换选中并加载新表单。
    /// 直接给 SelectedItem 赋值会触发 SelectionChanged，其中的 SaveFormToProfile
    /// 会用旧表单内容覆盖新选中项（启动/新建/复制/删除时尤其危险）。
    /// </summary>
    private void SelectProfile(ModProfile profile, bool saveCurrent = true)
    {
        if (saveCurrent) SaveFormToProfile();

        _current = profile;
        _suppressEvents = true;
        try
        {
            ProfileList.SelectedItem = profile;
        }
        finally
        {
            _suppressEvents = false;
        }
        LoadProfileToForm(profile);
    }

    /// <summary>把当前列表同步到设置并落盘（多个入口共用）。</summary>
    private void SaveSettings()
    {
        _settings.Profiles = _profiles.ToList();
        SettingsService.Save(_settings);

        // 让已打开的全局文件管理窗口同步到最新列表
        _fileManagerWindow?.RefreshAll();
    }

    /// <summary>确保 MOD 名称在列表中唯一（同名会共用同一个磁盘目录，导致内容/备份互相干扰）。</summary>
    private string EnsureUniqueName(string? name)
    {
        var baseName = FileManager.Sanitize(name ?? "");
        var candidate = baseName;
        for (int i = 2; _profiles.Any(p => string.Equals(FileManager.Sanitize(p.Name), candidate, StringComparison.OrdinalIgnoreCase)); i++)
            candidate = $"{baseName}_{i}";
        return candidate;
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
            DescriptionBox.Text = p?.Description ?? "";
            VisibilityBox.SelectedIndex = p == null ? 0 : (int)p.Visibility;
            ContentFolderBox.Text = p?.ContentFolder ?? "";
            PreviewBox.Text = p?.PreviewFile ?? "";
            PublishedIdBox.Text = p?.PublishedFileId ?? "";
            VdfDirBox.Text = p?.VdfDir ?? "";
            UpdatePublishedHint();
        }
        finally
        {
            _suppressEvents = false;
        }

        // 预览图留空时，尝试从该 MOD 的 preview 目录自动识别一张（把图丢进 preview 即可生效）
        if (p != null && string.IsNullOrWhiteSpace(p.PreviewFile))
        {
            var detected = FileManager.FindPreviewImage(_settings.RootDir, p);
            if (!string.IsNullOrEmpty(detected))
            {
                p.PreviewFile = detected;
                PreviewBox.Text = detected;
                Log($"已自动识别预览图：{detected}");
            }
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
        _current.Description = DescriptionBox.Text.Trim();
        _current.Visibility = VisibilityBox.SelectedIndex < 0
            ? WorkshopVisibility.Public
            : (WorkshopVisibility)VisibilityBox.SelectedIndex;
        _current.ContentFolder = ContentFolderBox.Text.Trim();
        _current.PreviewFile = PreviewBox.Text.Trim();
        _current.PublishedFileId = PublishedIdBox.Text.Trim();
        _current.VdfDir = VdfDirBox.Text.Trim();
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

    private void BrowseVdfDir_Click(object sender, RoutedEventArgs e)
    {
        // 这里选的是“目录”：VDF 会写到该目录下的 workshopitem.vdf
        var initial = _current != null && !string.IsNullOrWhiteSpace(_settings.RootDir)
            ? FileManager.OutputDir(_settings.RootDir, _current)
            : null;

        var dir = PickFolder(initial);
        if (dir != null) SetTargetText((Button)sender, dir);
    }

    private void SetTargetText(Button btn, string path)
    {
        var target = btn.Tag as string;
        var box = FindName(target) as TextBox;
        if (box != null) box.Text = path;
    }

    private string? PickFolder(string? initialDirectory = null)
    {
        // 用 WPF 实现文件夹选择（避免额外依赖）
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择文件夹",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

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
        // 比较清洗后的名字：不同名字可能落到同一个磁盘目录
        if (_profiles.Any(p => string.Equals(FileManager.Sanitize(p.Name), safe, StringComparison.OrdinalIgnoreCase)))
        { Warn($"已存在名为「{safe}」的 MOD（或与它共用同一个磁盘目录）。"); return; }

        var p = new ModProfile { Name = safe, Title = "" };
        _profiles.Add(p);
        _current = p;

        // 已设置根目录时：建立标准目录结构（已存在则只补全缺失的子目录），
        // 并自动填好内容文件夹 / VDF 输出目录 / 预览图
        if (!string.IsNullOrWhiteSpace(_settings.RootDir))
        {
            var modDir = FileManager.ModDir(_settings.RootDir, p);
            var existed = Directory.Exists(modDir);

            FileManager.EnsureStructure(_settings.RootDir, p);
            Log(existed
                ? $"提示：根目录下已存在文件夹「{safe}」，已补全缺失的子目录。"
                : $"已创建标准目录结构：{modDir}");

            p.ContentFolder = FileManager.ContentDir(_settings.RootDir, p);
            p.VdfDir = FileManager.OutputDir(_settings.RootDir, p);

            var preview = FileManager.FindPreviewImage(_settings.RootDir, p);
            if (!string.IsNullOrEmpty(preview))
            {
                p.PreviewFile = preview;
                Log($"已自动填写预览图：{preview}");
            }
            else
            {
                Log($"提示：把预览图放进 {FileManager.PreviewDir(_settings.RootDir, p)} 后，重新选中该 MOD 会自动填入预览图路径。");
            }

            Log($"已自动填写内容文件夹与 VDF 输出目录：{p.VdfDir}");
        }
        else
        {
            Log("提示：未设置 MOD 文件根目录，暂未创建目录结构（也无法自动填写内容文件夹 / VDF 输出目录）。可在「文件管理」中设置根目录后使用「创建标准目录结构」。");
        }

        // 同步到共享设置并保存，确保文件管理窗口立即能看到新 MOD
        SaveSettings();

        SelectProfile(p, saveCurrent: false);
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
            Description = _current.Description,
            PublishedFileId = "",
            VdfDir = ""
        };
        _profiles.Add(copy);
        SelectProfile(copy, saveCurrent: false);
        Log($"已复制配置「{copy.Name}」。注意：副本未复用原 MOD 的内容文件夹/预览图，请按需重新指定（避免两个 MOD 上传同一内容）。");
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;

        var name = _current.Name;
        bool deleteFiles = false;

        // 若设置了根目录且该 MOD 存在磁盘文件夹，询问是否一并删除文件夹（统一彻底删除）
        if (FileManager.ModDirExists(_settings.RootDir, _current))
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
            var error = FileManager.TryDeleteModDir(_settings.RootDir, probe);
            if (error == null)
                Log($"已删除 MOD 文件夹：{FileManager.ModDir(_settings.RootDir, probe)}");
            else
                Warn("删除文件夹失败：" + error);
        }

        // 同步保存，确保文件管理窗口同步
        SaveSettings();

        if (_profiles.Count == 0)
        {
            _profiles.Add(new ModProfile { Name = "示例 MOD", Title = "" });
            idx = 0;
        }

        idx = Math.Clamp(idx, 0, _profiles.Count - 1);
        // 不保存旧表单：被删 MOD 的残留内容不能写进新选中项
        SelectProfile(_profiles[idx], saveCurrent: false);
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
            p.Name = EnsureUniqueName(p.Name);   // 重名会导致两个 MOD 共用同一磁盘目录
            _profiles.Add(p);
            SelectProfile(p);
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
        // 整个程序只保留一个文件管理窗口：多开会让两个窗口互相覆盖整份配置
        if (_fileManagerWindow == null)
        {
            _fileManagerWindow = new FileManagerWindow(_settings) { Owner = this };
            _fileManagerWindow.Closed += (_, _) => _fileManagerWindow = null;
            _fileManagerWindow.Show();
            return;
        }

        if (_fileManagerWindow.WindowState == WindowState.Minimized)
            _fileManagerWindow.WindowState = WindowState.Normal;
        _fileManagerWindow.Activate();
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

        // AppID / PublishedFileID 必须是纯数字，填错格式 steamcmd 会直接报错，提前拦下
        if (!IsAllDigits(p.AppId))
        { Warn("「游戏 AppID」只能是数字，请检查是否包含空格或其它字符。"); return; }
        if (!string.IsNullOrWhiteSpace(p.PublishedFileId) && !IsAllDigits(p.PublishedFileId))
        { Warn("「PublishedFileID」只能是数字（上传成功后会自动填入，一般无需手动修改）。"); return; }
        if (p.Title.Length > MaxTitleLength)
        { Warn($"「创意工坊标题」超过 {MaxTitleLength} 个字符，Steam 会拒绝，请精简后再上传。"); return; }
        if (p.Description.Length > MaxDescriptionLength)
        { Warn($"「创意工坊简介」超过 {MaxDescriptionLength} 个字符，Steam 会拒绝，请精简后再上传。"); return; }

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
        // steamcmd 解析 VDF 里的相对路径时以它自己的工作目录为基准，容易出现莫名其妙的找不到目录，
        // 所以统一转成绝对路径再写进 VDF
        if (!string.IsNullOrWhiteSpace(p.ContentFolder))
        {
            try
            {
                p.ContentFolder = Path.GetFullPath(p.ContentFolder);
                ContentFolderBox.Text = p.ContentFolder;
            }
            catch (Exception ex)
            {
                Warn("「内容文件夹」路径无效：" + ex.Message);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(p.ContentFolder) || !Directory.Exists(p.ContentFolder))
        { Warn("「内容文件夹」无效或不存在，请选择正确的文件夹。"); return; }

        // contentfolder 若指到了 MOD 目录本身，备份/发布 zip 会被一起上传，先提醒
        if (Directory.Exists(Path.Combine(p.ContentFolder, "backup"))
            || Directory.Exists(Path.Combine(p.ContentFolder, "output"))
            || (!string.IsNullOrWhiteSpace(_settings.RootDir)
                && FileManager.IsSamePath(p.ContentFolder, _settings.RootDir)))
        {
            if (MessageBox.Show(this,
                    "「内容文件夹」看起来是整个 MOD 目录（或 MOD 根目录），\n" +
                    "会把 backup / output 里的备份与发布 zip 一并上传到创意工坊。\n\n确定继续吗？",
                    "提示", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        // 文件枚举与大小统计放到后台线程，MOD 很大时不至于卡住界面
        var contentFiles = await Task.Run(() => FileManager.ListFiles(p.ContentFolder));
        if (contentFiles.Count == 0)
        { Warn("「内容文件夹」为空（或只含 Thumbs.db / desktop.ini 等系统文件），请先导入或放入 MOD 文件。"); return; }
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
                if (!FileManager.IsSteamPreviewFile(p.PreviewFile))
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
        else
        {
            Log("提示：未设置预览图，创意工坊列表里将显示默认占位图，建议上传一张封面图。");
        }

        // 上传前自动备份（可选，全局设置）
        if (!string.IsNullOrWhiteSpace(_settings.RootDir) && _settings.AutoBackupBeforeUpload)
        {
            var bk = FileManager.CreateBackup(_settings.RootDir, p);
            if (!string.IsNullOrEmpty(bk)) Log($"已自动备份到：{bk}");
        }

        // 生成 VDF（预览图不满足要求时跳过 previewfile 字段，不临时改动用户数据）
        var vdfText = VdfGenerator.Generate(p, includePreview: !skipPreview);

        // VdfDir 为空 → 用系统临时目录；指定目录 → 写到该目录下的 workshopitem.vdf
        var useTempVdf = string.IsNullOrWhiteSpace(p.VdfDir);
        string vdfPath;
        try
        {
            var dir = useTempVdf ? Path.GetTempPath() : p.VdfDir;
            Directory.CreateDirectory(dir);
            vdfPath = useTempVdf
                ? Path.Combine(dir, $"workshopitem_{Guid.NewGuid():N}.vdf")
                : Path.Combine(dir, VdfFileName);
            File.WriteAllText(vdfPath, vdfText, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Warn("写入 VDF 失败：" + ex.Message);
            return;
        }
        Log($"已生成 VDF：{vdfPath}");

        SetUploading(true);
        var isNewItem = string.IsNullOrWhiteSpace(p.PublishedFileId);
        try
        {
            _uploadCts = new CancellationTokenSource();
            Log("开始上传，请稍候…（密码按需通过标准输入传递，不会出现在命令行；若需 Steam Guard 验证码会弹出输入框）");

            var result = await _uploader.UploadAsync(
                _settings.SteamCmdPath, _settings.SteamUsername, _settings.SteamPassword,
                p, vdfPath, deleteVdfAfterUpload: useTempVdf, _uploadCts.Token);

            Log($"--- steamcmd 退出码：{result.ExitCode} ---");
            Log(result.Outcome switch
            {
                UploadOutcome.Succeeded => "✅ " + result.Message,
                UploadOutcome.Failed => "❌ " + result.Message,
                _ => "⚠ " + result.Message
            });

            // 失败时把 steamcmd 的构建日志展示出来：错误原因往往只写在这里
            if (result.BuildLogTail.Count > 0)
            {
                Log($"--- steamcmd 构建日志（{result.BuildLogPath}）---");
                foreach (var logLine in result.BuildLogTail) Log("    " + logLine);
                Log("--- 构建日志结束 ---");
            }

            HandleUploadFinished(p, result, isNewItem);
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
            _uploadCts?.Dispose();
            _uploadCts = null;
        }
    }

    /// <summary>上传结束后的收尾：落盘配置，并在新建项目时引导用户完成创意工坊侧设置。</summary>
    private void HandleUploadFinished(ModProfile profile, UploadResult result, bool wasNewItem)
    {
        // 拿到创意工坊 ID 就直接落盘，避免用户忘记保存而丢掉
        if (result.PublishedFileId != null)
        {
            SaveSettings();
            Log("已自动保存配置（含创意工坊 ID），无需再手动点「保存设置」。");
        }

        if (!wasNewItem || result.Outcome != UploadOutcome.Succeeded || result.PublishedFileId == null) return;

        // Valve 文档：新项目需作者同意《创意工坊法律协议》后才会对其他人可见
        var open = MessageBox.Show(this,
            $"已创建创意工坊项目：{result.PublishedFileId}\n\n" +
            "新建的项目需要作者同意《Steam 创意工坊法律协议》后才会对其他人可见，" +
            "也可以顺便在网页上补充简介、标签和截图。\n\n是否现在打开项目页面？",
            "上传成功", MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (open == MessageBoxResult.Yes) OpenWorkshopPage(result.PublishedFileId);
    }

    /// <summary>在浏览器中打开创意工坊项目页面。</summary>
    private void OpenWorkshopPage(string publishedFileId)
    {
        var url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            Log($"已在浏览器打开项目页面：{url}");
        }
        catch (Exception ex)
        {
            Warn("打开项目页面失败：" + ex.Message);
        }
    }

    /// <summary>上传进度回传（steamcmd 输出的百分比）。</summary>
    private void OnUploadProgress(int percent)
    {
        if (!_isUploading) return;
        BusyBar.IsIndeterminate = false;
        BusyBar.Value = percent;
    }

    private void CancelUpload_Click(object sender, RoutedEventArgs e)
    {
        _uploadCts?.Cancel();
        _uploader.Kill();
        Log("正在取消上传…");
    }

    private void SetUploading(bool uploading)
    {
        _isUploading = uploading;
        UploadButton.IsEnabled = !uploading;
        CancelButton.Visibility = uploading ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = uploading;
        BusyBar.Visibility = uploading ? Visibility.Visible : Visibility.Collapsed;

        // 进度未知时用滚动条，收到 steamcmd 的百分比后切成确定进度
        BusyBar.IsIndeterminate = true;
        BusyBar.Value = 0;

        // 上传期间禁止增删/切换 MOD，避免把上传结果写到错误的配置上
        ProfileList.IsEnabled = !uploading;
        ProfileButtonPanel.IsEnabled = !uploading;
    }

    /// <summary>是否全是数字（AppID / PublishedFileID 校验用）。</summary>
    private static bool IsAllDigits(string value)
        => value.Length > 0 && value.All(char.IsAsciiDigit);

    private string PromptForGuardCode()
    {
        string? code = null;
        Dispatcher.Invoke(() =>
        {
            var dlg = new PromptDialog("Steam Guard 验证码",
                "Steam 需要输入验证码（Steam Guard）才能完成登录：", "", inputFontSize: 16)
            { Owner = this };
            if (dlg.ShowDialog() == true) code = dlg.Value;
        });
        return code ?? "";
    }

    // ---------------- 设置 / 日志 ----------------

    private void ClearSteamCmdCache_Click(object sender, RoutedEventArgs e)
    {
        // 上传进行中不允许清除
        if (_isUploading)
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
        if (IsSteamCmdRunning())
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

        foreach (var p in _profiles)
        {
            if (!string.IsNullOrWhiteSpace(p.ContentFolder) && !Directory.Exists(p.ContentFolder))
                broken.Add($"{p.Name} 内容文件夹：{p.ContentFolder}");
            if (!string.IsNullOrWhiteSpace(p.PreviewFile) && !File.Exists(p.PreviewFile))
                broken.Add($"{p.Name} 预览图：{p.PreviewFile}");
            // VdfDir 是“输出目录”，直接校验目录是否存在
            if (!string.IsNullOrWhiteSpace(p.VdfDir) && !Directory.Exists(p.VdfDir))
                broken.Add($"{p.Name} VDF 输出目录：{p.VdfDir}");
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
        foreach (var p in _profiles)
        {
            var cf = RepairPath(p.ContentFolder, oldRoot, newRoot);
            if (cf != p.ContentFolder) { p.ContentFolder = cf; fixedCount++; }

            var pf = RepairPath(p.PreviewFile, oldRoot, newRoot);
            if (pf != p.PreviewFile) { p.PreviewFile = pf; fixedCount++; }

            var vf = RepairPath(p.VdfDir, oldRoot, newRoot);
            if (vf != p.VdfDir) { p.VdfDir = vf; fixedCount++; }
        }

        if (!string.Equals(_settings.RootDir, newRoot, StringComparison.OrdinalIgnoreCase))
        {
            _settings.RootDir = newRoot;
            fixedCount++;
        }

        // 4. 顺带更新 VDF 文件里的 contentfolder / previewfile 路径
        foreach (var p in _profiles)
        {
            if (string.IsNullOrWhiteSpace(p.VdfDir)) continue;

            var vdfFile = Path.Combine(p.VdfDir, VdfFileName);
            if (!File.Exists(vdfFile)) continue;

            try
            {
                var c = File.ReadAllText(vdfFile);
                var c2 = ReplaceVdfField(c, "contentfolder", p.ContentFolder);
                c2 = ReplaceVdfField(c2, "previewfile", p.PreviewFile);
                if (c2 != c) File.WriteAllText(vdfFile, c2, new UTF8Encoding(false));
            }
            catch { /* 忽略 VDF 更新失败 */ }
        }

        SaveSettings();

        // 刷新表单显示
        if (_current != null) LoadProfileToForm(_current);
        Log($"已修复 {fixedCount} 处路径，新根目录：{newRoot}");
        MessageBox.Show(this,
            $"已修复 {fixedCount} 处路径。\n新根目录：{newRoot}\n\n请确认各 MOD 的内容文件夹、预览图、VDF 输出目录已更新。",
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
            return string.Concat(newRoot, v.AsSpan(root.Length));

        return value;
    }

    /// <summary>替换 VDF 文本中指定键的值为新值（保持 Tab 分隔格式）。</summary>
    private static string ReplaceVdfField(string vdfText, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return vdfText;

        // 按行处理而不是用正则：正则里的 \s+ 会跨行匹配，可能把相邻两行当作一行替换掉
        var lines = vdfText.Split('\n');
        var prefix = $"\"{key}\"";
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var keepCr = lines[i].EndsWith('\r');
            lines[i] = $"\t\"{key}\"\t\t\"{value}\"" + (keepCr ? "\r" : "");
        }
        return string.Join('\n', lines);
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveFormToProfile();
        ReadSettingsFromUi();
        SaveSettings();
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
            File.WriteAllText(dlg.FileName, _log.Text, Encoding.UTF8);
            Log($"已导出日志到：{dlg.FileName}");
        }
        catch (Exception ex) { Warn("导出失败：" + ex.Message); }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();

    private void Log(string line)
    {
        // 防御性脱敏：日志中不出现密码
        line = MaskPassword(line, _settings.SteamPassword);
        _log.Append(line);
        Logger.Write(line);
    }

    private Regex? _maskRegex;
    private string? _maskRegexFor;

    /// <summary>
    /// 在日志文本中隐藏密码：
    /// 1) 优先按“独立词边界”脱敏，避免短密码（如 "1"）把正常文本中的子串误替换为 ***；
    /// 2) 兜底做精确替换，覆盖密码出现在连续字母数字中间（如 URL 编码）的情况。
    /// 大小写敏感：若忽略大小写，密码为 “steam” 这类常见词时会把日志里的 Steam 一并抹掉。
    /// </summary>
    private string MaskPassword(string line, string password)
    {
        if (string.IsNullOrWhiteSpace(password) || !line.Contains(password, StringComparison.Ordinal)) return line;

        // 正则按密码缓存，避免每行日志都重新构造/编译
        if (_maskRegexFor != password)
        {
            _maskRegex = new Regex(
                $"(?<![\\w]){Regex.Escape(password)}(?![\\w])",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
            _maskRegexFor = password;
        }

        line = _maskRegex!.Replace(line, "***");
        // 兜底：精确替换
        return line.Replace(password, "***", StringComparison.Ordinal);
    }

    private void Warn(string message)
        => MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>是否有 steamcmd 进程正在运行（注意释放 Process 句柄）。</summary>
    private static bool IsSteamCmdRunning()
    {
        var procs = Process.GetProcessesByName("steamcmd");
        try { return procs.Length > 0; }
        finally { foreach (var proc in procs) proc.Dispose(); }
    }
}
