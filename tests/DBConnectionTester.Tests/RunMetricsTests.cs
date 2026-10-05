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

    [Fact]
    public void StatisticsIncludeDistributionAndFailureStreaks()
    {
        var metrics = new RunMetrics();
        var settings = Settings() with { Dns = false, Tcp = false, DatabaseTest = false };
        var samples = new[]
        {
            new StepResult(StepStatus.Success, 10, "", ""),
            new StepResult(StepStatus.Failed, 11, "", "timeout"),
            new StepResult(StepStatus.Failed, 12, "", "timeout"),
            new StepResult(StepStatus.Success, 20, "", ""),
            new StepResult(StepStatus.Success, 30, "", ""),
            new StepResult(StepStatus.Success, 40, "", ""),
            new StepResult(StepStatus.Failed, 13, "", "timeout")
        };

        for (var index = 0; index < samples.Length; index++)
            metrics.Add(settings, Cycle(index + 1, samples[index]));

        var statistics = metrics.CreateStatistics().Ping;
        Assert.Equal(7, statistics.Attempts);
        Assert.Equal(4, statistics.Successes);
        Assert.Equal(3, statistics.Failures);
        Assert.Equal(25, statistics.AverageMs);
        Assert.Equal(10, statistics.MinimumMs);
        Assert.Equal(40, statistics.MaximumMs);
        Assert.Equal(25, statistics.MedianMs);
        Assert.Equal(40, statistics.P95Ms);
        Assert.Equal(1, statistics.ConsecutiveFailures);
        Assert.Equal(2, statistics.MaximumConsecutiveFailures);
        Assert.Equal(57.14, statistics.SuccessRate!.Value, 2);
        Assert.NotNull(statistics.TimeSinceLastFailure(DateTimeOffset.Now));
    }

    [Fact]
    public void DisabledDnsIsIgnoredByMetrics()
    {
        var metrics = new RunMetrics();
        var settings = Settings() with { Dns = false, Ping = false, Tcp = false, DatabaseTest = false };
        var cycle = Cycle(DatabaseResult.Skipped()) with
        {
            Dns = new DnsResult(StepStatus.Failed, "", 50, "not found")
        };

        metrics.Add(settings, cycle);

        Assert.Equal(0, metrics.DnsFailures);
        Assert.Equal(0, metrics.CreateStatistics().Dns.Attempts);
    }

    private static TestCycleResult Cycle(DatabaseResult database) => new(
        1,
        DateTimeOffset.UtcNow,
        new DnsResult(StepStatus.Success, "127.0.0.1", 1, ""),
        new StepResult(StepStatus.Success, 2, "64", ""),
        new TcpResult(StepStatus.Success, 3, "127.0.0.1", "127.0.0.1", ""),
        database);

    private static TestCycleResult Cycle(long number, StepResult ping) => new(
        number,
        DateTimeOffset.UtcNow,
        DnsResult.Skipped(),
        ping,
        TcpResult.Skipped(),
        DatabaseResult.Skipped());

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
        true,
        "result.csv",
        "result.txt");
}
