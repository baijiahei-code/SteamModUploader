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
    /// <summary>把名称转换为安全的文件夹名。</summary>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "MOD";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Where(c => !invalid.Contains(c) && c != '.').ToArray();
        var s = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(s) ? "MOD" : s;
    }

    public static string ModDir(string root, ModProfile p) => Path.Combine(root, Sanitize(p.Name));
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

    /// <summary>列出内容文件夹中的所有文件（递归，返回完整路径）。</summary>
    public static List<string> ListContentFiles(string root, ModProfile p)
    {
        var dir = ContentDir(root, p);
        if (!Directory.Exists(dir)) return new();
        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
    }

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

    /// <summary>把内容文件夹打包为备份 zip；内容为空时返回空字符串。</summary>
    public static string CreateBackup(string root, ModProfile p)
    {
        var content = ContentDir(root, p);
        if (!Directory.Exists(content)
            || !Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories).Any())
            return "";

        var backupDir = BackupDir(root, p);
        Directory.CreateDirectory(backupDir);
        var zipPath = Path.Combine(backupDir, $"{Sanitize(p.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        ZipFile.CreateFromDirectory(content, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipPath;
    }

    /// <summary>列出所有备份 zip（最新在前）。</summary>
    public static List<string> ListBackups(string root, ModProfile p)
    {
        var dir = BackupDir(root, p);
        if (!Directory.Exists(dir)) return new();
        return Directory.EnumerateFiles(dir, "*.zip").OrderByDescending(f => f).ToList();
    }

    /// <summary>从备份 zip 恢复内容文件夹（先清空现有内容）。</summary>
    public static void RestoreBackup(string root, ModProfile p, string zipPath)
    {
        var content = ContentDir(root, p);
        Directory.CreateDirectory(content);

        foreach (var f in Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories))
            File.Delete(f);
        foreach (var d in Directory.EnumerateDirectories(content).Reverse())
            Directory.Delete(d);

        ExtractZipSafely(zipPath, content);
    }

    /// <summary>
    /// 安全解压：逐条目解压并校验目标路径必须位于目标目录内，
    /// 防止 zip-slip（路径穿越）条目把文件写到目标目录之外。
    /// 若备份被篡改或损坏，会抛出异常而不会写入任何文件。
    /// </summary>
    private static void ExtractZipSafely(string zipPath, string destDir)
    {
        var destFull = Path.GetFullPath(destDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            // 目录条目（以 / 结尾或名称为空）直接跳过
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.Name.Length == 0)
                continue;

            var entryFull = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            if (!entryFull.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"备份文件包含非法路径条目，已中止恢复：{entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(entryFull) ?? destDir);
            entry.ExtractToFile(entryFull, overwrite: true);
        }
    }

    /// <summary>把内容文件夹打包为「发布版」zip（放在 output 目录），供分发使用。</summary>
    public static string CreateReleaseZip(string root, ModProfile p)
    {
        var content = ContentDir(root, p);
        if (!Directory.Exists(content)
            || !Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories).Any())
            return "";

        var outDir = OutputDir(root, p);
        Directory.CreateDirectory(outDir);

        var version = string.IsNullOrWhiteSpace(p.ChangeNote)
            ? DateTime.Now.ToString("yyyyMMdd_HHmmss")
            : p.ChangeNote.Replace(" ", "_");
        var zipPath = Path.Combine(outDir, $"{Sanitize(p.Name)}_v{version}.zip");
        ZipFile.CreateFromDirectory(content, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipPath;
    }

    /// <summary>把来源文件夹中的文件（递归）复制/移动到内容文件夹，返回处理的文件数。</summary>
    public static int MigrateContent(string root, ModProfile p, string source, bool move)
    {
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source)) return 0;

        var dir = ContentDir(root, p);
        Directory.CreateDirectory(dir);
        int count = 0;
        foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
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
    /// 返回是否已实际删除。
    /// </summary>
    public static bool DeleteModDir(string root, ModProfile p)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;

        var dir = ModDir(root, p);
        if (string.IsNullOrWhiteSpace(dir)) return false;
        if (!Directory.Exists(dir)) return false;

        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirFull = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 安全校验：目标必须位于根目录之下（含根目录本身时拒绝），杜绝误删
        if (string.Equals(dirFull, rootFull, StringComparison.OrdinalIgnoreCase)) return false;
        if (!dirFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !dirFull.StartsWith(rootFull + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;

        Directory.Delete(dir, recursive: true);
        return true;
    }
}
