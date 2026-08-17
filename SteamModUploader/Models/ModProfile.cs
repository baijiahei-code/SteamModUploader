using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SteamModUploader.Models;

/// <summary>单个 MOD 的配置信息（对应一份 workshopitem VDF）。</summary>
public class ModProfile : INotifyPropertyChanged
{
    private string _name = "";

    /// <summary>显示名称（仅用于本地管理，不上传）。</summary>
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    /// <summary>创意工坊标题。</summary>
    public string Title { get; set; } = "";

    /// <summary>游戏 AppID（新建时留空，上传前需填写）。</summary>
    public string AppId { get; set; } = "";

    /// <summary>内容文件夹路径（上传内容）。</summary>
    public string ContentFolder { get; set; } = "";

    /// <summary>预览图路径（可选）。</summary>
    public string PreviewFile { get; set; } = "";

    /// <summary>可见性：0 公开 / 1 仅好友 / 2 私密。</summary>
    public int Visibility { get; set; } = 0;

    /// <summary>版本 / 更新说明（changenote）。</summary>
    public string ChangeNote { get; set; } = "";

    /// <summary>已有 MOD 的 PublishedFileID（更新时填写）。</summary>
    public string PublishedFileId { get; set; } = "";

    /// <summary>生成的 VDF 保存路径（为空则用临时目录）。</summary>
    public string VdfPath { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
