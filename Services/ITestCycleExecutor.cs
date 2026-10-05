using DBConnectionTester.Models;

namespace DBConnectionTester.Services;

public interface ITestCycleExecutor
{
    Task<TestCycleResult> ExecuteAsync(
        TestSettings settings,
        long number,
        CancellationToken token);
}

public sealed class TestCycleExecutor : ITestCycleExecutor
{
    public async Task<TestCycleResult> ExecuteAsync(
        TestSettings settings,
        long number,
        CancellationToken token)
    {
        var startedAt = DateTimeOffset.Now;
        var dns = settings.Profile.UsesNetwork
            ? await NetworkTester.TestDnsAsync(settings.Host, settings.Timeout, token)
            : DnsResult.Skipped();
        var ping = settings.Ping
            ? await NetworkTester.TestPingAsync(settings.Host, settings.Timeout, token)
            : StepResult.Skipped();
        var tcp = settings.Tcp
            ? await NetworkTester.TestTcpAsync(settings.Host, settings.Port, settings.Timeout, token)
            : TcpResult.Skipped();
        var database = settings.DatabaseTest
            ? await DatabaseTester.TestAsync(settings, token)
            : DatabaseResult.Skipped();

        return new TestCycleResult(number, startedAt, dns, ping, tcp, database);
    }
}
