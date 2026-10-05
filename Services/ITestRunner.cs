using DBConnectionTester.Models;

namespace DBConnectionTester.Services;

public interface ITestRunner
{
    Task<RunSummary> RunAsync(
        TestSettings settings,
        IProgress<TestProgress>? progress,
        CancellationToken token);
}
