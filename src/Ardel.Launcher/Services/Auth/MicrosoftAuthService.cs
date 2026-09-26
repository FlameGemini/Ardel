using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Ardel.Launcher.Models;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using Microsoft.Identity.Client;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.Msal;

namespace Ardel.Launcher.Services.Auth;

/// <summary>
/// Microsoft → Xbox → Minecraft JE login via CmlLib + MSAL.
/// Tokens are cached by JELoginHandler under LocalAppData\Ardel\msa.
/// </summary>
public sealed class MicrosoftAuthService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private JELoginHandler? _handler;
    private IPublicClientApplication? _msal;

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_handler is not null && _msal is not null)
                return;

            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Ardel",
                "msa");
            Directory.CreateDirectory(cacheDir);

            _msal = await MsalClientHelper
                .BuildApplicationWithCache(MicrosoftAuthConstants.ClientId)
                .ConfigureAwait(false);

            _handler = new JELoginHandlerBuilder()
                .WithAccountManager(Path.Combine(cacheDir, "accounts.json"))
                .Build();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Interactive browser / WAM login; returns launcher session + account id for storage.</summary>
    public async Task<MicrosoftAuthResult> SignInInteractivelyAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var handler = _handler ?? throw new InvalidOperationException("MSA handler not ready.");
        var app = _msal ?? throw new InvalidOperationException("MSAL not ready.");

        var authenticator = handler.CreateAuthenticatorWithNewAccount(cancellationToken);
        authenticator.AddMsalOAuth(app, msal => msal.Interactive());
        authenticator.AddXboxAuthForJE(xbox => xbox.Basic());
        authenticator.AddJEAuthenticator();

        var session = await authenticator.ExecuteForLauncherAsync().ConfigureAwait(false);
        var account = FindAccountForSession(handler, session)
                      ?? handler.AccountManager.GetAccounts().LastOrDefault();

        return new MicrosoftAuthResult(
            session,
            account?.Identifier ?? session.UUID ?? Guid.NewGuid().ToString("N"),
            session.Username ?? string.Empty,
            NormalizeUuid(session.UUID));
    }

    /// <summary>Silent refresh for a previously linked Microsoft account. Reuses valid token when unexpired.</summary>
    public Task<MSession> AuthenticateSilentlyAsync(
        string microsoftAccountId,
        CancellationToken cancellationToken = default) =>
        AuthenticateSilentlyAsync(microsoftAccountId, forceRefresh: false, cancellationToken);

    /// <summary>Silent refresh for a previously linked Microsoft account. Optionally force full re-auth.</summary>
    public async Task<MSession> AuthenticateSilentlyAsync(
        string microsoftAccountId,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var handler = _handler ?? throw new InvalidOperationException("MSA handler not ready.");
        var app = _msal ?? throw new InvalidOperationException("MSAL not ready.");

        var account = FindAccountById(handler, microsoftAccountId)
                      ?? throw new InvalidOperationException("Microsoft account session not found. Sign in again.");

        if (!forceRefresh && account is JEGameAccount je)
        {
            if (je.Token is not null &&
                !string.IsNullOrWhiteSpace(je.Token.AccessToken) &&
                je.Token.ExpiresOn > DateTime.UtcNow.AddMinutes(15) &&
                je.Profile is not null &&
                !string.IsNullOrWhiteSpace(je.Profile.Username) &&
                !string.IsNullOrWhiteSpace(je.Profile.UUID))
            {
                return new MSession(je.Profile.Username, je.Token.AccessToken, je.Profile.UUID)
                {
                    UserType = "msa"
                };
            }
        }

        var authenticator = handler.CreateAuthenticator(account, cancellationToken);
        authenticator.AddMsalOAuth(app, msal => msal.Silent());
        authenticator.AddXboxAuthForJE(xbox => xbox.Basic());
        authenticator.AddJEAuthenticator();

        var session = await authenticator.ExecuteForLauncherAsync().ConfigureAwait(false);
        session.UserType = "msa";
        return session;
    }

    /// <summary>
    /// Fetches official Mojang profile (skin URL, arm model, current player name) directly
    /// from Mojang session server using public endpoint. Resilient and fast with timeout.
    /// </summary>
    public static async Task<(string? SkinUrl, SkinArmModel ArmModel, string? PlayerName)> FetchMojangProfileAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        var cleanUuid = NormalizeUuid(uuid);
        if (cleanUuid.Length != 32)
            return (null, SkinArmModel.Classic, null);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            var url = $"https://sessionserver.mojang.com/session/minecraft/profile/{cleanUuid}";
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (null, SkinArmModel.Classic, null);

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var playerName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

            if (root.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Array)
            {
                foreach (var prop in props.EnumerateArray())
                {
                    if (prop.TryGetProperty("name", out var pName) &&
                        string.Equals(pName.GetString(), "textures", StringComparison.OrdinalIgnoreCase) &&
                        prop.TryGetProperty("value", out var pVal))
                    {
                        var base64 = pVal.GetString();
                        if (string.IsNullOrWhiteSpace(base64))
                            continue;

                        var decodedBytes = Convert.FromBase64String(base64);
                        var decodedJson = System.Text.Encoding.UTF8.GetString(decodedBytes);
                        using var texDoc = JsonDocument.Parse(decodedJson);
                        if (texDoc.RootElement.TryGetProperty("textures", out var tex) &&
                            tex.TryGetProperty("SKIN", out var skinObj))
                        {
                            var skinUrl = skinObj.TryGetProperty("url", out var u) ? u.GetString() : null;
                            var armModel = SkinArmModel.Classic;
                            if (skinObj.TryGetProperty("metadata", out var meta) &&
                                meta.TryGetProperty("model", out var model) &&
                                string.Equals(model.GetString(), "slim", StringComparison.OrdinalIgnoreCase))
                            {
                                armModel = SkinArmModel.Slim;
                            }

                            return (skinUrl, armModel, playerName);
                        }
                    }
                }
            }

            return (null, SkinArmModel.Classic, playerName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MSA] Fetch Mojang profile failed: {ex.Message}");
            return (null, SkinArmModel.Classic, null);
        }
    }

    public async Task SignOutAsync(string? microsoftAccountId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(microsoftAccountId))
            return;

        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var handler = _handler;
        if (handler is null)
            return;

        var account = FindAccountById(handler, microsoftAccountId);
        if (account is null)
            return;

        try
        {
            await handler.Signout(account).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MSA] Signout failed: {ex.Message}");
        }
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        string microsoftAccountId,
        Func<string, HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        var session = await AuthenticateSilentlyAsync(microsoftAccountId, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var token = session.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Failed to acquire valid access token.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Ardel/1.0");
        var request = requestFactory(token);
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            session = await AuthenticateSilentlyAsync(microsoftAccountId, forceRefresh: true, cancellationToken).ConfigureAwait(false);
            token = session.AccessToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Failed to refresh access token.");

            var retryRequest = requestFactory(token);
            response = await client.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>Upload a new skin PNG to Mojang official servers.</summary>
    public async Task UploadSkinAsync(
        string microsoftAccountId,
        byte[] pngBytes,
        SkinArmModel armModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(microsoftAccountId);
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length < 64)
            throw new ArgumentException("Skin data is too small to be a valid PNG.", nameof(pngBytes));

        var variantStr = armModel == SkinArmModel.Slim ? "slim" : "classic";
        using var response = await SendAuthenticatedAsync(
            microsoftAccountId,
            token =>
            {
                var content = new MultipartFormDataContent();
                content.Add(new StringContent(variantStr), "variant");

                var fileContent = new ByteArrayContent(pngBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                content.Add(fileContent, "file", "skin.png");

                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.minecraftservices.com/minecraft/profile/skins")
                {
                    Content = content
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Upload skin failed ({response.StatusCode}): {body}");
        }
    }

    /// <summary>Fetches all capes owned by this account from Mojang.</summary>
    public async Task<IReadOnlyList<MojangCape>> GetCapesAsync(
        string microsoftAccountId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(microsoftAccountId);

        using var response = await SendAuthenticatedAsync(
            microsoftAccountId,
            token =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new InvalidOperationException("MojangRateLimit");
            }
            return Array.Empty<MojangCape>();
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("capes", out var capesArr) || capesArr.ValueKind != JsonValueKind.Array)
            return Array.Empty<MojangCape>();

        var list = new List<MojangCape>();
        foreach (var item in capesArr.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var state = item.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : null;
            var url = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
            var alias = item.TryGetProperty("alias", out var aliasProp) ? aliasProp.GetString() : null;

            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(url))
            {
                var isActive = string.Equals(state, "ACTIVE", StringComparison.OrdinalIgnoreCase);
                if (!isActive && item.TryGetProperty("active", out var activeProp))
                {
                    isActive = activeProp.ValueKind == JsonValueKind.True ||
                               (activeProp.ValueKind == JsonValueKind.String && string.Equals(activeProp.GetString(), "true", StringComparison.OrdinalIgnoreCase));
                }

                Debug.WriteLine($"[MSA] Cape: id={id}, alias={alias}, state={state}, isActive={isActive}");
                list.Add(new MojangCape(id, alias ?? "Cape", url, isActive));
            }
        }

        return list;
    }

    /// <summary>Equips a cape by ID, or un-equips / hides it if capeId is null or empty.</summary>
    public async Task SetActiveCapeAsync(
        string microsoftAccountId,
        string? capeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(microsoftAccountId);

        using var response = await SendAuthenticatedAsync(
            microsoftAccountId,
            token =>
            {
                HttpRequestMessage req;
                if (string.IsNullOrWhiteSpace(capeId))
                {
                    req = new HttpRequestMessage(HttpMethod.Delete, "https://api.minecraftservices.com/minecraft/profile/capes/active");
                }
                else
                {
                    req = new HttpRequestMessage(HttpMethod.Put, "https://api.minecraftservices.com/minecraft/profile/capes/active")
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { capeId }),
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };
                }
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Debug.WriteLine($"[MSA] SetActiveCapeAsync failed: {response.StatusCode} {body}");
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new InvalidOperationException("MojangRateLimit");
            }
            throw new InvalidOperationException($"Update cape failed ({response.StatusCode}): {body}");
        }
        else
        {
            Debug.WriteLine($"[MSA] SetActiveCapeAsync succeeded: capeId={capeId}");
        }
    }

    /// <summary>Checks if the account is currently eligible to change player name (30-day cooldown).</summary>
    public async Task<(bool Allowed, DateTimeOffset? CreatedAt)> CheckNameChangeEligibilityAsync(
        string microsoftAccountId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(microsoftAccountId);

        using var response = await SendAuthenticatedAsync(
            microsoftAccountId,
            token =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile/namechange");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return (false, null);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var allowed = root.TryGetProperty("nameChangeAllowed", out var allowedProp) && allowedProp.GetBoolean();
        DateTimeOffset? created = null;
        if (root.TryGetProperty("createdAt", out var createdProp) &&
            DateTimeOffset.TryParse(createdProp.GetString(), out var dt))
        {
            created = dt;
        }

        return (allowed, created);
    }

    /// <summary>Checks if a requested Minecraft username is available.</summary>
    public async Task<(bool Available, string Status)> CheckNameAvailabilityAsync(
        string microsoftAccountId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(microsoftAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        using var response = await SendAuthenticatedAsync(
            microsoftAccountId,
            token =>
            {
                var url = $"https://api.minecraftservices.com/minecraft/profile/name/{Uri.EscapeDataString(newName)}/available";
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return (false, response.StatusCode.ToString());

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var status = doc.RootElement.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "" : "";
        var available = string.Equals(status, "AVAILABLE", StringComparison.OrdinalIgnoreCase);
        return (available, status);
    }

    /// <summary>Changes the player name for this Minecraft Java Edition account.</summary>
    public async Task<string> ChangePlayerNameAsync(
        string microsoftAccountId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(microsoftAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        using var response = await SendAuthenticatedAsync(
            microsoftAccountId,
            token =>
            {
                var url = $"https://api.minecraftservices.com/minecraft/profile/name/{Uri.EscapeDataString(newName)}";
                var req = new HttpRequestMessage(HttpMethod.Put, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Change name failed ({response.StatusCode}): {body}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var updatedName = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
        return updatedName ?? newName;
    }

    private static IXboxGameAccount? FindAccountById(JELoginHandler handler, string id)
    {
        var cleanId = NormalizeUuid(id);
        foreach (var a in handler.AccountManager.GetAccounts())
        {
            if (string.Equals(a.Identifier, id, StringComparison.OrdinalIgnoreCase))
                return a;

            if (string.Equals(NormalizeUuid(a.Identifier), cleanId, StringComparison.OrdinalIgnoreCase))
                return a;

            if (a is JEGameAccount je &&
                string.Equals(NormalizeUuid(je.Profile?.UUID), cleanId, StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }

    private static IXboxGameAccount? FindAccountForSession(JELoginHandler handler, MSession session)
    {
        var uuid = NormalizeUuid(session.UUID);
        foreach (var account in handler.AccountManager.GetAccounts())
        {
            if (account is JEGameAccount je &&
                string.Equals(NormalizeUuid(je.Profile?.UUID), uuid, StringComparison.OrdinalIgnoreCase))
                return account;
        }

        return null;
    }

    private static string NormalizeUuid(string? uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            return string.Empty;
        return uuid.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
    }
}

public sealed record MicrosoftAuthResult(
    MSession Session,
    string AccountId,
    string Username,
    string Uuid);

public sealed record MojangCape(
    string Id,
    string Alias,
    string Url,
    bool IsActive);
