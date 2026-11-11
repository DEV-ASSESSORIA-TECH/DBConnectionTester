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
        var inputSettings = ProfileSettingsMapper.CreateDefault();
        if (parse.GetValue<Guid?>("--profile") is { } profileId)
        {
            // Loading a saved profile must never create a missing store.
            store = await OpenStoreAsync(parse, allowCreate: false, token);
            var profile = await new PersistentSettingsRepository(store).GetAsync(profileId, token)
                ?? throw new CliException(2, "O perfil informado não existe.");
            inputSettings = ProfileSettingsMapper.FromProfile(profile);
        }
        inputSettings = ApplyOverrides(parse, inputSettings);
        var profileInfo = DatabaseProfiles.Get(inputSettings.DatabaseType);
        var needsPassword = inputSettings.DatabaseTest && profileInfo.UsesCredentials &&
            (!profileInfo.UsesSqlServerAuthentication || inputSettings.SqlServerAuthentication == SqlServerAuthentication.SqlLogin);
        if (needsPassword && secret is null)
            throw new CliException(2, "Informe --password-env ou --password-stdin para esta autenticação.");
        var validation = new TestSettingsValidator().Validate(inputSettings with { Password = secret ?? "" });
        if (!validation.IsValid) throw new CliException(2, validation.ErrorMessage);

        store ??= await OpenStoreAsync(parse, allowCreate: true, token);
        var settings = await new PersistentSettingsRepository(store).GetAsync(token);
        // Recovery must never mark a session from another live writer as interrupted.
        await RunRecoveryService.RecoverAsync(store, token);
        var progress = new ConsoleProgress(error);
        var runner = new TestRunner(new TestCycleExecutor(), new RunOutputFactory(store, settings));
        RunSummary summary;
        try { summary = await runner.RunAsync(validation.Settings!, progress, token); }
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
        return summary.Stopped ? 130 : summary.Failed ? 5 : progress.HasFailures ? 1 : 0;
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
