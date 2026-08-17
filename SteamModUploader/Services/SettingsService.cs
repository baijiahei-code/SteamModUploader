using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SteamModUploader.Models;

namespace SteamModUploader.Services;

/// <summary>应用设置与 MOD 配置的本地持久化服务。</summary>
public static class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamModUploader");

    public static string SettingsFile { get; set; } = Path.Combine(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SteamModUploader"),
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null)
                {
                    // 从加密字段解密回内存中的明文密码
                    s.SteamPassword = Decrypt(s.SteamPasswordEncrypted ?? "");
                    return s;
                }
            }
        }
        catch
        {
            // 读取失败则返回默认设置
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);

        // 保存前把上一版配置备份为 .prev，便于配置被误覆盖时恢复
        try
        {
            if (File.Exists(SettingsFile))
                File.Copy(SettingsFile, SettingsFile + ".prev", true);
        }
        catch { }

        // 加密后落盘；明文密码字段带 [JsonIgnore]，不会写入文件
        settings.SteamPasswordEncrypted = Encrypt(settings.SteamPassword);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFile, json);
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
