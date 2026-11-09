using NeoContext.Registry;

namespace NeoContext.Tests.Registry;

public sealed class ExporterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("neocontext-export-").FullName;
    private readonly RegistrySandbox _sandbox = new();

    public void Dispose()
    {
        _sandbox.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Exports_a_key_to_a_reg_file()
    {
        var key = _sandbox.Create(@"*\shell\Backup", ("MUIVerb", "Back me up"));

        var result = new RegistryExporter(_dir).Export(key);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains("Back me up", File.ReadAllText(result.Value));
    }

    [Fact]
    public void Missing_key_fails_without_throwing() =>
        Assert.False(new RegistryExporter(_dir).Export(_sandbox.Root / "missing").IsSuccess);
}
