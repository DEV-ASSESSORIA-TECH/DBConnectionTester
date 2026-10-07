using System.Net;
using System.Net.Sockets;
using DBConnectionTester.Models;
using DBConnectionTester.Services;
using DBConnectionTester.Services.Output;
using DBConnectionTester.Services.Storage;
using DBConnectionTester.Application;

namespace DBConnectionTester.Tests;

public sealed class TestRunnerTests
{
    [Fact]
    public void CsvSchemaIsGeneric()
    {
        Assert.Contains("DB_Type", CsvResultWriter.Header);
        Assert.Contains("DB_Connect_Status", CsvResultWriter.Header);
        Assert.Contains("DB_Query_Status", CsvResultWriter.Header);
        Assert.Contains("DB_Error", CsvResultWriter.Header);
        Assert.Contains("DB_Connect_Diagnostic_Code", CsvResultWriter.Header);
        Assert.Contains("DB_Query_Provider_Code", CsvResultWriter.Header);
        Assert.Contains("DNS_Suggestion_Code", CsvResultWriter.Header);
        Assert.DoesNotContain("MySQL", CsvResultWriter.Header, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1:A2)", "'@SUM(A1:A2)")]
    [InlineData("\t=1+1", "'\t=1+1")]
    [InlineData("safe text", "safe text")]
    public void CsvNeutralizesSpreadsheetFormulas(string input, string expected)
    {
        Assert.Equal(expected, CsvResultWriter.Escape(input));
    }

    [Fact]
    public async Task ConnectionFailureDoesNotAlsoCountAsQueryFailure()
    {
        var directory = CreateTemporaryDirectory();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var progress = new CapturingProgress();

        try
        {
            var settings = Settings() with
            {
                Port = NetworkPort.Create(port),
                Timeout = StageTimeout.Create(TimeSpan.FromSeconds(1))
            };
            var store = await SqliteApplicationStore.OpenOrCreateAsync(
                Path.Combine(directory, "data.db"),
                StorageScope.Custom);
            var runner = new TestRunner(
                new TestCycleExecutor(),
                new RunOutputFactory(store, new ApplicationSettings(ApplicationTheme.System, true, directory)));

            var summary = await runner.RunAsync(settings, progress, CancellationToken.None);

            Assert.NotNull(progress.Last);
            Assert.Equal(1, progress.Last.DatabaseConnectFailures);
            Assert.Equal(0, progress.Last.DatabaseQueryFailures);
            Assert.Equal(1, progress.Last.DatabaseFailures);
            Assert.NotNull(progress.Last.LatestCycle);
            Assert.Equal(1, progress.Last.LatestCycle.Number);
            var log = await File.ReadAllTextAsync(summary.TxtPath!);
            Assert.Contains("Mediana=", log);
            Assert.Contains("P95=", log);
            Assert.Contains("Maior sequência=", log);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingCsvIsNeverOverwritten()
    {
        var directory = CreateTemporaryDirectory();
        var settings = Settings();
        var paths = new OutputPaths(Path.Combine(directory, "result.csv"), Path.Combine(directory, "result.txt"));
        await File.WriteAllTextAsync(paths.CsvPath, "conteúdo anterior");

        try
        {
            await Assert.ThrowsAsync<IOException>(() =>
                RunOutputSession.CreateAsync(settings, paths));

            Assert.Equal("conteúdo anterior", await File.ReadAllTextAsync(paths.CsvPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingLogIsPreservedAndPartialCsvIsRemoved()
    {
        var directory = CreateTemporaryDirectory();
        var settings = Settings();
        var paths = new OutputPaths(Path.Combine(directory, "result.csv"), Path.Combine(directory, "result.txt"));
        await File.WriteAllTextAsync(paths.TxtPath, "log anterior");

        try
        {
            await Assert.ThrowsAsync<IOException>(() =>
                RunOutputSession.CreateAsync(settings, paths));

            Assert.Equal("log anterior", await File.ReadAllTextAsync(paths.TxtPath));
            Assert.False(File.Exists(paths.CsvPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationDuringOutputStillPersistsCompletedCycle()
    {
        using var cancellation = new CancellationTokenSource();
        var output = new CancellingOutput(cancellation);
        var runner = new TestRunner(new SuccessfulCycleExecutor(), new FixedOutputFactory(output));
        var settings = Settings() with { TestCount = RunCount.Create(2) };

        var summary = await runner.RunAsync(settings, progress: null, cancellation.Token);

        Assert.True(summary.Stopped);
        Assert.Equal(1, summary.Completed);
        Assert.True(output.CycleWritten);
        Assert.Equal(1, output.CompletedMetrics);
    }

    [Fact]
    public async Task SummaryFailureDoesNotMaskExecutionFailure()
    {
        var output = new FailingSummaryOutput();
        var runner = new TestRunner(new FailingCycleExecutor(), new FixedOutputFactory(output));

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            runner.RunAsync(Settings(), progress: null, CancellationToken.None));

        Assert.Collection(
            exception.InnerExceptions,
            error => Assert.IsType<InvalidOperationException>(error),
            error => Assert.IsType<IOException>(error));
        Assert.True(output.Disposed);
    }

    [Fact]
    public async Task DatabaseOutputPersistsStoppedRunAndCompletedCycle()
    {
        var directory = CreateTemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        try
        {
            var store = await SqliteApplicationStore.OpenOrCreateAsync(
                Path.Combine(directory, "data.db"),
                StorageScope.Custom);
            var runner = new TestRunner(
                new CancellingCycleExecutor(cancellation),
                new RunOutputFactory(store, ApplicationSettings.Default));

            var summary = await runner.RunAsync(
                Settings() with { TestCount = RunCount.Create(2) },
                progress: null,
                cancellation.Token);

            Assert.True(summary.Stopped);
            Assert.Equal(1, summary.Completed);
            await using var connection = await store.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT status || ':' || completed_cycles FROM runs WHERE run_id = $id;";
            command.Parameters.AddWithValue("$id", summary.RunId.ToString("D"));
            Assert.Equal("Stopped:1", await command.ExecuteScalarAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DatabaseOutputPersistsExecutionFailureBeforeRethrowing()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = await SqliteApplicationStore.OpenOrCreateAsync(
                Path.Combine(directory, "data.db"),
                StorageScope.Custom);
            var runner = new TestRunner(
                new FailingCycleExecutor(),
                new RunOutputFactory(store, ApplicationSettings.Default));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                runner.RunAsync(Settings(), progress: null, CancellationToken.None));

            await using var connection = await store.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT status || ':' || termination_reason FROM runs LIMIT 1;";
            Assert.Equal("Failed:ExecutionFailed", await command.ExecuteScalarAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UnavailableLegacyOutputAddsWarningButDatabaseRunCompletes()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = await SqliteApplicationStore.OpenOrCreateAsync(
                Path.Combine(directory, "data.db"),
                StorageScope.Custom);
            var runner = new TestRunner(
                new SuccessfulCycleExecutor(),
                new RunOutputFactory(store, new ApplicationSettings(ApplicationTheme.System, true, "\0")));

            var summary = await runner.RunAsync(Settings(), progress: null, CancellationToken.None);

            Assert.False(summary.Failed);
            Assert.Null(summary.CsvPath);
            await using var connection = await store.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM run_warnings WHERE run_id = $id;";
            command.Parameters.AddWithValue("$id", summary.RunId.ToString("D"));
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TestSettings Settings() => new(
        DatabaseType.MySqlMariaDb,
        "127.0.0.1",
        NetworkPort.Create(3306),
        "user",
        "password",
        "database",
        "",
        SqlServerAuthentication.SqlLogin,
        "SQL Anywhere 17",
        RunCount.Create(1),
        false,
        TestInterval.Create(TimeSpan.Zero),
        StageTimeout.Create(TimeSpan.FromSeconds(1)),
        true,
        false,
        false,
        true);

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "DBConnectionTester.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CapturingProgress : IProgress<TestProgress>
    {
        public TestProgress? Last { get; private set; }

        public void Report(TestProgress value) => Last = value;
    }

    private sealed class SuccessfulCycleExecutor : ITestCycleExecutor
    {
        public Task<TestCycleResult> ExecuteAsync(TestSettings settings, long number, CancellationToken token) =>
            Task.FromResult(new TestCycleResult(
                number,
                DateTimeOffset.UtcNow,
                DnsResult.Skipped(),
                StepResult.Skipped(),
                TcpResult.Skipped(),
                DatabaseResult.Skipped()));
    }

    private sealed class CancellingCycleExecutor(CancellationTokenSource cancellation) : ITestCycleExecutor
    {
        public Task<TestCycleResult> ExecuteAsync(TestSettings settings, long number, CancellationToken token)
        {
            cancellation.Cancel();
            return Task.FromResult(new TestCycleResult(
                number,
                DateTimeOffset.UtcNow,
                DnsResult.Skipped(),
                StepResult.Skipped(),
                TcpResult.Skipped(),
                DatabaseResult.Skipped()));
        }
    }

    private sealed class FixedOutputFactory(IRunOutput output) : IRunOutputFactory
    {
        public Task<IRunOutput> CreateAsync(TestSettings settings) => Task.FromResult(output);
    }

    private sealed class FailingCycleExecutor : ITestCycleExecutor
    {
        public Task<TestCycleResult> ExecuteAsync(TestSettings settings, long number, CancellationToken token) =>
            throw new InvalidOperationException("execution failure");
    }

    private sealed class FailingSummaryOutput : IRunOutput
    {
        public bool Disposed { get; private set; }
        public Guid RunId { get; } = Guid.NewGuid();
        public OutputPaths? LegacyPaths => null;

        public Task WriteCycleAsync(TestCycleResult cycle, CancellationToken token) => Task.CompletedTask;

        public Task CompleteAsync(RunSummary summary, RunMetrics metrics) =>
            throw new IOException("summary failure");

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellingOutput(CancellationTokenSource cancellation) : IRunOutput
    {
        public bool CycleWritten { get; private set; }
        public long CompletedMetrics { get; private set; }
        public Guid RunId { get; } = Guid.NewGuid();
        public OutputPaths? LegacyPaths => null;

        public Task WriteCycleAsync(TestCycleResult cycle, CancellationToken token)
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            CycleWritten = true;
            return Task.CompletedTask;
        }

        public Task CompleteAsync(RunSummary summary, RunMetrics metrics)
        {
            CompletedMetrics = metrics.Completed;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
