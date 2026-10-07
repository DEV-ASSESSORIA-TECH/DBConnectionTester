using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Tests;

public sealed class StorageMigrationServiceTests
{
    [Fact]
    public async Task ClonesStoreToEmptyDestinationWithNewIdentity()
    {
        using var fixture = await MigrationFixture.CreateAsync();
        var settings = new PersistentSettingsRepository(fixture.Source);
        await settings.SaveAsync(new ApplicationSettings(ApplicationTheme.Dark, false, fixture.PathFor("legacy")));

        var clone = await fixture.Service.CloneAsync(fixture.Source, fixture.PathFor("clone"), StorageScope.Portable);
        var clonedSettings = await new PersistentSettingsRepository(clone).GetAsync();

        Assert.NotEqual(fixture.Source.Descriptor.StoreId, clone.Descriptor.StoreId);
        Assert.Equal(fixture.Source.Descriptor.StoreId, clone.Descriptor.ClonedFromStoreId);
        Assert.Equal(StorageScope.Portable, clone.Descriptor.Scope);
        Assert.Equal(ApplicationTheme.Dark, clonedSettings.Theme);
    }

    [Fact]
    public async Task CreatesEmptyStoreAndOpensExistingStore()
    {
        using var fixture = await MigrationFixture.CreateAsync();
        var created = await fixture.Service.CreateEmptyAsync(fixture.PathFor("empty"), StorageScope.Custom);
        var existing = await StorageMigrationService.UseExistingAsync(created.Descriptor.DatabasePath);

        Assert.Equal(created.Descriptor.StoreId, existing.Descriptor.StoreId);
        Assert.Equal(StorageScope.Custom, existing.Descriptor.Scope);
    }

    [Fact]
    public async Task NeverReplacesOccupiedDestinationAndHonorsRunLock()
    {
        using var fixture = await MigrationFixture.CreateAsync();
        var occupied = fixture.PathFor("occupied");
        Directory.CreateDirectory(occupied);
        await File.WriteAllTextAsync(Path.Combine(occupied, "keep.txt"), "keep");

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.Service.CloneAsync(fixture.Source, occupied, StorageScope.Custom));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(occupied, "keep.txt")));

        using var lease = RunWriteLease.Acquire(fixture.Source);
        await Assert.ThrowsAsync<RunAlreadyActiveException>(() =>
            fixture.Service.CloneAsync(fixture.Source, fixture.PathFor("blocked"), StorageScope.Custom));
    }

    [Fact]
    public async Task SharedSetupGrantsAccessBeforeCreatingDatabase()
    {
        using var fixture = await MigrationFixture.CreateAsync();
        var sharedPath = fixture.PathFor(Path.Combine("shared", "data.db"));
        var security = new RecordingSecurity();
        var setup = new SharedMachineStorageSetup(sharedPath, security);

        var store = await setup.PrepareAsync();

        Assert.Equal(Path.GetDirectoryName(sharedPath), security.Directory);
        Assert.Equal(StorageScope.SharedMachine, store.Descriptor.Scope);
        Assert.True(File.Exists(sharedPath));
    }

    [Fact]
    public async Task SharedSetupDoesNotCreateDatabaseWhenPermissionsAreDenied()
    {
        using var fixture = await MigrationFixture.CreateAsync();
        var sharedPath = fixture.PathFor(Path.Combine("denied", "data.db"));
        var setup = new SharedMachineStorageSetup(sharedPath, new DeniedSecurity());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => setup.PrepareAsync());

        Assert.False(File.Exists(sharedPath));
    }

    private sealed class RecordingSecurity : ISharedDirectorySecurity
    {
        public string? Directory { get; private set; }
        public void GrantModifyToLocalUsers(string directoryPath) => Directory = directoryPath;
    }

    private sealed class DeniedSecurity : ISharedDirectorySecurity
    {
        public void GrantModifyToLocalUsers(string directoryPath) =>
            throw new UnauthorizedAccessException("denied");
    }

    private sealed class MigrationFixture : IDisposable
    {
        private MigrationFixture(string root, SqliteApplicationStore source)
        {
            Root = root;
            Source = source;
        }

        private string Root { get; }
        public SqliteApplicationStore Source { get; }
        public StorageMigrationService Service { get; } = new();
        public string PathFor(string name) => Path.Combine(Root, name);

        public static async Task<MigrationFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "DBConnectionTester.Tests", Guid.NewGuid().ToString("N"));
            var source = await SqliteApplicationStore.OpenOrCreateAsync(Path.Combine(root, "source", "data.db"), StorageScope.LocalUser);
            return new MigrationFixture(root, source);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
