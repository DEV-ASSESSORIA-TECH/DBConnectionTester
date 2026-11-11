using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Output;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Services.Export;

public sealed class RunExportService
{
    private const int ExportPageSize = 250;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };
    private readonly IRunHistoryRepository history;

    public RunExportService(IRunHistoryRepository history) => this.history = history;

    public async Task<RunExportResult> ExportAsync(
        Guid runId,
        RunExportFormat format,
        string destinationPath,
        CancellationToken token = default)
    {
        var details = await RequireRunAsync(runId, token);
        await using var stream = CreateNewFile(destinationPath);
        switch (format)
        {
            case RunExportFormat.Csv:
                await WriteCsvAsync(details, stream, token);
                break;
            case RunExportFormat.Text:
                await WriteTextAsync(details, stream, token);
                break;
            case RunExportFormat.Json:
                await WriteJsonAsync(details, stream, token);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
        return new RunExportResult(runId, [Path.GetFullPath(destinationPath)]);
    }

    public async Task<RunExportResult> ExportZipAsync(Guid runId, string destinationPath, CancellationToken token = default)
    {
        var details = await RequireRunAsync(runId, token);
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "DBConnectionTester", "exports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var baseName = $"run-{runId:D}";
            var csv = Path.Combine(temporaryDirectory, baseName + ".csv");
            var text = Path.Combine(temporaryDirectory, baseName + ".txt");
            var json = Path.Combine(temporaryDirectory, baseName + ".json");
            await WriteFileAsync(csv, stream => WriteCsvAsync(details, stream, token));
            await WriteFileAsync(text, stream => WriteTextAsync(details, stream, token));
            await WriteFileAsync(json, stream => WriteJsonAsync(details, stream, token));

            var files = new[] { csv, text, json };
            var manifest = new RunExportManifest(1, runId, DateTimeOffset.UtcNow,
                files.Select(path => new RunExportManifestFile(Path.GetFileName(path), Sha256(path))).ToArray());
            var manifestPath = Path.Combine(temporaryDirectory, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false), token);

            await using var destination = CreateNewFile(destinationPath);
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
            foreach (var path in files.Append(manifestPath))
                archive.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
            return new RunExportResult(runId, [Path.GetFullPath(destinationPath)]);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private async Task WriteCsvAsync(RunHistoryDetails details, Stream stream, CancellationToken token)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true);
        await writer.WriteLineAsync(CsvResultWriter.Header.AsMemory(), token);
        await foreach (var cycle in ReadCyclesAsync(details.Run.RunId, token))
            await writer.WriteLineAsync(BuildCsvRow(details.Run, cycle).AsMemory(), token);
        await writer.FlushAsync(token);
    }

    private async Task WriteTextAsync(RunHistoryDetails details, Stream stream, CancellationToken token)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true);
        var run = details.Run;
        await writer.WriteLineAsync("DB CONNECTION TESTER - EXECUÇÃO ARMAZENADA".AsMemory(), token);
        await writer.WriteLineAsync($"RunId: {run.RunId:D}".AsMemory(), token);
        await writer.WriteLineAsync($"Início: {run.StartedAt:O}".AsMemory(), token);
        await writer.WriteLineAsync($"Fim: {(run.FinishedAt is null ? "N/A" : run.FinishedAt.Value.ToString("O"))}".AsMemory(), token);
        await writer.WriteLineAsync($"Estado: {run.Status} | Encerramento: {run.TerminationReason?.ToString() ?? "N/A"}".AsMemory(), token);
        await writer.WriteLineAsync($"Máquina: {run.MachineName} | Tipo: {DatabaseProfiles.Get(run.DatabaseType).DisplayName}".AsMemory(), token);
        await writer.WriteLineAsync($"Destino: {run.Target} | Ciclos: {run.CompletedCycles}".AsMemory(), token);
        if (!string.IsNullOrWhiteSpace(run.FailureMessage))
            await writer.WriteLineAsync($"Falha: {run.FailureMessage}".AsMemory(), token);
        await writer.WriteLineAsync(new string('-', 120).AsMemory(), token);

        await foreach (var cycle in ReadCyclesAsync(run.RunId, token))
        {
            var stages = cycle.Stages.ToDictionary(stage => stage.Stage);
            await writer.WriteLineAsync(
                $"{cycle.StartedAt:O} | #{cycle.Number} | {string.Join(" | ", stages.Values.Select(s => $"{s.Stage}={s.Status.ToOutputText()} {s.ElapsedMs}ms"))}".AsMemory(), token);
            foreach (var stage in cycle.Stages.Where(stage => stage.DiagnosticCode is not null))
                await writer.WriteLineAsync($"  DIAGNÓSTICO {stage.Stage}: Código={stage.DiagnosticCode} | Sugestão={stage.SuggestionCode} | Confiança={stage.Confidence} | Detalhe={stage.TechnicalMessage}".AsMemory(), token);
        }

        if (details.StageSummaries.Count > 0)
        {
            await writer.WriteLineAsync(new string('-', 120).AsMemory(), token);
            await writer.WriteLineAsync("RESUMO".AsMemory(), token);
            foreach (var item in details.StageSummaries)
                await writer.WriteLineAsync($"{item.Stage}: OK={item.Statistics.Successes}/{item.Statistics.Attempts} | Falhas={item.Statistics.Failures} | Média={FormatMs(item.Statistics.AverageMs)} | P95={FormatMs(item.Statistics.P95Ms)}".AsMemory(), token);
        }
        foreach (var warning in details.Warnings)
            await writer.WriteLineAsync($"AVISO {warning.CreatedAt:O} [{warning.Code}] {warning.Message}".AsMemory(), token);
        await writer.FlushAsync(token);
    }

    private async Task WriteJsonAsync(RunHistoryDetails details, Stream stream, CancellationToken token)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", 1);
        writer.WritePropertyName("run");
        JsonSerializer.Serialize(writer, details.Run, JsonOptions);
        writer.WritePropertyName("settings");
        using (var settings = JsonDocument.Parse(details.SettingsSnapshotJson))
            settings.RootElement.WriteTo(writer);
        writer.WritePropertyName("stageSummaries");
        JsonSerializer.Serialize(writer, details.StageSummaries, JsonOptions);
        writer.WritePropertyName("warnings");
        JsonSerializer.Serialize(writer, details.Warnings, JsonOptions);
        writer.WriteStartArray("cycles");
        await foreach (var cycle in ReadCyclesAsync(details.Run.RunId, token))
            JsonSerializer.Serialize(writer, cycle, JsonOptions);
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(token);
    }

    private async IAsyncEnumerable<PersistedCycle> ReadCyclesAsync(
        Guid runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = await history.GetCyclesAsync(runId, new PageRequest(pageNumber, ExportPageSize), token);
            foreach (var cycle in page.Items)
                yield return cycle;
            if (page.Items.Count == 0 || pageNumber >= page.TotalPages)
                yield break;
        }
    }

    private async Task<RunHistoryDetails> RequireRunAsync(Guid runId, CancellationToken token) =>
        await history.GetDetailsAsync(runId, token)
        ?? throw new KeyNotFoundException($"A execução {runId:D} não existe.");

    private static string BuildCsvRow(RunHistoryItem run, PersistedCycle cycle)
    {
        var stages = cycle.Stages.ToDictionary(stage => stage.Stage, StringComparer.Ordinal);
        PersistedStageResult Stage(string name) => stages.TryGetValue(name, out var stage)
            ? stage : new PersistedStageResult(name, StepStatus.Skipped, 0, "", null, null, null, null, null, null, null, null, null, null);
        var dns = Stage("Dns");
        var ping = Stage("Ping");
        var tcp = Stage("Tcp");
        var connect = Stage("DatabaseConnect");
        var query = Stage("DatabaseQuery");
        var values = new List<string>
        {
            E(cycle.StartedAt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
            cycle.Number.ToString(CultureInfo.InvariantCulture), E(run.MachineName), E(DatabaseProfiles.Get(run.DatabaseType).DisplayName), E(run.Target),
            E(cycle.ResolvedIp), E(dns.Status.ToOutputText()), N(dns.ElapsedMs), E(ping.Status.ToOutputText()), N(ping.ElapsedMs), E(cycle.PingExtra),
            E(tcp.Status.ToOutputText()), N(tcp.ElapsedMs), E(cycle.TcpLocalIp), E(cycle.TcpRemoteIp),
            E(connect.Status.ToOutputText()), N(connect.ElapsedMs), E(query.Status.ToOutputText()), N(query.ElapsedMs), N(cycle.DatabaseTotalMs),
            E(Error(dns)), E(Error(ping)), E(Error(tcp)), E(Error(connect) ?? Error(query))
        };
        AddDiagnostic(values, dns);
        AddDiagnostic(values, ping);
        AddDiagnostic(values, tcp);
        AddDiagnostic(values, connect);
        AddDiagnostic(values, query);
        return string.Join(";", values);
    }

    private static void AddDiagnostic(ICollection<string> values, PersistedStageResult stage)
    {
        values.Add(E(stage.DiagnosticCode)); values.Add(E(stage.SuggestionCode)); values.Add(E(stage.Provider));
        values.Add(E(stage.ProviderCode)); values.Add(E(stage.SqlState)); values.Add(E(stage.NativeCode)); values.Add(E(stage.TechnicalMessage));
    }

    private static string? Error(PersistedStageResult stage) => stage.DiagnosticCode is null ? null : $"[{stage.DiagnosticCode}] {stage.UserMessage}";
    private static string E(string? value) => CsvResultWriter.Escape(value);
    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string FormatMs(double? value) => value is null ? "N/A" : value.Value.ToString("0.0", CultureInfo.InvariantCulture) + "ms";

    private static FileStream CreateNewFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return new FileStream(fullPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
    }

    private static async Task WriteFileAsync(string path, Func<Stream, Task> write)
    {
        await using var stream = CreateNewFile(path);
        await write(stream);
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
