using System.Net.Http;
using System.Text.Json;

namespace Aion2Dashboard.Services;

public sealed class UpdateCheckerService
{
    private static readonly HttpClient HttpClient = CreateClient();

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var requestUri = $"https://api.github.com/repos/{AppVersion.Repository}/releases/latest";
        using var response = await HttpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var tagProperty)
            ? tagProperty.GetString()
            : null;
        var releaseUrl = root.TryGetProperty("html_url", out var urlProperty)
            ? urlProperty.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(tagName))
        {
            return UpdateCheckResult.NoUpdate(AppVersion.Current);
        }

        var latestVersion = NormalizeVersion(tagName);
        var currentVersion = NormalizeVersion(AppVersion.Current);
        var isUpdateAvailable = latestVersion > currentVersion;

        return new UpdateCheckResult(
            isUpdateAvailable,
            tagName.Trim(),
            releaseUrl?.Trim() ?? string.Empty);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DPSVIEWER/1.0.2");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Version NormalizeVersion(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            text = text[1..];
        }

        return Version.TryParse(text, out var version)
            ? version
            : new Version(0, 0, 0);
    }
}

public sealed record UpdateCheckResult(bool IsUpdateAvailable, string LatestVersion, string ReleaseUrl)
{
    public static UpdateCheckResult NoUpdate(string currentVersion) =>
        new(false, currentVersion, string.Empty);
}
