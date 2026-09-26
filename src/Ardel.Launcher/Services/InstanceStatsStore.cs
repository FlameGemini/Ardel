using System.Text.Json;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

public sealed class InstanceStatsStore
{
    private const int MaxSessions = 50;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Dictionary<string, DateTime> _activeSessions = new(StringComparer.OrdinalIgnoreCase);

    public void RecordSessionStart(string versionId, string minecraftRoot)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return;

        _activeSessions[versionId] = DateTime.UtcNow;
        var data = Load(versionId, minecraftRoot);
        data.LaunchCount++;
        data.LastSessionStartUtc = DateTime.UtcNow;
        Save(versionId, minecraftRoot, data);
    }

    public void RecordSessionEnd(string versionId, string minecraftRoot, int? exitCode)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return;

        if (!_activeSessions.TryGetValue(versionId, out var startUtc))
            startUtc = DateTime.UtcNow;

        _activeSessions.Remove(versionId);
        var endUtc = DateTime.UtcNow;
        var duration = Math.Max(0, (int)(endUtc - startUtc).TotalSeconds);

        var data = Load(versionId, minecraftRoot);
        data.TotalPlaySeconds += duration;
        data.LastSessionEndUtc = endUtc;
        data.Sessions.Insert(0, new InstanceSessionRecord
        {
            StartUtc = startUtc,
            EndUtc = endUtc,
            DurationSeconds = duration,
            ExitCode = exitCode
        });

        if (data.Sessions.Count > MaxSessions)
            data.Sessions = data.Sessions.Take(MaxSessions).ToList();

        Save(versionId, minecraftRoot, data);
    }

    public InstanceStatsData Load(string versionId, string minecraftRoot)
    {
        var path = GetStatsPath(versionId, minecraftRoot);
        try
        {
            if (!File.Exists(path))
                return new InstanceStatsData();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<InstanceStatsData>(json, JsonOptions) ?? new InstanceStatsData();
        }
        catch
        {
            return new InstanceStatsData();
        }
    }

    private static void Save(string versionId, string minecraftRoot, InstanceStatsData data)
    {
        var path = GetStatsPath(versionId, minecraftRoot);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch
        {
            // ignore
        }
    }

    private static string GetStatsPath(string versionId, string minecraftRoot)
    {
        var instanceDir = GamePaths.EnsureVersionIsolation(versionId, minecraftRoot);
        return Path.Combine(instanceDir, "ardel-stats.json");
    }
}
