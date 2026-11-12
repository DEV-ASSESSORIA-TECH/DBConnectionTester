using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DBConnectionTester.Cli.Tests;

public sealed class CliProcessTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    public async Task HelpAndVersionDoNotOpenOrCreateStore(string option)
    {
        using var fixture = new CliFixture();
        var result = await fixture.ExecuteAsync([option]);
        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(result.Output);
        Assert.False(Directory.Exists(fixture.Data));
    }

    [Theory]
    [InlineData("--unknown", "value")]
    [InlineData("--count", "not-a-number")]
    [InlineData("--password", "never-echo-me")]
    public async Task InvalidArgumentsFailWithoutEchoingRejectedTokens(string option, string value)
    {
        using var fixture = new CliFixture();
        var result = await fixture.ExecuteAsync(["run", "--data-dir", fixture.Data, option, value]);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.DoesNotContain(value, result.Error);
        Assert.False(Directory.Exists(fixture.Data));
    }

    [Fact]
    public async Task ShortPasswordDoesNotCorruptIdentifiersOrPaths()
    {
        using var fixture = new CliFixture();
        // GUIDs and paths almost always contain "1"; masking must only touch free-text fields.
        var run = await fixture.ExecuteAsync(fixture.RunArguments("--password-env", "DBCT_TEST_PASSWORD"), "1");
        Assert.Equal(0, run.ExitCode);
        using var json = JsonDocument.Parse(run.Output);
        var id = json.RootElement.GetProperty("data").GetProperty("runId").GetGuid();
        var details = await fixture.ExecuteAsync(["history", "show", id.ToString(), "--data-dir", fixture.Data, "--format", "json"]);
        Assert.Equal(0, details.ExitCode);
    }

    [Fact]
    public async Task TcpTargetRequiresExplicitPort()
    {
        using var fixture = new CliFixture();
        var result = await fixture.ExecuteAsync(["run", "--data-dir", fixture.Data, "--type", "tcp",
            "--host", "127.0.0.1", "--count", "1", "--interval", "0"]);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--port", result.Error);
        Assert.False(Directory.Exists(fixture.Data));
    }

    [Fact]
    public async Task QueryDoesNotCreateMissingStore()
    {
        using var fixture = new CliFixture();
        var result = await fixture.ExecuteAsync(["history", "list", "--data-dir", fixture.Data, "--format", "json"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.False(Directory.Exists(fixture.Data));
    }

    [Fact]
    public async Task ExecutesQueriesAndExportsAllExistingFormatsWithoutSecrets()
    {
        using var fixture = new CliFixture();
        const string secret = "private-test-password";
        var run = await fixture.ExecuteAsync(fixture.RunArguments("--password-env", "DBCT_TEST_PASSWORD"), secret);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Ciclos:", run.Error);
        Assert.DoesNotContain(secret, run.Output + run.Error);
        using var result = JsonDocument.Parse(run.Output);
        Assert.Equal(1, result.RootElement.GetProperty("formatVersion").GetInt32());
        var id = result.RootElement.GetProperty("data").GetProperty("runId").GetGuid();
        Assert.False(result.RootElement.GetProperty("data").GetProperty("hasDiagnosticFailures").GetBoolean());

        var history = await fixture.ExecuteAsync(["history", "list", "--data-dir", fixture.Data,
            "--status", "Completed", "--page-size", "1", "--format", "json"]);
        Assert.Equal(0, history.ExitCode);
        using var list = JsonDocument.Parse(history.Output);
        Assert.Equal(1, list.RootElement.GetProperty("data").GetProperty("totalItems").GetInt32());
        var details = await fixture.ExecuteAsync(["history", "show", id.ToString(), "--data-dir", fixture.Data,
            "--cycles", "--page-size", "1", "--format", "json"]);
        Assert.Equal(0, details.ExitCode);
        using var detailJson = JsonDocument.Parse(details.Output);
        Assert.Single(detailJson.RootElement.GetProperty("data").GetProperty("cycles").GetProperty("items").EnumerateArray());

        foreach (var format in new[] { "csv", "txt", "json", "zip" })
        {
            var path = Path.Combine(fixture.Root, "report." + format);
            var args = new[] { "export", id.ToString(), "--data-dir", fixture.Data, "--format", format, "--output", path };
            Assert.Equal(0, (await fixture.ExecuteAsync(args)).ExitCode);
            var before = await File.ReadAllBytesAsync(path);
            Assert.NotEmpty(before);
            var again = await fixture.ExecuteAsync(args);
            Assert.Equal(2, again.ExitCode);
            Assert.Contains("já existe", again.Error);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
        Assert.DoesNotContain(secret, await File.ReadAllTextAsync(Path.Combine(fixture.Root, "report.json")));
        using var db = new SqliteConnection($"Data Source={Path.Combine(fixture.Data, "data.db")}");
        await db.OpenAsync();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT settings_snapshot_json FROM runs;";
        Assert.DoesNotContain(secret, (string)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task DiagnosticFailureHasExitOneEvenWhenRunCompleted()
    {
        using var fixture = new CliFixture();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var result = await fixture.ExecuteAsync(["run", "--data-dir", fixture.Data, "--type", "tcp",
            "--host", "127.0.0.1", "--port", port.ToString(), "--dns", "false", "--ping", "false",
            "--count", "1", "--timeout", "1", "--format", "json"]);
        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("data").GetProperty("hasDiagnosticFailures").GetBoolean());
        Assert.Equal("PlannedCountCompleted", json.RootElement.GetProperty("data").GetProperty("terminationReason").GetString());
    }

    [Fact]
    public async Task ProfileOptionsAreOverriddenWithoutUpdatingSavedProfile()
    {
        using var fixture = new CliFixture();
        var store = await fixture.StoreAsync();
        var repository = new PersistentSettingsRepository(store);
        var profile = await repository.SaveAsync(new ConnectionProfileDraft(null, "SQLite CLI", DatabaseType.Sqlite, "", null, "", "",
            fixture.Target, SqlServerAuthentication.Windows, "", ProfileExecutionDefaults.Default with { TestCount = 2 }));
        var result = await fixture.ExecuteAsync(["run", "--profile", profile.ProfileId.ToString(), "--count", "1",
            "--data-dir", fixture.Data, "--format", "json"]);
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(1, json.RootElement.GetProperty("data").GetProperty("completed").GetInt32());
        Assert.Equal(2, (await repository.GetAsync(profile.ProfileId))!.ExecutionDefaults.TestCount);
        foreach (var arguments in new[] { new[] { "profiles", "list" }, new[] { "profiles", "show", profile.ProfileId.ToString() } })
        {
            var query = await fixture.ExecuteAsync([.. arguments, "--data-dir", fixture.Data, "--format", "json"]);
            Assert.Equal(0, query.ExitCode);
            Assert.Contains(profile.ProfileId.ToString(), query.Output);
        }
    }

    [Fact]
    public async Task StdinSecretsDoNotCorruptJsonEvenWhenEqualToJsonKeywords()
    {
        using var fixture = new CliFixture();
        var result = await fixture.ExecuteAsync(fixture.RunArguments("--password-stdin"), stdin: "false");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("data").GetProperty("hasDiagnosticFailures").GetBoolean());
    }

    [Theory]
    [InlineData("--password-env", "DBCT_VARIABLE_THAT_DOES_NOT_EXIST")]
    [InlineData("--count", "0")]
    [InlineData("--timeout", "121")]
    public async Task InvalidExecutionConfigurationHasExitTwo(string option, string value)
    {
        using var fixture = new CliFixture();
        var args = fixture.RunArguments().ToList();
        var index = args.IndexOf(option);
        if (index >= 0) args[index + 1] = value;
        else args.AddRange([option, value]);
        Assert.Equal(2, (await fixture.ExecuteAsync(args.ToArray())).ExitCode);
        Assert.False(Directory.Exists(fixture.Data));
    }

    [Fact]
    public async Task MissingProfileStoreIsNotCreated()
    {
        using var fixture = new CliFixture();
        var result = await fixture.ExecuteAsync(["run", "--profile", Guid.NewGuid().ToString(),
            "--data-dir", fixture.Data]);
        Assert.Equal(3, result.ExitCode);
        Assert.False(Directory.Exists(fixture.Data));
    }

    [Theory]
    [InlineData("mysql")]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("sqlanywhere")]
    public async Task NetworkOnlyRunDoesNotRequireDatabaseCredentials(string type)
    {
        using var fixture = new CliFixture();
        var result = await fixture.ExecuteAsync(["run", "--type", type, "--host", "127.0.0.1",
            "--auth", "sql-login", "--odbc-driver", "", "--database-test", "false",
            "--tcp", "false", "--ping", "false", "--count", "1", "--data-dir", fixture.Data]);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RejectsMissingAndConflictingCredentialSources()
    {
        using var fixture = new CliFixture();
        Assert.Equal(2, (await fixture.ExecuteAsync(["run", "--type", "mysql", "--user", "user",
            "--data-dir", fixture.Data])).ExitCode);
        Assert.Equal(2, (await fixture.ExecuteAsync(fixture.RunArguments("--password-env", "DBCT_TEST_PASSWORD",
            "--password-stdin"), "secret")).ExitCode);
    }

    [Fact]
    public async Task FutureSchemaIsRejectedWithoutChangingVersion()
    {
        using var fixture = new CliFixture();
        var store = await fixture.StoreAsync();
        await using (var db = await store.OpenConnectionAsync())
        {
            using var command = db.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999;";
            await command.ExecuteNonQueryAsync();
        }
        var result = await fixture.ExecuteAsync(["history", "list", "--data-dir", fixture.Data]);
        Assert.Equal(3, result.ExitCode);
        var inspection = await SqliteApplicationStore.InspectAsync(store.Descriptor.DatabasePath);
        Assert.Equal(StoreInspectionStatus.FutureVersion, inspection.Status);
    }

    [Fact]
    public async Task CliRuntimeDoesNotReferenceWindowsDesktop()
    {
        var assembly = typeof(CliApplication).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "System.Windows.Forms");
        Assert.DoesNotContain(typeof(SqliteApplicationStore).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "System.Windows.Forms");
        using var fixture = new CliFixture();
        Assert.Equal(0, (await fixture.ExecuteAsync(["--version"])).ExitCode);
    }
}
