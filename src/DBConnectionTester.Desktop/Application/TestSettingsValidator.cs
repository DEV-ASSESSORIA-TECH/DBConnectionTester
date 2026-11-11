using DBConnectionTester.Models;

namespace DBConnectionTester.Application;

public sealed record TestSettingsInput(
    DatabaseType DatabaseType,
    string Host,
    int Port,
    string User,
    string Password,
    string Database,
    string SqliteFile,
    SqlServerAuthentication SqlServerAuthentication,
    string OdbcDriver,
    long TestCount,
    bool Continuous,
    TimeSpan Interval,
    TimeSpan Timeout,
    bool Dns,
    bool Ping,
    bool Tcp,
    bool DatabaseTest);

public sealed record TestSettingsValidationResult(TestSettings? Settings, string ErrorMessage)
{
    public bool IsValid => Settings is not null;

    public static TestSettingsValidationResult Success(TestSettings settings) => new(settings, "");
    public static TestSettingsValidationResult Failure(string message) => new(null, message);
}

public sealed class TestSettingsValidator
{
    public TestSettingsValidationResult Validate(TestSettingsInput input)
    {
        var profile = DatabaseProfiles.Get(input.DatabaseType);
        var host = input.Host.Trim();
        var sqliteFile = input.SqliteFile.Trim();
        var dns = input.Dns && profile.UsesNetwork;
        var ping = input.Ping && profile.UsesNetwork;
        var tcp = input.Tcp && profile.UsesNetwork;
        var databaseTest = input.DatabaseTest && profile.SupportsDatabaseTest;

        if (profile.UsesNetwork && string.IsNullOrWhiteSpace(host))
            return TestSettingsValidationResult.Failure("Informe o servidor ou host.");
        NetworkPort? port = null;
        if (profile.UsesNetwork && !NetworkPort.TryCreate(input.Port, out port))
            return TestSettingsValidationResult.Failure(
                $"A porta deve estar entre {NetworkPort.Minimum} e {NetworkPort.Maximum}.");
        if (!RunCount.TryCreate(input.TestCount, out var testCount))
            return TestSettingsValidationResult.Failure(
                $"A quantidade de testes deve estar entre {RunCount.Minimum:N0} e {RunCount.Maximum:N0}.");
        if (!TestInterval.TryCreate(input.Interval, out var interval))
            return TestSettingsValidationResult.Failure("O intervalo deve estar entre 0 segundos e 1 hora.");
        if (!StageTimeout.TryCreate(input.Timeout, out var timeout))
            return TestSettingsValidationResult.Failure("O timeout deve estar entre 1 e 120 segundos.");
        if (profile.UsesFile && !File.Exists(sqliteFile))
            return TestSettingsValidationResult.Failure("Selecione um arquivo SQLite existente.");
        if (profile.UsesOdbcDriver && string.IsNullOrWhiteSpace(input.OdbcDriver))
            return TestSettingsValidationResult.Failure("Informe o nome do driver ODBC do SQL Anywhere.");
        var credentialsRequired = profile.UsesCredentials &&
            (!profile.UsesSqlServerAuthentication || input.SqlServerAuthentication == SqlServerAuthentication.SqlLogin);
        if (credentialsRequired && string.IsNullOrWhiteSpace(input.User))
            return TestSettingsValidationResult.Failure("Informe o usuário do banco de dados.");
        if (!dns && !ping && !tcp && !databaseTest)
            return TestSettingsValidationResult.Failure("Selecione pelo menos uma camada de teste.");

        return TestSettingsValidationResult.Success(new TestSettings(
            profile.Type,
            host,
            port,
            input.User,
            input.Password,
            input.Database.Trim(),
            sqliteFile,
            input.SqlServerAuthentication,
            input.OdbcDriver.Trim(),
            testCount,
            input.Continuous,
            interval,
            timeout,
            dns,
            ping,
            tcp,
            databaseTest));
    }
}
