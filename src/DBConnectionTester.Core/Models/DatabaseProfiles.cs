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
    bool UsesOdbcDriver)
{
    // Single source for "does this target need a user and a password?" (GUI, CLI, validator).
    public bool RequiresCredentials(SqlServerAuthentication authentication) =>
        UsesCredentials && (!UsesSqlServerAuthentication || authentication == SqlServerAuthentication.SqlLogin);

    // A bare TCP probe (connect and close without the handshake) is an error for the server: MySQL/MariaDB count
    // it in Aborted_connects and in the per-host counter limited by max_connect_errors, which blocks the client.
    // With the database stage enabled, the TCP stage is measured on the driver's own connection instead.
    public bool TcpStageThroughDriver => Type == DatabaseType.MySqlMariaDb;
}

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
