using NeoContext.Core.Results;
using NeoContext.Registry;
using NeoContext.Registry.Ownership;

namespace NeoContext.Tests.Registry;

public sealed class OwnershipTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("neocontext-own-").FullName;
    private readonly RegistrySandbox _sandbox = new();

    public void Dispose()
    {
        _sandbox.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Without_elevation_ownership_is_refused_and_nothing_is_recorded()
    {
        if (RegistryStore.IsElevated)
        {
            return;
        }

        var service = new KeyOwnershipService(Path.Combine(_dir, "ownership.json"));
        var key = _sandbox.Create("Protected");

        Assert.Equal(ErrorKind.RequiresElevation, service.TakeOwnership(key).Error?.Kind);
        Assert.Empty(service.GetBackups());
    }

    [Fact]
    public void Elevated_take_and_restore_round_trips_the_security_descriptor()
    {
        if (!RegistryStore.IsElevated)
        {
            return;
        }

        var service = new KeyOwnershipService(Path.Combine(_dir, "ownership.json"));
        var key = _sandbox.Create("Protected");

        var backup = service.TakeOwnership(key);
        Assert.True(backup.IsSuccess, backup.Error?.Message);
        Assert.Single(service.GetBackups());

        Assert.True(service.Restore(backup.Value).IsSuccess);
        Assert.Empty(service.GetBackups());
    }

    [Fact]
    public void Merged_classes_root_is_rejected() =>
        Assert.False(new KeyOwnershipService(Path.Combine(_dir, "o.json")).TakeOwnership(RegistryPath.Parse(@"HKCR\*")).IsSuccess);
}
