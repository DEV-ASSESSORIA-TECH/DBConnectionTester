namespace DBConnectionTester.Models;

public sealed class RunMetrics
{
    private readonly StageStatistics dns = new();
    private readonly StageStatistics ping = new();
    private readonly StageStatistics tcp = new();
    private readonly StageStatistics databaseConnect = new();
    private readonly StageStatistics databaseQuery = new();

    public long Completed { get; private set; }
    public int DnsOk => checked((int)dns.Successes);
    public int DnsFailures => checked((int)dns.Failures);
    public int PingOk => checked((int)ping.Successes);
    public int PingFailures => checked((int)ping.Failures);
    public int TcpOk => checked((int)tcp.Successes);
    public int TcpFailures => checked((int)tcp.Failures);
    public int DatabaseConnectOk => checked((int)databaseConnect.Successes);
    public int DatabaseConnectFailures => checked((int)databaseConnect.Failures);
    public int DatabaseQueryOk => checked((int)databaseQuery.Successes);
    public int DatabaseQueryFailures => checked((int)databaseQuery.Failures);

    public void Add(TestSettings settings, TestCycleResult cycle)
    {
        Completed++;
        var recordedAt = DateTimeOffset.Now;
        if (settings.Dns)
            dns.Record(cycle.Dns.Status, cycle.Dns.ElapsedMs, recordedAt);
        if (settings.Ping)
            ping.Record(cycle.Ping.Status, cycle.Ping.ElapsedMs, recordedAt);
        if (settings.Tcp)
            tcp.Record(cycle.Tcp.Status, cycle.Tcp.ElapsedMs, recordedAt);
        if (settings.DatabaseTest)
        {
            databaseConnect.Record(cycle.Database.ConnectStatus, cycle.Database.ConnectMs, recordedAt);
            databaseQuery.Record(cycle.Database.QueryStatus, cycle.Database.QueryMs, recordedAt);
        }
    }

    public RunStatisticsSnapshot CreateStatistics() => new(
        dns.CreateSnapshot(),
        ping.CreateSnapshot(),
        tcp.CreateSnapshot(),
        databaseConnect.CreateSnapshot(),
        databaseQuery.CreateSnapshot());

    public TestProgress CreateProgress(TestCycleResult? latestCycle = null) => new(
        Completed,
        DnsFailures,
        PingFailures,
        TcpFailures,
        DatabaseConnectFailures,
        DatabaseQueryFailures,
        latestCycle,
        CreateStatistics());
}
