using System.Text.RegularExpressions;
using SteamModUploader.Models;

namespace SteamModUploader.Services;

/// <summary>解析已有的 workshopitem VDF 文件，用于导入已有 MOD 配置。</summary>
public static class VdfParser
{
    /// <summary>匹配 "key" "value" 形式的键值对（值中允许 \" 转义）。</summary>
    private static readonly Regex PairRegex = new(
        @"""([^""\r\n]+)""\s*""((?:[^""\\]|\\.)*)""",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// 还原 VDF 转义：只处理 \\ 与 \" 两种合法转义，其余反斜杠保持原样——
    /// 否则路径 D:\new 会被错还原成 D:new（Valve 自己生成的 VDF 并不双写反斜杠）。
    /// </summary>
    private static readonly Regex UnescapeRegex = new(@"\\([""\\])", RegexOptions.Compiled);

    public static ModProfile Parse(string text)
    {
        var p = new ModProfile();
        foreach (Match m in PairRegex.Matches(text))
        {
            var key = m.Groups[1].Value.ToLowerInvariant();
            var val = UnescapeRegex.Replace(m.Groups[2].Value.Trim(), "$1");
            switch (key)
            {
                case "appid": p.AppId = val; break;
                case "publishedfileid": p.PublishedFileId = val; break;
                case "contentfolder": p.ContentFolder = val; break;
                case "previewfile": p.PreviewFile = val; break;
                case "visibility":
                    // 只接受 0/1/2（公开/仅好友/私密），其余值回退为公开
                    if (int.TryParse(val, out var v) && v is >= 0 and <= 2)
                        p.Visibility = (WorkshopVisibility)v;
                    break;
                case "title": p.Title = val; break;
                case "description": p.Description = val; break;
                case "changenote": p.ChangeNote = val; break;
            }
        }

        if (string.IsNullOrWhiteSpace(p.Name))
            p.Name = string.IsNullOrWhiteSpace(p.Title) ? "导入的 MOD" : p.Title;

        return p;
    }
}
