using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using Microsoft.Data.Sqlite;

namespace DBConnectionTester.Tests;

public sealed class ApplicationStoreTests
{
    [Fact]
    public async Task CreatesVersionedStoreWithRequiredSchema()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "data.db");

        try
        {
            var store = await SqliteApplicationStore.OpenOrCreateAsync(path, StorageScope.LocalUser);

            Assert.Equal(SqliteApplicationStore.CurrentSchemaVersion, store.Descriptor.SchemaVersion);
            Assert.Equal(StorageScope.LocalUser, store.Descriptor.Scope);
            Assert.Equal(Path.GetFullPath(path), store.Descriptor.DatabasePath);
            Assert.NotEqual(Guid.Empty, store.Descriptor.StoreId);
            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(1L, await ScalarLongAsync(connection, "PRAGMA foreign_keys;"));
            Assert.Equal("wal", ((string)(await ScalarAsync(connection, "PRAGMA journal_mode;"))!).ToLowerInvariant());
            Assert.Equal(8L, await ScalarLongAsync(connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OpeningExistingStoreIsIdempotentAndPreservesIdentity()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "data.db");

        try
        {
            var first = await SqliteApplicationStore.OpenOrCreateAsync(path, StorageScope.Portable);
            var second = await SqliteApplicationStore.OpenOrCreateAsync(path, StorageScope.LocalUser);

            Assert.Equal(first.Descriptor.StoreId, second.Descriptor.StoreId);
            Assert.Equal(StorageScope.Portable, second.Descriptor.Scope);
            Assert.True(second.Descriptor.LastOpenedAt >= first.Descriptor.LastOpenedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectionRejectsFutureSchemaWithoutChangingIt()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "data.db");

        try
        {
            await SqliteApplicationStore.OpenOrCreateAsync(path, StorageScope.Custom);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA user_version = {SqliteApplicationStore.CurrentSchemaVersion + 1};";
                await command.ExecuteNonQueryAsync();
            }

            var inspection = await SqliteApplicationStore.InspectAsync(path);

            Assert.Equal(StoreInspectionStatus.FutureVersion, inspection.Status);
            await Assert.ThrowsAsync<ApplicationStoreException>(() =>
                SqliteApplicationStore.OpenOrCreateAsync(path, StorageScope.Custom));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectionRejectsUnrelatedOrCorruptFiles()
    {
        var directory = CreateTemporaryDirectory();
        var sqlitePath = Path.Combine(directory, "unrelated.db");
        var corruptPath = Path.Combine(directory, "corrupt.db");

        try
        {
            await using (var connection = new SqliteConnection($"Data Source={sqlitePath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE unrelated (id INTEGER PRIMARY KEY);";
                await command.ExecuteNonQueryAsync();
            }
            await File.WriteAllTextAsync(corruptPath, "not a sqlite database");

            Assert.Equal(StoreInspectionStatus.Invalid,
                (await SqliteApplicationStore.InspectAsync(sqlitePath)).Status);
            Assert.Equal(StoreInspectionStatus.Invalid,
                (await SqliteApplicationStore.InspectAsync(corruptPath)).Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql) =>
        Convert.ToInt64(await ScalarAsync(connection, sql));

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "DBConnectionTester.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
