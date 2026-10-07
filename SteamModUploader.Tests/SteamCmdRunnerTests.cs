using System.Text;
using SteamModUploader.Services;
using Xunit;

namespace SteamModUploader.Tests;

/// <summary>
/// steamcmd 进程启动参数的回归测试。
///
/// 背景：steamcmd 的本地化文本（如「正在检查可用更新...」）是按 UTF-8 输出的，
/// 而不指定 StandardOutputEncoding 时 .NET 用系统 ANSI 代码页（中文系统 = 936）解码，
/// 日志里会出现「姝ｅ湪妫€鏌ュ彲鐢ㄦ洿鏂?...」这种乱码。
/// </summary>
public class SteamCmdRunnerTests
{
    private static readonly string[] LoginArgs = { "+login", "user", "+quit" };
    private static readonly string[] QuitArgs = { "+quit" };
    private static readonly string[] NoArgs = System.Array.Empty<string>();

    [Fact]
    public void 启动参数_显式用UTF8解码输出()
    {
        var psi = SteamCmdRunner.BuildStartInfo(@"D:\steamcmd\steamcmd.exe", LoginArgs);

        Assert.NotNull(psi.StandardOutputEncoding);
        Assert.NotNull(psi.StandardErrorEncoding);
        Assert.Equal("utf-8", psi.StandardOutputEncoding!.WebName);
        Assert.Equal("utf-8", psi.StandardErrorEncoding!.WebName);
    }

    [Fact]
    public void 启动参数_其余设置保持原样()
    {
        var psi = SteamCmdRunner.BuildStartInfo(@"D:\steamcmd\steamcmd.exe", QuitArgs);

        Assert.Equal(@"D:\steamcmd\steamcmd.exe", psi.FileName);
        Assert.Equal(@"D:\steamcmd", psi.WorkingDirectory);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.True(psi.RedirectStandardInput);
        Assert.True(psi.CreateNoWindow);
        Assert.Equal(QuitArgs, psi.ArgumentList.ToArray());
    }

    [Fact]
    public void 按配置的编码解码_中文提示不会变成乱码()
    {
        var psi = SteamCmdRunner.BuildStartInfo("steamcmd.exe", NoArgs);
        var bytes = Encoding.UTF8.GetBytes("[  0%] 正在检查可用更新...");

        Assert.Equal("[  0%] 正在检查可用更新...", psi.StandardOutputEncoding!.GetString(bytes));
    }
}
