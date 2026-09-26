using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Ardel.Launcher.Services.Voice;

/// <summary>
/// Loopback-only WebSocket signaling for voice SDP/ICE. Not a game relay or matchmaker.
/// </summary>
public sealed class VoiceSignalingServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public const int DefaultPort = 17865;

    public int Port { get; private set; } = DefaultPort;
    public bool IsHost { get; private set; }
    public bool IsRunning => _listener is { IsListening: true };

    public string LocalWsUrl => $"ws://127.0.0.1:{Port}/voice";

    /// <summary>
    /// Bind loopback <see cref="DefaultPort"/> as host, or attach as client if another Ardel already hosts.
    /// </summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            return Task.CompletedTask;

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{DefaultPort}/");
        try
        {
            listener.Start();
            _listener = listener;
            Port = DefaultPort;
            IsHost = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }
        catch (Exception)
        {
            try { listener.Close(); } catch { /* ignore */ }
            // Another process already owns signaling — join that instance.
            Port = DefaultPort;
            IsHost = false;
            return Task.CompletedTask;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener;
        if (listener is null)
            return;

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                // GetContextAsync does not honor CT; Abort() unblocks it on dispose.
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                if (!listener.IsListening || ct.IsCancellationRequested)
                    break;
                continue;
            }

            _ = Task.Run(() => HandleContextAsync(ctx, ct), CancellationToken.None);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            if (!ctx.Request.IsWebSocketRequest ||
                !string.Equals(ctx.Request.Url?.AbsolutePath, "/voice", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            var wsContext = await ctx.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
            var client = new Client(Guid.NewGuid(), wsContext.WebSocket);
            _clients[client.Id] = client;

            await SendAsync(client, new { type = "welcome", peerId = client.Id.ToString("N") }, ct)
                .ConfigureAwait(false);

            await ReceiveLoopAsync(client, ct).ConfigureAwait(false);
        }
        catch
        {
            // ignore disconnects
        }
    }

    private async Task ReceiveLoopAsync(Client client, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (client.Socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await client.Socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await LeaveSessionAsync(client).ConfigureAwait(false);
                        _clients.TryRemove(client.Id, out _);
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                await HandleMessageAsync(client, json, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            await LeaveSessionAsync(client).ConfigureAwait(false);
            _clients.TryRemove(client.Id, out _);
            try
            {
                if (client.Socket.State == WebSocketState.Open)
                    await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                        .ConfigureAwait(false);
            }
            catch { /* ignore */ }
        }
    }

    private async Task HandleMessageAsync(Client client, string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeEl))
            return;

        var type = typeEl.GetString();
        switch (type)
        {
            case "hello":
                client.DisplayName = root.TryGetProperty("displayName", out var n)
                    ? ClampName(n.GetString())
                    : "Guest";
                break;

            case "create":
            {
                await LeaveSessionAsync(client).ConfigureAwait(false);
                var code = NewSessionCode();
                var session = new Session(code);
                _sessions[code] = session;
                session.Members[client.Id] = client;
                client.SessionCode = code;
                await SendAsync(client, new { type = "created", code, peerId = client.Id.ToString("N") }, ct)
                    .ConfigureAwait(false);
                await BroadcastPeersAsync(session, ct).ConfigureAwait(false);
                break;
            }

            case "join":
            {
                var code = root.TryGetProperty("code", out var c) ? (c.GetString() ?? "").Trim().ToUpperInvariant() : "";
                if (code.Length == 0 || !_sessions.TryGetValue(code, out var session))
                {
                    await SendAsync(client, new { type = "error", message = "not_found" }, ct)
                        .ConfigureAwait(false);
                    break;
                }

                if (session.Members.Count >= 8)
                {
                    await SendAsync(client, new { type = "error", message = "full" }, ct)
                        .ConfigureAwait(false);
                    break;
                }

                await LeaveSessionAsync(client).ConfigureAwait(false);
                session.Members[client.Id] = client;
                client.SessionCode = code;
                await SendAsync(client, new { type = "joined", code, peerId = client.Id.ToString("N") }, ct)
                    .ConfigureAwait(false);
                await BroadcastPeersAsync(session, ct).ConfigureAwait(false);
                break;
            }

            case "signal":
            {
                if (client.SessionCode is null ||
                    !_sessions.TryGetValue(client.SessionCode, out var session))
                    break;

                if (!root.TryGetProperty("to", out var toEl))
                    break;

                var toRaw = toEl.GetString() ?? "";
                if (!Guid.TryParseExact(toRaw, "N", out var toId) &&
                    !Guid.TryParse(toRaw, out toId))
                    break;

                if (!session.Members.TryGetValue(toId, out var target))
                    break;

                JsonElement? payload = root.TryGetProperty("payload", out var p) ? p.Clone() : null;
                await SendRawAsync(
                    target,
                    JsonSerializer.Serialize(new
                    {
                        type = "signal",
                        from = client.Id.ToString("N"),
                        payload
                    }, JsonOpts)).ConfigureAwait(false);
                break;
            }

            case "leave":
                await LeaveSessionAsync(client).ConfigureAwait(false);
                break;
        }
    }

    private async Task LeaveSessionAsync(Client client)
    {
        var code = client.SessionCode;
        if (code is null)
            return;

        client.SessionCode = null;
        if (!_sessions.TryGetValue(code, out var session))
            return;

        session.Members.TryRemove(client.Id, out _);
        if (session.Members.IsEmpty)
            _sessions.TryRemove(code, out _);
        else
            await BroadcastPeersAsync(session, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task BroadcastPeersAsync(Session session, CancellationToken ct)
    {
        var peers = session.Members.Values
            .Select(m => new { id = m.Id.ToString("N"), name = m.DisplayName })
            .ToArray();

        foreach (var member in session.Members.Values)
        {
            await SendAsync(member, new { type = "peers", peers }, ct).ConfigureAwait(false);
        }
    }

    private static Task SendAsync(Client client, object payload, CancellationToken ct)
    {
        _ = ct;
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        return SendRawAsync(client, json);
    }

    private static async Task SendRawAsync(Client client, string json)
    {
        if (client.Socket.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(json);
        await client.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static string NewSessionCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> chars = stackalloc char[6];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        return new string(chars);
    }

    private static string ClampName(string? name)
    {
        name = (name ?? "Guest").Trim();
        if (name.Length == 0)
            return "Guest";
        return name.Length <= 24 ? name : name[..24];
    }

    /// <summary>Stops listening immediately so the window can close without hanging.</summary>
    public void Shutdown()
    {
        if (!IsHost)
        {
            _listener = null;
            _cts = null;
            _acceptLoop = null;
            return;
        }

        try { _cts?.Cancel(); } catch { /* ignore */ }

        var listener = _listener;
        _listener = null;
        if (listener is not null)
        {
            try { listener.Abort(); } catch { /* ignore */ }
            try { listener.Close(); } catch { /* ignore */ }
        }

        foreach (var client in _clients.Values)
        {
            try { client.Socket.Abort(); } catch { /* ignore */ }
            try { client.Socket.Dispose(); } catch { /* ignore */ }
        }

        _clients.Clear();
        _sessions.Clear();

        try { _cts?.Dispose(); } catch { /* ignore */ }
        _cts = null;
        _acceptLoop = null;
        IsHost = false;
    }

    public ValueTask DisposeAsync()
    {
        Shutdown();
        return ValueTask.CompletedTask;
    }

    private sealed class Session(string code)
    {
        public string Code { get; } = code;
        public ConcurrentDictionary<Guid, Client> Members { get; } = new();
    }

    private sealed class Client(Guid id, WebSocket socket)
    {
        public Guid Id { get; } = id;
        public WebSocket Socket { get; } = socket;
        public string DisplayName { get; set; } = "Guest";
        public string? SessionCode { get; set; }
    }
}
