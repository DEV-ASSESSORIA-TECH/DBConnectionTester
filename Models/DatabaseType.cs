namespace DBConnectionTester.Models;

public enum DatabaseType
{
    MySqlMariaDb,
    PostgreSql,
    SqlServer,
    SapSqlAnywhere,
    Sqlite,
    TcpOnly
}

public enum SqlServerAuthentication
{
    Windows,
    SqlLogin
}
