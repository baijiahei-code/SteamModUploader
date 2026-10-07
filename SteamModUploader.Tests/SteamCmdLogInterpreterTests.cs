using SteamModUploader.Services;
using Xunit;

namespace SteamModUploader.Tests;

/// <summary>
/// steamcmd 输出「翻译层」的回归测试。
/// 背景：steamcmd 会打一大堆内部英文信息（自检、十六进制状态码、ERESULT 错误码），
/// 用户看不懂；这里保证噪音被识别、重要信息有中文解释、噪音不会被误当成失败原因。
/// </summary>
public class SteamCmdLogInterpreterTests
{
    [Theory]
    [InlineData("src\\clientdll.cpp (6487) : Assertion Failed: !m_bShutdown")]
    [InlineData("ILocalize::AddFile() failed to load file \"public/steambootstrapper_english.txt\".")]
    [InlineData("Redirecting stderr to 'D:\\steamcmd\\logs\\stderr.txt'")]
    [InlineData("CWorkThreadPool::~CWorkThreadPool: work queue empty")]
    [InlineData("Steam Console Client (c) Valve Corporation - version 1722172811")]
    [InlineData("-- type 'quit' to exit --")]
    public void 内部自检信息_识别为噪音(string line)
        => Assert.Equal(SteamCmdLineKind.Noise, SteamCmdLogInterpreter.Interpret(line).Kind);

    [Fact]
    public void 已知错误码_翻译成中文并给出建议()
    {
        var reading = SteamCmdLogInterpreter.Interpret("FAILED with result code 25");

        Assert.Equal(SteamCmdLineKind.Error, reading.Kind);
        Assert.NotNull(reading.Hint);
        Assert.Contains("限制", reading.Hint);
    }

    [Fact]
    public void 未知错误码_提示这是ERESULT编号()
    {
        var reading = SteamCmdLogInterpreter.Interpret("FAILED with result code 199");

        Assert.Equal(SteamCmdLineKind.Error, reading.Kind);
        Assert.NotNull(reading.Hint);
        Assert.Contains("ERESULT", reading.Hint);
    }

    [Fact]
    public void 权限错误_提示清除缓存与法律协议()
    {
        var reading = SteamCmdLogInterpreter.Interpret("ERROR! Failed to update workshop item: Access Denied");

        Assert.Equal(SteamCmdLineKind.Error, reading.Kind);
        Assert.NotNull(reading.Hint);
        Assert.Contains("没有权限", reading.Hint);
    }

    [Fact]
    public void 十六进制状态码_解释它不是错误码()
    {
        var reading = SteamCmdLogInterpreter.Interpret("Error! App '480' state is 0x406 after update job.");

        Assert.Equal(SteamCmdLineKind.Warning, reading.Kind);
        Assert.NotNull(reading.Hint);
        Assert.Contains("内部状态码", reading.Hint);
    }

    [Fact]
    public void 验证码提示_说明会弹输入框()
    {
        var reading = SteamCmdLogInterpreter.Interpret("Please enter the current code from your Steam Guard Mobile Authenticator app");

        Assert.Equal(SteamCmdLineKind.Warning, reading.Kind);
        Assert.NotNull(reading.Hint);
        Assert.Contains("验证码", reading.Hint);
    }

    [Fact]
    public void 上传进度行_解释百分比含义()
    {
        var reading = SteamCmdLogInterpreter.Interpret("Update state (0x61) uploading, progress: 12.34 (1234 / 10000)");

        Assert.Equal(SteamCmdLineKind.Info, reading.Kind);
        Assert.NotNull(reading.Hint);
        Assert.Contains("百分比", reading.Hint);
    }

    [Fact]
    public void 普通信息与成功行_不额外解释()
    {
        var login = SteamCmdLogInterpreter.Interpret("Logging in user 'tester' to Steam Public...OK");
        Assert.Equal(SteamCmdLineKind.Info, login.Kind);
        Assert.Null(login.Hint);

        var success = SteamCmdLogInterpreter.Interpret("Success. PublishedFileID: 1234567890");
        Assert.Equal(SteamCmdLineKind.Success, success.Kind);
    }

    [Fact]
    public void 诊断_噪音行不会当成失败原因()
    {
        var lines = new[]
        {
            "Redirecting stderr to 'D:\\steamcmd\\logs\\stderr.txt'",
            "src\\clientdll.cpp (6487) : Assertion Failed: !m_bShutdown",
            "Loading Steam API...OK",
            "Success. PublishedFileID: 1234567890"
        };

        Assert.Null(SteamCmdLogInterpreter.Diagnose(lines));
    }

    [Fact]
    public void 诊断_优先给明确错误而不是普通提醒()
    {
        var lines = new[]
        {
            "SteamCMD needs to restart",
            "Update state (0x61) uploading, progress: 5.00 (5 / 100)",
            "FAILED with result code 5"
        };

        var diagnosis = SteamCmdLogInterpreter.Diagnose(lines);

        Assert.NotNull(diagnosis);
        Assert.Contains("密码错误", diagnosis);
    }
}
