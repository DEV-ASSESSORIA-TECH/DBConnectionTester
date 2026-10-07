using System.IO.Compression;
using System.Text.Json;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Export;
using DBConnectionTester.Services.Output;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Tests;

public sealed class RunExportServiceTests
{
    [Fact]
    public async Task ExportsCsvTextAndJsonFromStoredRun()
    {
        using var fixture = await ExportFixture.CreateAsync();
        var runId = await fixture.AddRunAsync();
        var csv = fixture.PathFor("run.csv");
        var text = fixture.PathFor("run.txt");
        var json = fixture.PathFor("run.json");

        await fixture.Exporter.ExportAsync(runId, RunExportFormat.Csv, csv);
        await fixture.Exporter.ExportAsync(runId, RunExportFormat.Text, text);
        await fixture.Exporter.ExportAsync(runId, RunExportFormat.Json, json);

        var csvLines = await File.ReadAllLinesAsync(csv);
        Assert.Equal(CsvResultWriter.Header, csvLines[0].TrimStart('\uFEFF'));
        Assert.Equal(2, csvLines.Length);
        Assert.Contains("'=formula:3306", csvLines[1]);
        Assert.Contains(DiagnosticCodes.TcpRefused, csvLines[1]);
        Assert.Contains("RESUMO", await File.ReadAllTextAsync(text));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(json));
        Assert.Equal(1, document.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("Completed", document.RootElement.GetProperty("run").GetProperty("status").GetString());
        Assert.Single(document.RootElement.GetProperty("cycles").EnumerateArray());
        Assert.DoesNotContain("do-not-export", await File.ReadAllTextAsync(json));
    }

    [Fact]
    public async Task ZipContainsAllFormatsAndChecksummedManifest()
    {
        using var fixture = await ExportFixture.CreateAsync();
        var runId = await fixture.AddRunAsync();
        var zipPath = fixture.PathFor("run.zip");

        await fixture.Exporter.ExportZipAsync(runId, zipPath);

        using var archive = ZipFile.OpenRead(zipPath);
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(".csv"));
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(".txt"));
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(".json") && entry.FullName != "manifest.json");
        var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "manifest.json");
        using var reader = new StreamReader(manifestEntry.Open());
        using var manifest = JsonDocument.Parse(await reader.ReadToEndAsync());
        Assert.Equal(3, manifest.RootElement.GetProperty("files").GetArrayLength());
        Assert.All(manifest.RootElement.GetProperty("files").EnumerateArray(), file =>
            Assert.Equal(64, file.GetProperty("sha256").GetString()!.Length));
    }

    [Fact]
    public async Task NeverOverwritesAnExistingExport()
    {
        using var fixture = await ExportFixture.CreateAsync();
        var runId = await fixture.AddRunAsync();
        var path = fixture.PathFor("occupied.json");
        await File.WriteAllTextAsync(path, "keep");

        await Assert.ThrowsAsync<IOException>(() => fixture.Exporter.ExportAsync(runId, RunExportFormat.Json, path));
        Assert.Equal("keep", await File.ReadAllTextAsync(path));
    }

    private sealed class ExportFixture : IDisposable
    {
        private ExportFixture(string root, SqliteApplicationStore store)
        {
            Root = root;
            Writer = new RunRepository(store);
            Exporter = new RunExportService(new RunHistoryRepository(store));
        }

        private string Root { get; }
        private RunRepository Writer { get; }
        public RunExportService Exporter { get; }
        public string PathFor(string name) => Path.Combine(Root, name);

        public static async Task<ExportFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "DBConnectionTester.Tests", Guid.NewGuid().ToString("N"));
            var store = await SqliteApplicationStore.OpenOrCreateAsync(Path.Combine(root, "data.db"), StorageScope.Custom);
            return new ExportFixture(root, store);
        }

        public async Task<Guid> AddRunAsync()
        {
            var settings = new TestSettings(DatabaseType.MySqlMariaDb, "=formula", NetworkPort.Create(3306), "user", "do-not-export", "db", "",
                SqlServerAuthentication.SqlLogin, "", RunCount.Create(1), false, TestInterval.Create(TimeSpan.Zero), StageTimeout.Create(TimeSpan.FromSeconds(1)), true, true, true, true);
            var diagnostic = new DiagnosticIssue(DiagnosticCodes.TcpRefused, "SUGGEST", DiagnosticLayer.Tcp,
                "Conexão recusada", "Connection refused", null, DiagnosticConfidence.Exact);
            var cycle = new TestCycleResult(1, DateTimeOffset.UtcNow,
                new DnsResult(StepStatus.Success, "127.0.0.1", 1, null), new StepResult(StepStatus.Success, 2, "64", null),
                new TcpResult(StepStatus.Failed, 3, "127.0.0.1", "127.0.0.1", diagnostic),
                new DatabaseResult(StepStatus.Success, 4, StepStatus.Success, 5, 9, null, null));
            var runId = Guid.NewGuid();
            await Writer.BeginAsync(runId, settings);
            await Writer.WriteCycleAsync(runId, cycle);
            await Writer.AddWarningAsync(runId, "LEGACY", "Saída legada indisponível");
            var metrics = new RunMetrics();
            metrics.Add(settings, cycle);
            await Writer.CompleteAsync(runId, new RunCompletion(PersistedRunStatus.Completed, RunTerminationReason.PlannedCountCompleted, 1, null), metrics);
            return runId;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
