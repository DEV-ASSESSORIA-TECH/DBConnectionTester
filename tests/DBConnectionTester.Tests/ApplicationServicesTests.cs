using DBConnectionTester.Application;
using DBConnectionTester.Models;
using DBConnectionTester.Services;

namespace DBConnectionTester.Tests;

public sealed class ApplicationServicesTests
{
    [Fact]
    public void ValidatorRejectsMissingNetworkHost()
    {
        var validator = new TestSettingsValidator(new OutputPathPolicy());

        var result = validator.Validate(Input(Path.GetTempPath()) with { Host = "" });

        Assert.False(result.IsValid);
        Assert.Contains("host", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DnsCanBeTheOnlySelectedLayer()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        var validator = new TestSettingsValidator(new OutputPathPolicy());

        var result = validator.Validate(Input(outputPath) with
        {
            Ping = false,
            Tcp = false,
            DatabaseTest = false,
            Dns = true
        });

        Assert.True(result.IsValid);
        Assert.True(result.Settings!.Dns);
    }

    [Fact]
    public async Task OutputPolicyPreservesExistingRun()
    {
        var directory = CreateTemporaryDirectory();
        var originalPath = Path.Combine(directory, "result.csv");
        await File.WriteAllTextAsync(originalPath, "anterior");

        try
        {
            var result = new TestSettingsValidator(new OutputPathPolicy()).Validate(Input(originalPath));

            Assert.True(result.IsValid);
            Assert.NotEqual(originalPath, result.Settings!.CsvPath);
            Assert.Equal("anterior", await File.ReadAllTextAsync(originalPath));
            Assert.EndsWith(".txt", result.Settings.TxtPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CoordinatorPreventsParallelRunsAndStopsCurrentRun()
    {
        var runner = new BlockingRunner();
        using var coordinator = new RunCoordinator(runner);
        var firstRun = coordinator.StartAsync(Settings(), progress: null);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(Settings(), progress: null));

        coordinator.Stop();
        var summary = await firstRun;

        Assert.True(summary.Stopped);
        Assert.False(coordinator.IsRunning);
    }

    private static TestSettingsInput Input(string outputPath) => new(
        DatabaseType.MySqlMariaDb,
        "127.0.0.1",
        3306,
        "user",
        "password",
        "database",
        "",
        SqlServerAuthentication.SqlLogin,
        "SQL Anywhere 17",
        1,
        false,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        true,
        false,
        false,
        true,
        outputPath);

    private static TestSettings Settings() => new(
        DatabaseType.MySqlMariaDb,
        "127.0.0.1",
        3306,
        "user",
        "password",
        "database",
        "",
        SqlServerAuthentication.SqlLogin,
        "SQL Anywhere 17",
        1,
        false,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        true,
        false,
        false,
        true,
        "result.csv",
        "result.txt");

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "DBConnectionTester.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class BlockingRunner : ITestRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RunSummary> RunAsync(
            TestSettings settings,
            IProgress<TestProgress>? progress,
            CancellationToken token)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new RunSummary(0, false);
            }
            catch (OperationCanceledException)
            {
                return new RunSummary(0, true);
            }
        }
    }
}
