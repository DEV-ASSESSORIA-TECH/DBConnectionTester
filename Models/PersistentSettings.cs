namespace DBConnectionTester.Models;

public enum ApplicationTheme
{
    System,
    Light,
    Dark
}

public sealed record ApplicationSettings(
    ApplicationTheme Theme,
    bool LegacyOutputEnabled,
    string LegacyOutputDirectory)
{
    public static ApplicationSettings Default { get; } = new(
        ApplicationTheme.System,
        LegacyOutputEnabled: false,
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "DBConnectionTester",
            "Exports"));
}

public sealed record ProfileExecutionDefaults(
    long TestCount,
    bool Continuous,
    double IntervalSeconds,
    int TimeoutSeconds,
    bool Dns,
    bool Ping,
    bool Tcp,
    bool DatabaseTest,
    bool StartInBackground)
{
    public static ProfileExecutionDefaults Default { get; } = new(
        1_000,
        Continuous: false,
        IntervalSeconds: 5,
        TimeoutSeconds: 5,
        Dns: true,
        Ping: true,
        Tcp: true,
        DatabaseTest: true,
        StartInBackground: false);
}

public sealed record SavedConnectionProfile(
    Guid ProfileId,
    string Name,
    DatabaseType DatabaseType,
    string Host,
    int? Port,
    string UserName,
    string DatabaseName,
    string SqliteFile,
    SqlServerAuthentication SqlServerAuthentication,
    string OdbcDriver,
    ProfileExecutionDefaults ExecutionDefaults,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ConnectionProfileDraft(
    Guid? ProfileId,
    string Name,
    DatabaseType DatabaseType,
    string Host,
    int? Port,
    string UserName,
    string DatabaseName,
    string SqliteFile,
    SqlServerAuthentication SqlServerAuthentication,
    string OdbcDriver,
    ProfileExecutionDefaults ExecutionDefaults);
