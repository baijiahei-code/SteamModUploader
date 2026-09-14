using System.Globalization;
using System.IO;

namespace SteamModUploader.Services;

/// <summary>
/// 把操作日志写入本地日志文件（按天分文件），便于排查上传失败等问题。
/// 采用「缓冲 + 定时落盘」，避免上传时每行日志都开关一次文件。
/// </summary>
public static class Logger
{
    private static readonly object Sync = new();
    private static readonly List<string> Buffer = new();

    /// <summary>缓冲区达到该行数时立即落盘。</summary>
    private const int FlushThreshold = 200;

    /// <summary>日志保留天数，超期自动清理。</summary>
    private const int RetentionDays = 30;

    private static Timer? _timer;
    private static string? _lastCleanupDate;

    /// <summary>日志目录：%APPDATA%\SteamModUploader\logs</summary>
    public static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamModUploader", "logs");

    /// <summary>今天的日志文件。</summary>
    public static string CurrentFile => Path.Combine(LogDir, $"SteamModUploader_{DateTime.Now:yyyyMMdd}.log");

    /// <summary>写入一行日志（自动带完整时间戳），失败时静默，不影响主流程。</summary>
    public static void Write(string line)
    {
        lock (Sync)
        {
            Buffer.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}");
            if (Buffer.Count >= FlushThreshold)
                FlushLocked();
            else
                _timer ??= new Timer(_ => Flush(), null, 1000, 1000);
        }
    }

    /// <summary>立即把缓冲区写入磁盘（程序退出前调用，避免丢失最后几条日志）。</summary>
    public static void Flush()
    {
        lock (Sync) FlushLocked();
    }

    /// <summary>落盘并释放定时器。</summary>
    public static void Shutdown()
    {
        lock (Sync)
        {
            _timer?.Dispose();
            _timer = null;
            FlushLocked();
        }
    }

    private static void FlushLocked()
    {
        if (Buffer.Count == 0) return;

        // 先取走再写：即使写盘失败也不会让缓冲区无限膨胀
        var pending = Buffer.ToArray();
        Buffer.Clear();
        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllLines(CurrentFile, pending);
            CleanupOldLogsLocked();
        }
        catch
        {
            // 日志写入失败不影响主流程
        }
    }

    /// <summary>每天最多执行一次：清理超过保留期的历史日志。</summary>
    private static void CleanupOldLogsLocked()
    {
        var today = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (_lastCleanupDate == today) return;
        _lastCleanupDate = today;

        try
        {
            var limit = DateTime.Now.AddDays(-RetentionDays);
            foreach (var f in Directory.EnumerateFiles(LogDir, "SteamModUploader_*.log"))
            {
                if (File.GetLastWriteTime(f) < limit) File.Delete(f);
            }
        }
        catch
        {
            // 清理失败不影响主流程
        }
    }
}
