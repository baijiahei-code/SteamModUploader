using System.Windows;
using System.Windows.Threading;
using SteamModUploader.Services;

namespace SteamModUploader;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例：重复双击 exe 时不再开出第二个窗口。
        // 多份进程会并发读写同一份 settings.json，互相覆盖有丢失配置的风险，
        // 因此这里直接唤起已有窗口并退出本进程（不写任何文件）。
        if (!SingleInstance.TryAcquire())
        {
            if (!SingleInstance.ActivateRunningInstance())
            {
                MessageBox.Show(
                    "「Steam MOD 上传工具」已经在运行了，请查看任务栏（或按 Alt+Tab）。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        base.OnStartup(e);

        // 兜底异常处理：避免个别未捕获异常直接崩掉程序、丢失尚未保存的配置
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Write($"【未观察的任务异常】{args.Exception}");
            Logger.Flush();
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // 不用 new Exception(...) 包裹，直接用原始异常对象（非 Exception 时打印其文本）
            var detail = args.ExceptionObject as Exception;
            Logger.Write(detail != null ? $"【未处理异常】{detail}" : $"【未处理异常】{args.ExceptionObject}");
            Logger.Flush();
        };

        // App.xaml 里不再使用 StartupUri，主窗口在这里显式创建：
        // 必须等单实例检查通过之后才建窗口，否则第二个实例会闪出一个空窗口
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Write($"【界面异常】{e.Exception}");
        Logger.Flush();
        e.Handled = true;   // 不让整个程序崩溃，交由用户继续操作

        MessageBox.Show(
            "程序遇到一个未处理的错误，当前操作已中止：\n\n" +
            e.Exception.Message +
            "\n\n详细信息已写入日志，可用「导出日志」查看。",
            "错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstance.Release();   // 交还单实例所有权，下次才能正常启动
        Logger.Shutdown();          // 落盘并释放日志定时器，避免丢失最后几条日志
        base.OnExit(e);
    }
}
