using System.IO;
using SteamModUploader.Models;

namespace SteamModUploader.Services;

/// <summary>一次上传的结论。</summary>
public enum UploadOutcome
{
    /// <summary>steamcmd 明确报告成功，或回写了创意工坊 ID。</summary>
    Succeeded,

    /// <summary>输出里出现了明确的错误信息（或卡死被自动结束）。</summary>
    Failed,

    /// <summary>既没有成功标记也没有错误标记，无法确认结果。</summary>
    Unknown
}

/// <summary>一次上传的结果。</summary>
public sealed record UploadResult(
    UploadOutcome Outcome,
    int ExitCode,
    string? PublishedFileId,
    bool TimedOut,
    string Message,
    string? BuildLogPath,
    IReadOnlyList<string> BuildLogTail);

/// <summary>
/// 「上传 / 更新创意工坊项目」的流程封装（与界面无关）：
/// 组织 steamcmd 参数、按需提供 Steam Guard 验证码、跟踪进度、
/// 从输出与 VDF 回写值中获取创意工坊 ID、失败时给出 steamcmd 构建日志。
/// </summary>
public sealed class WorkshopUploader
{
    /// <summary>只保留最后若干行输出用于结果判定，避免长时间上传时占用过多内存。</summary>
    private const int OutputKeepLines = 200;

    /// <summary>成功标志（Valve 文档：成功后 VDF 中的 publishedfileid 会被回写）。</summary>
    private static readonly string[] SuccessMarkers =
    {
        "success. publishedfileid",
        "success. published file id",
        "uploaded item"
    };

    /// <summary>常见失败标志。</summary>
    private static readonly string[] FailureMarkers =
    {
        "error!",
        "failed",
        "access denied",
        "denied",
        "not logged in",
        "no permission",
        "invalid",
        "login failure"
    };

    private readonly SteamCmdRunner _runner = new();
    private readonly List<string> _recentOutput = new();

    public WorkshopUploader() => _runner.OutputReceived += (_, line) => OnOutput(line);

    /// <summary>steamcmd 的每一行输出（供界面显示日志）。</summary>
    public event EventHandler<string>? OutputReceived;

    /// <summary>解析到 PublishedFileID 时触发。</summary>
    public event EventHandler<string>? PublishedFileIdFound;

    /// <summary>上传进度变化（0-100）。</summary>
    public event EventHandler<int>? ProgressChanged;

    /// <summary>需要用户输入（Steam Guard 验证码）时的回调。</summary>
    public Func<string>? InputProvider
    {
        get => _runner.InputProvider;
        set => _runner.InputProvider = value;
    }

    /// <summary>
    /// 本次上传的目标 MOD。上传期间锁定，避免用户切换列表后结果被写到别的配置上。
    /// 上传结束后不清空：最后几行输出对应的界面回调可能晚于 await 继续执行。
    /// </summary>
    public ModProfile? Target { get; private set; }

    /// <summary>结束当前正在运行的 steamcmd（取消上传 / 程序退出）。</summary>
    public void Kill() => _runner.KillCurrent();

    /// <summary>
    /// 执行一次上传。
    /// deleteVdfAfterUpload 为 true 时会删除临时 VDF（删除前先回读它拿创意工坊 ID）。
    /// </summary>
    public async Task<UploadResult> UploadAsync(
        string steamCmdPath,
        string username,
        string password,
        ModProfile target,
        string vdfPath,
        bool deleteVdfAfterUpload,
        CancellationToken ct = default)
    {
        Target = target;
        _recentOutput.Clear();

        _runner.InitialInput = password;
        var args = new[] { "+login", username, "+workshop_build_item", vdfPath, "+quit" };

        int exitCode;
        try
        {
            exitCode = await _runner.RunAsync(steamCmdPath, args, ct).ConfigureAwait(true);
        }
        finally
        {
            // Valve 文档：命令成功时 steamcmd 会把创意工坊 ID 回写进 VDF。
            // 这比解析 stdout 更可靠，且必须在删除临时 VDF 之前读取。
            ReadBackPublishedFileId(vdfPath);

            if (deleteVdfAfterUpload) TryDeleteFile(vdfPath);
        }

        var timedOut = _runner.TimedOut;
        var id = string.IsNullOrWhiteSpace(target.PublishedFileId) ? null : target.PublishedFileId;
        var outcome = DetermineOutcome(exitCode, id, timedOut);

        // 成功时不需要构建日志；失败/结果不明时读出来，错误原因通常只写在那里
        var (logPath, logTail) = outcome == UploadOutcome.Succeeded
            ? (null, Array.Empty<string>())
            : ReadBuildLog(steamCmdPath, target.AppId);

        return new UploadResult(
            outcome, exitCode, id, timedOut,
            BuildMessage(outcome, exitCode, id, timedOut),
            logPath, logTail);
    }

