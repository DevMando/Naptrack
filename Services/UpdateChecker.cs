using System.Reflection;
using System.Text.Json;

namespace Naptrack.Services;

/// <summary>
/// Asks nuget.org whether a newer Naptrack has been published. Every failure path is
/// silent: no network, a rate limit, or a malformed response just means no banner.
/// </summary>
public class UpdateChecker
{
    private const string VersionIndexUrl = "https://api.nuget.org/v3-flatcontainer/naptrack/index.json";
    private const string UpdateCommand = "dotnet tool update -g Naptrack";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private static readonly HttpClient Http = new() { Timeout = RequestTimeout };

    private readonly ConfigService _config;

    static UpdateChecker()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd($"Naptrack/{CurrentVersion}");
    }

    public UpdateChecker(ConfigService config)
    {
        _config = config;
    }

    /// <summary>The newer version on nuget.org, or null when this build is current.</summary>
    public string? NewerVersion { get; private set; }

    public bool UpdateAvailable => NewerVersion is not null;

    public string Command => UpdateCommand;

    public static string CurrentVersion { get; } = ResolveCurrentVersion();

    public async Task CheckAsync(CancellationToken ct = default)
    {
        // Show what the last run found straight away, so the banner does not wait on the network.
        ApplyCandidate(_config.Config.LatestKnownVersion);

        var lastCheck = _config.Config.LastUpdateCheckUtc;
        if (lastCheck is not null && DateTimeOffset.UtcNow - lastCheck.Value < CheckInterval)
            return;

        try
        {
            var latest = await FetchLatestStableAsync(ct);
            if (latest is null)
                return;

            _config.Config.LatestKnownVersion = latest;
            _config.Config.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            await _config.SaveAsync();

            ApplyCandidate(latest);
        }
        catch
        {
            // Offline, rate limited, or nuget.org changed shape. Not worth bothering the user.
        }
    }

    private void ApplyCandidate(string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate) && IsNewer(candidate, CurrentVersion))
            NewerVersion = candidate;
    }

    private static async Task<string?> FetchLatestStableAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(VersionIndexUrl, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!document.RootElement.TryGetProperty("versions", out var versions)
            || versions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? best = null;

        foreach (var element in versions.EnumerateArray())
        {
            var value = element.GetString();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            // Skip prereleases: someone on a stable build should not be nudged onto an alpha.
            if (value.Contains('-'))
                continue;

            if (best is null || IsNewer(value, best))
                best = value;
        }

        return best;
    }

    private static bool IsNewer(string candidate, string baseline) =>
        TryParseVersion(candidate, out var left)
        && TryParseVersion(baseline, out var right)
        && left > right;

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version(0, 0, 0, 0);

        // "1.2.3-beta.1+abc123" compares on the "1.2.3" part.
        var core = value.Split('-', '+')[0].Trim();

        if (!Version.TryParse(core, out var parsed))
            return false;

        // Version treats unspecified components as -1, which would make 1.0.1 sort
        // below 1.0.1.0. Pad them so equal releases compare equal.
        version = new Version(
            parsed.Major,
            parsed.Minor,
            parsed.Build < 0 ? 0 : parsed.Build,
            parsed.Revision < 0 ? 0 : parsed.Revision);

        return true;
    }

    private static string ResolveCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly();

        var informational = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        return assembly?.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
