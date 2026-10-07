using SteamModUploader.Services;
using Xunit;

namespace SteamModUploader.Tests;

/// <summary>
/// 单实例守护的回归测试。
/// 背景：程序可以重复启动，一直双击就会开出很多个窗口，
/// 多份进程并发写 settings.json 有互相覆盖的风险。
/// 这里用独立互斥体名验证「第二个实例会被拦下」，避免和真实程序互相影响。
/// </summary>
public class SingleInstanceTests
{
    [Fact]
    public void 第二个实例会被拦下_释放后可以重新取得()
    {
        var name = @"Local\SteamModUploader.Tests." + Guid.NewGuid().ToString("N");

        Assert.True(SingleInstance.TryAcquire(name), "第一个实例应当取得所有权");
        try
        {
            Assert.False(SingleInstance.TryAcquire(name), "名字已存在时，第二个实例必须被拦下");
        }
        finally
        {
            SingleInstance.Release();
        }

        // 释放后应能再次取得（模拟程序退出后又启动）
        try
        {
            Assert.True(SingleInstance.TryAcquire(name), "释放后应当可以重新取得所有权");
        }
        finally
        {
            SingleInstance.Release();
        }
    }
}
