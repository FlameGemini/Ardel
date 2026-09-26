using System.Diagnostics;
using CmlLib.Core.ProcessBuilder;

namespace Ardel.Launcher.Services.SkinRelay;

/// <summary>
/// Owns the loopback skin relay for one game process and the JVM flags that attach it.
/// </summary>
public sealed class SkinRelaySession : IDisposable
{
    private readonly ArdelSkinRelay _relay;
    private bool _disposed;

    private SkinRelaySession(ArdelSkinRelay relay, string agentJar)
    {
        _relay = relay;
        JvmArguments =
        [
            new MArgument($"-javaagent:{QuotePath(agentJar)}={relay.BaseUri.GetLeftPart(UriPartial.Authority)}"),
            new MArgument("-Dauthlibinjector.side=client"),
            new MArgument("-Dardel.skinRelay=1")
        ];
    }

    public IReadOnlyList<MArgument> JvmArguments { get; }

    public static async Task<SkinRelaySession?> TryStartAsync(
        HttpClient http,
        bool preferMirror,
        string playerUuid,
        string playerName,
        string skinPngPath,
        bool slimArms,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(skinPngPath))
            return null;

        try
        {
            var agent = await AuthlibAgentCache
                .ResolveAgentJarAsync(http, preferMirror, cancellationToken)
                .ConfigureAwait(false);
            var png = await File.ReadAllBytesAsync(skinPngPath, cancellationToken).ConfigureAwait(false);
            var relay = ArdelSkinRelay.Create(playerUuid, playerName, png, slimArms);
            relay.Start();
            return new SkinRelaySession(relay, agent);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinRelay] Failed to start: {ex.Message}");
            return null;
        }
    }

    public void AttachToProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispose();
            if (process.HasExited)
                Dispose();
        }
        catch
        {
            _ = Task.Run(async () =>
            {
                try { await process.WaitForExitAsync().ConfigureAwait(false); }
                catch { /* ignore */ }
                finally { Dispose(); }
            });
        }
    }

    private static string QuotePath(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _relay.Dispose();
    }
}
