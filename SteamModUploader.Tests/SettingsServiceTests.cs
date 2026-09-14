using System.IO;
using SteamModUploader.Models;
using SteamModUploader.Services;
using Xunit;

namespace SteamModUploader.Tests;

/// <summary>配置持久化测试：加密、原子写入（.prev 备份）与脏数据容错。</summary>
public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "smu-settings-" + Guid.NewGuid().ToString("N"));

    private readonly string _originalSettingsFile;

    public SettingsServiceTests()
    {
        _originalSettingsFile = SettingsService.SettingsFile;
        Directory.CreateDirectory(_dir);
        SettingsService.SettingsFile = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        SettingsService.SettingsFile = _originalSettingsFile;
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }

    [Fact]
    public void 保存与读取_密码加密落盘且配置完整()
    {
        var settings = new AppSettings
        {
            SteamCmdPath = @"D:\steamcmd\steamcmd.exe",
            SteamUsername = "user",
            SteamPassword = "p@ssw0rd",
            RootDir = @"D:\mods"
        };
        settings.Profiles.Add(new ModProfile
        {
            Name = "M1",
            Title = "T1",
            AppId = "1234567",
            Visibility = WorkshopVisibility.Private
        });

        SettingsService.Save(settings);
        var loaded = SettingsService.Load();

        Assert.Equal("p@ssw0rd", loaded.SteamPassword);
        Assert.Equal("user", loaded.SteamUsername);
        Assert.Equal(@"D:\mods", loaded.RootDir);

        var profile = Assert.Single(loaded.Profiles);
        Assert.Equal("M1", profile.Name);
        Assert.Equal("T1", profile.Title);
        Assert.Equal(WorkshopVisibility.Private, profile.Visibility);

        // 磁盘上不得出现明文密码
        Assert.DoesNotContain("p@ssw0rd", File.ReadAllText(SettingsService.SettingsFile));
    }

    [Fact]
    public void 保存_会留下上一版备份()
    {
        SettingsService.Save(new AppSettings { SteamUsername = "first" });
        SettingsService.Save(new AppSettings { SteamUsername = "second" });

        Assert.True(File.Exists(SettingsService.SettingsFile + ".prev"));
        Assert.Equal("second", SettingsService.Load().SteamUsername);
    }

    [Fact]
    public void 读取_字段为null的配置不会崩()
    {
        File.WriteAllText(SettingsService.SettingsFile, """{"SteamCmdPath":null,"RootDir":null,"Profiles":null}""");

        var loaded = SettingsService.Load();

        Assert.Equal("", loaded.SteamCmdPath);
        Assert.Equal("", loaded.RootDir);
        Assert.Empty(loaded.Profiles);
    }

    [Fact]
    public void 读取_缺失或损坏的配置会回退为默认值()
    {
        Assert.Equal("", SettingsService.Load().SteamCmdPath);   // 文件不存在

        File.WriteAllText(SettingsService.SettingsFile, "{ 这不是合法 JSON");
        Assert.Equal("", SettingsService.Load().SteamCmdPath);
    }

    [Fact]
    public void 保存_替换时不会残留临时文件()
    {
        SettingsService.Save(new AppSettings { SteamUsername = "a" });
        SettingsService.Save(new AppSettings { SteamUsername = "b" });

        Assert.False(File.Exists(SettingsService.SettingsFile + ".tmp"));
    }
}
