using System.CommandLine;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Export;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Cli;

public sealed partial class CliApplication
{
    private async Task<int> ListProfilesAsync(ParseResult parse, CancellationToken token)
    {
        var store = await OpenStoreAsync(parse, false, token);
        WriteResult(parse, await new PersistentSettingsRepository(store).ListAsync(token));
        return 0;
    }

    private async Task<int> ShowProfileAsync(ParseResult parse, CancellationToken token)
    {
        var store = await OpenStoreAsync(parse, false, token);
        var profile = await new PersistentSettingsRepository(store).GetAsync(parse.GetValue<Guid>("id"), token)
            ?? throw new CliException(2, "O perfil informado não existe.");
        WriteResult(parse, profile);
        return 0;
    }

    private async Task<int> ListHistoryAsync(ParseResult parse, CancellationToken token)
    {
        var page = Page(parse);
        var from = parse.GetValue<DateTimeOffset?>("--from");
        var until = parse.GetValue<DateTimeOffset?>("--until");
        var status = parse.GetValue<PersistedRunStatus?>("--status");
        if (from > until || (status is { } value && !Enum.IsDefined(value)))
            throw new CliException(2, "Filtro inválido.");
        var store = await OpenStoreAsync(parse, false, token);
        var filter = new RunHistoryFilter(from, until, parse.GetValue<Guid?>("--profile"),
            parse.GetValue<string?>("--target"), status, parse.GetValue<string?>("--diagnostic"));
        WriteResult(parse, await new RunHistoryRepository(store).SearchAsync(filter, page, token));
        return 0;
    }

    private async Task<int> ShowHistoryAsync(ParseResult parse, CancellationToken token)
    {
        var includeCycles = parse.GetValue<bool>("--cycles");
        // Paging options only apply to the cycles page.
        var page = includeCycles ? Page(parse) : null;
        var store = await OpenStoreAsync(parse, false, token);
        var history = new RunHistoryRepository(store);
        var id = parse.GetValue<Guid>("id");
        var details = await history.GetDetailsAsync(id, token)
            ?? throw new CliException(2, "A execução informada não existe.");
        var cycles = page is not null ? await history.GetCyclesAsync(id, page, token) : null;
        WriteResult(parse, new { details, cycles });
        return 0;
    }

    private async Task<int> CyclesHistoryAsync(ParseResult parse, CancellationToken token)
    {
        var after = parse.GetValue<long>("--after");
        var limit = parse.GetValue<int>("--limit");
        if (after < 0 || limit is < 1 or > 500)
            throw new CliException(2, "--after deve ser 0 ou maior e --limit deve estar entre 1 e 500.");
        var store = await OpenStoreAsync(parse, false, token);
        var history = new RunHistoryRepository(store);
        var id = parse.GetValue<Guid>("id");
        var details = await history.GetDetailsAsync(id, token)
            ?? throw new CliException(2, "A execução informada não existe.");
        // One extra row tells whether another call is needed, without a COUNT.
        var cycles = await history.GetCyclesAfterAsync(id, after, limit + 1, token);
        var hasMore = cycles.Count > limit;
        var items = hasMore ? cycles.Take(limit).ToArray() : cycles.ToArray();
        WriteResult(parse, new
        {
            runId = id,
            status = details.Run.Status,
            items,
            lastCycle = items.Length > 0 ? items[^1].Number : after,
            hasMore
        });
        return 0;
    }

    private async Task<int> ExportAsync(ParseResult parse, CancellationToken token)
    {
        var store = await OpenStoreAsync(parse, false, token);
        var history = new RunHistoryRepository(store);
        var id = parse.GetValue<Guid>("id");
        var details = await history.GetDetailsAsync(id, token)
            ?? throw new CliException(2, "A execução informada não existe.");
        if (details.Run.Status == PersistedRunStatus.Running)
            throw new CliException(2, "Aguarde a finalização da execução antes de exportar.");
        var service = new RunExportService(history);
        var path = parse.GetValue<string>("--output")!;
        if (string.IsNullOrWhiteSpace(path)) throw new CliException(2, "Informe um novo arquivo de destino.");
        // Usage error, not an operational failure. The service still creates the file
        // with CreateNew, so a file appearing after this check is never overwritten.
        if (File.Exists(path) || Directory.Exists(path))
            throw new CliException(2, "O arquivo de destino já existe. Informe um novo caminho; a exportação não sobrescreve arquivos.");
        var format = parse.GetValue<string>("--format");
        var result = format == "zip" ? await service.ExportZipAsync(id, path, token) :
            await service.ExportAsync(id, format switch
            {
                "csv" => RunExportFormat.Csv, "txt" => RunExportFormat.Text, _ => RunExportFormat.Json
            }, path, token);
        output.WriteLine(JsonSerializerForExport(result));
        return 0;
    }

    private static string JsonSerializerForExport(RunExportResult result) =>
        System.Text.Json.JsonSerializer.Serialize(new { formatVersion = 1, data = result }, JsonOptions);
}
