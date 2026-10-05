using DBConnectionTester.Models;

namespace DBConnectionTester.Tests;

public sealed class DatabaseUiStateTests
{
    [Theory]
    [InlineData(DatabaseType.MySqlMariaDb)]
    [InlineData(DatabaseType.PostgreSql)]
    [InlineData(DatabaseType.SapSqlAnywhere)]
    public void NetworkDatabasesShowHostCredentialsAndDatabase(DatabaseType type)
    {
        var state = DatabaseUiState.Create(type, SqlServerAuthentication.SqlLogin);

        Assert.True(state.ShowHost);
        Assert.True(state.ShowPort);
        Assert.True(state.ShowCredentials);
        Assert.True(state.ShowDatabase);
        Assert.True(state.AllowDns);
        Assert.True(state.AllowPing);
        Assert.True(state.AllowTcp);
        Assert.True(state.AllowDatabaseTest);
    }

    [Fact]
    public void SqlServerAuthenticationControlsCredentialFields()
    {
        var windows = DatabaseUiState.Create(DatabaseType.SqlServer, SqlServerAuthentication.Windows);
        var sqlLogin = DatabaseUiState.Create(DatabaseType.SqlServer, SqlServerAuthentication.SqlLogin);

        Assert.True(windows.ShowSqlServerAuthentication);
        Assert.False(windows.ShowCredentials);
        Assert.True(sqlLogin.ShowCredentials);
    }

    [Fact]
    public void SqliteReplacesNetworkWithRequiredDatabaseFileTest()
    {
        var state = DatabaseUiState.Create(DatabaseType.Sqlite, SqlServerAuthentication.Windows);

        Assert.True(state.ShowSqliteFile);
        Assert.False(state.ShowHost);
        Assert.False(state.ShowPort);
        Assert.False(state.AllowDns);
        Assert.False(state.AllowPing);
        Assert.False(state.AllowTcp);
        Assert.False(state.AllowDatabaseTest);
        Assert.True(state.RequireDatabaseTest);
    }

    [Fact]
    public void TcpOnlyRequiresTcpAndHidesDatabaseFields()
    {
        var state = DatabaseUiState.Create(DatabaseType.TcpOnly, SqlServerAuthentication.Windows);

        Assert.True(state.ShowHost);
        Assert.True(state.RequireTcp);
        Assert.False(state.ShowCredentials);
        Assert.False(state.ShowDatabase);
        Assert.False(state.AllowDatabaseTest);
    }
}
