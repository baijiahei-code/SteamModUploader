using System.IO;

namespace SteamModUploader.Services;

/// <summary>把操作日志写入本地日志文件（按天分文件），便于排查上传失败等问题。</summary>
public static class Logger
{
    private static readonly object Sync = new();

    /// <summary>日志目录：%APPDATA%\SteamModUploader\logs</summary>
    public static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamModUploader", "logs");

    /// <summary>今天的日志文件。</summary>
    public static string CurrentFile => Path.Combine(LogDir, $"SteamModUploader_{DateTime.Now:yyyyMMdd}.log");

    /// <summary>写入一行日志（自动带完整时间戳），失败时静默，不影响主流程。</summary>
    public static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            lock (Sync)
            {
                File.AppendAllText(CurrentFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志写入失败不影响主流程
        }
    }
}
