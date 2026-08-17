using System.Text.Json.Serialization;

namespace SteamModUploader.Models;

/// <summary>全局应用设置。</summary>
public class AppSettings
{
    /// <summary>steamcmd.exe 路径。</summary>
    public string SteamCmdPath { get; set; } = "D:\\steamcmd\\steamcmd.exe";

    /// <summary>Steam 用户名。</summary>
    public string SteamUsername { get; set; } = "";

    /// <summary>内存中的 Steam 密码（不写入磁盘）。</summary>
    [JsonIgnore]
    public string SteamPassword { get; set; } = "";

    /// <summary>使用 Windows DPAPI 加密后持久化的密码（Base64）。</summary>
    public string SteamPasswordEncrypted { get; set; } = "";

    /// <summary>MOD 文件统一根目录（每个 MOD 在此下自动建立 content/preview/backup/output）。</summary>
    public string RootDir { get; set; } = "D:\\SteamMOD\\mods";

    /// <summary>上传前是否自动备份内容到 backup 文件夹。</summary>
    public bool AutoBackupBeforeUpload { get; set; } = true;

    /// <summary>MOD 配置文件列表。</summary>
    public List<ModProfile> Profiles { get; set; } = new();

    /// <summary>上次选中的 MOD 名称（用于启动时恢复）。</summary>
    public string LastProfileName { get; set; } = "";
}
