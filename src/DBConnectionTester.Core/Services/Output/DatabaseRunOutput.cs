using DBConnectionTester.Application;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Services.Output;

public sealed class DatabaseRunOutput : IRunOutput
{
    private readonly RunRepository repository;
    private readonly RunWriteLease lease;
    private bool disposed;

    private DatabaseRunOutput(Guid runId, RunRepository repository, RunWriteLease lease)
    {
        RunId = runId;
        this.repository = repository;
        this.lease = lease;
    }

    public Guid RunId { get; }
    public OutputPaths? LegacyPaths => null;

    public static async Task<DatabaseRunOutput> CreateAsync(
        SqliteApplicationStore store,
        TestSettings settings)
    {
        var lease = RunWriteLease.Acquire(store);
        try
        {
            var repository = new RunRepository(store);
            var runId = Guid.NewGuid();
            await repository.BeginAsync(runId, settings, settings.ProfileId);
            return new DatabaseRunOutput(runId, repository, lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public Task WriteCycleAsync(TestCycleResult cycle, CancellationToken token) =>
        repository.WriteCycleAsync(RunId, cycle, token);

    public Task CompleteAsync(RunSummary summary, RunMetrics metrics)
    {
        var status = summary.TerminationReason switch
        {
            RunTerminationReason.PlannedCountCompleted => PersistedRunStatus.Completed,
            RunTerminationReason.StoppedByUser => PersistedRunStatus.Stopped,
            RunTerminationReason.ExecutionFailed => PersistedRunStatus.Failed,
            _ => PersistedRunStatus.Interrupted
        };
        return repository.CompleteAsync(
            RunId,
            new RunCompletion(status, summary.TerminationReason, summary.Completed, summary.FailureMessage),
            metrics);
    }

    public Task AddWarningAsync(string code, string message) =>
        repository.AddWarningAsync(RunId, code, message);

    public ValueTask DisposeAsync()
    {
        if (disposed)
            return ValueTask.CompletedTask;
        disposed = true;
        lease.Dispose();
        return ValueTask.CompletedTask;
    }
}
