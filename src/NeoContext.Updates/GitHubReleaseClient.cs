using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using NeoContext.Core.Results;

namespace NeoContext.Updates;

/// <summary>A downloadable file attached to a release.</summary>
/// <param name="Sha256">Lower-case hex SHA-256 from GitHub's asset digest, or null when GitHub has none.</param>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size, string? Sha256);

/// <summary>A published release.</summary>
public sealed record ReleaseInfo(SemVersion Version, string TagName, Uri PageUrl, string Notes, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Reads the latest release of a repository from the GitHub REST API.</summary>
public sealed class GitHubReleaseClient(HttpClient http, string owner, string repository)
{
    public async Task<Result<ReleaseInfo>> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("NeoContext", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result<ReleaseInfo>.Failure(ErrorKind.NotFound, "No release has been published yet.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result<ReleaseInfo>.Failure(ErrorKind.Network, $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Parse(json);
        }
        catch (HttpRequestException ex)
        {
            return Result<ReleaseInfo>.Failure(ErrorKind.Network, ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<ReleaseInfo>.Failure(ErrorKind.Network, "The request to GitHub timed out.");
        }
    }

    /// <summary>Maps the API's JSON to <see cref="ReleaseInfo"/>. Public for tests.</summary>
    public static Result<ReleaseInfo> Parse(string json)
    {
        GitHubRelease? release;
        try
        {
            release = JsonSerializer.Deserialize<GitHubRelease>(json);
        }
        catch (JsonException ex)
        {
            return Result<ReleaseInfo>.Failure(ErrorKind.Unexpected, $"Unexpected response from GitHub: {ex.Message}");
        }

        if (release?.TagName is null || release.HtmlUrl is null || !SemVersion.TryParse(release.TagName, out var version))
        {
            return Result<ReleaseInfo>.Failure(ErrorKind.Unexpected, "The release has no usable version tag.");
        }

        var assets = (release.Assets ?? [])
            .Where(a => a.Name is not null && a.DownloadUrl is not null)
            .Select(a => new ReleaseAsset(a.Name!, a.DownloadUrl!, a.Size, ParseDigest(a.Digest)))
            .ToList();

        return new ReleaseInfo(version, release.TagName, release.HtmlUrl, release.Body ?? "", assets);
    }

    /// <summary>GitHub reports digests as "sha256:&lt;hex&gt;".</summary>
    private static string? ParseDigest(string? digest) =>
        digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..].ToLowerInvariant() : null;

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }

        [JsonPropertyName("html_url")] public Uri? HtmlUrl { get; set; }

        [JsonPropertyName("body")] public string? Body { get; set; }

        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")] public Uri? DownloadUrl { get; set; }

        [JsonPropertyName("size")] public long Size { get; set; }

        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }
}
