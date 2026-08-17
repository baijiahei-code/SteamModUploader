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

    /// <summary>进程启动后立即写入标准输入的内容（用于在命令行之外安全传递密码等）。</summary>
    public string? InitialInput { get; set; }

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
            try { proc.Kill(true); } catch { }
        });

        proc.OutputDataReceived += (_, e) => HandleData(proc, e.Data);
        proc.ErrorDataReceived += (_, e) => HandleData(proc, e.Data);

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // 通过标准输入传递密码等初始输入，避免密码出现在进程命令行中（可被其他进程读取）
        if (!string.IsNullOrWhiteSpace(InitialInput))
        {
            try
            {
                proc.StandardInput.WriteLine(InitialInput);
                proc.StandardInput.Flush();
            }
            catch
            {
                // 进程可能已退出，忽略写入错误
            }
        }

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return proc.ExitCode;
    }

    private void HandleData(Process proc, string? line)
    {
        if (line == null) return;
        OutputReceived?.Invoke(this, line);

        // steamcmd 需要验证码时，提示用户输入并写入 stdin
        if (InputProvider != null && IsAuthPrompt(line))
        {
            Task.Run(() =>
            {
                try
                {
                    var input = InputProvider();
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        proc.StandardInput.WriteLine(input);
                        proc.StandardInput.Flush();
                    }
                }
                catch
                {
                    // 进程可能已退出，忽略写入错误
                }
            });
        }
    }

    private static bool IsAuthPrompt(string line)
    {
        var lower = line.ToLowerInvariant();
        return lower.Contains("steam guard code")
            || lower.Contains("auth code")
            || lower.Contains("authenticator")
            || lower.Contains("enter the current auth code");
    }
}
