using DBConnectionTester.Models;
using DBConnectionTester.Services;

namespace DBConnectionTester.Tests;

public sealed class TestCycleExecutorTests
{
    [Fact]
    public async Task DisabledDnsIsSkippedWithoutResolvingHost()
    {
        var settings = new TestSettings(
            DatabaseType.MySqlMariaDb,
            "host-that-must-not-be-resolved.invalid",
            3306,
            "",
            "",
            "",
            "",
            SqlServerAuthentication.SqlLogin,
            "",
            1,
            false,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            false,
            false,
            false,
            false,
            "result.csv",
            "result.txt");

        var cycle = await new TestCycleExecutor().ExecuteAsync(settings, 1, CancellationToken.None);

        Assert.Equal(StepStatus.Skipped, cycle.Dns.Status);
        Assert.Equal(StepStatus.Skipped, cycle.Ping.Status);
        Assert.Equal(StepStatus.Skipped, cycle.Tcp.Status);
        Assert.Equal(StepStatus.Skipped, cycle.Database.ConnectStatus);
    }
}
