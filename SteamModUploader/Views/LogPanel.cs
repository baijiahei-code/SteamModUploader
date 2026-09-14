using System.Windows.Controls;

namespace SteamModUploader;

/// <summary>
/// 日志文本框的轻量封装：统一「追加 + 自动滚动 + 限制最大行数」，
/// 避免长时间运行后日志框无限增长拖慢界面。
/// </summary>
public sealed class LogPanel
{
    /// <summary>超过该行数即触发裁剪。</summary>
    private const int MaxLines = 3000;

    /// <summary>裁剪后保留的行数。</summary>
    private const int KeepLines = 1500;

    private readonly TextBox _box;
    private int _lineCount;

    public LogPanel(TextBox box) => _box = box;

    /// <summary>当前日志全文（供「导出日志」使用）。</summary>
    public string Text => _box.Text;

    public void Append(string line)
    {
        _box.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        _lineCount++;

        if (_lineCount > MaxLines) Trim();
        _box.ScrollToEnd();
    }

    public void Clear()
    {
        _box.Clear();
        _lineCount = 0;
    }

    private void Trim()
    {
        var text = _box.Text;
        int toDrop = _lineCount - KeepLines;
        int cut = 0;

        for (int i = 0; i < text.Length && toDrop > 0; i++)
        {
            if (text[i] == '\n')
            {
                toDrop--;
                cut = i + 1;
            }
        }
        if (cut <= 0) return;

        _box.Text = text.Substring(cut);
        _lineCount = KeepLines;
        _box.CaretIndex = _box.Text.Length;
    }
}
