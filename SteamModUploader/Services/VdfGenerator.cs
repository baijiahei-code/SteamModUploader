using System.Globalization;
using System.Text;
using SteamModUploader.Models;

namespace SteamModUploader.Services;

/// <summary>根据 MOD 配置生成 steamcmd 使用的 workshopitem VDF 文本。</summary>
public static class VdfGenerator
{
    public static string Generate(ModProfile p) => Generate(p, includePreview: true);

    /// <summary>
    /// 生成 VDF 文本。includePreview 为 false 时跳过 previewfile 字段
    /// （预览图不合规时使用，无需临时改动用户的配置数据）。
    /// </summary>
    public static string Generate(ModProfile p, bool includePreview)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"workshopitem\"");
        sb.AppendLine("{");
        AppendKey(sb, "appid", p.AppId);
        AppendKey(sb, "publishedfileid", p.PublishedFileId);
        AppendKey(sb, "contentfolder", p.ContentFolder);
        if (includePreview) AppendKey(sb, "previewfile", p.PreviewFile);
        AppendKey(sb, "visibility", ((int)p.Visibility).ToString(CultureInfo.InvariantCulture));
        AppendKey(sb, "title", p.Title);
        AppendKey(sb, "description", p.Description);
        AppendKey(sb, "changenote", p.ChangeNote);
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendKey(StringBuilder sb, string key, string value)
    {
        // 空值一律不写入：留空即“不提交该字段”，
        // 这样在 Steam 网页上改过的简介（description）等不会被本工具覆盖成空白。
        if (string.IsNullOrWhiteSpace(value)) return;

        sb.AppendLine(CultureInfo.InvariantCulture, $"\t\"{key}\"\t\t\"{Escape(value)}\"");
    }

    /// <summary>
    /// 转义 VDF 值：反斜杠与引号按 VDF 规则转义；
    /// 换行/制表符属于不可见控制字符，直接替换为空格，否则会破坏 VDF 结构。
    /// </summary>
    private static string Escape(string value)
        => value.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r\n", " ")
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Replace("\t", " ");
}
