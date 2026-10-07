using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteamModUploader.Services;

/// <summary>
/// 单实例守护。
///
/// 背景：程序原先没有单实例限制，一直双击 exe 就会开出很多个窗口。
/// 多份进程会并发读写同一份 settings.json（SaveSettings 是「读-改-写」），
/// 后保存的会把先保存的覆盖掉，存在配置丢失的风险。
/// 因此重复启动时不再开新窗口，而是把已经在运行的那个窗口唤到前台。
/// </summary>
public static class SingleInstance
{
    /// <summary>互斥体名（Local\ = 仅当前登录会话，不同用户互不干扰）。</summary>
    private const string DefaultMutexName = @"Local\SteamModUploader.SingleInstance";

    private static Mutex? _mutex;

    /// <summary>
    /// 尝试取得单实例所有权。
    /// 返回 false 表示已经有一个实例在运行（调用方应唤起它并直接退出本进程）。
    /// </summary>
    /// <param name="mutexName">互斥体名，测试时传独立名字即可避免与真实程序互相影响。</param>
    public static bool TryAcquire(string? mutexName = null)
    {
        // initiallyOwned: true —— 第一个进程创建后直接持有，无需再 WaitOne
        var mutex = new Mutex(initiallyOwned: true, mutexName ?? DefaultMutexName, out bool createdNew);
        if (createdNew)
        {
            _mutex = mutex;
            return true;
        }

        // 名字已存在 = 已有实例（注意：此时并没有拿到所有权，不能 ReleaseMutex）
        mutex.Dispose();
        return false;
    }

    /// <summary>释放单实例所有权（程序退出时调用）。</summary>
    public static void Release()
    {
        var mutex = _mutex;
        _mutex = null;
        if (mutex == null) return;

        try { mutex.ReleaseMutex(); } catch { /* 未持有或被系统回收 */ }
        try { mutex.Dispose(); } catch { }
    }

    /// <summary>
    /// 把已经在运行的实例窗口唤到前台（若窗口最小化则先还原）。
    /// 返回 false 表示在超时时间内没找到任何可用的窗口句柄。
    /// 新实例刚启动时对方可能还没建好窗口，因此这里会重试一小段时间。
    /// </summary>
    public static bool ActivateRunningInstance(TimeSpan? timeout = null)
    {
        var deadline = Environment.TickCount64 + (long)(timeout ?? TimeSpan.FromSeconds(3)).TotalMilliseconds;

        while (true)
        {
            if (TryActivateOnce()) return true;
            if (Environment.TickCount64 >= deadline) return false;
            Thread.Sleep(100);
        }
    }

    private static bool TryActivateOnce()
    {
        Process current = Process.GetCurrentProcess();
        Process[] others;
        try
        {
            others = Process.GetProcessesByName(current.ProcessName);
        }
        catch
        {
            return false;
        }

        try
        {
            foreach (var process in others)
            {
                if (process.Id == current.Id) continue;

                IntPtr handle;
                try
                {
                    process.Refresh();
                    handle = process.MainWindowHandle;
                }
                catch
                {
                    continue;   // 进程可能刚好退出
                }

                if (handle == IntPtr.Zero) continue;

                if (IsIconic(handle)) ShowWindowAsync(handle, SW_RESTORE);
                SetForegroundWindow(handle);
                return true;
            }
        }
        finally
        {
            foreach (var process in others) process.Dispose();
        }

        return false;
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
}
