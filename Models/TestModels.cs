namespace DBConnectionTester.Models;

public sealed record TestSettings(
    DatabaseType DatabaseType,
    string Host,
    int Port,
    string User,
    string Password,
    string Database,
    string SqliteFile,
    SqlServerAuthentication SqlServerAuthentication,
    string OdbcDriver,
    long TestCount,
    bool Continuous,
    TimeSpan Interval,
    TimeSpan Timeout,
    bool Ping,
    bool Tcp,
    bool DatabaseTest,
    string CsvPath,
    string TxtPath)
{
    public DatabaseProfile Profile => DatabaseProfiles.Get(DatabaseType);
    public string Target => DatabaseType == DatabaseType.Sqlite ? SqliteFile : $"{Host}:{Port}";
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

public sealed record DnsResult(StepStatus Status, string ResolvedIp, long ElapsedMs, string Error)
{
    public bool Ok => Status == StepStatus.Success;
    public static DnsResult Skipped() => new(StepStatus.Skipped, "", 0, "");
}

public sealed record StepResult(StepStatus Status, long ElapsedMs, string Extra, string Error)
{
    public bool Ok => Status == StepStatus.Success;
    public static StepResult Skipped() => new(StepStatus.Skipped, 0, "", "");
}

public sealed record TcpResult(StepStatus Status, long ElapsedMs, string LocalIp, string RemoteIp, string Error)
{
    public bool Ok => Status == StepStatus.Success;
    public static TcpResult Skipped() => new(StepStatus.Skipped, 0, "", "", "");
}

public sealed record DatabaseResult(
    StepStatus ConnectStatus,
    long ConnectMs,
    StepStatus QueryStatus,
    long QueryMs,
    long TotalMs,
    string Error)
{
    public bool ConnectOk => ConnectStatus == StepStatus.Success;
    public bool QueryOk => QueryStatus == StepStatus.Success;
    public static DatabaseResult Skipped() => new(StepStatus.Skipped, 0, StepStatus.Skipped, 0, 0, "");
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
    int DatabaseQueryFailures)
{
    public int DatabaseFailures => DatabaseConnectFailures + DatabaseQueryFailures;
}

public sealed record RunSummary(long Completed, bool Stopped);
