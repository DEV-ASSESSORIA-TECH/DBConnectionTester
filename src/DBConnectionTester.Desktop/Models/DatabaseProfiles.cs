namespace DBConnectionTester.Models;

public sealed record DatabaseProfile(
    DatabaseType Type,
    string DisplayName,
    int? DefaultPort,
    bool UsesNetwork,
    bool SupportsDatabaseTest,
    bool UsesCredentials,
    bool UsesFile,
    bool UsesSqlServerAuthentication,
    bool UsesOdbcDriver);

public static class DatabaseProfiles
{
    private static readonly IReadOnlyDictionary<DatabaseType, DatabaseProfile> Profiles =
        new Dictionary<DatabaseType, DatabaseProfile>
        {
            [DatabaseType.MySqlMariaDb] = new(DatabaseType.MySqlMariaDb, "MySQL / MariaDB", 3306, true, true, true, false, false, false),
            [DatabaseType.PostgreSql] = new(DatabaseType.PostgreSql, "PostgreSQL", 5432, true, true, true, false, false, false),
            [DatabaseType.SqlServer] = new(DatabaseType.SqlServer, "SQL Server", 1433, true, true, true, false, true, false),
            [DatabaseType.SapSqlAnywhere] = new(DatabaseType.SapSqlAnywhere, "SAP SQL Anywhere", 2638, true, true, true, false, false, true),
            [DatabaseType.Sqlite] = new(DatabaseType.Sqlite, "SQLite", null, false, true, false, true, false, false),
            [DatabaseType.TcpOnly] = new(DatabaseType.TcpOnly, "Somente TCP", null, true, false, false, false, false, false)
        };

    public static IReadOnlyList<DatabaseProfile> All { get; } =
        Enum.GetValues<DatabaseType>().Select(Get).ToArray();

    public static DatabaseProfile Get(DatabaseType type) => Profiles[type];
}
