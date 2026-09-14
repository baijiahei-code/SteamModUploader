using System.IO;
using System.IO.Compression;
using SteamModUploader.Models;
using SteamModUploader.Services;
using Xunit;

namespace SteamModUploader.Tests;

/// <summary>文件管理的目录命名、备份/恢复与安全校验测试（在临时目录中进行）。</summary>
public class FileManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "smu-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }

    [Theory]
    [InlineData("", "MOD")]
    [InlineData("   ", "MOD")]
    [InlineData("..", "MOD")]
    [InlineData(".", "MOD")]
    [InlineData("a:b", "a_b")]
    [InlineData("a?b*c", "a_b_c")]
    [InlineData("abc.", "abc")]
    [InlineData("v1.0", "v1.0")]
    public void Sanitize_清洗非法字符且不越界(string input, string expected)
        => Assert.Equal(expected, FileManager.Sanitize(input));

    [Fact]
    public void Sanitize_是幂等的()
    {
        var once = FileManager.Sanitize("a:b*c");
        Assert.Equal(once, FileManager.Sanitize(once));
    }

    [Fact]
    public void 内容统计_忽略系统垃圾文件()
    {
        var p = new ModProfile { Name = "垃圾文件" };
        var content = FileManager.ContentDir(_root, p);
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "Thumbs.db"), "x");
        File.WriteAllText(Path.Combine(content, "desktop.ini"), "x");

        Assert.False(FileManager.ContentHasFiles(_root, p));

        File.WriteAllText(Path.Combine(content, "real.pak"), "x");

        Assert.True(FileManager.ContentHasFiles(_root, p));
        Assert.Single(FileManager.ListContentFiles(_root, p));
    }

    [Fact]
    public void 备份与恢复_支持嵌套目录()
    {
        var p = new ModProfile { Name = "嵌套目录" };
        var content = FileManager.ContentDir(_root, p);
        var deep = Path.Combine(content, "sub", "deep");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(content, "a.txt"), "A");
        File.WriteAllText(Path.Combine(deep, "b.txt"), "B");

        var zip = FileManager.CreateBackup(_root, p);
        Assert.False(string.IsNullOrEmpty(zip));

        // 破坏现有内容后从备份恢复
        File.Delete(Path.Combine(content, "a.txt"));
        File.WriteAllText(Path.Combine(deep, "b.txt"), "changed");

        FileManager.RestoreBackup(_root, p, zip);

        Assert.Equal("A", File.ReadAllText(Path.Combine(content, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(deep, "b.txt")));
    }

    [Fact]
    public void 备份_内容为空时返回空字符串()
    {
        var p = new ModProfile { Name = "空内容" };
        Directory.CreateDirectory(FileManager.ContentDir(_root, p));

        Assert.Equal("", FileManager.CreateBackup(_root, p));
        Assert.Equal("", FileManager.CreateReleaseZip(_root, p));
    }

    [Fact]
    public void 发布包_文件名会清洗非法字符且不覆盖已有文件()
    {
        var p = new ModProfile { Name = "打包:测试", ChangeNote = "v1.0: 修复" };
        var content = FileManager.ContentDir(_root, p);
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "a.txt"), "A");

        var first = FileManager.CreateReleaseZip(_root, p);
        var second = FileManager.CreateReleaseZip(_root, p);

        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.NotEqual(first, second);
        Assert.DoesNotContain(":", Path.GetFileName(first));
    }

    [Fact]
    public void 备份列表_按时间倒序()
    {
        var p = new ModProfile { Name = "排序" };
        var content = FileManager.ContentDir(_root, p);
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "a.txt"), "A");

        var older = FileManager.CreateBackup(_root, p);
        File.SetLastWriteTime(older, DateTime.Now.AddMinutes(-10));
        var newer = FileManager.CreateBackup(_root, p);

        var list = FileManager.ListBackups(_root, p);
        Assert.Equal(newer, list[0]);
        Assert.Equal(older, list[1]);
    }

    [Fact]
    public void 恢复_路径穿越条目会中止且不动现有内容()
    {
        var p = new ModProfile { Name = "安全" };
        var content = FileManager.ContentDir(_root, p);
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "keep.txt"), "keep");

        var badZip = Path.Combine(_root, "bad.zip");
        using (var fs = File.Create(badZip))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("../escaped.txt").Open());
            writer.Write("evil");
        }

        Assert.Throws<InvalidDataException>(() => FileManager.RestoreBackup(_root, p, badZip));

        // 校验失败时不得清空现有内容，也不得写出目标目录之外
        Assert.True(File.Exists(Path.Combine(content, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
    }

    [Fact]
    public void 恢复_压缩包损坏时不动现有内容()
    {
        var p = new ModProfile { Name = "损坏" };
        var content = FileManager.ContentDir(_root, p);
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "keep.txt"), "keep");

        var corruptZip = Path.Combine(_root, "corrupt.zip");
        File.WriteAllText(corruptZip, "这不是一个 zip 文件");

        Assert.ThrowsAny<Exception>(() => FileManager.RestoreBackup(_root, p, corruptZip));
        Assert.True(File.Exists(Path.Combine(content, "keep.txt")));
    }

    [Fact]
    public void 迁移_来源包含目标时拒绝执行()
    {
        var p = new ModProfile { Name = "迁移" };
        Directory.CreateDirectory(FileManager.ContentDir(_root, p));

        Assert.Throws<InvalidOperationException>(
            () => FileManager.MigrateContent(_root, p, _root, move: false));
    }

    [Fact]
    public void 迁移_递归复制文件并忽略系统垃圾文件()
    {
        var p = new ModProfile { Name = "迁移复制" };
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(Path.Combine(source, "paks"));
        File.WriteAllText(Path.Combine(source, "paks", "mod.pak"), "P");
        File.WriteAllText(Path.Combine(source, "Thumbs.db"), "junk");

        var count = FileManager.MigrateContent(_root, p, source, move: false);
        var files = FileManager.ListContentFiles(_root, p);

        Assert.Equal(1, count);
        Assert.Single(files);
        Assert.EndsWith("mod.pak", files[0]);
    }

    [Fact]
    public void 删除目录_拒绝根目录以外的目标并返回原因()
    {
        Directory.CreateDirectory(_root);
        var p = new ModProfile { Name = "不存在" };

        // 目录不存在时视为成功
        Assert.Null(FileManager.TryDeleteModDir(_root, p));

        Directory.CreateDirectory(FileManager.ContentDir(_root, p));
        Assert.Null(FileManager.TryDeleteModDir(_root, p));
        Assert.False(FileManager.ModDirExists(_root, p));
    }

    [Fact]
    public void 删除目录_根目录本身不会被删除()
    {
        Directory.CreateDirectory(_root);

        // 名字清洗后为空 → 落到 "MOD"，同样不能等于根目录
        var p = new ModProfile { Name = "MOD" };
        Directory.CreateDirectory(FileManager.ModDir(_root, p));

        Assert.Null(FileManager.TryDeleteModDir(_root, p));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void 同名MOD_解析到同一个磁盘目录()
    {
        var a = new ModProfile { Name = "a:b" };
        var b = new ModProfile { Name = "a?b" };

        Assert.Equal(FileManager.ModDir(_root, a), FileManager.ModDir(_root, b));
    }

    [Fact]
    public void 图片判断_图集扩展名与Steam预览图要求不同()
    {
        Assert.True(FileManager.IsImageFile("a.WEBP"));
        Assert.False(FileManager.IsSteamPreviewFile("a.webp"));
        Assert.True(FileManager.IsSteamPreviewFile("a.JPG"));
        Assert.True(FileManager.IsSteamPreviewFile("a.jpeg"));
        Assert.False(FileManager.IsSteamPreviewFile("a.png.txt"));
    }

    [Fact]
    public void 预览图识别_steam支持的格式优先且忽略非图片()
    {
        var p = new ModProfile { Name = "预览识别" };
        var dir = FileManager.PreviewDir(_root, p);
        Directory.CreateDirectory(dir);

        Assert.Equal("", FileManager.FindPreviewImage(_root, p));

        // 只有 webp（Steam 不支持预览）+ 一个文本文件
        File.WriteAllText(Path.Combine(dir, "cover.webp"), "x");
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "x");
        Assert.EndsWith("cover.webp", FileManager.FindPreviewImage(_root, p));

        // 出现 jpg/png 后应优先选它
        File.WriteAllText(Path.Combine(dir, "cover.png"), "x");
        Assert.EndsWith("cover.png", FileManager.FindPreviewImage(_root, p));
    }
}
