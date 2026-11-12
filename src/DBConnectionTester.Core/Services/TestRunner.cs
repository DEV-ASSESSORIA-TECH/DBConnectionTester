using System.Runtime.ExceptionServices;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Output;

namespace DBConnectionTester.Services;

public sealed class TestRunner : ITestRunner
{
    private readonly ITestCycleExecutor cycleExecutor;
    private readonly IRunOutputFactory outputFactory;

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
        var output = await outputFactory.CreateAsync(settings);
        var metrics = new RunMetrics();
        var terminationReason = RunTerminationReason.PlannedCountCompleted;
        Exception? failure = null;

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
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            terminationReason = RunTerminationReason.StoppedByUser;
        }
        catch (Exception exception)
        {
            failure = exception;
            terminationReason = RunTerminationReason.ExecutionFailed;
        }

        var summary = new RunSummary(
            output.RunId,
            metrics.Completed,
            terminationReason,
            failure?.Message,
            output.LegacyPaths?.CsvPath,
            output.LegacyPaths?.TxtPath);
        try
        {
            await output.CompleteAsync(summary, metrics);
        }
        catch (Exception exception)
        {
            failure = Combine(failure, exception, "A execução e a finalização do relatório falharam.");
        }

        try
        {
            await output.DisposeAsync();
        }
        catch (Exception exception)
        {
            failure = Combine(failure, exception, "A execução e o fechamento dos arquivos falharam.");
        }

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();

        return summary;
    }

    private static Exception Combine(Exception? current, Exception next, string message) =>
        current is null ? next : new AggregateException(message, current, next);
}
