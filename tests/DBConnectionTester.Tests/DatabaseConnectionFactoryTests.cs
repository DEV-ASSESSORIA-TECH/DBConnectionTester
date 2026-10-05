using System.Data.Odbc;
using DBConnectionTester.Models;
using DBConnectionTester.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace DBConnectionTester.Tests;

public sealed class DatabaseConnectionFactoryTests
{
    [Fact]
    public void SelectsExpectedProviderForEveryDatabase()
    {
        using var mysql = DatabaseConnectionFactory.Create(Settings(DatabaseType.MySqlMariaDb));
        using var postgres = DatabaseConnectionFactory.Create(Settings(DatabaseType.PostgreSql));
        using var sqlServer = DatabaseConnectionFactory.Create(Settings(DatabaseType.SqlServer));
        using var sqlAnywhere = DatabaseConnectionFactory.Create(Settings(DatabaseType.SapSqlAnywhere));

        Assert.IsType<MySqlConnection>(mysql);
        Assert.IsType<NpgsqlConnection>(postgres);
        Assert.IsType<SqlConnection>(sqlServer);
        Assert.IsType<OdbcConnection>(sqlAnywhere);
    }

    [Fact]
    public void SqlServerSupportsWindowsAndSqlLogin()
    {
        var windows = new SqlConnectionStringBuilder(DatabaseConnectionFactory.BuildConnectionString(
            Settings(DatabaseType.SqlServer) with { SqlServerAuthentication = SqlServerAuthentication.Windows }));
        var sqlLogin = new SqlConnectionStringBuilder(DatabaseConnectionFactory.BuildConnectionString(
            Settings(DatabaseType.SqlServer) with
            {
                SqlServerAuthentication = SqlServerAuthentication.SqlLogin,
                User = "user;name",
                Password = "p;\"word"
            }));

        Assert.True(windows.IntegratedSecurity);
        Assert.False(sqlLogin.IntegratedSecurity);
        Assert.Equal("user;name", sqlLogin.UserID);
        Assert.Equal("p;\"word", sqlLogin.Password);
        Assert.False(sqlLogin.Pooling);
    }

    [Fact]
    public void BuildersPreserveSpecialCharacters()
    {
        var mysql = new MySqlConnectionStringBuilder(DatabaseConnectionFactory.BuildConnectionString(
            Settings(DatabaseType.MySqlMariaDb) with { User = "u;ser", Password = "p;ass", Database = "db name" }));
        var postgres = new NpgsqlConnectionStringBuilder(DatabaseConnectionFactory.BuildConnectionString(
            Settings(DatabaseType.PostgreSql) with { User = "u;ser", Password = "p;ass", Database = "db name" }));
        var sqlAnywhere = new OdbcConnectionStringBuilder(DatabaseConnectionFactory.BuildConnectionString(
            Settings(DatabaseType.SapSqlAnywhere) with { OdbcDriver = "SQL Anywhere } 17", Password = "p;ass" }));

        Assert.Equal("u;ser", mysql.UserID);
        Assert.Equal("p;ass", mysql.Password);
        Assert.Equal("u;ser", postgres.Username);
        Assert.Equal("p;ass", postgres.Password);
        Assert.Contains("pwd={p;ass}", sqlAnywhere.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Driver={SQL Anywhere }} 17}", sqlAnywhere.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqliteRejectsMissingFile()
    {
        var settings = Settings(DatabaseType.Sqlite) with
        {
            SqliteFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db")
        };

        Assert.Throws<FileNotFoundException>(() => DatabaseConnectionFactory.Create(settings));
    }

    [Fact]
    public void SqliteUsesReadOnlyConnectionForExistingFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var connection = DatabaseConnectionFactory.Create(Settings(DatabaseType.Sqlite) with { SqliteFile = path });
            var builder = new SqliteConnectionStringBuilder(connection.ConnectionString);

            Assert.IsType<SqliteConnection>(connection);
            Assert.Equal(SqliteOpenMode.ReadOnly, builder.Mode);
            Assert.False(builder.Pooling);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TcpOnlyCannotCreateDatabaseConnection()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionFactory.Create(Settings(DatabaseType.TcpOnly)));
    }

    private static TestSettings Settings(DatabaseType type) => new(
        type,
        "localhost",
        DatabaseProfiles.Get(type).DefaultPort ?? 9000,
        "user",
        "password",
        "database",
        "",
        SqlServerAuthentication.SqlLogin,
        "SQL Anywhere 17",
        1,
        false,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(5),
        true,
        true,
        type != DatabaseType.TcpOnly,
        "result.csv",
        "result.txt");
}
