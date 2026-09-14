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

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new AppSettings();

            var json = File.ReadAllText(SettingsFile);
            var s = JsonSerializer.Deserialize<AppSettings>(json);
            if (s == null) return new AppSettings();

            Normalize(s);
            // 从加密字段解密回内存中的明文密码
            s.SteamPassword = Decrypt(s.SteamPasswordEncrypted);
            return s;
        }
        catch (Exception ex)
        {
            // 读取失败则返回默认设置，但记入日志便于排查
            Logger.Write($"读取配置失败，已回退为默认配置：{ex.Message}");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);

        // 加密后落盘；明文密码字段带 [JsonIgnore]，不会写入文件
        settings.SteamPasswordEncrypted = Encrypt(settings.SteamPassword);
        var json = JsonSerializer.Serialize(settings, SerializerOptions);

        // 先写临时文件再原子替换：避免写到一半崩溃/断电留下半截 JSON；
        // File.Replace 会同时把上一版留作 settings.json.prev，便于误覆盖后恢复
        var temp = SettingsFile + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(SettingsFile))
            File.Replace(temp, SettingsFile, SettingsFile + ".prev", ignoreMetadataErrors: true);
        else
            File.Move(temp, SettingsFile);
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