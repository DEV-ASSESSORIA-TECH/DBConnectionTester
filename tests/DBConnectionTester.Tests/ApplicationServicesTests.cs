using DBConnectionTester.Application;
using DBConnectionTester.Models;
using DBConnectionTester.Services;

namespace DBConnectionTester.Tests;

public sealed class ApplicationServicesTests
{
    [Fact]
    public void ValidatorRejectsMissingNetworkHost()
    {
        var validator = new TestSettingsValidator();

        var result = validator.Validate(Input() with { Host = "" });

        Assert.False(result.IsValid);
        Assert.Contains("host", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DnsCanBeTheOnlySelectedLayer()
    {
        var validator = new TestSettingsValidator();

        var result = validator.Validate(Input() with
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
    public void ValidatorRejectsInvalidExecutionAndNetworkValues()
    {
        var validator = new TestSettingsValidator();
        var valid = Input();
        var invalidInputs = new[]
        {
            valid with { Port = 0 },
            valid with { Port = 65_536 },
            valid with { TestCount = 0 },
            valid with { TestCount = RunCount.Maximum + 1 },
            valid with { Interval = TimeSpan.FromMilliseconds(-1) },
            valid with { Interval = TestInterval.Maximum + TimeSpan.FromMilliseconds(1) },
            valid with { Timeout = TimeSpan.Zero },
            valid with { Timeout = StageTimeout.Maximum + TimeSpan.FromSeconds(1) }
        };

        Assert.All(invalidInputs, input => Assert.False(validator.Validate(input).IsValid));
    }

    [Fact]
    public void ValidatorRequiresCredentialsOnlyWhenAuthenticationUsesThem()
    {
        var validator = new TestSettingsValidator();

        var sqlLogin = validator.Validate(Input() with
        {
            DatabaseType = DatabaseType.SqlServer,
            SqlServerAuthentication = SqlServerAuthentication.SqlLogin,
            User = " "
        });
        var windowsAuthentication = validator.Validate(Input() with
        {
            DatabaseType = DatabaseType.SqlServer,
            SqlServerAuthentication = SqlServerAuthentication.Windows,
            User = " "
        });

        Assert.False(sqlLogin.IsValid);
        Assert.True(windowsAuthentication.IsValid);
    }

    [Fact]
    public void ValidatorProducesTypedDomainValues()
    {
        var result = new TestSettingsValidator().Validate(Input());

        Assert.True(result.IsValid);
        Assert.Equal(3306, result.Settings!.Port!.Value);
        Assert.Equal(1, result.Settings.TestCount.Value);
        Assert.Equal(TimeSpan.Zero, result.Settings.Interval.Value);
        Assert.Equal(TimeSpan.FromSeconds(1), result.Settings.Timeout.Value);
    }

    [Fact]
    public async Task OutputPolicyCreatesUniqueLegacyPairWithoutOverwriting()
    {
        var directory = CreateTemporaryDirectory();
        var policy = new OutputPathPolicy();
        var runId = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2026, 10, 7, 12, 30, 0, TimeSpan.Zero);

        try
        {
            var first = policy.PrepareLegacy(directory, runId, startedAt);
            await File.WriteAllTextAsync(first.CsvPath, "anterior");
            var second = policy.PrepareLegacy(directory, runId, startedAt);

            Assert.NotEqual(first.CsvPath, second.CsvPath);
            Assert.Equal("anterior", await File.ReadAllTextAsync(first.CsvPath));
            Assert.EndsWith(".txt", second.TxtPath, StringComparison.OrdinalIgnoreCase);
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

    private static TestSettingsInput Input() => new(
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
        true);

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
                return new RunSummary(Guid.NewGuid(), 0, RunTerminationReason.PlannedCountCompleted, null);
            }
            catch (OperationCanceledException)
            {
                return new RunSummary(Guid.NewGuid(), 0, RunTerminationReason.StoppedByUser, null);
            }
        }
    }
}
