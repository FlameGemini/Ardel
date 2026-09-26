using System.Text;

namespace Ardel.LogViewer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var logPath = args.Length > 0 ? args[0] : string.Empty;
        if (string.IsNullOrWhiteSpace(logPath))
        {
            MessageBox.Show("Missing log path.", "Ardel Log", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Application.Run(new LogForm(logPath));
    }
}

internal sealed class LogForm : Form
{
    private static readonly Color NormalColor = Color.FromArgb(235, 235, 235);
    private static readonly Color WarnColor = Color.FromArgb(255, 204, 51);
    private static readonly Color ErrorColor = Color.FromArgb(255, 96, 96);

    private readonly string _logPath;
    private readonly RichTextBox _box;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly StringBuilder _buffer = new(64_000);
    private string _pendingLine = string.Empty;
    private FileStream? _stream;
    private StreamReader? _reader;
    private string? _openedPath;
    private long _readPosition;
    private bool _stickToBottom = true;
    private int _lineCount;
    private bool _started;

    public LogForm(string logPath)
    {
        _logPath = logPath;
        Text = "Ardel — Game log";
        Width = 920;
        Height = 580;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(480, 280);
        BackColor = Color.FromArgb(32, 32, 32);

        _box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            WordWrap = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(32, 32, 32),
            ForeColor = NormalColor,
            Font = new Font("Consolas", 10f),
            DetectUrls = false,
            HideSelection = false,
            Text = "Waiting for latest.log…"
        };
        _box.MouseUp += (_, _) => _stickToBottom = NearBottom();
        _box.KeyUp += (_, _) => _stickToBottom = NearBottom();
        Controls.Add(_box);

        _timer = new System.Windows.Forms.Timer { Interval = 300 };
        _timer.Tick += (_, _) =>
        {
            try { Pump(); }
            catch { DisposeReaders(); }
        };
        _timer.Start();

        FormClosed += (_, _) =>
        {
            _timer.Stop();
            DisposeReaders();
        };
    }

    private bool NearBottom()
    {
        try { return _box.SelectionStart >= Math.Max(0, _box.TextLength - 8); }
        catch { return true; }
    }

    private void Pump()
    {
        if (!File.Exists(_logPath))
            return;

        var info = new FileInfo(_logPath);
        if (_openedPath is null ||
            !string.Equals(_openedPath, info.FullName, StringComparison.OrdinalIgnoreCase) ||
            (_stream is not null && info.Length < _readPosition))
        {
            DisposeReaders();
            _pendingLine = string.Empty;
        }

        EnsureReader();
        if (_reader is null || _stream is null)
            return;

        var chunk = _reader.ReadToEnd();
        _readPosition = _stream.Position;
        if (string.IsNullOrEmpty(chunk))
            return;

        AppendChunk(chunk);
        if (_stickToBottom)
        {
            _box.SelectionStart = _box.TextLength;
            _box.ScrollToCaret();
        }
    }

    private void AppendChunk(string chunk)
    {
        if (!_started)
        {
            _started = true;
            _box.Clear();
            _buffer.Clear();
            _lineCount = 0;
            _pendingLine = string.Empty;
        }

        _buffer.Append(chunk);

        var combined = _pendingLine + chunk;
        var parts = combined.Split('\n');
        _pendingLine = combined.EndsWith('\n') ? string.Empty : parts[^1];

        var end = combined.EndsWith('\n') ? parts.Length : parts.Length - 1;
        for (var i = 0; i < end; i++)
        {
            var line = parts[i];
            AppendColoredLine(line);
            _lineCount++;
        }

        TrimIfNeeded();
    }

    private void TrimIfNeeded()
    {
        const int maxLines = 2500;
        if (_lineCount <= maxLines)
            return;

        var text = _buffer.ToString();
        var keepFrom = 0;
        var remaining = maxLines;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] != '\n')
                continue;
            remaining--;
            if (remaining > 0)
                continue;
            keepFrom = i + 1;
            break;
        }

        if (keepFrom <= 0)
            return;

        _buffer.Clear();
        _buffer.Append(text.AsSpan(keepFrom));
        _lineCount = maxLines;
        _pendingLine = string.Empty;

        RebuildViewFromBuffer();
    }

    private void RebuildViewFromBuffer()
    {
        _box.Clear();
        foreach (var line in _buffer.ToString().Split('\n'))
        {
            if (line.Length == 0)
                continue;
            AppendColoredLine(line);
        }
    }

    private void AppendColoredLine(string line)
    {
        var color = ClassifyLine(line) switch
        {
            LogLineKind.Error => ErrorColor,
            LogLineKind.Warning => WarnColor,
            _ => NormalColor
        };

        _box.SelectionStart = _box.TextLength;
        _box.SelectionLength = 0;
        _box.SelectionColor = color;
        _box.AppendText(line);
        _box.AppendText(Environment.NewLine);
    }

    private static LogLineKind ClassifyLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return LogLineKind.Normal;

        // Minecraft: [time] [thread/LEVEL]: ...
        if (line.Contains("/ERROR]:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(" ERROR ", StringComparison.Ordinal) ||
            line.Contains("Exception:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Caused by:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("FATAL", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Crash Report", StringComparison.OrdinalIgnoreCase))
            return LogLineKind.Error;

        if (line.Contains("/WARN]:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[WARN]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(" WARN ", StringComparison.Ordinal) ||
            line.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
            return LogLineKind.Warning;

        return LogLineKind.Normal;
    }

    private void EnsureReader()
    {
        if (_reader is not null)
            return;

        _stream = new FileStream(
            _logPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        _openedPath = Path.GetFullPath(_logPath);
        if (_stream.Length > 96_000)
            _stream.Seek(-96_000, SeekOrigin.End);
        _readPosition = _stream.Position;
        _reader = new StreamReader(_stream, detectEncodingFromByteOrderMarks: true);
    }

    private void DisposeReaders()
    {
        try { _reader?.Dispose(); } catch { /* ignore */ }
        try { _stream?.Dispose(); } catch { /* ignore */ }
        _reader = null;
        _stream = null;
        _openedPath = null;
        _readPosition = 0;
    }

    private enum LogLineKind
    {
        Normal,
        Warning,
        Error
    }
}
