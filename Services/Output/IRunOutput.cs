using DBConnectionTester.Models;

namespace DBConnectionTester.Services.Output;

public interface IRunOutput : IAsyncDisposable
{
    Task WriteCycleAsync(TestCycleResult cycle, CancellationToken token);
    Task CompleteAsync(RunSummary summary, RunMetrics metrics);
}
