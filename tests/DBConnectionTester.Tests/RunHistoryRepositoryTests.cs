using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Tests;

public sealed class RunHistoryRepositoryTests
{
    [Fact]
    public async Task RecentRunsReturnOnlyNewestThreeAndDoNotDependOnCycles()
    {
        using var fixture = await HistoryFixture.CreateAsync();
        Assert.Empty(await fixture.History.GetRecentAsync());
        await fixture.AddRunAsync("oldest", PersistedRunStatus.Completed);
        var third = await fixture.AddRunAsync("third", PersistedRunStatus.Stopped);
        var second = await fixture.AddRunAsync("second", PersistedRunStatus.Failed);
        var newest = Guid.NewGuid();
        await fixture.Writer.BeginAsync(newest, Settings("newest-without-cycles"));
        var recent = await fixture.History.GetRecentAsync();
        Assert.Equal(new[] { newest, second, third }, recent.Select(item => item.RunId));
        Assert.Equal(0, recent[0].CompletedCycles);
        Assert.Equal(PersistedRunStatus.Running, recent[0].Status);
        Assert.DoesNotContain(recent, item => item.Target.Contains("oldest"));
    }

    [Fact]
    public async Task SearchesRunsWithFiltersAndPagination()
    {
        using var fixture = await HistoryFixture.CreateAsync();
        var completed = await fixture.AddRunAsync("server-a", PersistedRunStatus.Completed, withDiagnostic: true);
        await fixture.AddRunAsync("server-b", PersistedRunStatus.Stopped);
        await fixture.AddRunAsync("server-a-secondary", PersistedRunStatus.Completed);

        var page = await fixture.History.SearchAsync(
            new RunHistoryFilter(Target: "server-a", Status: PersistedRunStatus.Completed),
            new PageRequest(1, 1));
        var diagnostic = await fixture.History.SearchAsync(
            new RunHistoryFilter(DiagnosticCode: DiagnosticCodes.TcpRefused),
            new PageRequest());

        Assert.Equal(2, page.TotalItems);
        Assert.Single(page.Items);
        Assert.Equal(2, page.TotalPages);
        Assert.Single(diagnostic.Items);
        Assert.Equal(completed, diagnostic.Items[0].RunId);
    }

    [Fact]
    public async Task LoadsDetailsAndOnlyTheRequestedCyclePage()
    {
        using var fixture = await HistoryFixture.CreateAsync();
        var runId = Guid.NewGuid();
        await fixture.Writer.BeginAsync(runId, Settings("server"));
        await fixture.Writer.WriteCycleAsync(runId, Cycle(1));
        await fixture.Writer.WriteCycleAsync(runId, Cycle(2));
        await fixture.Writer.AddWarningAsync(runId, "LEGACY", "Falha no TXT");
        var metrics = new RunMetrics();
        metrics.Add(Settings("server"), Cycle(1));
        metrics.Add(Settings("server"), Cycle(2));
        await fixture.Writer.CompleteAsync(runId,
            new RunCompletion(PersistedRunStatus.Completed, RunTerminationReason.PlannedCountCompleted, 2, null), metrics);

        var details = await fixture.History.GetDetailsAsync(runId);
        var cycles = await fixture.History.GetCyclesAsync(runId, new PageRequest(2, 1));

        Assert.NotNull(details);
        Assert.Equal(5, details.StageSummaries.Count);
        Assert.Single(details.Warnings);
        Assert.Equal(2, cycles.TotalItems);
        Assert.Single(cycles.Items);
        Assert.Equal(2, cycles.Items[0].Number);
        Assert.Equal(5, cycles.Items[0].Stages.Count);
    }

    private static TestSettings Settings(string host) => new(
        DatabaseType.MySqlMariaDb, host, NetworkPort.Create(3306), "user", "never-store-me", "database", "",
        SqlServerAuthentication.SqlLogin, "", RunCount.Create(2), false, TestInterval.Create(TimeSpan.Zero),
        StageTimeout.Create(TimeSpan.FromSeconds(1)), true, true, true, true);

    private static TestCycleResult Cycle(long number, bool withDiagnostic = false)
    {
        var diagnostic = withDiagnostic
            ? new DiagnosticIssue(DiagnosticCodes.TcpRefused, "TRY", DiagnosticLayer.Tcp, "Recusado", "detail", null, DiagnosticConfidence.Exact)
            : null;
        return new TestCycleResult(number, DateTimeOffset.UtcNow,
            new DnsResult(StepStatus.Success, "127.0.0.1", 1, null),
            new StepResult(StepStatus.Success, 2, "64", null),
            new TcpResult(diagnostic is null ? StepStatus.Success : StepStatus.Failed, 3, "127.0.0.1", "127.0.0.1", diagnostic),
            new DatabaseResult(StepStatus.Success, 4, StepStatus.Success, 5, 9, null, null));
    }

    private sealed class HistoryFixture : IDisposable
    {
        private HistoryFixture(string root, SqliteApplicationStore store)
        {
            Root = root;
            Writer = new RunRepository(store);
            History = new RunHistoryRepository(store);
        }

        private string Root { get; }
        public RunRepository Writer { get; }
        public RunHistoryRepository History { get; }

        public static async Task<HistoryFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "DBConnectionTester.Tests", Guid.NewGuid().ToString("N"));
            var store = await SqliteApplicationStore.OpenOrCreateAsync(Path.Combine(root, "data.db"), StorageScope.Custom);
            return new HistoryFixture(root, store);
        }

        public async Task<Guid> AddRunAsync(string host, PersistedRunStatus status, bool withDiagnostic = false)
        {
            var runId = Guid.NewGuid();
            var settings = Settings(host);
            var cycle = Cycle(1, withDiagnostic);
            await Writer.BeginAsync(runId, settings);
            await Writer.WriteCycleAsync(runId, cycle);
            var metrics = new RunMetrics();
            metrics.Add(settings, cycle);
            await Writer.CompleteAsync(runId,
                new RunCompletion(status, status == PersistedRunStatus.Stopped
                    ? RunTerminationReason.StoppedByUser : RunTerminationReason.PlannedCountCompleted, 1, null), metrics);
            await Task.Delay(2);
            return runId;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
