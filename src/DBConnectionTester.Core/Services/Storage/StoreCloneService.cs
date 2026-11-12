using System.Globalization;
using DBConnectionTester.Models;
using Microsoft.Data.Sqlite;

namespace DBConnectionTester.Services.Storage;

internal static class StoreCloneService
{
    public static async Task<SqliteApplicationStore> CloneAsync(
        SqliteApplicationStore source,
        string destinationDatabasePath,
        StorageScope scope,
        CancellationToken token = default)
    {
        var fullPath = Path.GetFullPath(destinationDatabasePath);
        if (File.Exists(fullPath))
            throw new IOException("O banco de destino já existe.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        try
        {
            await using var sourceConnection = await source.OpenConnectionAsync(token);
            await using var destinationConnection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
            await destinationConnection.OpenAsync(token);
            sourceConnection.BackupDatabase(destinationConnection);

            var newStoreId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            await using var command = destinationConnection.CreateCommand();
            command.CommandText = """
                UPDATE store_metadata
                SET store_id = $storeId,
                    storage_scope = $scope,
                    cloned_from_store_id = $sourceStoreId,
                    created_at = $now,
                    last_opened_at = $now
                WHERE singleton_id = 1;
                """;
            command.Parameters.AddWithValue("$storeId", newStoreId.ToString("D"));
            command.Parameters.AddWithValue("$scope", scope.ToString());
            command.Parameters.AddWithValue("$sourceStoreId", source.Descriptor.StoreId.ToString("D"));
            command.Parameters.AddWithValue("$now", now);
            if (await command.ExecuteNonQueryAsync(token) != 1)
                throw new ApplicationStoreException("O clone não contém metadados válidos.");
            await destinationConnection.CloseAsync();

            return await SqliteApplicationStore.OpenOrCreateAsync(fullPath, scope, token: token);
        }
        catch
        {
            TryDelete(fullPath);
            TryDelete(fullPath + "-wal");
            TryDelete(fullPath + "-shm");
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
