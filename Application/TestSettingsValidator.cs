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
    bool Ping,
    bool Tcp,
    bool DatabaseTest,
    string RequestedCsvPath);

public sealed record TestSettingsValidationResult(TestSettings? Settings, string ErrorMessage)
{
    public bool IsValid => Settings is not null;

    public static TestSettingsValidationResult Success(TestSettings settings) => new(settings, "");
    public static TestSettingsValidationResult Failure(string message) => new(null, message);
}

public sealed class TestSettingsValidator
{
    private readonly OutputPathPolicy outputPathPolicy;

    public TestSettingsValidator(OutputPathPolicy outputPathPolicy)
    {
        this.outputPathPolicy = outputPathPolicy;
    }

    public TestSettingsValidationResult Validate(TestSettingsInput input)
    {
        var profile = DatabaseProfiles.Get(input.DatabaseType);
        var host = input.Host.Trim();
        var sqliteFile = input.SqliteFile.Trim();

        if (profile.UsesNetwork && string.IsNullOrWhiteSpace(host))
            return TestSettingsValidationResult.Failure("Informe o servidor ou host.");
        if (profile.UsesFile && !File.Exists(sqliteFile))
            return TestSettingsValidationResult.Failure("Selecione um arquivo SQLite existente.");
        if (profile.UsesOdbcDriver && string.IsNullOrWhiteSpace(input.OdbcDriver))
            return TestSettingsValidationResult.Failure("Informe o nome do driver ODBC do SQL Anywhere.");
        if (!input.Ping && !input.Tcp && !input.DatabaseTest)
            return TestSettingsValidationResult.Failure("Selecione pelo menos uma camada de teste.");

        OutputPaths output;
        try
        {
            output = outputPathPolicy.Prepare(input.RequestedCsvPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
                                          PathTooLongException or UnauthorizedAccessException or IOException)
        {
            return TestSettingsValidationResult.Failure(
                $"Não foi possível preparar o arquivo de saída: {exception.Message}");
        }

        return TestSettingsValidationResult.Success(new TestSettings(
            profile.Type,
            host,
            input.Port,
            input.User,
            input.Password,
            input.Database.Trim(),
            sqliteFile,
            input.SqlServerAuthentication,
            input.OdbcDriver.Trim(),
            input.TestCount,
            input.Continuous,
            input.Interval,
            input.Timeout,
            input.Ping,
            input.Tcp,
            input.DatabaseTest,
            output.CsvPath,
            output.TxtPath));
    }
}
