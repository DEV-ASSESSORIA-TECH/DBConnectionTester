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

public sealed record DnsResult(string Status, string ResolvedIp, long ElapsedMs, string Error)
{
    public static DnsResult Skipped() => new("N/A", "", 0, "");
}

public sealed record StepResult(bool Ok, string Status, long ElapsedMs, string Extra, string Error)
{
    public static StepResult Skipped() => new(true, "N/A", 0, "", "");
}

public sealed record TcpResult(bool Ok, string Status, long ElapsedMs, string LocalIp, string RemoteIp, string Error)
{
    public static TcpResult Skipped() => new(true, "N/A", 0, "", "", "");
}

public sealed record DatabaseResult(
    bool ConnectOk,
    string ConnectStatus,
    long ConnectMs,
    bool QueryOk,
    string QueryStatus,
    long QueryMs,
    long TotalMs,
    string Error)
{
    public static DatabaseResult Skipped() => new(true, "N/A", 0, true, "N/A", 0, 0, "");
}

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
