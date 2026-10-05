using System.Net;
using System.Net.Sockets;
using DBConnectionTester.Models;
using DBConnectionTester.Services;
using DBConnectionTester.Services.Output;

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
        Assert.DoesNotContain("MySQL", CsvResultWriter.Header, StringComparison.OrdinalIgnoreCase);
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
            var settings = Settings(directory) with
            {
                Port = port,
                Timeout = TimeSpan.FromSeconds(1)
            };

            await new TestRunner().RunAsync(settings, progress, CancellationToken.None);

            Assert.NotNull(progress.Last);
            Assert.Equal(1, progress.Last.DatabaseConnectFailures);
            Assert.Equal(0, progress.Last.DatabaseQueryFailures);
            Assert.Equal(1, progress.Last.DatabaseFailures);
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
        var settings = Settings(directory);
        await File.WriteAllTextAsync(settings.CsvPath, "conteúdo anterior");

        try
        {
            await Assert.ThrowsAsync<IOException>(() =>
                new TestRunner().RunAsync(settings, progress: null, CancellationToken.None));

            Assert.Equal("conteúdo anterior", await File.ReadAllTextAsync(settings.CsvPath));
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
        var settings = Settings(directory);
        await File.WriteAllTextAsync(settings.TxtPath, "log anterior");

        try
        {
            await Assert.ThrowsAsync<IOException>(() =>
                new TestRunner().RunAsync(settings, progress: null, CancellationToken.None));

            Assert.Equal("log anterior", await File.ReadAllTextAsync(settings.TxtPath));
            Assert.False(File.Exists(settings.CsvPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TestSettings Settings(string directory) => new(
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
        false,
        false,
        true,
        Path.Combine(directory, "result.csv"),
        Path.Combine(directory, "result.txt"));

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
}
