using System.Text.RegularExpressions;

namespace SteamModUploader.Services;

/// <summary>steamcmd 输出行的性质。</summary>
public enum SteamCmdLineKind
{
    /// <summary>普通信息，原样显示。</summary>
    Info,

    /// <summary>steamcmd 内部的初始化 / 退出自检信息，对用户没有意义（界面折叠，完整内容仍写入日志文件）。</summary>
    Noise,

    /// <summary>明确的成功提示。</summary>
    Success,

    /// <summary>需要留意、但本身不是错误的信息（正常流程，只是不容易看懂）。</summary>
    Warning,

    /// <summary>明确的失败信息。</summary>
    Error
}

/// <summary>一行输出的解读结果：Kind 决定怎么显示，Hint 是补给人看的中文解释（可为空）。</summary>
public sealed record SteamCmdLineReading(SteamCmdLineKind Kind, string? Hint);

/// <summary>
/// steamcmd 输出的「翻译层」。
///
/// steamcmd 会把大量内部英文信息直接打到控制台：初始化自检、十六进制状态码、
/// 以及 "FAILED with result code 25" 这类只知道编号的错误。用户看不懂是必然的。
/// 这里统一做两件事：
/// 1) 认出对用户毫无意义的噪音行（界面折叠，日志文件里仍然保留原始内容）；
/// 2) 给重要但看不懂的行补一句中文解释 + 处理建议。
///
/// 纯函数、无副作用，便于单元测试。
/// </summary>
public static partial class SteamCmdLogInterpreter
{
    /// <summary>错误码 / 错误编号形如 "FAILED with result code 25"、"Error code 5"。</summary>
    [GeneratedRegex(@"(?:result|error)\s*code\D{0,3}(\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex ResultCodePattern();

    /// <summary>steamcmd 内部状态码，形如 "App '480' state is 0x406 after update job"。</summary>
    [GeneratedRegex(@"state is 0x[0-9a-f]+", RegexOptions.IgnoreCase)]
    private static partial Regex HexStatePattern();

    /// <summary>上传进度行，形如 "Update state (0x61) uploading, progress: 12.34 (1234 / 10000)"。</summary>
    [GeneratedRegex(@"update state \(0x[0-9a-f]+\)", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateStatePattern();

    /// <summary>Steam 返回码（ERESULT）的常见含义与处理建议。</summary>
    private static readonly Dictionary<int, string> ResultCodeHints = new()
    {
        [2] = "通用失败（Fail）：Steam 拒绝了请求，请检查 AppID / PublishedFileID 是否正确。",
        [3] = "连不上 Steam：检查网络（或代理 / VPN）后重试。",
        [5] = "Steam 密码错误：请到下方设置栏重新填写密码后重试。",
        [6] = "该账号正在别的设备上登录：请先在其它设备退出 Steam，再重新上传。",
        [8] = "参数无效：VDF 里的字段有问题（常见于 AppID / PublishedFileID 不是纯数字，或缺少必要字段）。",
        [9] = "文件未找到：「内容文件夹」或预览图路径不存在，请检查路径后再试。",
        [10] = "Steam 正忙：稍等一会儿再重试。",
        [11] = "当前状态不允许这次操作：该项目可能正在审核中，稍后再试。",
        [15] = "没有权限（Access Denied）：常见原因是这个创意工坊项目不属于当前账号、还没同意《Steam 创意工坊法律协议》，或者 steamcmd 缓存的旧登录凭据已失效（可点「清除缓存」后重试）。",
        [16] = "操作超时：网络不稳定，稍后重试。",
        [17] = "该账号已被封禁：请到 Steam 客服页面查询账号状态。",
        [18] = "账号不存在：检查 Steam 用户名是否填写正确。",
        [20] = "Steam 服务暂时不可用：等几分钟再试。",
        [21] = "还没有登录成功：检查用户名 / 密码 / 验证码；反复失败可点「清除缓存」清掉旧凭据。",
        [22] = "上一次请求还在处理中：稍等一会儿再试。",
        [24] = "权限不足：请确认该创意工坊项目属于当前账号，且已同意创意工坊法律协议。",
        [25] = "超出限制：上传太频繁（或内容过大）被 Steam 限制，等 5～10 分钟再试，不要连点上传。",
        [26] = "登录凭据已失效：重新填写密码，必要时点「清除缓存」后重试。",
        [27] = "登录会话已过期：重新上传一次即可。",
        [29] = "重复请求：上一次提交仍在处理，稍等再试。",
        [84] = "触发了 Steam 的频率限制：等 5～10 分钟再重试。",
    };

    /// <summary>
    /// steamcmd 内部的初始化 / 退出自检信息。
    /// 这些行对用户没有任何意义，但里面有 "failed"、"Assertion Failed" 之类的字样，
    /// 容易被误当成上传失败，所以统一识别出来。
    /// </summary>
    private static readonly string[] NoiseMarkers =
    {
        "redirecting stderr to",
        "ilocalize::addfile() failed to load file",   // 缺少语言文件，steamcmd 常见且无害
        "assertion failed",
        "cworkthreadpool",
        "work queue empty",
        "src\\clientdll.cpp",
        "steam console client",
        "-- type 'quit' to exit --",
        "loading steam api",
        "unloading steam api",
        "waiting for user info",
        "connecting anonymously to steam public",
    };

    /// <summary>常见失败提示 → 中文解释（从上往下匹配，越具体越靠前）。</summary>
    private static readonly (string Needle, string Hint)[] ErrorRules =
    {
        ("access denied",
            "没有权限：确认该创意工坊项目属于当前账号、已同意《创意工坊法律协议》；也常见于 steamcmd 缓存的旧登录凭据失效（可点「清除缓存」后重试）。"),
        ("denied",
            "Steam 拒绝了这次操作：确认项目属于当前账号，必要时点「清除缓存」后重试。"),
        ("invalid password",
            "密码错误：请到下方设置栏重新填写 Steam 密码后重试。"),
        ("logged in elsewhere",
            "该账号正在其它设备上登录：请先在其它设备退出 Steam，再重新上传。"),
        ("rate limit",
            "触发了 Steam 的频率限制：上传太密集，等 5～10 分钟再试。"),
        ("no connection",
            "连不上 Steam：检查网络（或代理 / VPN）后重试。"),
        ("login failure",
            "登录失败：检查用户名 / 密码 / 验证码；反复失败可点「清除缓存」清掉旧凭据。"),
        ("steamupdater: error: download failed",
            "steamcmd 更新自己时下载失败：多为网络 / 代理问题。Valve 官方建议先在「Internet 选项 → 局域网设置」里勾选「自动检测设置」，再重试。"),
        ("steam needs to be online to update",
            "steamcmd 认为自己处于离线状态、无法完成自更新：检查网络 / 代理 / 防火墙后重试。"),
        ("steamupdater: error",
            "steamcmd 在自我更新时报错：先确认网络可用（代理拦截会导致自更新失败），再重试。"),
        ("no subscription",
            "该 AppID 需要登录账号，且账号得已拥有这份游戏（Valve 官方说明：出现 No subscription 表示需要登录，或该账号没有这份游戏）。"),
        ("failed to install app",
            "Steam 拒绝安装 / 更新该 AppID：确认 AppID 填的是 MOD 所属游戏的 ID（专用服务器可在官方 Dedicated Servers 列表里查），且当前账号拥有该游戏。"),
        ("failed to update workshop item",
            "steamcmd 更新创意工坊项目失败：真正的失败原因通常在下一行的错误码说明或「构建日志」里。"),
        ("not logged in",
            "还没有登录成功：检查用户名 / 密码，必要时点「清除缓存」后重试。"),
        ("invalid appid",
            "AppID 无效：请填纯数字的游戏 AppID（例如 4000）。"),
        ("invalid publishedfileid",
            "PublishedFileID 无效：新建项目时该字段留空或填 0，更新已有项目时才填创意工坊 ID。"),
        ("timeout",
            "操作超时：网络不稳定，稍后重试。"),
        ("disk quota",
            "超出云端存储配额：清理创意工坊项目里没用的文件后重试。"),
    };

    /// <summary>需要用户交互的提示（验证码 / 密码），不是错误但要解释清楚。</summary>
    private static readonly string[] AuthPromptMarkers =
    {
        "steam guard code",
        "auth code",
        "authenticator",
        "two-factor",
    };

    /// <summary>steamcmd 在自我更新（第一次运行或长期未用时会出现，会明显变慢）。</summary>
    private static readonly string[] SelfUpdateMarkers =
    {
        "checking for available updates",
        "downloading update",
        "update complete, launching",
        "verifying installation",
        // steamcmd 的本地化输出（中文系统上就是中文，程序中已按 UTF-8 解码）
        "正在检查可用更新",
        "正在下载更新",
        "正在验证安装",
        "更新完成",
    };

    /// <summary>解读一行 steamcmd 输出。</summary>
    public static SteamCmdLineReading Interpret(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return new SteamCmdLineReading(SteamCmdLineKind.Noise, null);

        var text = line.Trim();

        // 1) 明确的成功
        if (ContainsAny(text, "success. publishedfileid", "success. published file id", "uploaded item", "committing update...success"))
            return new SteamCmdLineReading(SteamCmdLineKind.Success, null);

        // 2) 内部自检信息（必须排在错误兜底之前：这些行里有 failed / Assertion 字样）
        if (ContainsAny(text, NoiseMarkers))
            return new SteamCmdLineReading(SteamCmdLineKind.Noise, null);

        // 3) 带数值错误码的行 —— 把编号翻译成人话
        var codeMatch = ResultCodePattern().Match(text);
        if (codeMatch.Success && int.TryParse(codeMatch.Groups[1].Value, out var code))
        {
            var hint = ResultCodeHints.TryGetValue(code, out var known)
                ? $"steamcmd 报错（result code {code}）——{known}"
                : $"steamcmd 报错：错误码 {code}。这是 Steam 的 ERESULT 编号，程序暂不认识它；" +
                  "可以点「导出日志」把这一行和下面的构建日志一起反馈。";
            return new SteamCmdLineReading(SteamCmdLineKind.Error, hint);
        }

        // 4) 十六进制状态码：用户最容易看不懂的那类（如 "App '480' state is 0x406"）
        if (HexStatePattern().IsMatch(text))
            return new SteamCmdLineReading(SteamCmdLineKind.Warning,
                "这里的 0x 开头是 steamcmd 的内部状态码，不是给用户看的错误码；真正的失败原因看下面几行或「构建日志」。");

        // 5) 常见失败提示
        foreach (var (needle, hint) in ErrorRules)
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return new SteamCmdLineReading(SteamCmdLineKind.Error, hint);

        // 6) 需要输入验证码 / 密码
        if (ContainsAny(text, AuthPromptMarkers))
            return new SteamCmdLineReading(SteamCmdLineKind.Warning,
                "Steam 要求输入验证码（Steam Guard）：程序会弹出输入框，请填入手机 / 邮箱收到的验证码。");

        if (text.EndsWith(':') && text.Contains("password", StringComparison.OrdinalIgnoreCase))
            return new SteamCmdLineReading(SteamCmdLineKind.Warning,
                "steamcmd 正在询问密码：程序会自动把设置栏里的密码填进去，日志里不会显示密码内容。");

        // 7) steamcmd 自我更新（更新后会要求重启，第二次运行才正常）
        if (text.Contains("steamcmd needs to restart", StringComparison.OrdinalIgnoreCase))
            return new SteamCmdLineReading(SteamCmdLineKind.Warning,
                "steamcmd 刚更新完自己，需要重启进程：再点一次「上传」即可。");

        if (ContainsAny(text, SelfUpdateMarkers))
            return new SteamCmdLineReading(SteamCmdLineKind.Warning,
                "steamcmd 正在自动更新自己（第一次运行或很久没用时会这样），更新完才会继续上传，可能要等几分钟。");

        // 8) 上传进度
        if (UpdateStatePattern().IsMatch(text))
            return new SteamCmdLineReading(SteamCmdLineKind.Info,
                "这行是 steamcmd 的上传进度：括号里的 0x61 之类是它的内部状态码，progress 后面的数字就是百分比。");

        // 9) 兜底：带 error / fail 字样但没能识别出来的行，至少告诉用户去哪儿找原因
        if (text.Contains("error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("fail", StringComparison.OrdinalIgnoreCase))
            return new SteamCmdLineReading(SteamCmdLineKind.Error,
                "这一行是 steamcmd 报出的失败信息，程序没能自动识别具体原因；可对照下面的「构建日志」或点「导出日志」进一步排查。");

        return new SteamCmdLineReading(SteamCmdLineKind.Info, null);
    }

    /// <summary>
    /// 从本次上传的输出里提炼一句中文结论（用于失败时直接告诉用户怎么办）。
    /// 优先取「明确错误」，其次取「需要留意」的提示；都没有则返回 null（界面用通用文案兜底）。
    /// </summary>
    public static string? Diagnose(IEnumerable<string>? lines)
    {
        if (lines == null) return null;

        var readings = lines.Select(Interpret).Where(r => r.Hint != null).ToList();
        return readings.FirstOrDefault(r => r.Kind == SteamCmdLineKind.Error)?.Hint
            ?? readings.FirstOrDefault(r => r.Kind == SteamCmdLineKind.Warning)?.Hint;
    }

    private static bool ContainsAny(string text, params string[] markers)
        => markers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
}
