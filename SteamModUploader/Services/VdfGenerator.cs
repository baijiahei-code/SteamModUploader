using System.Text;
using SteamModUploader.Models;

namespace SteamModUploader.Services;

/// <summary>根据 MOD 配置生成 steamcmd 使用的 workshopitem VDF 文本。</summary>
public static class VdfGenerator
{
    public static string Generate(ModProfile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"workshopitem\"");
        sb.AppendLine("{");
        AppendKey(sb, "appid", p.AppId);
        AppendKey(sb, "publishedfileid", p.PublishedFileId);
        AppendKey(sb, "contentfolder", p.ContentFolder);
        AppendKey(sb, "previewfile", p.PreviewFile);
        AppendKey(sb, "visibility", p.Visibility.ToString());
        AppendKey(sb, "title", p.Title);
        AppendKey(sb, "changenote", p.ChangeNote);
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendKey(StringBuilder sb, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.AppendLine($"\t\"{key}\"\t\t\"{Escape(value)}\"");
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
