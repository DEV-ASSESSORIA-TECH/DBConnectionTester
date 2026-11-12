using DBConnectionTester.Models;
using DBConnectionTester.Services;

namespace DBConnectionTester.Application;

public sealed class RunCoordinator : IDisposable
{
    private readonly ITestRunner runner;
    private CancellationTokenSource? cancellation;

    public RunCoordinator(ITestRunner runner)
    {
        this.runner = runner;
    }

    public bool IsRunning => Volatile.Read(ref cancellation) is not null;

    public async Task<RunSummary> StartAsync(TestSettings settings, IProgress<TestProgress>? progress)
    {
        var source = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref cancellation, source, null) is not null)
        {
            source.Dispose();
            throw new InvalidOperationException("Já existe um teste em execução.");
        }

        try
        {
            return await runner.RunAsync(settings, progress, source.Token);
        }
        finally
        {
            Interlocked.CompareExchange(ref cancellation, null, source);
            source.Dispose();
        }
    }

    public void Stop() => Volatile.Read(ref cancellation)?.Cancel();

    public void Dispose()
    {
        var source = Interlocked.Exchange(ref cancellation, null);
        source?.Cancel();
        source?.Dispose();
    }
}
