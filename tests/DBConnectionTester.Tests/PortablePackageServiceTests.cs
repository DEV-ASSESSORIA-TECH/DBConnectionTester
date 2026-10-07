using System.IO.Compression;
using System.Text;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Portability;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Tests;

public sealed class PortablePackageServiceTests
{
    [Fact]
    public async Task CreatesAndRestoresPackageWithClonedIdentity()
    {
        using var fixture = await PackageFixture.CreateAsync();
        var package = fixture.PathFor("portable.zip");
        var destination = fixture.PathFor("restored");

        var created = await fixture.Service.CreateAsync(package, includeExecutable: false);
        var restored = await fixture.Service.RestoreAsync(package, destination);

        Assert.False(created.IncludesExecutable);
        Assert.NotEqual(fixture.Store.Descriptor.StoreId, created.PortableStoreId);
        Assert.Equal(created.PortableStoreId, restored.Store.StoreId);
        Assert.Equal(fixture.Store.Descriptor.StoreId, restored.Store.ClonedFromStoreId);
        Assert.Equal(StorageScope.Portable, restored.Store.Scope);
        Assert.True(File.Exists(Path.Combine(destination, "Data", "data.db")));
    }

    [Fact]
    public async Task IncludesExecutableOnlyThroughSingleFileProvider()
    {
        using var fixture = await PackageFixture.CreateAsync(withExecutable: true);
        var package = fixture.PathFor("with-exe.zip");

        var result = await fixture.Service.CreateAsync(package, includeExecutable: true);

        using var archive = ZipFile.OpenRead(package);
        Assert.True(result.IncludesExecutable);
        Assert.NotNull(archive.GetEntry("DBConnectionTester.exe"));
    }

    [Fact]
    public async Task RejectsInvalidChecksumAndOccupiedDestination()
    {
        using var fixture = await PackageFixture.CreateAsync();
        var package = fixture.PathFor("portable.zip");
        await fixture.Service.CreateAsync(package, includeExecutable: false);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            var database = archive.GetEntry("Data/data.db")!;
            database.Delete();
            var replacement = archive.CreateEntry("Data/data.db");
            await using var stream = replacement.Open();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("tampered"));
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.RestoreAsync(package, fixture.PathFor("invalid")));
        var occupied = fixture.PathFor("occupied");
        Directory.CreateDirectory(occupied);
        await File.WriteAllTextAsync(Path.Combine(occupied, "keep.txt"), "keep");
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.RestoreAsync(package, occupied));
        Assert.True(File.Exists(Path.Combine(occupied, "keep.txt")));
    }

    [Fact]
    public async Task RefusesPackageOperationsDuringActiveRun()
    {
        using var fixture = await PackageFixture.CreateAsync();
        using var lease = RunWriteLease.Acquire(fixture.Store);

        await Assert.ThrowsAsync<RunAlreadyActiveException>(() =>
            fixture.Service.CreateAsync(fixture.PathFor("blocked.zip"), includeExecutable: false));
    }

    private sealed class PackageFixture : IDisposable
    {
        private PackageFixture(string root, SqliteApplicationStore store, PortablePackageService service)
        {
            Root = root;
            Store = store;
            Service = service;
        }

        private string Root { get; }
        public SqliteApplicationStore Store { get; }
        public PortablePackageService Service { get; }
        public string PathFor(string name) => Path.Combine(Root, name);

        public static async Task<PackageFixture> CreateAsync(bool withExecutable = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "DBConnectionTester.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = await SqliteApplicationStore.OpenOrCreateAsync(Path.Combine(root, "source", "data.db"), StorageScope.LocalUser);
            IApplicationBinaryProvider provider = new FakeBinaryProvider(null);
            if (withExecutable)
            {
                var executable = Path.Combine(root, "DBConnectionTester.exe");
                await File.WriteAllTextAsync(executable, "test executable");
                provider = new FakeBinaryProvider(executable);
            }
            return new PackageFixture(root, store, new PortablePackageService(store, provider));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private sealed record FakeBinaryProvider(string? Path) : IApplicationBinaryProvider
    {
        public string? GetSingleFileExecutablePath() => Path;
    }
}
