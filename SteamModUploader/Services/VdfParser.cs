using System.Text.RegularExpressions;
using SteamModUploader.Models;

namespace SteamModUploader.Services;

/// <summary>解析已有的 workshopitem VDF 文件，用于导入已有 MOD 配置。</summary>
public static class VdfParser
{
    public static ModProfile Parse(string text)
    {
        var p = new ModProfile();
        foreach (Match m in Regex.Matches(text, @"""(\w+)""\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Singleline))
        {
            var key = m.Groups[1].Value.ToLowerInvariant();
            // 还原 VDF 中的转义（\" 为引号、\\ 为反斜杠）
            var val = Regex.Replace(m.Groups[2].Value.Trim(), @"\\(.)", "$1");
            switch (key)
            {
                case "appid": p.AppId = val; break;
                case "publishedfileid": p.PublishedFileId = val; break;
                case "contentfolder": p.ContentFolder = val; break;
                case "previewfile": p.PreviewFile = val; break;
                case "visibility": p.Visibility = int.TryParse(val, out var v) ? v : 0; break;
                case "title": p.Title = val; break;
                case "changenote": p.ChangeNote = val; break;
            }
        }

        if (string.IsNullOrWhiteSpace(p.Name))
            p.Name = string.IsNullOrWhiteSpace(p.Title) ? "导入的 MOD" : p.Title;

        return p;
    }
}
