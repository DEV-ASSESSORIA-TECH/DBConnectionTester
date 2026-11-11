using DBConnectionTester.Application;
using DBConnectionTester.Models;

namespace DBConnectionTester.Services.Output;

public interface IRunOutput : IAsyncDisposable
{
    Guid RunId { get; }
    OutputPaths? LegacyPaths { get; }
    Task WriteCycleAsync(TestCycleResult cycle, CancellationToken token);
    Task CompleteAsync(RunSummary summary, RunMetrics metrics);
}
