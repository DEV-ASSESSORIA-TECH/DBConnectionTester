using DBConnectionTester.Application;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Services.Output;

public interface IRunOutputFactory
{
    Task<IRunOutput> CreateAsync(TestSettings settings);
}

public sealed class RunOutputFactory : IRunOutputFactory
{
    private readonly SqliteApplicationStore store;
    private readonly ApplicationSettings applicationSettings;
    private readonly OutputPathPolicy outputPathPolicy;

    public RunOutputFactory(
        SqliteApplicationStore store,
        ApplicationSettings applicationSettings,
        OutputPathPolicy? outputPathPolicy = null)
    {
        this.store = store;
        this.applicationSettings = applicationSettings;
        this.outputPathPolicy = outputPathPolicy ?? new OutputPathPolicy();
    }

    public async Task<IRunOutput> CreateAsync(TestSettings settings)
    {
        var database = await DatabaseRunOutput.CreateAsync(store, settings);
        if (!applicationSettings.LegacyOutputEnabled)
            return database;

        try
        {
            var paths = outputPathPolicy.PrepareLegacy(
                applicationSettings.LegacyOutputDirectory,
                database.RunId,
                DateTimeOffset.Now);
            var legacy = await RunOutputSession.CreateAsync(settings, paths);
            return new CompositeRunOutput(database, legacy, paths);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
                                          PathTooLongException or UnauthorizedAccessException or IOException)
        {
            await database.AddWarningAsync(
                "DBT-OUTPUT-001",
                $"A saída CSV/TXT não pôde ser iniciada: {exception.Message}");
            return database;
        }
    }
}
