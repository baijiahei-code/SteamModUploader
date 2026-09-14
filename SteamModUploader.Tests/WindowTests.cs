using System.IO;
using System.Threading;
using System.Windows;
using SteamModUploader.Models;
using Xunit;

namespace SteamModUploader.Tests;

/// <summary>
/// 窗口构造的回归测试。
/// 背景：曾出现「设置过根目录后，『文件管理（全局）』点了打不开」——
/// 构造函数里给 RootDirBox 赋值会同步触发 TextChanged，而事件处理器用到的防抖定时器
/// 当时还没初始化，抛 NullReferenceException 导致窗口构造失败。
/// 这里的测试会在 STA 线程上真实构造窗口，能在编译期之后直接暴露这类问题。
/// </summary>
public class WindowTests
{
    private static readonly object AppLock = new();

    [Fact]
    public void 文件管理窗口_能正常创建()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "smu-window-" + Guid.NewGuid().ToString("N"));

        var error = RunSta(() =>
        {
            // 根目录为空（默认配置）
            _ = new FileManagerWindow(new AppSettings());

            // 已配置根目录 + 开启上传前自动备份：
            // 构造时会触发 TextChanged 与 Checked，是此前出错的那条路径
            _ = new FileManagerWindow(new AppSettings
            {
                RootDir = tempRoot,
                AutoBackupBeforeUpload = true
            });
        });

        Assert.Null(error);
    }

    /// <summary>
    /// 在 STA 线程上执行，并预先准备好已加载 App.xaml 资源的 Application
    /// （窗口里的 {StaticResource ...} 依赖它）。
    /// </summary>
    private static Exception? RunSta(Action action)
    {
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                // Application 是进程级单例，多个测试可能并发进入
                lock (AppLock)
                {
                    if (Application.Current == null)
                    {
                        var app = new App();
                        app.InitializeComponent();
                    }
                }

                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return error;
    }
}
