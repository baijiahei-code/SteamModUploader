using System.Globalization;
using System.Text.RegularExpressions;

namespace SteamModUploader.Services;

/// <summary>
/// 从 steamcmd 输出中解析创意工坊结果。
/// 纯函数、无副作用，便于单元测试。
/// </summary>
public static partial class WorkshopOutputParser
{
    // 关键词与数字之间允许少量符号/空白（steamcmd 会输出 "PublishedFileID : 123..."、
    // "publishedfileid = 123..."、"Success. PublishedFileID: 123..." 等多种写法）
    [GeneratedRegex(@"publishedfileid\D{0,4}(\d{9,})", RegexOptions.IgnoreCase)]
    private static partial Regex PublishedFileIdPattern();

    [GeneratedRegex(@"itemid\D{0,4}(\d{9,})", RegexOptions.IgnoreCase)]
    private static partial Regex ItemIdPattern();

    // steamcmd 的上传进度形如："Update state (0x61) uploading, progress: 12.34 (1234 / 10000)"
    [GeneratedRegex(@"progress:\s*([\d.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ProgressPattern();

    [GeneratedRegex(@"(\d{1,3})\s*%", RegexOptions.IgnoreCase)]
    private static partial Regex PercentPattern();

    /// <summary>尝试从一行输出中解析上传进度（0-100）。</summary>
    public static bool TryParseProgress(string? line, out int percent)
    {
        percent = 0;
        if (string.IsNullOrEmpty(line)) return false;

        var m = ProgressPattern().Match(line);
        if (m.Success
            && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            percent = (int)Math.Clamp(value, 0, 100);
            return true;
        }

        var pm = PercentPattern().Match(line);
        if (pm.Success && int.TryParse(pm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
        {
            percent = Math.Clamp(p, 0, 100);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 尝试解析一行输出中的创意工坊项目 ID。
    /// 命中关键词（publishedfileid / itemid）且能取到 9 位以上数字时返回 true。
    /// </summary>
    public static bool TryParsePublishedFileId(string? line, out string publishedFileId)
    {
        publishedFileId = "";
        if (string.IsNullOrEmpty(line)) return false;
        if (!line.Contains("publishedfileid", StringComparison.OrdinalIgnoreCase)
            && !line.Contains("itemid", StringComparison.OrdinalIgnoreCase))
            return false;

        var m = PublishedFileIdPattern().Match(line);
        if (!m.Success) m = ItemIdPattern().Match(line);
        if (!m.Success) return false;

        publishedFileId = m.Groups[1].Value;
        return true;
    }
}
