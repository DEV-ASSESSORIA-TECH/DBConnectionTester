using DBConnectionTester.Models;

namespace DBConnectionTester.Application;

public static class ProfileSettingsMapper
{
    public static TestSettingsInput CreateDefault(DatabaseType type = DatabaseType.MySqlMariaDb)
    {
        var defaults = ProfileExecutionDefaults.Default;
        return new(type, "127.0.0.1", DatabaseProfiles.Get(type).DefaultPort ?? 3306,
            "", "", "", "", SqlServerAuthentication.Windows, "SQL Anywhere 17",
            defaults.TestCount, defaults.Continuous, TimeSpan.FromSeconds(defaults.IntervalSeconds),
            TimeSpan.FromSeconds(defaults.TimeoutSeconds), defaults.Dns, defaults.Ping, defaults.Tcp,
            defaults.DatabaseTest);
    }

    public static TestSettingsInput FromProfile(SavedConnectionProfile profile)
    {
        var defaults = profile.ExecutionDefaults;
        return new(profile.DatabaseType, profile.Host, profile.Port ?? 3306, profile.UserName, "",
            profile.DatabaseName, profile.SqliteFile, profile.SqlServerAuthentication, profile.OdbcDriver,
            defaults.TestCount, defaults.Continuous, TimeSpan.FromSeconds(defaults.IntervalSeconds),
            TimeSpan.FromSeconds(defaults.TimeoutSeconds), defaults.Dns, defaults.Ping, defaults.Tcp,
            defaults.DatabaseTest, profile.ProfileId);
    }
}
