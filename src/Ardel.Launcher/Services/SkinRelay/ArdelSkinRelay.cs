using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ardel.Launcher.Services.SkinRelay;

/// <summary>
/// Ardel local skin relay: loopback host that speaks the public authlib-injector
/// profile contract while exposing Ardel-specific texture paths and a stable keypair.
/// </summary>
internal sealed class ArdelSkinRelay : IDisposable
{
    private static readonly JsonSerializerOptions JsonUtf8 = new()
    {
        PropertyNamingPolicy = null
    };

    private readonly HttpListener _http = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly RSA _signingKey;
    private readonly byte[] _skinBytes;
    private readonly string _skinToken;
    private readonly string _uuidCompact;
    private readonly string _userName;
    private readonly bool _slimModel;
    private readonly string _texturePath;
    private Task? _acceptLoop;
    private bool _disposed;

    private ArdelSkinRelay(
        RSA signingKey,
        string uuid,
        string userName,
        byte[] skinBytes,
        bool slimModel,
        int port)
    {
        _signingKey = signingKey;
        _userName = userName;
        _uuidCompact = uuid.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        _skinBytes = skinBytes;
        _slimModel = slimModel;
        _skinToken = Convert.ToHexString(SHA256.HashData(skinBytes))[..20].ToLowerInvariant();
        _texturePath = $"/ardel/skin/{_skinToken}.png";

        Port = port;
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        _http.Prefixes.Add(BaseUri.AbsoluteUri);
    }

    public int Port { get; }
    public Uri BaseUri { get; }

    public static ArdelSkinRelay Create(string uuid, string userName, byte[] skinPng, bool slimModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uuid);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentNullException.ThrowIfNull(skinPng);

