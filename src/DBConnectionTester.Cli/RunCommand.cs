using System.CommandLine;
using DBConnectionTester.Application;
using DBConnectionTester.Models;
using DBConnectionTester.Services;
using DBConnectionTester.Services.Output;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Cli;

public sealed partial class CliApplication
{
    private async Task<int> RunAsync(ParseResult parse, CancellationToken token)
    {
        var envName = parse.GetValue<string?>("--password-env");
        var fromStdin = parse.GetValue<bool>("--password-stdin");
        if (envName is not null && fromStdin)
            throw new CliException(2, "Use somente uma fonte de senha: --password-env ou --password-stdin.");
        if (envName is not null)
            secret = Environment.GetEnvironmentVariable(envName)
                ?? throw new CliException(2, "A variável de ambiente de senha não existe.");
        if (fromStdin)
            secret = await input.ReadLineAsync(token)
                ?? throw new CliException(2, "A entrada padrão não forneceu uma senha.");

        token.ThrowIfCancellationRequested();
        SqliteApplicationStore? store = null;
        // The GUI default (1000 cycles every 5 s, ~83 min) suits a monitoring window, not a
        // terminal command. Without a profile the CLI answers in about ten seconds.
        var inputSettings = ProfileSettingsMapper.CreateDefault() with
        {
            TestCount = CliRunDefaults.Count,
            Interval = TimeSpan.FromSeconds(CliRunDefaults.IntervalSeconds)
        };
        if (parse.GetValue<Guid?>("--profile") is { } profileId)
        {
            // Loading a saved profile must never create a missing store.
            store = await OpenStoreAsync(parse, allowCreate: false, token);
            var profile = await new PersistentSettingsRepository(store).GetAsync(profileId, token)
                ?? throw new CliException(2, "O perfil informado não existe.");
            inputSettings = ProfileSettingsMapper.FromProfile(profile);
        }
        var previousType = inputSettings.DatabaseType;
        inputSettings = ApplyOverrides(parse, inputSettings);
        var profileInfo = DatabaseProfiles.Get(inputSettings.DatabaseType);
        // A target type without a default port (tcp) must not silently inherit 3306.
        if (inputSettings.DatabaseType != previousType && profileInfo.UsesNetwork &&
            profileInfo.DefaultPort is null && parse.GetValue<int?>("--port") is null)
            throw new CliException(2, "Informe --port para o tipo de destino selecionado.");
        var needsPassword = inputSettings.DatabaseTest &&
            profileInfo.RequiresCredentials(inputSettings.SqlServerAuthentication);
        if (needsPassword && secret is null)
            throw new CliException(2, "Informe --password-env ou --password-stdin para esta autenticação.");
        var validation = new TestSettingsValidator().Validate(inputSettings with { Password = secret ?? "" });
        if (!validation.IsValid) throw new CliException(2, validation.ErrorMessage);

        store ??= await OpenStoreAsync(parse, allowCreate: true, token);
        var settings = await new PersistentSettingsRepository(store).GetAsync(token);
        var progress = new ConsoleProgress(error, quiet: parse.GetValue<bool>("--no-progress"));
        // Recovery runs under the run's own write lease, so it can never mark a session
        // from another live writer as interrupted.
        var outputs = new RunIdCapturingFactory(new RunOutputFactory(store, settings, recoverInterruptedRuns: true));
        var runner = new TestRunner(new TestCycleExecutor(), outputs);
        RunSummary summary;
        try { summary = await runner.RunAsync(validation.Settings!, progress, token); }
        catch (Exception) when (outputs.RunId is { } failedRunId)
        {
            // The run was persisted as Failed: report its ID so automation can inspect it.
            var failedDetails = await new RunHistoryRepository(store).GetDetailsAsync(failedRunId, CancellationToken.None);
            WriteResult(parse, new
            {
                runId = failedRunId,
                completed = failedDetails?.Run.CompletedCycles ?? progress.Latest?.Completed ?? 0,
                terminationReason = RunTerminationReason.ExecutionFailed,
                hasDiagnosticFailures = progress.HasFailures,
                statistics = progress.Latest?.Statistics,
                warnings = failedDetails?.Warnings,
                failureMessage = failedDetails?.Run.FailureMessage
            });
            error.WriteLine("A execução falhou e foi registrada no histórico como Failed.");
            return 5;
        }
        finally { progress.Complete(); }
        var details = await new RunHistoryRepository(store).GetDetailsAsync(summary.RunId, CancellationToken.None);
        WriteResult(parse, new
        {
            summary.RunId, summary.Completed, summary.TerminationReason,
            hasDiagnosticFailures = progress.HasFailures,
            statistics = progress.Latest?.Statistics,
            warnings = details?.Warnings,
            summary.CsvPath, summary.TxtPath
        });
        return summary.Stopped ? 130 : progress.HasFailures ? 1 : 0;
    }

    private sealed class RunIdCapturingFactory(IRunOutputFactory inner) : IRunOutputFactory
    {
        public Guid? RunId { get; private set; }

        public async Task<IRunOutput> CreateAsync(TestSettings settings)
        {
            var output = await inner.CreateAsync(settings);
            RunId = output.RunId;
            return output;
        }
    }

    private static TestSettingsInput ApplyOverrides(ParseResult parse, TestSettingsInput settings)
    {
        var typeName = parse.GetValue<string?>("--type");
        var type = typeName switch
        {
            "mysql" => DatabaseType.MySqlMariaDb, "postgresql" => DatabaseType.PostgreSql,
            "sqlserver" => DatabaseType.SqlServer, "sqlanywhere" => DatabaseType.SapSqlAnywhere,
            "sqlite" => DatabaseType.Sqlite, "tcp" => DatabaseType.TcpOnly, _ => settings.DatabaseType
        };
        var auth = parse.GetValue<string?>("--auth");
        var interval = parse.GetValue<double?>("--interval");
        var timeout = parse.GetValue<double?>("--timeout");
        return settings with
        {
            DatabaseType = type,
            Host = parse.GetValue<string?>("--host") ?? settings.Host,
            Port = parse.GetValue<int?>("--port") ??
                (typeName is null ? settings.Port : DatabaseProfiles.Get(type).DefaultPort ?? settings.Port),
            User = parse.GetValue<string?>("--user") ?? settings.User,
            Database = parse.GetValue<string?>("--database") ?? settings.Database,
            SqliteFile = parse.GetValue<string?>("--sqlite-file") ?? settings.SqliteFile,
            OdbcDriver = parse.GetValue<string?>("--odbc-driver") ?? settings.OdbcDriver,
            SqlServerAuthentication = auth is null ? settings.SqlServerAuthentication :
                auth == "windows" ? SqlServerAuthentication.Windows : SqlServerAuthentication.SqlLogin,
            TestCount = parse.GetValue<long?>("--count") ?? settings.TestCount,
            Continuous = parse.GetValue<bool?>("--continuous") ?? settings.Continuous,
            Interval = interval is null ? settings.Interval : TimeSpan.FromSeconds(interval.Value),
            Timeout = timeout is null ? settings.Timeout : TimeSpan.FromSeconds(timeout.Value),
            Dns = parse.GetValue<bool?>("--dns") ?? settings.Dns,
            Ping = parse.GetValue<bool?>("--ping") ?? settings.Ping,
            Tcp = parse.GetValue<bool?>("--tcp") ?? settings.Tcp,
            DatabaseTest = parse.GetValue<bool?>("--database-test") ?? settings.DatabaseTest
        };
    }
}

internal static class CliRunDefaults
{
    public const long Count = 10;
    public const double IntervalSeconds = 1;
}
