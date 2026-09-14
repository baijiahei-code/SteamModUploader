using System.Diagnostics;
using System.IO;

namespace SteamModUploader.Services;

/// <summary>
/// 负责启动 steamcmd.exe 并捕获其输出。
/// 支持在登录需要 Steam Guard 验证码时向用户请求输入。
/// </summary>
public class SteamCmdRunner
{
    /// <summary>steamcmd 输出的每一行。</summary>
    public event EventHandler<string>? OutputReceived;

    /// <summary>当需要输入验证码等交互信息时调用，返回用户输入的内容（同步、在 UI 线程执行）。</summary>
    public Func<string>? InputProvider { get; set; }

    /// <summary>
    /// 进程启动后需要写入标准输入的内容（目前是 Steam 密码）。
    /// 只有 steamcmd 确实询问时才会写入，避免密码出现在命令行中（可被其他进程读取）。
    /// </summary>
    public string? InitialInput { get; set; }

    /// <summary>检测不到密码提示时的兑底写入延迟（秒）。</summary>
    private const int PasswordFallbackDelaySeconds = 10;

    /// <summary>多久没有任何输出就认为卡死（默认 5 分钟），到期会结束进程。</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>本次运行是否因长时间无输出而被强制结束。</summary>
    public bool TimedOut => Volatile.Read(ref _timedOut) == 1;

    private Process? _currentProcess;
    private int _inputRequested;
    private int _passwordRequested;
    private int _passwordNotNeeded;
    private int _passwordSent;
    private int _timedOut;
    private long _lastOutputTicks;

    /// <summary>强制结束当前正在运行的 steamcmd（用于取消上传 / 程序退出）。</summary>
    public void KillCurrent()
    {
        try { _currentProcess?.Kill(entireProcessTree: true); }
        catch { /* 进程可能已退出 */ }
    }

    public async Task<int> RunAsync(string steamCmdPath, string[] args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = steamCmdPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(steamCmdPath) ?? ""
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        using var cancelReg = ct.Register(() =>
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
        });

        proc.OutputDataReceived += (_, e) => HandleData(proc, e.Data);
        proc.ErrorDataReceived += (_, e) => HandleData(proc, e.Data);

        Interlocked.Exchange(ref _inputRequested, 0);
        Interlocked.Exchange(ref _passwordRequested, 0);
        Interlocked.Exchange(ref _passwordNotNeeded, 0);
        Interlocked.Exchange(ref _passwordSent, 0);
        Interlocked.Exchange(ref _timedOut, 0);
        Volatile.Write(ref _lastOutputTicks, Environment.TickCount64);

        using var exitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? passwordTask = null;
        Task? watchdogTask = null;

        proc.Start();
        _currentProcess = proc;

        try
        {
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            if (!string.IsNullOrWhiteSpace(InitialInput))
                passwordTask = SendPasswordWhenNeededAsync(proc, InitialInput, exitCts.Token);

            watchdogTask = WatchForStallAsync(proc, exitCts.Token);

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return proc.ExitCode;
        }
        finally
        {
            _currentProcess = null;
            exitCts.Cancel();

            foreach (var task in new[] { passwordTask, watchdogTask })
            {
                if (task == null) continue;
                try { await task.ConfigureAwait(false); } catch { /* 已内部处理 */ }
            }

            // 等待异步输出读取结束，确保最后几行日志（如 publishedfileid）不丢失
            try { proc.WaitForExit(); } catch { }
        }
    }

    /// <summary>
    /// 卡死监护：超过 IdleTimeout 一直没有新输出就结束进程。
    /// 用于应对 steamcmd 卡在交互提示、自更新或网络挂起的情况。
    /// </summary>
    private async Task WatchForStallAsync(Process proc, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            var idle = TimeSpan.FromMilliseconds(Environment.TickCount64 - Volatile.Read(ref _lastOutputTicks));
            if (idle < IdleTimeout) continue;

            Interlocked.Exchange(ref _timedOut, 1);
            try { proc.Kill(entireProcessTree: true); } catch { }
            return;
        }
    }

    /// <summary>
    /// 按需把密码写进标准输入：
    /// · 检测到密码提示 → 立即写入；
    /// · 已确认走缓存登录（登录成功且没问密码）→ 永不写入，
    ///   否则残留的密码会被后续提示（如 Steam Guard）当成输入误读；
    /// · 两者都没出现（提示文案变化等）→ 超时后照旧写入，保证仍能登录。
    /// </summary>
    private async Task SendPasswordWhenNeededAsync(Process proc, string password, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(PasswordFallbackDelaySeconds);
        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            if (Volatile.Read(ref _passwordNotNeeded) == 1) return;
            if (Volatile.Read(ref _passwordRequested) == 1) break;

            try { await Task.Delay(100, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }

        if (ct.IsCancellationRequested || Volatile.Read(ref _passwordNotNeeded) == 1) return;
        if (Interlocked.Exchange(ref _passwordSent, 1) != 0) return;

        TryWriteInput(proc, password);
    }

    private void HandleData(Process proc, string? line)
    {
        if (line == null) return;

        // 记录最后一次输出时间，供卡死监护判断
        Volatile.Write(ref _lastOutputTicks, Environment.TickCount64);
        OutputReceived?.Invoke(this, line);

        // 记录登录阶段状态，供 SendPasswordWhenNeededAsync 判断是否需要写密码
        if (IsPasswordPrompt(line)) Interlocked.Exchange(ref _passwordRequested, 1);
        else if (IsLoginSucceeded(line)) Interlocked.Exchange(ref _passwordNotNeeded, 1);

        // steamcmd 可能连续输出多行“需要验证码”之类的提示，这里保证只向用户请求一次
        if (InputProvider == null || !IsAuthPrompt(line)) return;
        if (Interlocked.Exchange(ref _inputRequested, 1) != 0) return;

        Task.Run(() =>
        {
            try
            {
                var input = InputProvider();
                if (!string.IsNullOrWhiteSpace(input)) TryWriteInput(proc, input);
            }
            catch
            {
                // 进程可能已退出，忽略写入错误
            }
        });
    }

    /// <summary>steamcmd 是否正在询问密码（形如 “password:”）。</summary>
    private static bool IsPasswordPrompt(string line)
    {
        var trimmed = line.TrimEnd();
        return trimmed.EndsWith("password:", StringComparison.OrdinalIgnoreCase)
            || (trimmed.EndsWith(':') && trimmed.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>是否已用缓存凭据登录成功（此时不会再询问密码）。</summary>
    private static bool IsLoginSucceeded(string line)
        => line.Contains("Logging in user", StringComparison.OrdinalIgnoreCase)
        && line.Contains("OK", StringComparison.OrdinalIgnoreCase);

    private static void TryWriteInput(Process proc, string input)
    {
        try
        {
            proc.StandardInput.WriteLine(input);
            proc.StandardInput.Flush();
        }
        catch
        {
            // 进程可能已退出，忽略写入错误
        }
    }

    private static bool IsAuthPrompt(string line)
    {
        return line.Contains("steam guard code", StringComparison.OrdinalIgnoreCase)
            || line.Contains("auth code", StringComparison.OrdinalIgnoreCase)
            || line.Contains("authenticator", StringComparison.OrdinalIgnoreCase)
            || line.Contains("enter the current auth code", StringComparison.OrdinalIgnoreCase);
    }
}
