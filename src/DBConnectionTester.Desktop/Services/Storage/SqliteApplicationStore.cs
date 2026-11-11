using System.Globalization;
using DBConnectionTester.Models;
using Microsoft.Data.Sqlite;

namespace DBConnectionTester.Services.Storage;

public sealed class SqliteApplicationStore
{
    public const int CurrentSchemaVersion = 1;
    private const int BusyTimeoutMilliseconds = 5_000;

    private SqliteApplicationStore(StoreDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public StoreDescriptor Descriptor { get; }

    public static async Task<SqliteApplicationStore> OpenOrCreateAsync(
        string databasePath,
        StorageScope scope,
        Guid? clonedFromStoreId = null,
        CancellationToken token = default)
    {
        var fullPath = NormalizePath(databasePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ApplicationStoreException("Não foi possível determinar a pasta do banco da aplicação.");
        Directory.CreateDirectory(directory);

        var wasEmpty = !File.Exists(fullPath) || new FileInfo(fullPath).Length == 0;
        try
        {
            await using var connection = await OpenConfiguredConnectionAsync(fullPath, readOnly: false, token);
            var version = await ReadSchemaVersionAsync(connection, token);
            if (version > CurrentSchemaVersion)
                throw new ApplicationStoreException(
                    $"O banco usa o esquema {version}, mais recente que o suportado ({CurrentSchemaVersion}).");

            if (version == 0)
            {
                if (!wasEmpty && !await HasMetadataTableAsync(connection, token))
                    throw new ApplicationStoreException("O arquivo SQLite não é um banco do DB Connection Tester.");
                await ApplyInitialMigrationAsync(connection, scope, clonedFromStoreId, token);
            }

            var descriptor = await ReadAndTouchDescriptorAsync(connection, fullPath, token);
            return new SqliteApplicationStore(descriptor);
        }
        catch (ApplicationStoreException)
        {
            throw;
        }
        catch (SqliteException exception)
        {
            throw new ApplicationStoreException("Não foi possível abrir ou migrar o banco da aplicação.", exception);
        }
    }

    public static async Task<StoreInspection> InspectAsync(
        string databasePath,
        CancellationToken token = default)
    {
        var fullPath = NormalizePath(databasePath);
        if (!File.Exists(fullPath))
            return new StoreInspection(fullPath, StoreInspectionStatus.Missing, null, "");

        try
        {
            await using var connection = await OpenConfiguredConnectionAsync(fullPath, readOnly: true, token);
            var version = await ReadSchemaVersionAsync(connection, token);
            if (version > CurrentSchemaVersion)
            {
                return new StoreInspection(
                    fullPath,
                    StoreInspectionStatus.FutureVersion,
                    null,
                    $"Esquema {version}; esta versão suporta até {CurrentSchemaVersion}.");
            }

            if (version <= 0 || !await HasMetadataTableAsync(connection, token))
                return new StoreInspection(fullPath, StoreInspectionStatus.Invalid, null,
                    "O arquivo não contém metadados reconhecidos da aplicação.");

            var descriptor = await ReadDescriptorAsync(connection, fullPath, version, token);
            return new StoreInspection(fullPath, StoreInspectionStatus.Compatible, descriptor, "");
        }
        catch (Exception exception) when (exception is SqliteException or FormatException or InvalidOperationException)
        {
            return new StoreInspection(fullPath, StoreInspectionStatus.Invalid, null, exception.Message);
        }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken token = default) =>
        await OpenConfiguredConnectionAsync(Descriptor.DatabasePath, readOnly: false, token);

    private static async Task ApplyInitialMigrationAsync(
        SqliteConnection connection,
        StorageScope scope,
        Guid? clonedFromStoreId,
        CancellationToken token)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        try
        {
            await ExecuteAsync(connection, transaction, SchemaSql, token);
            var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            await using var metadata = connection.CreateCommand();
            metadata.Transaction = transaction;
            metadata.CommandText = """
                INSERT INTO store_metadata
                    (singleton_id, store_id, storage_scope, cloned_from_store_id, created_at, last_opened_at)
                VALUES
                    (1, $storeId, $scope, $clonedFrom, $createdAt, $lastOpenedAt);
                """;
            metadata.Parameters.AddWithValue("$storeId", Guid.NewGuid().ToString("D"));
            metadata.Parameters.AddWithValue("$scope", scope.ToString());
            metadata.Parameters.AddWithValue("$clonedFrom",
                clonedFromStoreId is null ? DBNull.Value : clonedFromStoreId.Value.ToString("D"));
            metadata.Parameters.AddWithValue("$createdAt", now);
            metadata.Parameters.AddWithValue("$lastOpenedAt", now);
            await metadata.ExecuteNonQueryAsync(token);
            await ExecuteAsync(connection, transaction, $"PRAGMA user_version = {CurrentSchemaVersion};", token);
            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<StoreDescriptor> ReadAndTouchDescriptorAsync(
        SqliteConnection connection,
        string path,
        CancellationToken token)
    {
        if (!await HasMetadataTableAsync(connection, token))
            throw new ApplicationStoreException("O banco não contém os metadados obrigatórios.");

        var version = await ReadSchemaVersionAsync(connection, token);
        var now = DateTimeOffset.UtcNow;
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE store_metadata SET last_opened_at = $now WHERE singleton_id = 1;";
        update.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
        if (await update.ExecuteNonQueryAsync(token) != 1)
            throw new ApplicationStoreException("O banco não possui uma identidade de armazenamento válida.");
        return await ReadDescriptorAsync(connection, path, version, token);
    }

    private static async Task<StoreDescriptor> ReadDescriptorAsync(
        SqliteConnection connection,
        string path,
        int version,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT store_id, storage_scope, cloned_from_store_id, created_at, last_opened_at
            FROM store_metadata
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new InvalidOperationException("Os metadados do armazenamento estão ausentes.");

        var storeId = Guid.Parse(reader.GetString(0));
        var scope = Enum.Parse<StorageScope>(reader.GetString(1), ignoreCase: false);
        Guid? clonedFrom = reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2));
        var createdAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
        var lastOpenedAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture);
        return new StoreDescriptor(storeId, path, scope, clonedFrom, createdAt, lastOpenedAt, version);
    }

