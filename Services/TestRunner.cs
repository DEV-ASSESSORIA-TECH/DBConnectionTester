using DBConnectionTester.Models;
using DBConnectionTester.Services.Output;

namespace DBConnectionTester.Services;

public sealed class TestRunner
{
    public async Task<RunSummary> RunAsync(
        TestSettings settings,
        IProgress<TestProgress>? progress,
        CancellationToken token)
    {
        await using var output = await RunOutputSession.CreateAsync(settings);
        var metrics = new RunMetrics();
        var stopped = false;

        try
        {
            for (long number = 1; settings.Continuous || number <= settings.TestCount; number++)
            {
                token.ThrowIfCancellationRequested();
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

                var cycle = new TestCycleResult(number, startedAt, dns, ping, tcp, database);
                metrics.Add(settings, cycle);
                await output.WriteCycleAsync(cycle, token);
                progress?.Report(metrics.CreateProgress());

                if (!settings.Continuous && number >= settings.TestCount)
                    break;
                if (settings.Interval > TimeSpan.Zero)
                    await Task.Delay(settings.Interval, token);
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }
        finally
        {
            await output.CompleteAsync(new RunSummary(metrics.Completed, stopped), metrics);
        }

        return new RunSummary(metrics.Completed, stopped);
    }
}
