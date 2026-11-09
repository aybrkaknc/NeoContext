using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;
using NeoContext.Core.Services;

namespace NeoContext.Tests.Core;

public class ContextMenuServiceTests
{
    private const EntryCapabilities All = EntryCapabilities.Toggle | EntryCapabilities.Rename | EntryCapabilities.Delete;

    private static ContextMenuEntry Verb(string key, EntryState state = EntryState.Enabled, EntryCapabilities caps = All) =>
        new($"verb:{key}", EntryKind.ShellVerb, MenuSection.Files, key, key, state, $@"HKCR\*\shell\{key}", caps);

    [Fact]
    public void Refresh_collects_entries_and_keeps_going_when_a_provider_fails()
    {
        var good = new FakeProvider(EntryKind.ShellVerb, Verb("a"), Verb("b"));
        var bad = new FakeProvider(EntryKind.WinX) { LoadError = new Error(ErrorKind.AccessDenied, "boom") };
        var service = new ContextMenuService([good, bad], new ProtectionPolicy(true));

        var entries = service.Refresh();

        Assert.Equal(2, entries.Count);
        Assert.Single(service.LoadErrors);
    }

    [Fact]
    public void Disabling_the_open_verb_is_refused_when_protection_is_on()
    {
        var provider = new FakeProvider(EntryKind.ShellVerb);
        var service = new ContextMenuService([provider], new ProtectionPolicy(true));

        var result = service.SetState(Verb("open"), EntryState.Disabled);

        Assert.Equal(ErrorKind.Protected, result.Error?.Kind);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public void Disabling_the_open_verb_is_allowed_when_protection_is_off()
    {
        var provider = new FakeProvider(EntryKind.ShellVerb);
        var service = new ContextMenuService([provider], new ProtectionPolicy(false));

        var result = service.SetState(Verb("open"), EntryState.Disabled);

        Assert.True(result.IsSuccess);
        Assert.Equal(["SetState:open:Disabled"], provider.Calls);
    }

    [Fact]
    public void Enabling_a_protected_entry_is_always_allowed()
    {
        var provider = new FakeProvider(EntryKind.ShellVerb);
        var service = new ContextMenuService([provider], new ProtectionPolicy(true));

        Assert.True(service.SetState(Verb("open", EntryState.Disabled), EntryState.Enabled).IsSuccess);
    }

    [Fact]
    public void Setting_the_current_state_does_nothing()
    {
        var provider = new FakeProvider(EntryKind.ShellVerb);
        var service = new ContextMenuService([provider], new ProtectionPolicy(true));

        Assert.True(service.SetState(Verb("x"), EntryState.Enabled).IsSuccess);
        Assert.Empty(provider.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_rejects_empty_names(string name)
    {
        var service = new ContextMenuService([new FakeProvider(EntryKind.ShellVerb)], new ProtectionPolicy(true));

        Assert.Equal(ErrorKind.InvalidInput, service.Rename(Verb("x"), name).Error?.Kind);
    }

    [Fact]
    public void Rename_rejects_names_over_the_explorer_limit()
    {
        var service = new ContextMenuService([new FakeProvider(EntryKind.ShellVerb)], new ProtectionPolicy(true));

        var tooLong = new string('a', EntryNameRules.MaxLength + 1);

        Assert.Equal(ErrorKind.InvalidInput, service.Rename(Verb("x"), tooLong).Error?.Kind);
    }

    [Fact]
    public void Rename_trims_the_name_before_passing_it_on()
    {
        var provider = new FakeProvider(EntryKind.ShellVerb);
        var service = new ContextMenuService([provider], new ProtectionPolicy(true));

        service.Rename(Verb("x"), "  New name  ");

        Assert.Equal(["Rename:x:New name"], provider.Calls);
    }

    [Fact]
    public void Operations_without_the_capability_are_refused()
    {
        var provider = new FakeProvider(EntryKind.ShellVerb);
        var service = new ContextMenuService([provider], new ProtectionPolicy(true));
        var readOnly = Verb("x", caps: EntryCapabilities.None);

        Assert.Equal(ErrorKind.NotSupported, service.SetState(readOnly, EntryState.Disabled).Error?.Kind);
        Assert.Equal(ErrorKind.NotSupported, service.Rename(readOnly, "y").Error?.Kind);
        Assert.Equal(ErrorKind.NotSupported, service.Delete(readOnly).Error?.Kind);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public void Shortcut_open_handler_is_protected()
    {
        var policy = new ProtectionPolicy(true);
        var handler = new ContextMenuEntry("x", EntryKind.ShellExtension, MenuSection.Files,
            ProtectionPolicy.ShortcutOpenHandler.ToString("B"), "Open", EntryState.Enabled, "loc", All);

        Assert.True(policy.IsProtected(handler));
    }

    private sealed class FakeProvider(EntryKind kind, params ContextMenuEntry[] entries) : IEntryProvider
    {
        public List<string> Calls { get; } = [];

        public Error? LoadError { get; init; }

        public EntryKind Kind => kind;

        public bool ChangesRequireExplorerRestart => false;

        public Result<IReadOnlyList<ContextMenuEntry>> Load() =>
            LoadError is null ? Result<IReadOnlyList<ContextMenuEntry>>.Success(entries) : LoadError;

        public Result SetState(ContextMenuEntry entry, EntryState state) => Record($"SetState:{entry.Key}:{state}");

        public Result Rename(ContextMenuEntry entry, string newName) => Record($"Rename:{entry.Key}:{newName}");

        public Result Delete(ContextMenuEntry entry) => Record($"Delete:{entry.Key}");

        private Result Record(string call)
        {
            Calls.Add(call);
            return Result.Success();
        }
    }
}
