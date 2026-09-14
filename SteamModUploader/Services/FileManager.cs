using System.Globalization;
using System.IO;
using System.IO.Compression;
using SteamModUploader.Models;

namespace SteamModUploader.Services;

/// <summary>
/// MOD 文件的统一目录管理。
/// 目录结构：<根目录>/<MOD名>/content、preview、backup、output
/// </summary>
public static class FileManager
{
    /// <summary>
    /// 把名称转换为安全的文件夹名：非法字符替换为下划线，去掉首尾空白与结尾的点，
    /// 并杜绝 "." / ".." 这类会越界的目录名。
    /// </summary>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "MOD";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var s = new string(chars).Trim().TrimEnd('.', ' ');

        return string.IsNullOrWhiteSpace(s) || s == "." || s == ".." ? "MOD" : s;
    }

    /// <summary>旧版命名规则（直接剔除非法字符和点），仅用于兼容老版本已创建的目录。</summary>
    private static string LegacySanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "MOD";
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Where(c => !invalid.Contains(c) && c != '.').ToArray()).Trim();
        return string.IsNullOrWhiteSpace(s) ? "MOD" : s;
    }

    /// <summary>
    /// MOD 目录。命名规则升级后，若新版目录尚不存在、但老版目录已存在，则继续沿用老目录，
    /// 避免升级后“找不到”之前已建好的 content / preview / backup。
    /// </summary>
    public static string ModDir(string root, ModProfile p)
    {
        var primary = Path.Combine(root, Sanitize(p.Name));
        if (Directory.Exists(primary)) return primary;

        var legacy = Path.Combine(root, LegacySanitize(p.Name));
        if (!string.Equals(legacy, primary, StringComparison.OrdinalIgnoreCase) && Directory.Exists(legacy))
            return legacy;

        return primary;
    }

    /// <summary>该 MOD 在磁盘上是否已有目录。</summary>
    public static bool ModDirExists(string root, ModProfile p)
        => !string.IsNullOrWhiteSpace(root) && Directory.Exists(ModDir(root, p));

    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };

    /// <summary>按扩展名判断是否为可用作预览图的图片文件。</summary>
    public static bool IsImageFile(string path)
        => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>是否是符合 Steam 要求的预览图（只接受 jpg / png）。</summary>
    public static bool IsSteamPreviewFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png";
    }

    /// <summary>
    /// 在 MOD 的 preview 目录里自动找一张预览图（Steam 支持的 jpg/png 优先）。
    /// 找不到返回空字符串。
    /// </summary>
    public static string FindPreviewImage(string root, ModProfile p)
    {
        var dir = PreviewDir(root, p);
        if (!Directory.Exists(dir)) return "";

        try
        {
            return Directory.EnumerateFiles(dir)
                .Where(IsImageFile)
                .OrderByDescending(IsSteamPreviewFile)          // jpg / png 优先
                .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? "";
        }
        catch
        {
            return "";
        }
    }
    public static string ContentDir(string root, ModProfile p) => Path.Combine(ModDir(root, p), "content");
    public static string PreviewDir(string root, ModProfile p) => Path.Combine(ModDir(root, p), "preview");
    public static string BackupDir(string root, ModProfile p) => Path.Combine(ModDir(root, p), "backup");
    public static string OutputDir(string root, ModProfile p) => Path.Combine(ModDir(root, p), "output");

    /// <summary>创建标准目录结构（content/preview/backup/output）。</summary>
    public static void EnsureStructure(string root, ModProfile p)
    {
        Directory.CreateDirectory(ContentDir(root, p));
        Directory.CreateDirectory(PreviewDir(root, p));
        Directory.CreateDirectory(BackupDir(root, p));
        Directory.CreateDirectory(OutputDir(root, p));
    }

    /// <summary>不该被上传/备份/迁移的文件：系统或资源管理器自动生成的垃圾文件。</summary>
    private static readonly string[] IgnoredFileNames = { "thumbs.db", "desktop.ini", ".ds_store" };

    /// <summary>是否是应被忽略的系统垃圾文件。</summary>
    public static bool IsIgnoredFile(string path)
        => IgnoredFileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>递归列出目录下的文件（自动忽略系统垃圾文件），返回完整路径。</summary>
    public static List<string> ListFiles(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return new();
        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !IsIgnoredFile(f))
            .ToList();
    }

    /// <summary>目录下是否存在有效文件（自动忽略系统垃圾文件）。</summary>
    public static bool HasFiles(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Any(f => !IsIgnoredFile(f));
    }

    /// <summary>列出内容文件夹中的所有文件（递归，返回完整路径）。</summary>
    public static List<string> ListContentFiles(string root, ModProfile p)
        => ListFiles(ContentDir(root, p));

    /// <summary>把若干文件复制到内容文件夹。</summary>
    public static void ImportFiles(string root, ModProfile p, IEnumerable<string> files)
    {
        var dir = ContentDir(root, p);
        Directory.CreateDirectory(dir);
        foreach (var f in files)
        {
            var dest = Path.Combine(dir, Path.GetFileName(f));
            File.Copy(f, dest, true);
        }
    }

    /// <summary>把预览图复制到 preview 文件夹，返回目标路径。</summary>
    public static string ImportPreview(string root, ModProfile p, string file)
    {
        var dir = PreviewDir(root, p);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, Path.GetFileName(file));
        File.Copy(file, dest, true);
        return dest;
    }

    /// <summary>内容文件夹是否存在有效文件（忽略系统垃圾文件）。</summary>
    public static bool ContentHasFiles(string root, ModProfile p)
        => HasFiles(ContentDir(root, p));

    /// <summary>
    /// 生成不与现有文件冲突的路径：已存在时依次追加 _2、_3……，
    /// 避免同一秒内连续备份/打包因文件重名而抛异常。
    /// </summary>
    private static string UniquePath(string dir, string fileNameWithoutExt, string extension)
    {
        var path = Path.Combine(dir, fileNameWithoutExt + extension);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(dir, $"{fileNameWithoutExt}_{i}{extension}");
        return path;
    }

    /// <summary>把内容文件夹打包为备份 zip；内容为空时返回空字符串。</summary>
    public static string CreateBackup(string root, ModProfile p)
    {
        if (!ContentHasFiles(root, p)) return "";

        var backupDir = BackupDir(root, p);
        Directory.CreateDirectory(backupDir);
        var zipPath = UniquePath(backupDir, $"{Sanitize(p.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}", ".zip");
        ZipFile.CreateFromDirectory(ContentDir(root, p), zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipPath;
    }

    /// <summary>列出所有备份 zip（按修改时间倒序，最新在前）。</summary>
    public static List<string> ListBackups(string root, ModProfile p)
    {
        var dir = BackupDir(root, p);
        if (!Directory.Exists(dir)) return new();
        return Directory.EnumerateFiles(dir, "*.zip")
            .OrderByDescending(File.GetLastWriteTime)
            .ToList();
    }

    /// <summary>
    /// 从备份 zip 恢复内容文件夹。
    /// 先完整校验压缩包（路径安全 + 体积），校验通过后才清空现有内容，
    /// 避免“内容已删、备份却打不开”导致用户数据凭空消失。
    /// </summary>
    public static void RestoreBackup(string root, ModProfile p, string zipPath)
    {
        var content = ContentDir(root, p);

        // 1) 只读打开并校验（此步骤不动任何用户数据）
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = CollectSafeEntries(archive, content);

        // 2) 校验通过，清空并重建内容目录
        //    整体递归删除：逐个删除子目录会因“目录非空”（含嵌套文件夹）而失败
        if (Directory.Exists(content))
            Directory.Delete(content, recursive: true);
        Directory.CreateDirectory(content);

        // 3) 解压
        ExtractEntries(entries, content);
    }

    /// <summary>单个压缩包解压后的体积上限，防止恶意/异常 zip 把磁盘写满。</summary>
    private const long MaxExtractBytes = 20L * 1024 * 1024 * 1024;

    /// <summary>
    /// 校验压缩包条目：目标路径必须位于目标目录内（防 zip-slip 路径穿越），
    /// 且解压后总体积不能超出上限（防 zip 炸弹）。校验不过时抛异常，不写入任何文件。
    /// </summary>
    private static List<(ZipArchiveEntry Entry, string FullPath)> CollectSafeEntries(
        ZipArchive archive, string destDir)
    {
        var destFull = Normalize(destDir) + Path.DirectorySeparatorChar;
        var result = new List<(ZipArchiveEntry, string)>();
        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            // 目录条目（以 / 结尾或名称为空）直接跳过
            if (entry.FullName.EndsWith('/') || entry.Name.Length == 0)
                continue;

            var entryFull = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            if (!entryFull.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"压缩包包含非法路径条目，已中止：{entry.FullName}");

            totalBytes += entry.Length;
            if (totalBytes > MaxExtractBytes)
                throw new InvalidDataException(
                    $"压缩包解压后体积超过 {MaxExtractBytes / 1024 / 1024 / 1024} GB，已中止以免磁盘被写满。");

            result.Add((entry, entryFull));
        }

        return result;
    }

    private static void ExtractEntries(List<(ZipArchiveEntry Entry, string FullPath)> entries, string destDir)
    {
        foreach (var (entry, entryFull) in entries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(entryFull) ?? destDir);
            entry.ExtractToFile(entryFull, overwrite: true);
        }
    }

    /// <summary>把内容文件夹打包为「发布版」zip（放在 output 目录），供分发使用。</summary>
    public static string CreateReleaseZip(string root, ModProfile p)
    {
        if (!ContentHasFiles(root, p)) return "";

        var outDir = OutputDir(root, p);
        Directory.CreateDirectory(outDir);

        // 版本串来自“更新说明”，同样必须做文件名清洗（可能含 : / \\ 等非法字符）
        var version = string.IsNullOrWhiteSpace(p.ChangeNote)
            ? DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
            : Sanitize(p.ChangeNote).Replace(' ', '_');
        var zipPath = UniquePath(outDir, $"{Sanitize(p.Name)}_v{version}", ".zip");
        ZipFile.CreateFromDirectory(ContentDir(root, p), zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipPath;
    }

    /// <summary>把来源文件夹中的文件（递归）复制/移动到内容文件夹，返回处理的文件数。</summary>
    public static int MigrateContent(string root, ModProfile p, string source, bool move)
    {
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source)) return 0;

        var dir = ContentDir(root, p);

        // 来源包含目标时会“边复制边枚举”，可能无限循环或写坏数据，直接拒绝
        if (IsSubPathOf(dir, source))
            throw new InvalidOperationException($"来源文件夹包含目标内容文件夹，无法迁移：{source}");

        Directory.CreateDirectory(dir);
        int count = 0;
        foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                     .Where(f => !IsIgnoredFile(f)))
        {
            var rel = Path.GetRelativePath(source, f);
            var dest = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? dir);
            if (move) File.Move(f, dest, true);
            else File.Copy(f, dest, true);
            count++;
        }
        return count;
    }

    /// <summary>
    /// 安全删除某个 MOD 的磁盘文件夹（含 content / preview / backup / output 全部内容）。
    /// 仅在目标位于根目录之下、且不等于根目录本身时执行，防止误删根目录或越界路径。
    /// 返回 null 表示成功（或本来就没有目录），否则返回可展示的失败原因。
    /// </summary>
    public static string? TryDeleteModDir(string root, ModProfile p)
    {
        if (string.IsNullOrWhiteSpace(root)) return "未设置 MOD 文件根目录。";

        var dir = ModDir(root, p);
        if (!Directory.Exists(dir)) return null;

        // 安全校验：目标必须严格位于根目录之下（等于根目录本身时拒绝），杜绝误删
        if (!IsSubPathOf(dir, root) || IsSamePath(dir, root))
            return $"路径不在 MOD 根目录内，已取消删除：{dir}";

        try
        {
            Directory.Delete(dir, recursive: true);
            return null;
        }
        catch (Exception ex)
        {
            return $"文件夹可能正被资源管理器或其他程序占用，请关闭后重试。（{ex.Message}）";
        }
    }

    /// <summary>两个路径是否指向同一位置。</summary>
    public static bool IsSamePath(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>判断 child 是否位于 parent 目录之内（含与 parent 相同）。</summary>
    private static bool IsSubPathOf(string child, string parent)
    {
        var c = Normalize(child);
        var p = Normalize(parent);
        if (string.Equals(c, p, StringComparison.OrdinalIgnoreCase)) return true;
        return c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || c.StartsWith(p + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>规范化路径（补全为绝对路径并去掉结尾分隔符）。</summary>
    private static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
