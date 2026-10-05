namespace DBConnectionTester.Models;

public sealed class RunMetrics
{
    public long Completed { get; private set; }
    public int DnsOk { get; private set; }
    public int DnsFailures { get; private set; }
    public long DnsSum { get; private set; }
    public int PingOk { get; private set; }
    public int PingFailures { get; private set; }
    public long PingSum { get; private set; }
    public int TcpOk { get; private set; }
    public int TcpFailures { get; private set; }
    public long TcpSum { get; private set; }
    public int DatabaseConnectOk { get; private set; }
    public int DatabaseConnectFailures { get; private set; }
    public long DatabaseConnectSum { get; private set; }
    public int DatabaseQueryOk { get; private set; }
    public int DatabaseQueryFailures { get; private set; }
    public long DatabaseQuerySum { get; private set; }

    public void Add(TestSettings settings, TestCycleResult cycle)
    {
        Completed++;
        if (settings.Profile.UsesNetwork)
        {
            if (cycle.Dns.Ok) { DnsOk++; DnsSum += cycle.Dns.ElapsedMs; } else DnsFailures++;
        }
        if (settings.Ping)
        {
            if (cycle.Ping.Ok) { PingOk++; PingSum += cycle.Ping.ElapsedMs; } else PingFailures++;
        }
        if (settings.Tcp)
        {
            if (cycle.Tcp.Ok) { TcpOk++; TcpSum += cycle.Tcp.ElapsedMs; } else TcpFailures++;
        }
        if (settings.DatabaseTest)
        {
            if (cycle.Database.ConnectOk)
            {
                DatabaseConnectOk++;
                DatabaseConnectSum += cycle.Database.ConnectMs;
                if (cycle.Database.QueryOk)
                {
                    DatabaseQueryOk++;
                    DatabaseQuerySum += cycle.Database.QueryMs;
                }
                else
                {
                    DatabaseQueryFailures++;
                }
            }
            else
            {
                DatabaseConnectFailures++;
            }
        }
    }

    public TestProgress CreateProgress(TestCycleResult? latestCycle = null) => new(
        Completed,
        DnsFailures,
        PingFailures,
        TcpFailures,
        DatabaseConnectFailures,
        DatabaseQueryFailures,
        latestCycle);

    public static long Average(long sum, int count) => count == 0 ? 0 : sum / count;
}
