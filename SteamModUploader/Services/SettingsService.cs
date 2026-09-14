using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SteamModUploader.Models;

namespace SteamModUploader.Services;

/// <summary>应用设置与 MOD 配置的本地持久化服务。</summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static string _settingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamModUploader", "settings.json");

    /// <summary>设置文件路径（可改，便于测试时指向临时目录）。</summary>
    public static string SettingsFile
    {
        get => _settingsFile;
        set => _settingsFile = value;
    }

    /// <summary>设置文件所在目录（跟随 SettingsFile，不再单独维护一份常量）。</summary>
    private static string SettingsDir => Path.GetDirectoryName(SettingsFile) ?? ".";

    /// <summary>历史配置备份目录：每次保存前留一份带时间戳的副本。</summary>
    private static string BackupDir => Path.Combine(SettingsDir, "backups");

    /// <summary>最多保留的备份份数。</summary>
    private const int MaxBackups = 10;

    public static AppSettings Load()
    {
        var settings = TryLoadFrom(SettingsFile, logFailure: true);
        if (settings != null) return settings;

        // 主配置存在但读不出来（被写坏 / 手工改错）时，从最近一份自动备份恢复，
        // 避免"一次误写就彻底丢失配置"。文件不存在时不走这里，免得误恢复用户已清空的配置。
        if (!File.Exists(SettingsFile))
            return new AppSettings();

        foreach (var backup in EnumerateBackups())
        {
            var recovered = TryLoadFrom(backup, logFailure: false);
            if (recovered == null) continue;

            Logger.Write($"主配置无法读取，已从备份恢复：{backup}");
            return recovered;
        }

        return new AppSettings();
    }

    /// <summary>读取并反序列化配置；失败返回 null。</summary>
    private static AppSettings? TryLoadFrom(string path, bool logFailure)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<AppSettings>(json);
            if (s == null) return null;

            Normalize(s);
            // 从加密字段解密回内存中的明文密码
            s.SteamPassword = Decrypt(s.SteamPasswordEncrypted);
            return s;
        }
        catch (Exception ex)
        {
            if (logFailure) Logger.Write($"读取配置失败：{path}（{ex.Message}）");
            return null;
        }
    }

    /// <summary>按时间从新到旧枚举自动备份。</summary>
    private static IEnumerable<string> EnumerateBackups()
    {
        try
        {
            if (!Directory.Exists(BackupDir)) return Array.Empty<string>();
            return Directory.EnumerateFiles(BackupDir, "settings_*.json")
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);

        // 加密后落盘；明文密码字段带 [JsonIgnore]，不会写入文件
        settings.SteamPasswordEncrypted = Encrypt(settings.SteamPassword);
        var json = JsonSerializer.Serialize(settings, SerializerOptions);

        // 每次保存前先留一份带时间戳的副本（.prev 会被下一次保存覆盖，只靠它不够）
        BackupCurrentFile();

        // 先写临时文件再原子替换：避免写到一半崩溃/断电留下半截 JSON；
        // File.Replace 会同时把上一版留作 settings.json.prev，便于误覆盖后恢复
        var temp = SettingsFile + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(SettingsFile))
            File.Replace(temp, SettingsFile, SettingsFile + ".prev", ignoreMetadataErrors: true);
        else
            File.Move(temp, SettingsFile);
    }

    /// <summary>把当前配置复制到 backups 目录，并清理超出保留份数的旧备份。</summary>
    private static void BackupCurrentFile()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return;
            Directory.CreateDirectory(BackupDir);

            var current = File.ReadAllText(SettingsFile);

            // 与最近一份备份内容相同则跳过，避免频繁保存产生大量重复文件
            var newest = EnumerateBackups().FirstOrDefault();
            if (newest != null && File.ReadAllText(newest) == current) return;

            File.WriteAllText(Path.Combine(BackupDir, $"settings_{DateTime.Now:yyyyMMdd_HHmmss}.json"), current);

            foreach (var old in EnumerateBackups().Skip(MaxBackups))
                File.Delete(old);
        }
        catch
        {
            // 备份失败不影响保存主流程
        }
    }

    /// <summary>补齐反序列化后可能为 null 的字段，避免调用方到处判空。</summary>
    private static void Normalize(AppSettings s)
    {
        s.SteamCmdPath = s.SteamCmdPath ?? "";
        s.SteamUsername = s.SteamUsername ?? "";
        s.SteamPasswordEncrypted = s.SteamPasswordEncrypted ?? "";
        s.RootDir = s.RootDir ?? "";
        s.LastProfileName = s.LastProfileName ?? "";
        s.Profiles = (s.Profiles ?? new List<ModProfile>()).Where(p => p != null).ToList();
    }

    /// <summary>应用专属熵，防止同一 Windows 用户下其他程序用默认熵直接解密本应用保存的密码。</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SteamModUploader.v1");

    /// <summary>使用 Windows DPAPI（当前用户）+ 应用专属熵加密字符串，返回 Base64。</summary>
    private static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>解密 DPAPI 密文；失败（如换用户/换机器，或为旧格式密文）时返回空字符串。</summary>
    private static string Decrypt(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return "";
        try
        {
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return "";
        }
    }
}