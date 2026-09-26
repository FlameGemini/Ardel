using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Views;

/// <summary>
/// Live latest.log viewer hosted inside <see cref="MainWindow"/>.
/// Must not use a second WinUI <see cref="Window"/> — that tears down the process
/// when the game exits (log churn / window lifetime).
/// </summary>
public sealed class GameLogPanel : UserControl
{
    private readonly string _logPath;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _text;
    private readonly DispatcherQueueTimer _timer;
    private readonly StringBuilder _buffer = new(48_000);
    private readonly StringBuilder _pending = new();
    private long _readPosition;
    private bool _hasOpened;
    private bool _stickToBottom = true;
    private bool _userScrolling;
    private int _lineCount;
    private bool _stopped;
    private int _pumpBusy;
    private bool _flushScheduled;

    public GameLogPanel(string instanceDirectory)
    {
        _logPath = Path.Combine(instanceDirectory, "logs", "latest.log");

        var pageBg = TryBrush("SolidBackgroundFillColorBaseBrush")
                     ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 28, 28, 28));
        var cardBg = TryBrush("LayerFillColorDefaultBrush")
                     ?? pageBg;
        var textFg = TryBrush("TextFillColorPrimaryBrush")
                     ?? new SolidColorBrush(Microsoft.UI.Colors.White);
        var stroke = TryBrush("CardStrokeColorDefaultBrush")
                     ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60));

        _text = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = textFg,
            TextWrapping = TextWrapping.NoWrap,
            IsTextSelectionEnabled = true,
            Text = Loc.Get(LocKeys.GameLog_Waiting)
        };

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            Padding = new Thickness(10),
            Content = _text,
            BringIntoViewOnFocusChange = false
        };
        _scroll.ViewChanged += OnScrollViewChanged;

        var title = new TextBlock
        {
            Text = Loc.Get(LocKeys.GameLog_Title),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var closeBtn = new Button
        {
            Content = "\uE8BB",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(closeBtn, Loc.Get(LocKeys.Action_Close));
        closeBtn.Click += (_, _) => RequestClose?.Invoke(this, EventArgs.Empty);

        var header = new Grid
        {
            Padding = new Thickness(12, 8, 8, 8),
            Background = TryBrush("CardBackgroundFillColorDefaultBrush") ?? cardBg
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(title);
        Grid.SetColumn(closeBtn, 1);
        header.Children.Add(closeBtn);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(header);
        Grid.SetRow(_scroll, 1);
        body.Children.Add(_scroll);

        // Solid chrome — no AcrylicBrush (element acrylic freezes WinUI on tick/redraw).
        Content = new Border
        {
            Background = cardBg,
            BorderBrush = stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = body,
            Width = 520,
            MinWidth = 360,
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(16, 56, 16, 16)
        };

        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        IsHitTestVisible = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(400);
        _timer.IsRepeating = true;
        _timer.Tick += OnTimerTick;
        _timer.Start();

        Unloaded += (_, _) => Stop();
    }

    public event EventHandler? RequestClose;

    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
        try { _timer.Stop(); } catch { /* ignore */ }
        try { _timer.Tick -= OnTimerTick; } catch { /* ignore */ }
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (_stopped)
            return;

        // Never ReadToEnd on the UI thread — that starves clicks while the game logs.
        if (Interlocked.CompareExchange(ref _pumpBusy, 1, 0) != 0)
            return;

        var path = _logPath;
        var from = _readPosition;
        var firstOpen = !_hasOpened;
        var dispatcher = DispatcherQueue;

        _ = Task.Run(() =>
        {
            string? chunk = null;
            long newPos = from;
            try
            {
                if (!File.Exists(path))
                    return;

                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                if (firstOpen)
                {
                    if (stream.Length > 64_000)
                        stream.Seek(-64_000, SeekOrigin.End);
                }
                else if (stream.Length < from)
                {
                    // Log rotated / truncated.
                    if (stream.Length > 64_000)
                        stream.Seek(-64_000, SeekOrigin.End);
                    else
                        stream.Seek(0, SeekOrigin.Begin);
                }
                else
                {
                    stream.Seek(from, SeekOrigin.Begin);
                }

                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                chunk = reader.ReadToEnd();
                newPos = stream.Position;
            }
            catch
            {
                // Game may briefly lock/replace the file.
            }
            finally
            {
                if (!dispatcher.TryEnqueue(() => ApplyPumpResult(firstOpen, newPos, chunk)))
                    Interlocked.Exchange(ref _pumpBusy, 0);
            }
        });
    }

    private void ApplyPumpResult(bool firstOpen, long newPos, string? chunk)
    {
        try
        {
            if (_stopped)
                return;

            if (firstOpen)
                _hasOpened = true;
            _readPosition = newPos;

            if (string.IsNullOrEmpty(chunk))
                return;

            _pending.Append(chunk);
            ScheduleFlush();
        }
        finally
        {
            Interlocked.Exchange(ref _pumpBusy, 0);
        }
    }

    private void ScheduleFlush()
    {
        if (_flushScheduled)
            return;
        _flushScheduled = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FlushPending);
    }

    private void FlushPending()
    {
        _flushScheduled = false;
        if (_stopped || _pending.Length == 0)
            return;

        var chunk = _pending.ToString();
        _pending.Clear();
        AppendChunk(chunk);

        // Skip scroll when a huge flush just landed — next quiet tick can catch up.
        if (chunk.Length > 32_000)
            return;

        if (_stickToBottom && !_userScrolling)
        {
            _scroll.UpdateLayout();
            _scroll.ChangeView(null, _scroll.ScrollableHeight, null, disableAnimation: true);
        }
    }

    private static Brush? TryBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var resource) &&
            resource is Brush brush)
            return brush;
        return null;
    }

    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate)
        {
            _userScrolling = true;
            return;
        }

        var distance = _scroll.ScrollableHeight - _scroll.VerticalOffset;
        _stickToBottom = distance < 48;
        _userScrolling = false;
    }

    private void AppendChunk(string chunk)
    {
        if (_text.Text == Loc.Get(LocKeys.GameLog_Waiting))
        {
            _buffer.Clear();
            _lineCount = 0;
        }

        _buffer.Append(chunk);
        _lineCount += CountNewlines(chunk);

        const int maxLines = 1500;
        if (_lineCount > maxLines)
        {
            var text = _buffer.ToString();
            var keepFrom = FindNthNewlineFromEnd(text, maxLines);
            if (keepFrom > 0)
            {
                _buffer.Clear();
                _buffer.Append(text.AsSpan(keepFrom));
                _lineCount = maxLines;
            }
        }

        _text.Text = _buffer.ToString();
    }

    private static int CountNewlines(string s)
    {
        var n = 0;
        foreach (var ch in s)
        {
            if (ch == '\n')
                n++;
        }

        return n;
    }

    private static int FindNthNewlineFromEnd(string text, int n)
    {
        var remaining = n;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] != '\n')
                continue;
            remaining--;
            if (remaining <= 0)
                return i + 1;
        }

        return 0;
    }
}
