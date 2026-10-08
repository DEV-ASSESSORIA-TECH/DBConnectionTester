using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Tests;

public sealed class SettingsCommitServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavesSourceAndCopiesOnlyWhenRequested(bool copy)
    {
        using var fixture = await Fixture.CreateAsync();
        var portableSeen = Guid.NewGuid();
        fixture.Preferences.Value = new StoragePreference(fixture.Source.Descriptor.StoreId,
            fixture.Source.Descriptor.DatabasePath, StorageScope.Custom, portableSeen);
        var edited = ApplicationSettings.Default with { Theme = ApplicationTheme.Dark };
        await new SettingsCommitService().SaveAsync(fixture.Source, fixture.Repository,
            ApplicationSettings.Default, edited, fixture.Target.Descriptor, copy, fixture.Preferences);
        Assert.Equal(edited, await fixture.Repository.GetAsync());
        Assert.Equal(copy ? edited : ApplicationSettings.Default, await fixture.TargetRepository.GetAsync());
        Assert.Equal(fixture.Target.Descriptor.DatabasePath, fixture.Preferences.Value!.DatabasePath);
        Assert.Equal(portableSeen, fixture.Preferences.Value.ObservedPortableStoreId);
        var locations = new StorageLocations(fixture.Source.Descriptor.DatabasePath,
            Path.Combine(fixture.Root, "shared", "data.db"), Path.Combine(fixture.Root, "portable", "data.db"));
        var restarted = await new StorageResolver(locations, fixture.Preferences).ResolveAsync([]);
        Assert.Equal(fixture.Target.Descriptor.StoreId, restarted.SelectedStore!.Descriptor.StoreId);
        Assert.Equal(copy ? edited : ApplicationSettings.Default,
            await new PersistentSettingsRepository(restarted.SelectedStore).GetAsync());
    }

    [Fact]
    public async Task FailedSchedulingRestoresBothDatabasesAndPreviousSelection()
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = new StoragePreference(fixture.Source.Descriptor.StoreId, fixture.Source.Descriptor.DatabasePath, StorageScope.Custom, null);
        fixture.Preferences.Value = initial;
        fixture.Preferences.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => new SettingsCommitService().SaveAsync(fixture.Source,
            fixture.Repository, ApplicationSettings.Default, ApplicationSettings.Default with { Theme = ApplicationTheme.Dark },
            fixture.Target.Descriptor, true, fixture.Preferences));
        Assert.Equal(ApplicationSettings.Default, await fixture.Repository.GetAsync());
        Assert.Equal(ApplicationSettings.Default, await fixture.TargetRepository.GetAsync());
        Assert.Equal(initial, fixture.Preferences.Value);
    }

    [Fact]
    public async Task ReplacedIdentityIsRejectedBeforeWritingPreferences()
    {
        using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ApplicationStoreException>(() => new SettingsCommitService().SaveAsync(fixture.Source,
            fixture.Repository, ApplicationSettings.Default, ApplicationSettings.Default with { Theme = ApplicationTheme.Dark },
            fixture.Target.Descriptor with { StoreId = Guid.NewGuid() }, true, fixture.Preferences));
        Assert.Equal(ApplicationSettings.Default, await fixture.Repository.GetAsync());
        Assert.Null(fixture.Preferences.Value);
    }

    private sealed class Preferences : IStoragePreferenceStore
    {
        public StoragePreference? Value;
        public bool Fail;
        public StoragePreference? Read() => Value;
        public void Write(StoragePreference preference)
        {
            if (Fail) throw new IOException("Scheduling failed");
            Value = preference;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root = Path.Combine(Path.GetTempPath(), "DBCT-settings-commit", Guid.NewGuid().ToString("N"));
        public SqliteApplicationStore Source = null!, Target = null!;
        public PersistentSettingsRepository Repository => new(Source);
        public PersistentSettingsRepository TargetRepository => new(Target);
        public Preferences Preferences = new();
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.Source = await SqliteApplicationStore.OpenOrCreateAsync(Path.Combine(fixture.Root, "source", "data.db"), StorageScope.Custom);
            fixture.Target = await SqliteApplicationStore.OpenOrCreateAsync(Path.Combine(fixture.Root, "target", "data.db"), StorageScope.Custom);
            return fixture;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
