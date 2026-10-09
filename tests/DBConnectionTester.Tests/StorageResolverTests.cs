using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Tests;

public sealed class StorageResolverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RememberedPathWinsWhenManualCopiesShareAnIdentity(bool chooseOriginal)
    {
        using var fixture = new ResolverFixture();
        var original = await fixture.CreateLocalAsync();
        var copyPath = Path.Combine(fixture.Root, "manual-copy", "data.db");
        Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
        await using (var source = await original.OpenConnectionAsync())
        await using (var destination = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        { DataSource = copyPath, Pooling = false }.ToString()))
        {
            await destination.OpenAsync();
            source.BackupDatabase(destination);
        }
        var selected = (await SqliteApplicationStore.InspectAsync(chooseOriginal ? original.Descriptor.DatabasePath : copyPath)).Descriptor!;
        await fixture.Resolver.ActivateAsync(selected, [original.Descriptor, selected]);

        for (var restart = 0; restart < 2; restart++)
        {
            var result = await fixture.Resolver.ResolveAsync([]);
            Assert.Equal(original.Descriptor.StoreId, result.SelectedStore!.Descriptor.StoreId);
            Assert.Equal(selected.DatabasePath, result.SelectedStore.Descriptor.DatabasePath);
            Assert.Equal(selected.DatabasePath, fixture.Preferences.Value!.DatabasePath);
            Assert.False(result.RequiresSelection);
        }
    }

    [Fact]
    public async Task CreatesLocalStoreWhenNoCandidateExists()
    {
        using var fixture = new ResolverFixture();

        var result = await fixture.Resolver.ResolveAsync([]);

        Assert.NotNull(result.SelectedStore);
        Assert.Equal(StorageScope.LocalUser, result.SelectedStore.Descriptor.Scope);
        Assert.Equal(fixture.Locations.LocalDatabasePath, result.SelectedStore.Descriptor.DatabasePath);
        Assert.Equal(result.SelectedStore.Descriptor.StoreId, fixture.Preferences.Value?.StoreId);
    }

    [Fact]
    public async Task SelectsOnlyCompatibleCandidate()
    {
        using var fixture = new ResolverFixture();
        var shared = await SqliteApplicationStore.OpenOrCreateAsync(
            fixture.Locations.SharedDatabasePath,
            StorageScope.SharedMachine);

        var result = await fixture.Resolver.ResolveAsync([]);

        Assert.Equal(shared.Descriptor.StoreId, result.SelectedStore?.Descriptor.StoreId);
        Assert.False(result.RequiresSelection);
    }

    [Fact]
    public async Task RequiresSelectionForMultipleStoresWithoutPreference()
    {
        using var fixture = new ResolverFixture();
        await fixture.CreateLocalAsync();
        await fixture.CreateSharedAsync();

        var result = await fixture.Resolver.ResolveAsync([]);

        Assert.True(result.RequiresSelection);
        Assert.Null(result.SelectedStore);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public async Task ReusesRememberedStoreWhenThereIsNoNewPortableStore()
    {
        using var fixture = new ResolverFixture();
        var local = await fixture.CreateLocalAsync();
        await fixture.CreateSharedAsync();
        fixture.Preferences.Value = new StoragePreference(
            local.Descriptor.StoreId,
            local.Descriptor.DatabasePath,
            local.Descriptor.Scope,
            null);

        var result = await fixture.Resolver.ResolveAsync([]);

        Assert.Equal(local.Descriptor.StoreId, result.SelectedStore?.Descriptor.StoreId);
    }

    [Fact]
    public async Task NewPortableStoreRequiresSelectionButObservedOneDoesNotPromptAgain()
    {
        using var fixture = new ResolverFixture();
        var local = await fixture.CreateLocalAsync();
        var portable = await fixture.CreatePortableAsync();
        fixture.Preferences.Value = new StoragePreference(
            local.Descriptor.StoreId,
            local.Descriptor.DatabasePath,
            local.Descriptor.Scope,
            null);

        var first = await fixture.Resolver.ResolveAsync([]);
        Assert.True(first.RequiresSelection);

        await fixture.Resolver.ActivateAsync(local.Descriptor, first.Candidates);
        var second = await fixture.Resolver.ResolveAsync([]);

        Assert.Equal(local.Descriptor.StoreId, second.SelectedStore?.Descriptor.StoreId);
        Assert.Equal(portable.Descriptor.StoreId, fixture.Preferences.Value?.ObservedPortableStoreId);
    }

    [Fact]
    public async Task ExplicitDirectoryCreatesAndRemembersCustomStore()
    {
        using var fixture = new ResolverFixture();
        var customDirectory = Path.Combine(fixture.Root, "chosen");

        var result = await fixture.Resolver.ResolveAsync(["--data-dir", customDirectory]);

        Assert.Equal(StorageScope.Custom, result.SelectedStore?.Descriptor.Scope);
        Assert.Equal(Path.Combine(customDirectory, "data.db"), result.SelectedStore?.Descriptor.DatabasePath);
        Assert.Equal(result.SelectedStore?.Descriptor.StoreId, fixture.Preferences.Value?.StoreId);
    }

    [Fact]
    public async Task InvalidKnownStoreIsNotOverwritten()
    {
        using var fixture = new ResolverFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.Locations.LocalDatabasePath)!);
        await File.WriteAllTextAsync(fixture.Locations.LocalDatabasePath, "invalid database");

        await Assert.ThrowsAsync<ApplicationStoreException>(() => fixture.Resolver.ResolveAsync([]));

        Assert.Equal("invalid database", await File.ReadAllTextAsync(fixture.Locations.LocalDatabasePath));
    }

    private sealed class ResolverFixture : IDisposable
    {
        public ResolverFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "DBConnectionTester.Tests", Guid.NewGuid().ToString("N"));
            Locations = new StorageLocations(
                Path.Combine(Root, "local", "data.db"),
                Path.Combine(Root, "shared", "data.db"),
                Path.Combine(Root, "portable", "Data", "data.db"));
            Preferences = new MemoryPreferenceStore();
            Resolver = new StorageResolver(Locations, Preferences);
        }

        public string Root { get; }
        public StorageLocations Locations { get; }
        public MemoryPreferenceStore Preferences { get; }
        public StorageResolver Resolver { get; }

        public Task<SqliteApplicationStore> CreateLocalAsync() =>
            SqliteApplicationStore.OpenOrCreateAsync(Locations.LocalDatabasePath, StorageScope.LocalUser);

        public Task<SqliteApplicationStore> CreateSharedAsync() =>
            SqliteApplicationStore.OpenOrCreateAsync(Locations.SharedDatabasePath, StorageScope.SharedMachine);

        public Task<SqliteApplicationStore> CreatePortableAsync() =>
            SqliteApplicationStore.OpenOrCreateAsync(Locations.PortableDatabasePath, StorageScope.Portable);

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class MemoryPreferenceStore : IStoragePreferenceStore
    {
        public StoragePreference? Value { get; set; }
        public StoragePreference? Read() => Value;
        public void Write(StoragePreference preference) => Value = preference;
    }
}