        var port = ReserveLoopbackPort();
        var key = LoadOrCreateSigningKey();
        return new ArdelSkinRelay(key, uuid, userName.Trim(), skinPng, slimModel, port);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _http.Start();
        _acceptLoop = Task.Run(() => AcceptAsync(_lifetime.Token));
    }

    private async Task AcceptAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _http.GetContextAsync().WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => DispatchAsync(context), CancellationToken.None);
        }
    }

    private async Task DispatchAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var method = context.Request.HttpMethod ?? "GET";

            switch ((method, path))
            {
                case ("GET", "/") or ("GET", ""):
                    await WriteJsonAsync(context, BuildRootDocument()).ConfigureAwait(false);
                    return;
                case ("GET", "/ardel/v1/ping"):
                    await WriteJsonAsync(context, new Dictionary<string, object?>
                    {
                        ["ok"] = true,
                        ["relay"] = "ardel-skin",
                        ["user"] = _userName
                    }).ConfigureAwait(false);
                    return;
                case ("GET", "/status"):
                    await WriteJsonAsync(context, new Dictionary<string, object?>
                    {
                        ["user.count"] = 1,
                        ["token.count"] = 0,
                        ["pendingAuthentication.count"] = 0
                    }).ConfigureAwait(false);
                    return;
                case ("POST", "/api/profiles/minecraft"):
                    await WriteJsonAsync(context, new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["id"] = _uuidCompact,
                            ["name"] = _userName
                        }
                    }).ConfigureAwait(false);
                    return;
                case ("POST", "/sessionserver/session/minecraft/join"):
                    await WriteStatusAsync(context, 204).ConfigureAwait(false);
                    return;
            }

            if (method == "GET" &&
                path.StartsWith("/sessionserver/session/minecraft/hasJoined", StringComparison.Ordinal))
            {
                var name = context.Request.QueryString["username"];
                if (string.Equals(name, _userName, StringComparison.Ordinal))
                    await WriteJsonAsync(context, BuildSignedProfile()).ConfigureAwait(false);
                else
                    await WriteStatusAsync(context, 204).ConfigureAwait(false);
                return;
            }

            if (method == "GET" &&
                path.StartsWith("/sessionserver/session/minecraft/profile/", StringComparison.Ordinal))
            {
                var id = path["/sessionserver/session/minecraft/profile/".Length..];
                if (string.Equals(id, _uuidCompact, StringComparison.OrdinalIgnoreCase))
                    await WriteJsonAsync(context, BuildSignedProfile()).ConfigureAwait(false);
                else
                    await WriteStatusAsync(context, 204).ConfigureAwait(false);
                return;
            }

            if (method == "GET" &&
                string.Equals(path, _texturePath, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 200;
                context.Response.ContentType = "image/png";
                context.Response.Headers["X-Ardel-Skin-Relay"] = "1";
                context.Response.Headers["Cache-Control"] = "public, max-age=604800";
                context.Response.ContentLength64 = _skinBytes.Length;
                await context.Response.OutputStream.WriteAsync(_skinBytes).ConfigureAwait(false);
                context.Response.Close();
                return;
            }

            await WriteStatusAsync(context, 404).ConfigureAwait(false);
        }
        catch
        {
            try { context.Response.Abort(); } catch { /* ignore */ }
        }
    }

    private Dictionary<string, object?> BuildRootDocument() => new()
    {
        ["signaturePublickey"] = ExportPemPublicKey(_signingKey),
        ["skinDomains"] = new[] { "127.0.0.1", "localhost" },
        ["meta"] = new Dictionary<string, object?>
        {
            ["serverName"] = "Ardel Skin Relay",
            ["implementationName"] = "ardel-skin-relay",
            ["implementationVersion"] = "2026.1",
            ["feature.non_email_login"] = true,
            ["feature.ardel_skin_relay"] = true
        }
    };

    private Dictionary<string, object?> BuildSignedProfile()
    {
        var skinEntry = new Dictionary<string, object?>
        {
            ["url"] = $"{BaseUri.GetLeftPart(UriPartial.Authority)}{_texturePath}"
        };
        if (_slimModel)
            skinEntry["metadata"] = new Dictionary<string, object?> { ["model"] = "slim" };

        // Keep Mojang texture property field names (required by the client).
        var textureBlob = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["profileId"] = _uuidCompact,
            ["profileName"] = _userName,
            ["textures"] = new Dictionary<string, object?>
            {
                ["SKIN"] = skinEntry
            }
        };

        var encoded = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(textureBlob)));
        var signature = Convert.ToBase64String(
            _signingKey.SignData(
                Encoding.UTF8.GetBytes(encoded),
                HashAlgorithmName.SHA1,
                RSASignaturePadding.Pkcs1));

        return new Dictionary<string, object?>
        {
            ["id"] = _uuidCompact,
            ["name"] = _userName,
            ["properties"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "textures",
                    ["value"] = encoded,
                    ["signature"] = signature
                }
            }
        };
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static RSA LoadOrCreateSigningKey()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel",
            "runtime");
        Directory.CreateDirectory(dir);
        var keyPath = Path.Combine(dir, "skin-relay.rsa.pkcs8");

        var rsa = RSA.Create(2048);
        try
        {
            if (File.Exists(keyPath))
            {
                rsa.ImportPkcs8PrivateKey(File.ReadAllBytes(keyPath), out _);
                return rsa;
            }
        }
        catch
        {
            rsa.Dispose();
            rsa = RSA.Create(2048);
        }

        File.WriteAllBytes(keyPath, rsa.ExportPkcs8PrivateKey());
        return rsa;
    }

    private static string ExportPemPublicKey(RSA rsa)
    {
        var der = rsa.ExportSubjectPublicKeyInfo();
        var b64 = Convert.ToBase64String(der);
        var sb = new StringBuilder(b64.Length + 64);
        sb.Append("-----BEGIN PUBLIC KEY-----\n");
        for (var i = 0; i < b64.Length; i += 64)
        {
            sb.Append(b64.AsSpan(i, Math.Min(64, b64.Length - i)));
            sb.Append('\n');
        }

        sb.Append("-----END PUBLIC KEY-----");
        return sb.ToString();
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, object body)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonUtf8);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers["X-Ardel-Skin-Relay"] = "1";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    private static Task WriteStatusAsync(HttpListenerContext context, int status)
    {
        context.Response.StatusCode = status;
        context.Response.Close();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _lifetime.Cancel(); } catch { /* ignore */ }
        try { _http.Stop(); } catch { /* ignore */ }
        try { _http.Close(); } catch { /* ignore */ }
        _signingKey.Dispose();
        _lifetime.Dispose();
    }
}
