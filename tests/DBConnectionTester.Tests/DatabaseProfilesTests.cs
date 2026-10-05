using DBConnectionTester.Models;

namespace DBConnectionTester.Tests;

public sealed class DatabaseProfilesTests
{
    [Theory]
    [InlineData(DatabaseType.MySqlMariaDb, 3306)]
    [InlineData(DatabaseType.PostgreSql, 5432)]
    [InlineData(DatabaseType.SqlServer, 1433)]
    [InlineData(DatabaseType.SapSqlAnywhere, 2638)]
    public void NetworkDatabaseHasExpectedDefaultPort(DatabaseType type, int expectedPort)
    {
        var profile = DatabaseProfiles.Get(type);

        Assert.True(profile.UsesNetwork);
        Assert.True(profile.SupportsDatabaseTest);
        Assert.Equal(expectedPort, profile.DefaultPort);
    }

    [Fact]
    public void SqliteUsesFileAndNoNetwork()
    {
        var profile = DatabaseProfiles.Get(DatabaseType.Sqlite);

        Assert.True(profile.UsesFile);
        Assert.False(profile.UsesNetwork);
        Assert.Null(profile.DefaultPort);
    }

    [Fact]
    public void TcpOnlyDoesNotSupportDatabaseTest()
    {
        var profile = DatabaseProfiles.Get(DatabaseType.TcpOnly);

        Assert.True(profile.UsesNetwork);
        Assert.False(profile.SupportsDatabaseTest);
    }
}
