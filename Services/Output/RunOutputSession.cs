using DBConnectionTester.Models;

namespace DBConnectionTester.Services.Output;

public sealed class RunOutputSession : IRunOutput
{
    private readonly CsvResultWriter csv;
    private readonly TextLogWriter log;

    private RunOutputSession(CsvResultWriter csv, TextLogWriter log)
    {
        this.csv = csv;
        this.log = log;
    }

    public static async Task<RunOutputSession> CreateAsync(TestSettings settings)
    {
        var csv = await CsvResultWriter.CreateAsync(settings);
        try
        {
            var log = await TextLogWriter.CreateAsync(settings);
            return new RunOutputSession(csv, log);
        }
        catch
        {
            await csv.DisposeAsync();
            TryDeleteNewFile(settings.CsvPath);
            throw;
        }
    }

    public async Task WriteCycleAsync(TestCycleResult cycle, CancellationToken token)
    {
        await csv.WriteCycleAsync(cycle, token);
        await log.WriteCycleAsync(cycle, token);
    }

    public Task CompleteAsync(RunSummary summary, RunMetrics metrics) => log.CompleteAsync(summary, metrics);

    public async ValueTask DisposeAsync()
    {
        await log.DisposeAsync();
        await csv.DisposeAsync();
    }

    private static void TryDeleteNewFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Preserve the original creation error. An empty file may remain for manual inspection.
        }
    }
}
