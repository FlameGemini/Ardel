using HtmlAgilityPack;
using System.Xml.Linq;

namespace CmlLib.Core.Installer.Forge.Versions;

public class ForgeVersionLoader
{
    private const string MavenMetadataUrl =
        "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";

    private readonly HttpClient _httpClient;

    public ForgeVersionLoader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IEnumerable<ForgeVersion>> GetForgeVersions(string mcVersion)
    {
        // Official maven-metadata first (short timeout). BMCL per-MC index is fallback only
        // so installs don't hang when maven.minecraftforge.net is unreachable.
        try
        {
            var fromMaven = (await LoadFromMavenMetadataAsync(mcVersion, timeoutSeconds: 3)
                .ConfigureAwait(false)).ToList();
            if (fromMaven.Count > 0)
                return fromMaven;
        }
        catch
        {
            // fall through
        }

        try
        {
            var fromBmcl = (await LoadFromBmclApiAsync(mcVersion).ConfigureAwait(false)).ToList();
            if (fromBmcl.Count > 0)
                return fromBmcl;
        }
        catch
        {
            // fall through
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var url = $"https://files.minecraftforge.net/net/minecraftforge/forge/index_{mcVersion}.html";
        using var response = await _httpClient.GetAsync(url, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return findForgeVersionsInHtml(html, mcVersion);
    }

    private async Task<IEnumerable<ForgeVersion>> LoadFromMavenMetadataAsync(
        string mcVersion,
        int timeoutSeconds = 20)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var response = await _httpClient.GetAsync(MavenMetadataUrl, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var prefix = mcVersion + "-";
        var doc = XDocument.Parse(xml);
        var matched = doc
            .Descendants("version")
            .Select(e => e.Value?.Trim())
            .Where(v => !string.IsNullOrEmpty(v) &&
                        v!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(v => v!.Substring(prefix.Length))
            .Where(forge => !string.IsNullOrWhiteSpace(forge))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Reverse()
            .ToList();

        return BuildVersionsWithInstallerUrls(mcVersion, matched);
    }

    private async Task<IEnumerable<ForgeVersion>> LoadFromBmclApiAsync(string mcVersion)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var url = $"https://bmclapi2.bangbang93.com/forge/minecraft/{mcVersion}";
        using var response = await _httpClient.GetAsync(url, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var matched = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(json, "\"version\"\\s*:\\s*\"([^\"]+)\""))
        {
            var forgeVersion = match.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(forgeVersion))
                matched.Add(forgeVersion);
        }

        matched.Reverse();
        return BuildVersionsWithInstallerUrls(mcVersion, matched);
    }

    private static IEnumerable<ForgeVersion> BuildVersionsWithInstallerUrls(
        string mcVersion,
        IReadOnlyList<string> forgeVersions)
    {
        var list = new List<ForgeVersion>(forgeVersions.Count);
        foreach (var forgeVersion in forgeVersions)
        {
            var full = mcVersion + "-" + forgeVersion;
            var installerUrl =
                "https://maven.minecraftforge.net/net/minecraftforge/forge/" +
                full + "/forge-" + full + "-installer.jar";

            list.Add(new ForgeVersion(mcVersion, forgeVersion)
            {
                Files = new[]
                {
                    new ForgeVersionFile
                    {
                        Type = "installer",
                        DirectUrl = installerUrl
                    }
                }
            });
        }

        if (list.Count > 0)
            list[0].IsLatestVersion = true;
        return list;
    }

    private IEnumerable<ForgeVersion> findForgeVersionsInHtml(string html, string mcVersion)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var rows = document.DocumentNode
            .SelectNodes("//html[1]//body[1]//main[1]//div[2]//div[2]//div[2]//table[1]//tbody[1]//tr");
        if (rows == null)
            return Enumerable.Empty<ForgeVersion>();

        return rows
            .Select(node => getForgeVersion(node, mcVersion))
            .Where(node => node != null)!;
    }

    private ForgeVersion? getForgeVersion(HtmlNode node, string mcVersion)
    {
        string? forgeVersion = null;
        string? time = null;
        IEnumerable<ForgeVersionFile>? files = null;

        var tds = node.Descendants("td");
        HtmlNode? versionNode = null;
        foreach (var td in tds)
        {
            if (td.HasClass("download-version"))
            {
                forgeVersion = td.GetDirectInnerText().Trim().Split(' ')[0].Replace("\n", "").Replace("\r", "");
                versionNode = td;
            }
            if (td.HasClass("download-time"))
                time = td.InnerText.Trim();
            if (td.HasClass("download-files"))
                files = getForgeVersionFiles(td);
        }

        if (string.IsNullOrEmpty(forgeVersion))
            return null;

        var version = new ForgeVersion(mcVersion, forgeVersion)
        {
            Time = time,
            Files = files
        };
        if (versionNode != null)
            checkVersionPromo(versionNode, version);

        return version;
    }

    private void checkVersionPromo(HtmlNode node, ForgeVersion version)
    {
        foreach (var child in node.Descendants())
        {
            if (child.HasClass("promo-latest"))
                version.IsLatestVersion = true;
            if (child.HasClass("promo-recommended"))
                version.IsRecommendedVersion = true;
        }
    }

    private IEnumerable<ForgeVersionFile> getForgeVersionFiles(HtmlNode node)
    {
        var lis = node.SelectNodes("ul[1]/li");
        if (lis == null)
            return Enumerable.Empty<ForgeVersionFile>();

        var files = new List<ForgeVersionFile>();
        foreach (var li in lis)
        {
            var forgeVersionFile = new ForgeVersionFile();
            string? firstLink = null, secondLink = null;

            var firstANode = li.SelectSingleNode("a[1]");
            if (firstANode != null)
            {
                firstLink = firstANode.GetAttributeValue("href", "").Trim();
                forgeVersionFile.Type = firstANode.InnerText.Trim();
            }

            var infoTooltip = li.Descendants().FirstOrDefault(n => n.HasClass("info-tooltip"));
            if (infoTooltip != default)
            {
                try
                {
                    if (infoTooltip.ChildNodes.Count > 6)
                    {
                        forgeVersionFile.MD5 = infoTooltip.ChildNodes[2].InnerText.Trim();
                        forgeVersionFile.SHA1 = infoTooltip.ChildNodes[6].InnerText.Trim();
                    }

                    secondLink = infoTooltip
                        .Descendants("a")
                        .FirstOrDefault()?
                        .GetAttributeValue("href", "")?
                        .Trim();
                }
                catch
                {
                    // ignore tooltip parse errors
                }
            }

            if (string.IsNullOrEmpty(secondLink))
            {
                forgeVersionFile.DirectUrl = AdfocUrlInterceptor.Unwrap(firstLink);
            }
            else
            {
                forgeVersionFile.AdUrl = firstLink;
                forgeVersionFile.DirectUrl =
                    AdfocUrlInterceptor.Unwrap(secondLink) ?? AdfocUrlInterceptor.Unwrap(firstLink);
            }

            files.Add(forgeVersionFile);
        }

        return files;
    }
}
