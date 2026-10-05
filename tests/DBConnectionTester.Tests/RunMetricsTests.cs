using DBConnectionTester.Models;

namespace DBConnectionTester.Tests;

public sealed class RunMetricsTests
{
    [Fact]
    public void ConnectionFailureDoesNotCountAsQueryFailure()
    {
        var metrics = new RunMetrics();
        var settings = Settings() with { Ping = false, Tcp = false };
        var cycle = Cycle(new DatabaseResult(
            StepStatus.Failed, 12, StepStatus.Skipped, 0, 12, "connection refused"));

        metrics.Add(settings, cycle);

        Assert.Equal(1, metrics.DatabaseConnectFailures);
        Assert.Equal(0, metrics.DatabaseQueryFailures);
    }

    [Fact]
    public void SuccessfulCycleAccumulatesEveryEnabledLayer()
    {
        var metrics = new RunMetrics();
        var settings = Settings();

        var cycle = Cycle(new DatabaseResult(
            StepStatus.Success, 8, StepStatus.Success, 2, 10, ""));
        metrics.Add(settings, cycle);
        var progress = metrics.CreateProgress(cycle);

        Assert.Equal(1, metrics.Completed);
        Assert.Equal(1, metrics.DnsOk);
        Assert.Equal(1, metrics.PingOk);
        Assert.Equal(1, metrics.TcpOk);
        Assert.Equal(1, metrics.DatabaseConnectOk);
        Assert.Equal(1, metrics.DatabaseQueryOk);
        Assert.Same(cycle, progress.LatestCycle);
    }

    private static TestCycleResult Cycle(DatabaseResult database) => new(
        1,
        DateTimeOffset.UtcNow,
        new DnsResult(StepStatus.Success, "127.0.0.1", 1, ""),
        new StepResult(StepStatus.Success, 2, "64", ""),
        new TcpResult(StepStatus.Success, 3, "127.0.0.1", "127.0.0.1", ""),
        database);

    private static TestSettings Settings() => new(
        DatabaseType.MySqlMariaDb,
        "127.0.0.1",
        3306,
        "user",
        "password",
        "database",
        "",
        SqlServerAuthentication.SqlLogin,
        "SQL Anywhere 17",
        1,
        false,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        true,
        true,
        true,
        "result.csv",
        "result.txt");
}
