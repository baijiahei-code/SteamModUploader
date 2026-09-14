using System.IO;
using System.Runtime.CompilerServices;
using SteamModUploader.Services;

namespace SteamModUploader.Tests;

/// <summary>
/// 测试程序集的进程级初始化。
///
/// 重要：<see cref="SettingsService"/> 是静态类，默认指向真实的
/// %APPDATA%\SteamModUploader\settings.json。任何测试（尤其是构造窗口的测试）
/// 一旦间接触发保存，就会覆盖用户真实配置 —— 2026-09-14 就因此丢过一次配置。
/// 这里在测试进程启动时统一把配置路径重定向到临时目录，从根上杜绝这种事故。
/// </summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void RedirectSettingsFileToTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"smu-tests-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);

        SettingsService.SettingsFile = Path.Combine(dir, "settings.json");
    }
}
