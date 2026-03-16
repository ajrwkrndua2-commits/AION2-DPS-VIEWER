using System.Net.Http;
using System.Text.Json;

namespace Aion2Dashboard.Services;

public sealed class UpdateCheckerService
{
    private static readonly HttpClient HttpClient = CreateClient();

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(cancellationToken);
        if (release is null || string.IsNullOrWhiteSpace(release.Value.TagName))
        {
            return UpdateCheckResult.NoUpdate(AppVersion.Current, "릴리즈 정보를 찾지 못했습니다.");
        }

        var latestVersion = NormalizeVersion(release.Value.TagName);
        var currentVersion = NormalizeVersion(AppVersion.Current);
        var isUpdateAvailable = latestVersion > currentVersion;

        return new UpdateCheckResult(
            isUpdateAvailable,
            release.Value.TagName.Trim(),
            release.Value.ReleaseUrl.Trim(),
            isUpdateAvailable
                ? "새 버전이 있습니다."
                : $"최신 버전입니다. v{AppVersion.Current}");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DPSVIEWER/1.0.8");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static async Task<(string TagName, string ReleaseUrl)?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        var latestUri = $"https://api.github.com/repos/{AppVersion.Repository}/releases/latest";
        try
        {
            using var latestResponse = await HttpClient.GetAsync(latestUri, cancellationToken);
            if (latestResponse.IsSuccessStatusCode)
            {
                await using var latestStream = await latestResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var latestDocument = await JsonDocument.ParseAsync(latestStream, cancellationToken: cancellationToken);
                var latestRoot = latestDocument.RootElement;
                var latestTag = latestRoot.TryGetProperty("tag_name", out var latestTagProperty)
                    ? latestTagProperty.GetString()
                    : null;
                var latestUrl = latestRoot.TryGetProperty("html_url", out var latestUrlProperty)
                    ? latestUrlProperty.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(latestTag))
                {
                    return (latestTag, latestUrl ?? string.Empty);
                }
            }
        }
        catch
        {
        }

        var releasesUri = $"https://api.github.com/repos/{AppVersion.Repository}/releases";
        using var response = await HttpClient.GetAsync(releasesUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        foreach (var release in document.RootElement.EnumerateArray())
        {
            var isDraft = release.TryGetProperty("draft", out var draftProperty) && draftProperty.GetBoolean();
            if (isDraft)
            {
                continue;
            }

            var tagName = release.TryGetProperty("tag_name", out var tagProperty)
                ? tagProperty.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            var releaseUrl = release.TryGetProperty("html_url", out var urlProperty)
                ? urlProperty.GetString()
                : null;

            return (tagName, releaseUrl ?? string.Empty);
        }

        return null;
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

public sealed record UpdateCheckResult(bool IsUpdateAvailable, string LatestVersion, string ReleaseUrl, string Message)
{
    public static UpdateCheckResult NoUpdate(string currentVersion, string message) =>
        new(false, currentVersion, string.Empty, message);
}
