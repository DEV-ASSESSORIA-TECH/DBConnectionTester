namespace DBConnectionTester.Models;

public sealed record DatabaseUiState(
    bool ShowHost,
    bool ShowPort,
    bool ShowCredentials,
    bool ShowDatabase,
    bool ShowSqlServerAuthentication,
    bool ShowOdbcDriver,
    bool ShowSqliteFile,
    bool AllowPing,
    bool AllowTcp,
    bool AllowDatabaseTest,
    bool RequireTcp,
    bool RequireDatabaseTest)
{
    public static DatabaseUiState Create(DatabaseType type, SqlServerAuthentication authentication)
    {
        var profile = DatabaseProfiles.Get(type);
        var showCredentials = profile.UsesCredentials &&
            (!profile.UsesSqlServerAuthentication || authentication == SqlServerAuthentication.SqlLogin);

        return new DatabaseUiState(
            profile.UsesNetwork,
            profile.UsesNetwork,
            showCredentials,
            profile.SupportsDatabaseTest && !profile.UsesFile,
            profile.UsesSqlServerAuthentication,
            profile.UsesOdbcDriver,
            profile.UsesFile,
            profile.UsesNetwork,
            profile.UsesNetwork && type != DatabaseType.TcpOnly,
            profile.SupportsDatabaseTest && type != DatabaseType.Sqlite,
            type == DatabaseType.TcpOnly,
            type == DatabaseType.Sqlite);
    }
}
