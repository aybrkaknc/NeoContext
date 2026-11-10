using System.Net;
using System.Security.Cryptography;
using System.Text;
using NeoContext.Core.Dictionaries;
using NeoContext.Updates;

namespace NeoContext.Tests.Updates;

public sealed class DictionaryUpdaterTests : IDisposable
{
    private const string BaseUrl = "https://example.test/dictionaries/";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"neocontext-dic-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeHandler(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = request.RequestUri!.Segments[^1];
            return Task.FromResult(files.TryGetValue(name, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Manifest(params (string Name, string Sha)[] entries) =>
        Encoding.UTF8.GetBytes("{\"files\":[" + string.Join(",", entries.Select(e => $"{{\"name\":\"{e.Name}\",\"sha256\":\"{e.Sha}\"}}")) + "]}");

    private (DictionaryUpdater Updater, DictionaryStore Store) Create(Dictionary<string, byte[]> files)
    {
        var store = new DictionaryStore(_directory);
        return (new DictionaryUpdater(new HttpClient(new FakeHandler(files)), BaseUrl, store), store);
    }

    [Fact]
    public async Task A_file_with_a_matching_hash_is_saved_and_used_before_the_built_in_copy()
    {
        var xml = Encoding.UTF8.GetBytes("<Dictionary><Group><Text>From the web</Text></Group></Dictionary>");
        var (updater, store) = Create(new() { ["manifest.json"] = Manifest(("EnhanceMenusDic.xml", Sha(xml))), ["EnhanceMenusDic.xml"] = xml });

        var result = await updater.UpdateAsync();

        Assert.Equal(["EnhanceMenusDic.xml"], result.Value);
        using var stream = store.Open("EnhanceMenusDic.xml");
        Assert.Contains("From the web", new StreamReader(stream).ReadToEnd());
        Assert.Empty((await updater.UpdateAsync()).Value); // second run: already current
    }

    [Fact]
    public async Task A_file_with_the_wrong_hash_is_rejected_and_nothing_is_kept()
    {
        var xml = Encoding.UTF8.GetBytes("<Dictionary/>");
        var (updater, store) = Create(new() { ["manifest.json"] = Manifest(("EnhanceMenusDic.xml", new string('a', 64))), ["EnhanceMenusDic.xml"] = xml });

        var result = await updater.UpdateAsync();

        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(Path.Combine(store.WebDirectory!, "EnhanceMenusDic.xml")));
    }

    [Fact]
    public async Task Unknown_names_and_broken_xml_are_never_written()
    {
        var evil = Encoding.UTF8.GetBytes("x");
        var broken = Encoding.UTF8.GetBytes("<not closed");
        var (updater, store) = Create(new()
        {
            ["manifest.json"] = Manifest(("..\evil.txt", Sha(evil)), ("DetailedEditDic.xml", Sha(broken))),
            ["DetailedEditDic.xml"] = broken,
        });

        var result = await updater.UpdateAsync();

        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(Path.Combine(_directory, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(store.WebDirectory!, "DetailedEditDic.xml")));
    }

    [Fact]
    public void The_user_copy_wins_over_the_web_copy_and_a_new_app_version_drops_the_web_copy()
    {
        var store = new DictionaryStore(_directory);
        store.ResetWebIfAppChanged("1.0.0");
        File.WriteAllText(Path.Combine(store.WebDirectory!, "GuidInfosDic.ini"), "web");
        Directory.CreateDirectory(Path.Combine(_directory, "user"));
        File.WriteAllText(Path.Combine(_directory, "user", "GuidInfosDic.ini"), "mine");

        using (var stream = store.Open("GuidInfosDic.ini"))
        {
            Assert.Equal("mine", new StreamReader(stream).ReadToEnd());
        }

        store.ResetWebIfAppChanged("1.0.0");
        Assert.True(File.Exists(Path.Combine(store.WebDirectory!, "GuidInfosDic.ini")));
        store.ResetWebIfAppChanged("1.1.0");
        Assert.False(File.Exists(Path.Combine(store.WebDirectory!, "GuidInfosDic.ini")));
    }
}
