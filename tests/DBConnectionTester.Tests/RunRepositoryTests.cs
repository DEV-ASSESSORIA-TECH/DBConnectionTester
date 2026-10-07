using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using DBConnectionTester.Services.Output;
using Microsoft.Data.Sqlite;

namespace DBConnectionTester.Tests;

public sealed class RunRepositoryTests
{
    [Fact]
    public async Task DatabaseOutputLinksRunToSelectedProfile()
    {
        using var fixture = await RunFixture.CreateAsync();
        var profiles = new PersistentSettingsRepository(fixture.Store);
        var saved = await profiles.SaveAsync(new ConnectionProfileDraft(
            null, "Produção", DatabaseType.MySqlMariaDb, "server", 3306, "user", "db", "",
            SqlServerAuthentication.SqlLogin, "", ProfileExecutionDefaults.Default));
        var settings = Settings("temporary-password") with { ProfileId = saved.ProfileId };

        await using var output = await DatabaseRunOutput.CreateAsync(fixture.Store, settings);

        await using var connection = await fixture.Store.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT profile_id FROM runs WHERE run_id = $id;";
        command.Parameters.AddWithValue("$id", output.RunId.ToString("D"));
        Assert.Equal(saved.ProfileId.ToString("D"), await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task PersistsCycleAndCompletionWithoutPassword()
    {
        using var fixture = await RunFixture.CreateAsync();
        var runId = Guid.NewGuid();
        var settings = Settings("top-secret-password");
        var metrics = new RunMetrics();
        var cycle = SuccessfulCycle();

        await fixture.Repository.BeginAsync(runId, settings);
        await fixture.Repository.WriteCycleAsync(runId, cycle);
        metrics.Add(settings, cycle);
        await fixture.Repository.CompleteAsync(
            runId,
            new RunCompletion(PersistedRunStatus.Completed, RunTerminationReason.PlannedCountCompleted, 1, null),
            metrics);

        await using var connection = await fixture.Store.OpenConnectionAsync();
        Assert.Equal("Completed", await ScalarStringAsync(connection,
            "SELECT status FROM runs WHERE run_id = $id;", runId));
        Assert.Equal(1L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM cycles WHERE run_id = $id;", runId));
        Assert.Equal(5L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM stage_results WHERE run_id = $id;", runId));
        Assert.Equal(5L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM run_stage_summaries WHERE run_id = $id;", runId));
        var snapshot = await ScalarStringAsync(connection,
            "SELECT settings_snapshot_json FROM runs WHERE run_id = $id;", runId);
        Assert.DoesNotContain("top-secret-password", snapshot);
        Assert.DoesNotContain("password", snapshot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecoversRunningEntriesAsInterrupted()
    {
        using var fixture = await RunFixture.CreateAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await fixture.Repository.BeginAsync(first, Settings("one"));
        await fixture.Repository.BeginAsync(second, Settings("two"));

        Assert.Equal(2, await fixture.Repository.RecoverInterruptedAsync());

        await using var connection = await fixture.Store.OpenConnectionAsync();
        Assert.Equal(2L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM runs WHERE status = 'Interrupted';", null));
    }

    [Fact]
    public async Task OnlyOneWriterLeaseCanBeHeldAndReadersRemainAvailable()
    {
        using var fixture = await RunFixture.CreateAsync();
        using var first = RunWriteLease.Acquire(fixture.Store);

        Assert.Throws<RunAlreadyActiveException>(() => RunWriteLease.Acquire(fixture.Store));
        await using var connection = await fixture.Store.OpenConnectionAsync();
        Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT 1;", null));

        first.Dispose();
        using var next = RunWriteLease.Acquire(fixture.Store);
    }

    private static TestSettings Settings(string password) => new(
        DatabaseType.MySqlMariaDb,
        "127.0.0.1",
        NetworkPort.Create(3306),
        "user",
        password,
        "database",
        "",
        SqlServerAuthentication.SqlLogin,
        "",
        RunCount.Create(1),
        false,
        TestInterval.Create(TimeSpan.Zero),
        StageTimeout.Create(TimeSpan.FromSeconds(1)),
        true,
        true,
        true,
        true);

    private static TestCycleResult SuccessfulCycle() => new(
        1,
        DateTimeOffset.UtcNow,
        new DnsResult(StepStatus.Success, "127.0.0.1", 1, null),
        new StepResult(StepStatus.Success, 2, "64", null),
        new TcpResult(StepStatus.Success, 3, "127.0.0.1", "127.0.0.1", null),
        new DatabaseResult(StepStatus.Success, 4, StepStatus.Success, 5, 9, null, null));

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql, Guid? id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (id is Guid value)
            command.Parameters.AddWithValue("$id", value.ToString("D"));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql, Guid? id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (id is Guid value)
            command.Parameters.AddWithValue("$id", value.ToString("D"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed class RunFixture : IDisposable
    {
        private RunFixture(string root, SqliteApplicationStore store)
        {
            Root = root;
            Store = store;
            Repository = new RunRepository(store);
        }

        public string Root { get; }
        public SqliteApplicationStore Store { get; }
        public RunRepository Repository { get; }

        public static async Task<RunFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "DBConnectionTester.Tests", Guid.NewGuid().ToString("N"));
            var store = await SqliteApplicationStore.OpenOrCreateAsync(
                Path.Combine(root, "data.db"),
                StorageScope.Custom);
            return new RunFixture(root, store);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
