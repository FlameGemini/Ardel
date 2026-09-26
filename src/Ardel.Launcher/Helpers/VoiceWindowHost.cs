using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Services;
using Ardel.Launcher.Services.Voice;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Secondary window that hosts only WebView2 for the voice UI (HTML/TS→JS), not WinUI chrome.
/// </summary>
public static class VoiceWindowHost
{
    private const int HomeWidthDip = 920;
    private const int HomeHeightDip = 540;
    private const int SessionWidthDip = 900;
    private const int JoinHeightDip = 820;
    private const int LiveBaseHeightDip = 700;
    private const int LiveMemberRowDip = 52;
    private const int LiveMinHeightDip = 760;
    private const int LiveMaxHeightDip = 1280;
    private const int LiveHeightSlackDip = 96;
    /// <summary>AppWindow.Resize includes title bar; content height is client-area only.</summary>
    private const int WindowChromeHeightDip = 48;
    private const string VirtualHost = "ardel.assets";
    private const string VoicePageUrl = "https://ardel.assets/voice/index.html";

    private static readonly JsonSerializerOptions BootJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly object Gate = new();
    private static Window? _window;
    private static WebView2? _webView;
    private static VoiceSignalingServer? _signaling;
    private static CoreWebView2Environment? _env;
    private static Task<CoreWebView2Environment>? _envTask;
    private static bool _opening;
    private static bool _closing;
    private static string _layoutStep = "home";
    private static int _memberCount = 1;
    private static int? _contentHeightDip;

    public static void WarmUp() => _ = EnsureEnvironmentAsync();