    private static async Task<SqliteConnection> OpenConfiguredConnectionAsync(
        string path,
        bool readOnly,
        CancellationToken token)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = BusyTimeoutMilliseconds / 1000
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            await connection.OpenAsync(token);
            await ExecuteAsync(connection, null, "PRAGMA foreign_keys = ON;", token);
            await ExecuteAsync(connection, null, $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};", token);
            if (!readOnly)
            {
                await ExecuteAsync(connection, null, "PRAGMA journal_mode = WAL;", token);
                await ExecuteAsync(connection, null, "PRAGMA synchronous = NORMAL;", token);
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<int> ReadSchemaVersionAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> HasMetadataTableAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'store_metadata';";
        return Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Informe o caminho do banco da aplicação.", nameof(path));
        return Path.GetFullPath(path.Trim());
    }

    private const string SchemaSql = """
        CREATE TABLE store_metadata (
            singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
            store_id TEXT NOT NULL UNIQUE,
            storage_scope TEXT NOT NULL,
            cloned_from_store_id TEXT NULL,
            created_at TEXT NOT NULL,
            last_opened_at TEXT NOT NULL
        );

        CREATE TABLE app_settings (
            setting_key TEXT NOT NULL PRIMARY KEY,
            value_json TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE connection_profiles (
            profile_id TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL UNIQUE,
            database_type TEXT NOT NULL,
            host TEXT NOT NULL,
            port INTEGER NULL,
            user_name TEXT NOT NULL,
            database_name TEXT NOT NULL,
            sqlite_file TEXT NOT NULL,
            sql_server_authentication TEXT NOT NULL,
            odbc_driver TEXT NOT NULL,
            execution_defaults_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE runs (
            run_id TEXT NOT NULL PRIMARY KEY,
            profile_id TEXT NULL REFERENCES connection_profiles(profile_id) ON DELETE SET NULL,
            status TEXT NOT NULL,
            termination_reason TEXT NULL,
            started_at TEXT NOT NULL,
            finished_at TEXT NULL,
            application_version TEXT NOT NULL,
            machine_name TEXT NOT NULL,
            database_type TEXT NOT NULL,
            target TEXT NOT NULL,
            settings_snapshot_json TEXT NOT NULL,
            completed_cycles INTEGER NOT NULL DEFAULT 0,
            failure_message TEXT NULL
        );

        CREATE TABLE cycles (
            run_id TEXT NOT NULL REFERENCES runs(run_id) ON DELETE CASCADE,
            cycle_number INTEGER NOT NULL,
            started_at TEXT NOT NULL,
            resolved_ip TEXT NOT NULL,
            ping_extra TEXT NOT NULL,
            tcp_local_ip TEXT NOT NULL,
            tcp_remote_ip TEXT NOT NULL,
            database_total_ms INTEGER NOT NULL,
            PRIMARY KEY (run_id, cycle_number)
        );

        CREATE TABLE stage_results (
            run_id TEXT NOT NULL,
            cycle_number INTEGER NOT NULL,
            stage TEXT NOT NULL,
            status TEXT NOT NULL,
            elapsed_ms INTEGER NOT NULL,
            extra TEXT NOT NULL,
            diagnostic_code TEXT NULL,
            suggestion_code TEXT NULL,
            confidence TEXT NULL,
            user_message TEXT NULL,
            technical_message TEXT NULL,
            provider TEXT NULL,
            provider_code TEXT NULL,
            sql_state TEXT NULL,
            native_code TEXT NULL,
            provider_errors_json TEXT NULL,
            PRIMARY KEY (run_id, cycle_number, stage),
            FOREIGN KEY (run_id, cycle_number) REFERENCES cycles(run_id, cycle_number) ON DELETE CASCADE
        );

        CREATE TABLE run_stage_summaries (
            run_id TEXT NOT NULL REFERENCES runs(run_id) ON DELETE CASCADE,
            stage TEXT NOT NULL,
            attempts INTEGER NOT NULL,
            successes INTEGER NOT NULL,
            failures INTEGER NOT NULL,
            average_ms REAL NULL,
            minimum_ms INTEGER NULL,
            maximum_ms INTEGER NULL,
            median_ms REAL NULL,
            p95_ms INTEGER NULL,
            consecutive_failures INTEGER NOT NULL,
            maximum_consecutive_failures INTEGER NOT NULL,
            last_failure_at TEXT NULL,
            PRIMARY KEY (run_id, stage)
        );

        CREATE TABLE run_warnings (
            warning_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            run_id TEXT NOT NULL REFERENCES runs(run_id) ON DELETE CASCADE,
            created_at TEXT NOT NULL,
            warning_code TEXT NOT NULL,
            message TEXT NOT NULL
        );

        CREATE INDEX ix_runs_started_at ON runs(started_at DESC);
        CREATE INDEX ix_runs_profile_started ON runs(profile_id, started_at DESC);
        CREATE INDEX ix_runs_status_started ON runs(status, started_at DESC);
        CREATE INDEX ix_stage_results_diagnostic ON stage_results(diagnostic_code) WHERE diagnostic_code IS NOT NULL;
        """;
}