    private void OnOutput(string line)
    {
        _recentOutput.Add(line);
        if (_recentOutput.Count > OutputKeepLines) _recentOutput.RemoveRange(0, _recentOutput.Count - OutputKeepLines);

        OutputReceived?.Invoke(this, line);

        if (WorkshopOutputParser.TryParseProgress(line, out var percent))
            ProgressChanged?.Invoke(this, percent);

        if (Target == null) return;
        if (!WorkshopOutputParser.TryParsePublishedFileId(line, out var id)) return;

        Target.PublishedFileId = id;
        PublishedFileIdFound?.Invoke(this, id);
    }

    /// <summary>回读 VDF 里的 publishedfileid（steamcmd 成功后会自动写入）。</summary>
    private void ReadBackPublishedFileId(string vdfPath)
    {
        var target = Target;
        if (target == null) return;

        try
        {
            if (!File.Exists(vdfPath)) return;

            var parsed = VdfParser.Parse(File.ReadAllText(vdfPath));
            var id = parsed.PublishedFileId?.Trim() ?? "";
            if (id.Length == 0 || id == "0" || id == target.PublishedFileId) return;

            target.PublishedFileId = id;
            PublishedFileIdFound?.Invoke(this, id);
        }
        catch
        {
            // 回读失败不影响主流程（stdout 解析仍可能拿到 ID）
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { /* 进程可能仍持有句柄，留待系统清理 */ }
    }

    private UploadOutcome DetermineOutcome(int exitCode, string? id, bool timedOut)
    {
        if (timedOut) return UploadOutcome.Failed;
        if (HasMarker(SuccessMarkers)) return UploadOutcome.Succeeded;
        if (!string.IsNullOrWhiteSpace(id) && id != "0") return UploadOutcome.Succeeded;
        if (HasMarker(FailureMarkers)) return UploadOutcome.Failed;

        return exitCode == 0 ? UploadOutcome.Unknown : UploadOutcome.Failed;
    }

    private bool HasMarker(IEnumerable<string> markers)
        => _recentOutput.Any(line => markers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase)));

    private static string BuildMessage(UploadOutcome outcome, int exitCode, string? id, bool timedOut) => outcome switch
    {
        UploadOutcome.Succeeded => $"上传成功，创意工坊项目 ID：{id}（steamcmd 退出码 {exitCode}）。",
        UploadOutcome.Failed when timedOut =>
            "上传失败：steamcmd 长时间没有任何输出，已自动结束进程（可能是网络中断或卡在交互提示）。",
        UploadOutcome.Failed => "上传失败，请在下方日志与「构建日志」中查看具体原因。",
        _ => $"上传命令已结束（退出码 {exitCode}），但输出里没有明确的成功/失败标志，请到创意工坊页面确认结果。"
    };

    /// <summary>
    /// 读取 steamcmd 的创意工坊构建日志尾部。
    /// Valve 文档指出错误信息大多只写在 depot_build_&lt;appid&gt;.log 里，控制台不一定显示。
    /// </summary>
    private static (string? Path, IReadOnlyList<string> Lines) ReadBuildLog(
        string steamCmdPath, string appId, int maxLines = 30)
    {
        try
        {
            var baseDir = Path.GetDirectoryName(steamCmdPath) ?? "";
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
                return (null, Array.Empty<string>());

            var candidates = new List<string>();
            foreach (var sub in new[] { Path.Combine("logs", "workshopbuilds"), "workshopbuilds", "logs" })
            {
                var dir = Path.Combine(baseDir, sub);
                if (!Directory.Exists(dir)) continue;

                var exact = Path.Combine(dir, $"depot_build_{appId}.log");
                if (File.Exists(exact)) candidates.Add(exact);

                try { candidates.AddRange(Directory.EnumerateFiles(dir, "depot_build_*.log")); }
                catch { /* 忽略单个目录的读取错误 */ }
            }

            var logPath = candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();

            if (logPath == null) return (null, Array.Empty<string>());

            var lines = File.ReadAllLines(logPath);
            return (logPath, lines.Length <= maxLines ? lines : lines[^maxLines..]);
        }
        catch
        {
            return (null, Array.Empty<string>());
        }
    }
}