    private static Task<CoreWebView2Environment> EnsureEnvironmentAsync()
    {
        if (_env is not null)
            return Task.FromResult(_env);
        return _envTask ??= CreateEnvironmentAsync();
    }

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel",
            "WebView2",
            "Voice",
            Environment.ProcessId.ToString());
        Directory.CreateDirectory(folder);

        try
        {
            // WinAppSDK CreateAsync() has no folder overload — isolate via env var.
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", folder);
            _env = await CoreWebView2Environment.CreateAsync().AsTask().ConfigureAwait(false);
            return _env;
        }
        catch (Exception ex)
        {
            _envTask = null;
            Debug.WriteLine($"[Voice] WebView2 env failed: {ex.Message}");
            throw;
        }
    }

    public static async void Show()
    {
        try
        {
            await ShowAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Voice] Show failed: {ex}");
            TearDown();
        }
    }

    public static async Task ShowAsync()
    {
        lock (Gate)
        {
            if (_window is not null)
            {
                ApplyWindowSize(_window);
                _window.Activate();
                return;
            }

            if (_opening)
                return;
            _opening = true;
        }

        try
        {
            await ShowCoreAsync().ConfigureAwait(true);
        }
        finally
        {
            lock (Gate) _opening = false;
        }
    }

    private static async Task ShowCoreAsync()
    {
        _closing = false;
        _signaling = new VoiceSignalingServer();

        var themeClass = ResolveThemeClass();
        var isLight = themeClass == "theme-light";
        var pageBg = isLight
            ? Windows.UI.Color.FromArgb(255, 0xE8, 0xEA, 0xEE)
            : Windows.UI.Color.FromArgb(255, 0x12, 0x15, 0x1A);

        var webView = new WebView2
        {
            DefaultBackgroundColor = pageBg,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _webView = webView;

        var root = new Grid { Background = new SolidColorBrush(pageBg) };
        root.Children.Add(webView);

        var window = new Window
        {
            Title = Loc.Get(LocKeys.Voice_WindowTitle),
            Content = root
        };

        // Assign before any await so a second Show() activates this window instead of opening another.
        lock (Gate) _window = window;
        window.Closed += (_, _) => TearDown();

        ApplyWindowSize(window, "home");
        window.Activate();
        ApplyWindowSize(window, "home");

        await _signaling.StartAsync().ConfigureAwait(true);

        var env = await EnsureEnvironmentAsync().ConfigureAwait(true);
        await webView.EnsureCoreWebView2Async(env).AsTask().ConfigureAwait(true);

        var core = webView.CoreWebView2;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsPinchZoomEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = true;
        core.WebMessageReceived += OnWebMessageReceived;

        var assetsDir = ResolveAssetsDirectory();
        var voiceDir = Path.Combine(assetsDir, "voice");
        var indexPath = Path.GetFullPath(Path.Combine(voiceDir, "index.html"));
        if (!File.Exists(indexPath))
            throw new FileNotFoundException("Voice UI missing.", indexPath);

        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            assetsDir,
            CoreWebView2HostResourceAccessKind.Allow);

        core.PermissionRequested += (_, args) =>
        {
            if (args.PermissionKind == CoreWebView2PermissionKind.Microphone)
            {
                args.State = CoreWebView2PermissionState.Allow;
                args.Handled = true;
            }
        };

        var bootJson = JsonSerializer.Serialize(new
        {
            ws = _signaling.LocalWsUrl,
            name = ResolveDisplayName(),
            theme = themeClass,
            i18n = BuildI18nMap()
        }, BootJsonOpts);

        await core.AddScriptToExecuteOnDocumentCreatedAsync(
                $"window.__ARDEL_VOICE__ = {bootJson};")
            .AsTask()
            .ConfigureAwait(true);

        // Prefer virtual-host https (reliable local assets). file:// is a weak fallback.
        var cacheBust = File.GetLastWriteTimeUtc(indexPath).Ticks;
        var primary = $"{VoicePageUrl}?v={cacheBust}";
        var ok = await NavigateAsync(core, primary).ConfigureAwait(true);
        if (!ok)
        {
            Debug.WriteLine("[Voice] virtual-host navigate failed; trying file://.");
            ok = await NavigateAsync(core, new Uri(indexPath).AbsoluteUri).ConfigureAwait(true);
        }

        if (!ok)
        {
            Debug.WriteLine("[Voice] file:// failed; NavigateToString + virtual base.");
            await NavigateEmbeddedAsync(core, indexPath, bootJson).ConfigureAwait(true);
        }
        else
        {
            try
            {
                await core.ExecuteScriptAsync($"window.__ARDEL_VOICE__ = {bootJson};")
                    .AsTask()
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Voice] Boot reinforce failed: {ex.Message}");
            }
        }

        ApplyWindowSize(window, "home");
    }

    private static void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var doc = JsonDocument.Parse(args.WebMessageAsJson);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                var raw = root.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return;
                using var inner = JsonDocument.Parse(raw);
                ApplyLayoutMessage(inner.RootElement);
            }
            else
            {
                ApplyLayoutMessage(root);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Voice] WebMessage failed: {ex.Message}");
        }
    }

    private static void ApplyLayoutMessage(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var typeEl) ||
            !string.Equals(typeEl.GetString(), "layout", StringComparison.OrdinalIgnoreCase))
            return;

        if (!root.TryGetProperty("step", out var stepEl))
            return;

        var step = stepEl.GetString();
        if (string.IsNullOrWhiteSpace(step))
            return;

        int? members = null;
        if (root.TryGetProperty("members", out var membersEl) &&
            membersEl.TryGetInt32(out var memberCount))
            members = memberCount;

        int? contentHeight = null;
        if (root.TryGetProperty("height", out var heightEl) &&
            heightEl.TryGetInt32(out var measured))
            contentHeight = measured;

        var window = _window;
        if (window is null)
            return;

        ApplyWindowSize(window, step, members, contentHeight);
    }

    private static async Task NavigateEmbeddedAsync(CoreWebView2 core, string indexPath, string bootJson)
    {
        var html = await File.ReadAllTextAsync(indexPath).ConfigureAwait(true);
        const string baseTag = "<base href=\"https://ardel.assets/voice/\">";
        if (html.Contains("<head>", StringComparison.OrdinalIgnoreCase))
            html = html.Replace("<head>", "<head>" + baseTag, StringComparison.OrdinalIgnoreCase);
        else
            html = baseTag + html;

        // Ensure boot even if document-created script missed this about:blank-style load.
        var bootScript = $"<script>window.__ARDEL_VOICE__ = {bootJson.Replace("</", "<\\/", StringComparison.Ordinal)};</script>";
        if (html.Contains("</head>", StringComparison.OrdinalIgnoreCase))
            html = html.Replace("</head>", bootScript + "</head>", StringComparison.OrdinalIgnoreCase);
        else
            html = bootScript + html;

        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNav(object sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            core.NavigationCompleted -= OnNav;
            ready.TrySetResult(args.IsSuccess);
        }

        core.NavigationCompleted += OnNav;
        core.NavigateToString(html);
        var finished = await Task.WhenAny(ready.Task, Task.Delay(6000)).ConfigureAwait(true);
        if (finished != ready.Task)
        {
            core.NavigationCompleted -= OnNav;
            Debug.WriteLine("[Voice] NavigateToString timeout");
            core.NavigateToString(
                "<!doctype html><meta charset=utf-8>" +
                "<body style='font:15px sans-serif;background:#12151a;color:#e6e9ed;padding:24px'>" +
                "<h1>Ardel Voice</h1><p>Failed to load voice UI.</p>" +
                $"<p style='opacity:.7;word-break:break-all'>{System.Security.SecurityElement.Escape(indexPath)}</p></body>");
        }
    }

    private static async Task<bool> NavigateAsync(CoreWebView2 core, string uri)
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNav(object sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            core.NavigationCompleted -= OnNav;
            if (!args.IsSuccess)
                Debug.WriteLine($"[Voice] Nav failed ({uri}): {args.WebErrorStatus}");
            ready.TrySetResult(args.IsSuccess);
        }

        core.NavigationCompleted += OnNav;
        core.Navigate(uri);

        var finished = await Task.WhenAny(ready.Task, Task.Delay(6000)).ConfigureAwait(true);
        if (finished != ready.Task)
        {
            core.NavigationCompleted -= OnNav;
            Debug.WriteLine($"[Voice] Nav timeout: {uri}");
            return false;
        }

        return await ready.Task.ConfigureAwait(true);
    }

    private static void ApplyWindowSize(
        Window window,
        string? step = null,
        int? members = null,
        int? contentHeightDip = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(step))
                _layoutStep = NormalizeLayoutStep(step);

            if (members.HasValue)
                _memberCount = Math.Clamp(members.Value, 0, 8);

            if (contentHeightDip.HasValue && contentHeightDip.Value > 0)
                _contentHeightDip = contentHeightDip;
            else if (_layoutStep == "home")
                _contentHeightDip = null;

            var session = IsSessionLayout(_layoutStep);
            var width = session ? SessionWidthDip : HomeWidthDip;
            var height = ResolveHeight() + WindowChromeHeightDip;

            var appWindow = window.AppWindow;
            appWindow.Resize(new SizeInt32(width, height));
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = true;
                presenter.PreferredMinimumWidth = 720;
                presenter.PreferredMinimumHeight = session ? 680 : 420;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Voice] Resize failed: {ex.Message}");
        }
    }

    private static int ResolveHeight()
    {
        if (_layoutStep == "home")
            return HomeHeightDip;

        if (_layoutStep == "join")
        {
            if (_contentHeightDip is int joinMeasured && joinMeasured > 0)
                return Math.Clamp(joinMeasured + LiveHeightSlackDip, JoinHeightDip - 40, LiveMaxHeightDip);
            return JoinHeightDip;
        }

        if (_contentHeightDip is int measured && measured > 0)
            return Math.Clamp(measured + LiveHeightSlackDip, LiveMinHeightDip, LiveMaxHeightDip);

        var count = Math.Max(1, _memberCount);
        var height = LiveBaseHeightDip + (count - 1) * LiveMemberRowDip + LiveHeightSlackDip;
        return Math.Clamp(height, LiveMinHeightDip, LiveMaxHeightDip);
    }

    private static string NormalizeLayoutStep(string step) =>
        step.Trim().ToLowerInvariant() switch
        {
            "join" => "join",
            "live" or "session" => "live",
            _ => "home"
        };

    private static bool IsSessionLayout(string step) =>
        step is "join" or "live";

    private static void TearDown()
    {
        if (_closing)
            return;
        _closing = true;

        try { _signaling?.Shutdown(); }
        catch (Exception ex) { Debug.WriteLine($"[Voice] Signaling shutdown: {ex.Message}"); }
        finally { _signaling = null; }

        var webView = _webView;
        _webView = null;
        if (webView is not null)
        {
            try { webView.Close(); }
            catch (Exception ex) { Debug.WriteLine($"[Voice] WebView close: {ex.Message}"); }
        }

        lock (Gate) _window = null;
        _layoutStep = "home";
        _memberCount = 1;
        _contentHeightDip = null;
        _closing = false;
    }

    private static string ResolveAssetsDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets");
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Assets missing: {path}");
        return path;
    }

    private static string ResolveDisplayName()
    {
        try
        {
            var settings = App.Services.GetRequiredService<SettingsService>();
            var name = settings.Load().PlayerName?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch { /* ignore */ }

        return Loc.Get(LocKeys.Voice_GuestName);
    }

    private static string ResolveThemeClass()
    {
        try
        {
            var code = App.ActiveThemeCode;
            var element = AppThemeController.ResolveElementTheme(code);
            if (element == ElementTheme.Light)
                return "theme-light";
            if (element == ElementTheme.Dark)
                return "theme-dark";

            if (App.MainWindowInstance is MainWindow mw)
                return AppThemeController.ResolveSystemIsDark(mw) ? "theme-dark" : "theme-light";
        }
        catch { /* ignore */ }

        return Application.Current.RequestedTheme == ApplicationTheme.Dark
            ? "theme-dark"
            : "theme-light";
    }

    private static Dictionary<string, string> BuildI18nMap() =>
        new()
        {
            ["title"] = Loc.Get(LocKeys.Voice_WindowTitle),
            ["tagline"] = Loc.Get(LocKeys.Voice_Tagline),
            ["displayName"] = Loc.Get(LocKeys.Voice_DisplayName),
            ["start"] = Loc.Get(LocKeys.Voice_StartSession),
            ["createRoom"] = Loc.Get(LocKeys.Voice_CreateRoom),
            ["joinRoom"] = Loc.Get(LocKeys.Voice_JoinRoom),
            ["joinLabel"] = Loc.Get(LocKeys.Voice_JoinLabel),
            ["codePlaceholder"] = Loc.Get(LocKeys.Voice_CodePlaceholder),
            ["join"] = Loc.Get(LocKeys.Voice_Join),
            ["inviteCode"] = Loc.Get(LocKeys.Voice_InviteCode),
            ["copy"] = Loc.Get(LocKeys.Voice_Copy),
            ["mute"] = Loc.Get(LocKeys.Voice_Mute),
            ["unmute"] = Loc.Get(LocKeys.Voice_Unmute),
            ["leave"] = Loc.Get(LocKeys.Voice_Leave),
            ["members"] = Loc.Get(LocKeys.Voice_Members),
            ["youSuffix"] = Loc.Get(LocKeys.Voice_YouSuffix),
            ["guest"] = Loc.Get(LocKeys.Voice_GuestName),
            ["back"] = Loc.Get(LocKeys.Action_Back),
            ["statusIdle"] = Loc.Get(LocKeys.Voice_StatusIdle),
            ["statusMic"] = Loc.Get(LocKeys.Voice_StatusMic),
            ["statusSignaling"] = Loc.Get(LocKeys.Voice_StatusSignaling),
            ["statusInSession"] = Loc.Get(LocKeys.Voice_StatusInSession),
            ["statusLeft"] = Loc.Get(LocKeys.Voice_StatusLeft),
            ["statusCopied"] = Loc.Get(LocKeys.Voice_StatusCopied),
            ["statusCopyFail"] = Loc.Get(LocKeys.Voice_StatusCopyFail),
            ["statusNeedCode"] = Loc.Get(LocKeys.Voice_StatusNeedCode),
            ["statusDisconnected"] = Loc.Get(LocKeys.Voice_StatusDisconnected),
            ["errNotFound"] = Loc.Get(LocKeys.Voice_ErrNotFound),
            ["errFull"] = Loc.Get(LocKeys.Voice_ErrFull),
            ["errGeneric"] = Loc.Get(LocKeys.Voice_ErrGeneric),
        };
}
