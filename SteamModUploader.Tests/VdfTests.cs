using SteamModUploader.Models;
using SteamModUploader.Services;
using Xunit;

namespace SteamModUploader.Tests;

/// <summary>VDF 生成 / 解析 / 上传输出解析的测试。</summary>
public class VdfTests
{
    [Fact]
    public void 生成与解析_能往返保留含反斜杠的路径()
    {
        var p = new ModProfile
        {
            AppId = "1234567",
            Title = "我的 MOD",
            ContentFolder = @"D:\new\temp\content",
            PreviewFile = @"D:\img\cover.png",
            Visibility = WorkshopVisibility.Friends,
            ChangeNote = "v1.0",
            PublishedFileId = "1234567890"
        };

        var parsed = VdfParser.Parse(VdfGenerator.Generate(p));

        Assert.Equal(p.AppId, parsed.AppId);
        Assert.Equal(p.Title, parsed.Title);
        Assert.Equal(p.ContentFolder, parsed.ContentFolder);
        Assert.Equal(p.PreviewFile, parsed.PreviewFile);
        Assert.Equal(p.ChangeNote, parsed.ChangeNote);
        Assert.Equal(p.PublishedFileId, parsed.PublishedFileId);
        Assert.Equal(WorkshopVisibility.Friends, parsed.Visibility);
    }

    [Fact]
    public void 生成_转义引号与反斜杠_并把换行换成空格()
    {
        var p = new ModProfile { Title = "a\"b\\c", ChangeNote = "第一行\n第二行" };

        var text = VdfGenerator.Generate(p);

        Assert.Contains("\"title\"\t\t\"a\\\"b\\\\c\"", text);
        Assert.Contains("\"changenote\"\t\t\"第一行 第二行\"", text);

        var parsed = VdfParser.Parse(text);
        Assert.Equal("a\"b\\c", parsed.Title);
        Assert.Equal("第一行 第二行", parsed.ChangeNote);
    }

    [Fact]
    public void 生成_可跳过预览图字段()
    {
        var p = new ModProfile { Title = "t", PreviewFile = @"D:\a.png" };

        Assert.Contains("previewfile", VdfGenerator.Generate(p));
        Assert.DoesNotContain("previewfile", VdfGenerator.Generate(p, includePreview: false));
    }

    [Fact]
    public void 生成_省略空字段并始终写入可见性()
    {
        var text = VdfGenerator.Generate(new ModProfile { Title = "t" });

        Assert.DoesNotContain("publishedfileid", text);
        Assert.DoesNotContain("contentfolder", text);
        Assert.Contains("\"visibility\"\t\t\"0\"", text);
    }

    [Fact]
    public void 解析_非法可见性回退为公开()
    {
        var parsed = VdfParser.Parse("\"workshopitem\" { \"visibility\" \"9\" \"title\" \"t\" }");

        Assert.Equal(WorkshopVisibility.Public, parsed.Visibility);
        Assert.Equal("t", parsed.Title);
    }

    [Fact]
    public void 解析_键名大小写不敏感且未命中标题时给出默认名()
    {
        var parsed = VdfParser.Parse("\"WorkshopItem\" { \"Title\" \"某个标题\" }");

        Assert.Equal("某个标题", parsed.Title);
        Assert.Equal("某个标题", parsed.Name);
    }

    [Fact]
    public void 生成_简介留空时不写入该字段()
    {
        var p = new ModProfile { Title = "t", Description = "" };
        Assert.DoesNotContain("description", VdfGenerator.Generate(p));

        // 只有空白也算留空：这样才能“不提交简介”，不会把网页上写的内容覆盖成空
        p.Description = "   ";
        Assert.DoesNotContain("description", VdfGenerator.Generate(p));
    }

    [Fact]
    public void 生成_填写简介时会写入并可往返()
    {
        var p = new ModProfile { Title = "t", Description = "第一行\n第二行" };
        var text = VdfGenerator.Generate(p);

        Assert.Contains("\"description\"\t\t\"第一行 第二行\"", text);
        Assert.Equal("第一行 第二行", VdfParser.Parse(text).Description);
    }

    [Fact]
    public void 往返_不含简介的配置导入后简介仍为空()
    {
        var text = VdfGenerator.Generate(new ModProfile { Title = "t", ChangeNote = "v1" });

        Assert.Equal("", VdfParser.Parse(text).Description);
    }

    [Theory]
    [InlineData("Update state (0x61) uploading, progress: 12.34 (1234 / 10000)", 12)]
    [InlineData("Uploading content... 45%", 45)]
    [InlineData("progress: 100.0 (10000 / 10000)", 100)]
    public void 上传输出_能解析上传进度(string line, int expected)
    {
        Assert.True(WorkshopOutputParser.TryParseProgress(line, out var percent));
        Assert.Equal(expected, percent);
    }

    [Theory]
    [InlineData("Logging in user 'x' to Steam Public...OK")]
    [InlineData("")]
    public void 上传输出_无进度的行不会误判(string line)
        => Assert.False(WorkshopOutputParser.TryParseProgress(line, out _));

    [Theory]
    [InlineData("PublishedFileID = 1234567890", "1234567890")]
    [InlineData("publishedfileid: 987654321", "987654321")]
    [InlineData("ItemId = 111222333", "111222333")]
    public void 上传输出_能解析创意工坊ID(string line, string expected)
    {
        Assert.True(WorkshopOutputParser.TryParsePublishedFileId(line, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("Logging in user 'x' to Steam Public...OK")]
    [InlineData("publishedfileid = 123")]
    [InlineData("")]
    public void 上传输出_无关内容不会误判(string line)
    {
        Assert.False(WorkshopOutputParser.TryParsePublishedFileId(line, out _));
    }
}
