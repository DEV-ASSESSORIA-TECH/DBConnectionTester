namespace DBConnectionTester.Models;

public sealed record TestSettings
{
    internal TestSettings(
        DatabaseType databaseType,
        string host,
        NetworkPort? port,
        string user,
        string password,
        string database,
        string sqliteFile,
        SqlServerAuthentication sqlServerAuthentication,
        string odbcDriver,
        RunCount testCount,
        bool continuous,
        TestInterval interval,
        StageTimeout timeout,
        bool dns,
        bool ping,
        bool tcp,
        bool databaseTest)
    {
        DatabaseType = databaseType;
        Host = host;
        Port = port;
        User = user;
        Password = password;
        Database = database;
        SqliteFile = sqliteFile;
        SqlServerAuthentication = sqlServerAuthentication;
        OdbcDriver = odbcDriver;
        TestCount = testCount;
        Continuous = continuous;
        Interval = interval;
        Timeout = timeout;
        Dns = dns;
        Ping = ping;
        Tcp = tcp;
        DatabaseTest = databaseTest;
    }

    public DatabaseType DatabaseType { get; internal init; }
    public string Host { get; internal init; }
    public NetworkPort? Port { get; internal init; }
    public string User { get; internal init; }
    public string Password { get; internal init; }
    public string Database { get; internal init; }
    public string SqliteFile { get; internal init; }
    public SqlServerAuthentication SqlServerAuthentication { get; internal init; }
    public string OdbcDriver { get; internal init; }
    public RunCount TestCount { get; internal init; }
    public bool Continuous { get; internal init; }
    public TestInterval Interval { get; internal init; }
    public StageTimeout Timeout { get; internal init; }
    public bool Dns { get; internal init; }
    public bool Ping { get; internal init; }
    public bool Tcp { get; internal init; }
    public bool DatabaseTest { get; internal init; }
    public Guid? ProfileId { get; internal init; }

    public DatabaseProfile Profile => DatabaseProfiles.Get(DatabaseType);
    public string Target => DatabaseType == DatabaseType.Sqlite ? SqliteFile : $"{Host}:{Port!.Value}";
}

public enum StepStatus
{
    Success,
    Failed,
    Skipped
}

public static class StepStatusExtensions
{
    public static string ToOutputText(this StepStatus status) => status switch
    {
        StepStatus.Success => "OK",
        StepStatus.Failed => "FALHA",
        _ => "N/A"
    };
}

public sealed record DnsResult(StepStatus Status, string ResolvedIp, long ElapsedMs, DiagnosticIssue? Diagnostic)
{
    public bool Ok => Status == StepStatus.Success;
    public string Error => Diagnostic?.Summary ?? "";
    public static DnsResult Skipped() => new(StepStatus.Skipped, "", 0, null);
}

public sealed record StepResult(StepStatus Status, long ElapsedMs, string Extra, DiagnosticIssue? Diagnostic)
{
    public bool Ok => Status == StepStatus.Success;
    public string Error => Diagnostic?.Summary ?? "";
    public static StepResult Skipped() => new(StepStatus.Skipped, 0, "", null);
}

public sealed record TcpResult(StepStatus Status, long ElapsedMs, string LocalIp, string RemoteIp, DiagnosticIssue? Diagnostic)
{
    public bool Ok => Status == StepStatus.Success;
    public string Error => Diagnostic?.Summary ?? "";
    public static TcpResult Skipped() => new(StepStatus.Skipped, 0, "", "", null);
}

public sealed record DatabaseResult(
    StepStatus ConnectStatus,
    long ConnectMs,
    StepStatus QueryStatus,
    long QueryMs,
    long TotalMs,
    DiagnosticIssue? ConnectDiagnostic,
    DiagnosticIssue? QueryDiagnostic)
{
    public bool ConnectOk => ConnectStatus == StepStatus.Success;
    public bool QueryOk => QueryStatus == StepStatus.Success;
    public string Error => ConnectDiagnostic?.Summary ?? QueryDiagnostic?.Summary ?? "";
    public static DatabaseResult Skipped() => new(StepStatus.Skipped, 0, StepStatus.Skipped, 0, 0, null, null);
}

public sealed record TestCycleResult(
    long Number,
    DateTimeOffset StartedAt,
    DnsResult Dns,
    StepResult Ping,
    TcpResult Tcp,
    DatabaseResult Database);

public sealed record TestProgress(
    long Completed,
    int DnsFailures,
    int PingFailures,
    int TcpFailures,
    int DatabaseConnectFailures,
    int DatabaseQueryFailures,
    TestCycleResult? LatestCycle = null,
    RunStatisticsSnapshot? Statistics = null)
{
    public int DatabaseFailures => DatabaseConnectFailures + DatabaseQueryFailures;
}

public sealed record RunSummary(
    Guid RunId,
    long Completed,
    RunTerminationReason TerminationReason,
    string? FailureMessage,
    string? CsvPath = null,
    string? TxtPath = null)
{
    public bool Stopped => TerminationReason == RunTerminationReason.StoppedByUser;
    public bool Failed => TerminationReason == RunTerminationReason.ExecutionFailed;
}
