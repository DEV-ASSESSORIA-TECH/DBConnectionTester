using System.Data.Common;
using System.Data.Odbc;
using DBConnectionTester.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace DBConnectionTester.Services;

public static class DatabaseConnectionFactory
{
    public static DbConnection Create(TestSettings settings)
    {
        var timeout = Math.Max(1, (int)Math.Ceiling(settings.Timeout.TotalSeconds));

        return settings.DatabaseType switch
        {
            DatabaseType.MySqlMariaDb => CreateMySql(settings, timeout),
            DatabaseType.PostgreSql => CreatePostgreSql(settings, timeout),
            DatabaseType.SqlServer => CreateSqlServer(settings, timeout),
            DatabaseType.SapSqlAnywhere => CreateSqlAnywhere(settings, timeout),
            DatabaseType.Sqlite => CreateSqlite(settings, timeout),
            _ => throw new InvalidOperationException("O tipo selecionado não oferece conexão de banco de dados.")
        };
    }

    public static string BuildConnectionString(TestSettings settings)
    {
        using var connection = Create(settings);
        return connection.ConnectionString;
    }

    private static MySqlConnection CreateMySql(TestSettings settings, int timeout)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.Host,
            Port = (uint)settings.Port,
            UserID = settings.User,
            Password = settings.Password,
            Pooling = false,
            ConnectionTimeout = (uint)timeout,
            DefaultCommandTimeout = (uint)timeout
        };
        if (!string.IsNullOrWhiteSpace(settings.Database))
            builder.Database = settings.Database;
        return new MySqlConnection(builder.ConnectionString);
    }

    private static NpgsqlConnection CreatePostgreSql(TestSettings settings, int timeout)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = settings.Host,
            Port = settings.Port,
            Username = settings.User,
            Password = settings.Password,
            Pooling = false,
            Timeout = timeout,
            CommandTimeout = timeout
        };
        if (!string.IsNullOrWhiteSpace(settings.Database))
            builder.Database = settings.Database;
        return new NpgsqlConnection(builder.ConnectionString);
    }

    private static SqlConnection CreateSqlServer(TestSettings settings, int timeout)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{settings.Host},{settings.Port}",
            IntegratedSecurity = settings.SqlServerAuthentication == SqlServerAuthentication.Windows,
            Pooling = false,
            ConnectTimeout = timeout,
            CommandTimeout = timeout
        };
        if (settings.SqlServerAuthentication == SqlServerAuthentication.SqlLogin)
        {
            builder.UserID = settings.User;
            builder.Password = settings.Password;
        }
        if (!string.IsNullOrWhiteSpace(settings.Database))
            builder.InitialCatalog = settings.Database;
        return new SqlConnection(builder.ConnectionString);
    }

    private static OdbcConnection CreateSqlAnywhere(TestSettings settings, int timeout)
    {
        var builder = new OdbcConnectionStringBuilder { Driver = settings.OdbcDriver };
        builder["UID"] = settings.User;
        builder["PWD"] = settings.Password;
        builder["HOST"] = $"{settings.Host}:{settings.Port}";
        builder["Connection Timeout"] = timeout;
        if (!string.IsNullOrWhiteSpace(settings.Database))
            builder["DBN"] = settings.Database;
        return new OdbcConnection(builder.ConnectionString);
    }

    private static SqliteConnection CreateSqlite(TestSettings settings, int timeout)
    {
        if (!File.Exists(settings.SqliteFile))
            throw new FileNotFoundException("O arquivo SQLite selecionado não existe.", settings.SqliteFile);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(settings.SqliteFile),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = timeout
        };
        return new SqliteConnection(builder.ConnectionString);
    }
}
