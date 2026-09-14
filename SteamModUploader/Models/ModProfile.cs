using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SteamModUploader.Models;

/// <summary>创意工坊可见性（对应 VDF 的 visibility 字段）。</summary>
public enum WorkshopVisibility
{
    /// <summary>公开。</summary>
    Public = 0,

    /// <summary>仅好友可见。</summary>
    Friends = 1,

    /// <summary>私密。</summary>
    Private = 2
}

/// <summary>单个 MOD 的配置信息（对应一份 workshopitem VDF）。</summary>
public class ModProfile : INotifyPropertyChanged
{
    private string _name = "";
    private string _title = "";
    private string _appId = "";
    private string _contentFolder = "";
    private string _previewFile = "";
    private WorkshopVisibility _visibility = WorkshopVisibility.Public;
    private string _changeNote = "";
    private string _description = "";
    private string _publishedFileId = "";
    private string _vdfDir = "";

    /// <summary>显示名称（仅用于本地管理，不上传）。</summary>
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>创意工坊标题。</summary>
    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    /// <summary>游戏 AppID（新建时留空，上传前需填写）。</summary>
    public string AppId
    {
        get => _appId;
        set => Set(ref _appId, value);
    }

    /// <summary>内容文件夹路径（上传内容）。</summary>
    public string ContentFolder
    {
        get => _contentFolder;
        set => Set(ref _contentFolder, value);
    }

    /// <summary>预览图路径（可选）。</summary>
    public string PreviewFile
    {
        get => _previewFile;
        set => Set(ref _previewFile, value);
    }

    /// <summary>可见性：0 公开 / 1 仅好友 / 2 私密。</summary>
    public WorkshopVisibility Visibility
    {
        get => _visibility;
        set => Set(ref _visibility, value);
    }

    /// <summary>版本 / 更新说明（changenote）。</summary>
    public string ChangeNote
    {
        get => _changeNote;
        set => Set(ref _changeNote, value);
    }

    /// <summary>
    /// 创意工坊简介（description）。
    /// 留空时不写入 VDF，因此不会改动已在 Steam 网页上填写 / 修改过的简介。
    /// </summary>
    public string Description
    {
        get => _description;
        set => Set(ref _description, value);
    }

    /// <summary>已有 MOD 的 PublishedFileID（更新时填写）。</summary>
    public string PublishedFileId
    {
        get => _publishedFileId;
        set => Set(ref _publishedFileId, value);
    }

    /// <summary>
    /// 生成的 VDF 输出目录（为空则用系统临时目录）。
    /// 指定目录时会在其中生成 workshopitem.vdf；临时目录的文件在上传后会被删除。
    /// </summary>
    public string VdfDir
    {
        get => _vdfDir;
        set => Set(ref _vdfDir, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 统一属性写入：所有属性都会触发变更通知。
    /// 左侧列表项绑定 Name / Title；若只有 Name 通知，改完标题后列表会一直显示旧值。
    /// </summary>
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
