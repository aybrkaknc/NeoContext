using System.Net;
using System.Security.Cryptography;
using System.Text;
using NeoContext.Core.Results;
using NeoContext.Updates;

namespace NeoContext.Tests.Updates;

public sealed class UpdateTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("neocontext-update-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3, "")]
    [InlineData("1.2", 1, 2, 0, "")]
    [InlineData("2.0.1-beta.2+sha.abc", 2, 0, 1, "beta.2")]
    [InlineData("0.1.0.0", 0, 1, 0, "")]
    public void Parses_tolerant_versions(string text, int major, int minor, int patch, string pre)
    {
        Assert.Equal(new SemVersion(major, minor, patch, pre), SemVersion.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.x")]
    public void Rejects_non_versions(string text) => Assert.False(SemVersion.TryParse(text, out _));

    [Theory]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0-beta", "1.0.0")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10")]
    [InlineData("1.0.0-alpha", "1.0.0-beta")]
    [InlineData("1.9.0", "1.10.0")]
    public void Orders_versions(string lower, string higher) =>
        Assert.True(SemVersion.Parse(lower) < SemVersion.Parse(higher));

    [Fact]
    public void Parses_github_release_json_with_digest()
    {
        const string Json = """
            {"tag_name":"v0.2.0","html_url":"https://github.com/o/r/releases/tag/v0.2.0","body":"Notes",
             "assets":[{"name":"NeoContext.zip","browser_download_url":"https://example.com/a.zip","size":10,
                        "digest":"sha256:ABCDEF"},
                       {"name":"old.zip","browser_download_url":"https://example.com/b.zip","size":5}]}
            """;

        var release = GitHubReleaseClient.Parse(Json).Value;

        Assert.Equal(SemVersion.Parse("0.2.0"), release.Version);
        Assert.Equal("abcdef", release.Assets[0].Sha256);
        Assert.Null(release.Assets[1].Sha256);
    }

    [Fact]
    public async Task Checker_reports_newer_release_and_treats_no_release_as_up_to_date()
    {
        var newer = new UpdateChecker(Client(HttpStatusCode.OK, """{"tag_name":"v9.0.0","html_url":"https://x/y","assets":[]}"""), SemVersion.Parse("1.0.0"));
        var same = new UpdateChecker(Client(HttpStatusCode.OK, """{"tag_name":"v1.0.0","html_url":"https://x/y","assets":[]}"""), SemVersion.Parse("1.0.0"));
        var none = new UpdateChecker(Client(HttpStatusCode.NotFound, "{}"), SemVersion.Parse("1.0.0"));
        var down = new UpdateChecker(Client(HttpStatusCode.ServiceUnavailable, ""), SemVersion.Parse("1.0.0"));

        Assert.True((await newer.CheckAsync()).Value.IsUpdateAvailable);
        Assert.False((await same.CheckAsync()).Value.IsUpdateAvailable);
        Assert.False((await none.CheckAsync()).Value.IsUpdateAvailable);
        Assert.Equal(ErrorKind.Network, (await down.CheckAsync()).Error?.Kind);
    }

    [Fact]
    public void Schedule_is_due_after_the_interval_or_when_never_checked()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.True(UpdateSchedule.IsDue(null, 7, now));
        Assert.False(UpdateSchedule.IsDue(now.AddDays(-6), 7, now));
        Assert.True(UpdateSchedule.IsDue(now.AddDays(-7), 7, now));
    }

    [Fact]
    public async Task Download_keeps_a_file_whose_digest_matches()
    {
        var content = Encoding.UTF8.GetBytes("release payload");
        var asset = Asset("NeoContext.zip", Convert.ToHexStringLower(SHA256.HashData(content)));

        var result = await new UpdateDownloader(Http(content)).DownloadAsync(asset, _dir, SignaturePolicy.NotRequired);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(content, File.ReadAllBytes(result.Value));
    }

    [Fact]
    public async Task Download_rejects_a_mismatching_digest_and_leaves_nothing_behind()
    {
        var asset = Asset("NeoContext.zip", new string('0', 64));

        var result = await new UpdateDownloader(Http("tampered"u8.ToArray())).DownloadAsync(asset, _dir, SignaturePolicy.NotRequired);

        Assert.Equal(ErrorKind.Integrity, result.Error?.Kind);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Download_refuses_assets_without_a_digest()
    {
        var result = await new UpdateDownloader(Http([])).DownloadAsync(Asset("a.zip", null), _dir, SignaturePolicy.NotRequired);

        Assert.Equal(ErrorKind.Integrity, result.Error?.Kind);
    }

    [Fact]
    public async Task Download_requiring_a_signature_rejects_unsigned_files()
    {
        var content = "MZ not really signed"u8.ToArray();
        var asset = Asset("Setup.exe", Convert.ToHexStringLower(SHA256.HashData(content)));

        var result = await new UpdateDownloader(Http(content)).DownloadAsync(asset, _dir, SignaturePolicy.Required);

        Assert.Equal(ErrorKind.Integrity, result.Error?.Kind);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Authenticode_accepts_a_signed_executable()
    {
        var signed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        if (!File.Exists(signed))
        {
            return;
        }

        Assert.True(AuthenticodeVerifier.Verify(signed).IsSuccess);
    }

    private static ReleaseAsset Asset(string name, string? sha) => new(name, new Uri($"https://example.com/{name}"), 0, sha);

    private static GitHubReleaseClient Client(HttpStatusCode status, string body) =>
        new(new HttpClient(new StubHandler(status, Encoding.UTF8.GetBytes(body))), "owner", "repo");

    private static HttpClient Http(byte[] body) => new(new StubHandler(HttpStatusCode.OK, body));

    private sealed class StubHandler(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }
}
