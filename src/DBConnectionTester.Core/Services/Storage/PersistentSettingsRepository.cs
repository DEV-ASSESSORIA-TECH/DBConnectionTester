using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBConnectionTester.Models;
using Microsoft.Data.Sqlite;

namespace DBConnectionTester.Services.Storage;

public interface IApplicationSettingsRepository
{
    Task<ApplicationSettings> GetAsync(CancellationToken token = default);
    Task SaveAsync(ApplicationSettings settings, CancellationToken token = default);
}

public interface IConnectionProfileRepository
{
    Task<IReadOnlyList<SavedConnectionProfile>> ListAsync(CancellationToken token = default);
    Task<SavedConnectionProfile?> GetAsync(Guid profileId, CancellationToken token = default);
    Task<SavedConnectionProfile> SaveAsync(ConnectionProfileDraft draft, CancellationToken token = default);
    Task DeleteAsync(Guid profileId, CancellationToken token = default);
}

public sealed class PersistentSettingsRepository : IApplicationSettingsRepository, IConnectionProfileRepository
{
    private const string ApplicationSettingsKey = "application";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SqliteApplicationStore store;

    public PersistentSettingsRepository(SqliteApplicationStore store)
    {
        this.store = store;
    }

    public async Task<ApplicationSettings> GetAsync(CancellationToken token = default)
    {
        await using var connection = await store.OpenConnectionAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json FROM app_settings WHERE setting_key = $key;";
        command.Parameters.AddWithValue("$key", ApplicationSettingsKey);
        var value = await command.ExecuteScalarAsync(token) as string;
        return value is null
            ? ApplicationSettings.Default
            : JsonSerializer.Deserialize<ApplicationSettings>(value, JsonOptions)
              ?? throw new ApplicationStoreException("As configurações persistidas são inválidas.");
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken token = default)
    {
        ValidateSettings(settings);
        await using var connection = await store.OpenConnectionAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings (setting_key, value_json, updated_at)
            VALUES ($key, $value, $updatedAt)
            ON CONFLICT(setting_key) DO UPDATE SET
                value_json = excluded.value_json,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", ApplicationSettingsKey);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(settings, JsonOptions));
        command.Parameters.AddWithValue("$updatedAt", Format(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task<IReadOnlyList<SavedConnectionProfile>> ListAsync(CancellationToken token = default)
    {
        var profiles = new List<SavedConnectionProfile>();
        await using var connection = await store.OpenConnectionAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = ProfileSelectSql + " ORDER BY normalized_name;";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            profiles.Add(ReadProfile(reader));
        return profiles;
    }

    public async Task<SavedConnectionProfile?> GetAsync(Guid profileId, CancellationToken token = default)
    {
        await using var connection = await store.OpenConnectionAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = ProfileSelectSql + " WHERE profile_id = $profileId;";
        command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? ReadProfile(reader) : null;
    }

    public async Task<SavedConnectionProfile> SaveAsync(
        ConnectionProfileDraft draft,
        CancellationToken token = default)
    {
        ValidateProfile(draft);
        var profileId = draft.ProfileId ?? Guid.NewGuid();
        var existing = draft.ProfileId is null ? null : await GetAsync(profileId, token);
        var now = DateTimeOffset.UtcNow;
        var createdAt = existing?.CreatedAt ?? now;
        var name = draft.Name.Trim();

        try
        {
            await using var connection = await store.OpenConnectionAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO connection_profiles
                    (profile_id, name, normalized_name, database_type, host, port, user_name, database_name,
                     sqlite_file, sql_server_authentication, odbc_driver, execution_defaults_json, created_at, updated_at)
                VALUES
                    ($profileId, $name, $normalizedName, $databaseType, $host, $port, $userName, $databaseName,
                     $sqliteFile, $sqlAuthentication, $odbcDriver, $defaults, $createdAt, $updatedAt)
                ON CONFLICT(profile_id) DO UPDATE SET
                    name = excluded.name,
                    normalized_name = excluded.normalized_name,
                    database_type = excluded.database_type,
                    host = excluded.host,
                    port = excluded.port,
                    user_name = excluded.user_name,
                    database_name = excluded.database_name,
                    sqlite_file = excluded.sqlite_file,
                    sql_server_authentication = excluded.sql_server_authentication,
                    odbc_driver = excluded.odbc_driver,
                    execution_defaults_json = excluded.execution_defaults_json,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$normalizedName", NormalizeName(name));
            command.Parameters.AddWithValue("$databaseType", draft.DatabaseType.ToString());
            command.Parameters.AddWithValue("$host", draft.Host.Trim());
            command.Parameters.AddWithValue("$port", draft.Port is null ? DBNull.Value : draft.Port.Value);
            command.Parameters.AddWithValue("$userName", draft.UserName.Trim());
            command.Parameters.AddWithValue("$databaseName", draft.DatabaseName.Trim());
            command.Parameters.AddWithValue("$sqliteFile", draft.SqliteFile.Trim());
            command.Parameters.AddWithValue("$sqlAuthentication", draft.SqlServerAuthentication.ToString());
            command.Parameters.AddWithValue("$odbcDriver", draft.OdbcDriver.Trim());
            command.Parameters.AddWithValue("$defaults", JsonSerializer.Serialize(draft.ExecutionDefaults, JsonOptions));
            command.Parameters.AddWithValue("$createdAt", Format(createdAt));
            command.Parameters.AddWithValue("$updatedAt", Format(now));
            await command.ExecuteNonQueryAsync(token);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new ArgumentException("Já existe um perfil com esse nome.", nameof(draft), exception);
        }

        return (await GetAsync(profileId, token))!;
    }

    public async Task DeleteAsync(Guid profileId, CancellationToken token = default)
    {
        await using var connection = await store.OpenConnectionAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM connection_profiles WHERE profile_id = $profileId;";
        command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
        await command.ExecuteNonQueryAsync(token);
    }

    private static SavedConnectionProfile ReadProfile(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        Enum.Parse<DatabaseType>(reader.GetString(2)),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetInt32(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        Enum.Parse<SqlServerAuthentication>(reader.GetString(8)),
        reader.GetString(9),
        JsonSerializer.Deserialize<ProfileExecutionDefaults>(reader.GetString(10), JsonOptions)
            ?? throw new ApplicationStoreException("As opções do perfil são inválidas."),
        DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture));

    private static void ValidateSettings(ApplicationSettings settings)
    {
        if (settings.LegacyOutputEnabled && string.IsNullOrWhiteSpace(settings.LegacyOutputDirectory))
            throw new ArgumentException("Informe a pasta para a saída CSV/TXT contínua.", nameof(settings));
    }

    private static void ValidateProfile(ConnectionProfileDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
            throw new ArgumentException("Informe o nome do perfil.", nameof(draft));
        if (draft.Name.Trim().Length > 100)
            throw new ArgumentException("O nome do perfil deve ter no máximo 100 caracteres.", nameof(draft));
        if (draft.Port is < 1 or > 65_535)
            throw new ArgumentException("A porta deve estar entre 1 e 65535.", nameof(draft));
        if (draft.ExecutionDefaults.TestCount is < 1 or > 10_000_000)
            throw new ArgumentException("A quantidade de testes do perfil é inválida.", nameof(draft));
        if (draft.ExecutionDefaults.IntervalSeconds is < 0 or > 3_600)
            throw new ArgumentException("O intervalo do perfil é inválido.", nameof(draft));
        if (draft.ExecutionDefaults.TimeoutSeconds is < 1 or > 120)
            throw new ArgumentException("O timeout do perfil é inválido.", nameof(draft));
    }

    private static string NormalizeName(string name) => name.Trim().ToUpperInvariant();
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private const string ProfileSelectSql = """
        SELECT profile_id, name, database_type, host, port, user_name, database_name, sqlite_file,
               sql_server_authentication, odbc_driver, execution_defaults_json, created_at, updated_at
        FROM connection_profiles
        """;
}
