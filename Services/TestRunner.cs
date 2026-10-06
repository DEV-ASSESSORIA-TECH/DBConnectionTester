using DBConnectionTester.Models;
using DBConnectionTester.Services.Output;

namespace DBConnectionTester.Services;

public sealed class TestRunner : ITestRunner
{
    private readonly ITestCycleExecutor cycleExecutor;
    private readonly IRunOutputFactory outputFactory;

    public TestRunner()
        : this(new TestCycleExecutor(), new RunOutputFactory())
    {
    }

    public TestRunner(ITestCycleExecutor cycleExecutor, IRunOutputFactory outputFactory)
    {
        this.cycleExecutor = cycleExecutor;
        this.outputFactory = outputFactory;
    }

    public async Task<RunSummary> RunAsync(
        TestSettings settings,
        IProgress<TestProgress>? progress,
        CancellationToken token)
    {
        await using var output = await outputFactory.CreateAsync(settings);
        var metrics = new RunMetrics();
        var stopped = false;

        try
        {
            for (long number = 1; settings.Continuous || number <= settings.TestCount.Value; number++)
            {
                token.ThrowIfCancellationRequested();
                var cycle = await cycleExecutor.ExecuteAsync(settings, number, token);
                await output.WriteCycleAsync(cycle, CancellationToken.None);
                metrics.Add(settings, cycle);
                progress?.Report(metrics.CreateProgress(cycle));

                if (!settings.Continuous && number >= settings.TestCount.Value)
                    break;
                if (settings.Interval.Value > TimeSpan.Zero)
                    await Task.Delay(settings.Interval.Value, token);
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
