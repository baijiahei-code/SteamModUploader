using System.IO;

namespace SteamModUploader.Services;

/// <summary>在常见位置探测 steamcmd.exe，避免把默认路径硬编码到某个盘符。</summary>
public static class SteamCmdLocator
{
    /// <summary>在常见位置查找 steamcmd.exe；找不到返回空字符串。</summary>
    public static string Find()
    {
        try
        {
            foreach (var candidate in Candidates())
            {
                try
                {
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                    // 单个候选路径无效（如无权限）时继续尝试下一个
                }
            }
        }
        catch
        {
            // 探测失败不影响主流程
        }
        return "";
    }

    private static IEnumerable<string> Candidates()
    {
        // 绿色版常把 steamcmd 放在工具旁边
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "steamcmd", "steamcmd.exe");
        yield return Path.Combine(baseDir, "steamcmd.exe");

        // 各固定磁盘根目录下的 steamcmd（多数教程的安装位置）
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;

            var root = drive.RootDirectory.FullName;
            yield return Path.Combine(root, "steamcmd", "steamcmd.exe");
            yield return Path.Combine(root, "SteamCMD", "steamcmd.exe");
            yield return Path.Combine(root, "Steam", "steamcmd", "steamcmd.exe");
        }
    }
}
