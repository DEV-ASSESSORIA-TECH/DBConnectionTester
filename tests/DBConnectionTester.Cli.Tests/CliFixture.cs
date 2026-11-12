using System.Diagnostics;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using Microsoft.Data.Sqlite;

namespace DBConnectionTester.Cli.Tests;

internal sealed class CliFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "DBCT CLI tests", Guid.NewGuid().ToString("N"));
    public string Data => Path.Combine(Root, "store");
    public string Target => Path.Combine(Root, "target.db");
    public StorageLocations Locations => new(Path.Combine(Root, "local", "data.db"),
        Path.Combine(Root, "shared", "data.db"), Path.Combine(Root, "portable", "Data", "data.db"));

    public CliFixture()
    {
        Directory.CreateDirectory(Root);
        using var connection = new SqliteConnection($"Data Source={Target}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE fixture (id INTEGER);";
        command.ExecuteNonQuery();
    }

    public Task<SqliteApplicationStore> StoreAsync() => SqliteApplicationStore.OpenOrCreateAsync(
        Path.Combine(Data, "data.db"), StorageScope.Custom);

    public string[] RunArguments(params string[] extra) =>
        ["run", "--data-dir", Data, "--type", "sqlite", "--sqlite-file", Target,
         "--count", "1", "--interval", "0", "--format", "json", .. extra];

    public static string ExecutablePath
    {
        get
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            return Path.Combine(RepositoryRoot(), "src", "DBConnectionTester.Cli", "bin", configuration,
                "net10.0-windows", "dbct.exe");
        }
    }

    private static readonly System.Text.UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public Process Start(string[] arguments, string? password = null)
    {
        var info = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Root,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            // Contract: redirected CLI streams are UTF-8 on every platform. Reading them with the
            // Windows console code page would garble accented messages.
            StandardOutputEncoding = Utf8, StandardErrorEncoding = Utf8, StandardInputEncoding = Utf8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (password is not null) info.Environment["DBCT_TEST_PASSWORD"] = password;
        return Process.Start(info)!;
    }

    public async Task<ProcessResult> ExecuteAsync(string[] arguments, string? password = null, string? stdin = null)
    {
        using var process = Start(arguments, password);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (stdin is not null) await process.StandardInput.WriteLineAsync(stdin);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return new(process.ExitCode, await stdout, await stderr);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DBConnectionTester.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(Root, recursive: true);
    }
}

internal sealed record ProcessResult(int ExitCode, string Output, string Error);

internal sealed class MemoryPreferences : IStoragePreferenceStore
{
    public StoragePreference? Value { get; set; }
    public int Writes { get; private set; }
    public StoragePreference? Read() => Value;
    public void Write(StoragePreference preference) { Writes++; Value = preference; }
}
