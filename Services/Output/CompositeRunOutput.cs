using DBConnectionTester.Application;
using DBConnectionTester.Models;

namespace DBConnectionTester.Services.Output;

public sealed class CompositeRunOutput : IRunOutput
{
    private readonly DatabaseRunOutput database;
    private RunOutputSession? legacy;

    public CompositeRunOutput(DatabaseRunOutput database, RunOutputSession legacy, OutputPaths paths)
    {
        this.database = database;
        this.legacy = legacy;
        LegacyPaths = paths;
    }

    public Guid RunId => database.RunId;
    public OutputPaths? LegacyPaths { get; }

    public async Task WriteCycleAsync(TestCycleResult cycle, CancellationToken token)
    {
        await database.WriteCycleAsync(cycle, token);
        if (legacy is null)
            return;
        try
        {
            await legacy.WriteCycleAsync(cycle, token);
        }
        catch (Exception exception)
        {
            await DisableLegacyAsync("DBT-OUTPUT-002", "A gravação CSV/TXT foi desativada", exception);
        }
    }

    public async Task CompleteAsync(RunSummary summary, RunMetrics metrics)
    {
        if (legacy is not null)
        {
            try
            {
                await legacy.CompleteAsync(summary, metrics);
            }
            catch (Exception exception)
            {
                await DisableLegacyAsync("DBT-OUTPUT-003", "A finalização CSV/TXT falhou", exception);
            }
        }
        await database.CompleteAsync(summary, metrics);
    }

    public async ValueTask DisposeAsync()
    {
        if (legacy is not null)
        {
            try
            {
                await legacy.DisposeAsync();
            }
            catch (Exception exception)
            {
                await database.AddWarningAsync(
                    "DBT-OUTPUT-004",
                    $"O fechamento da saída CSV/TXT falhou: {exception.Message}");
            }
            legacy = null;
        }
        await database.DisposeAsync();
    }

    private async Task DisableLegacyAsync(string code, string prefix, Exception exception)
    {
        var failed = legacy;
        legacy = null;
        if (failed is not null)
        {
            try
            {
                await failed.DisposeAsync();
            }
            catch
            {
                // O aviso original é mais relevante que uma segunda falha de descarte.
            }
        }
        await database.AddWarningAsync(code, $"{prefix}: {exception.Message}");
    }
}
