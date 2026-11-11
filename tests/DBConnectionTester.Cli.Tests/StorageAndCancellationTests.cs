using System.Text.Json;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using Xunit;

namespace DBConnectionTester.Cli.Tests;

public sealed class StorageAndCancellationTests
{
    [Fact]
    public async Task ExplicitDirectoryDoesNotChangeDesktopPreference()
    {
        using var fixture = new CliFixture();
        var preferences = new MemoryPreferences { Value = new(Guid.NewGuid(), fixture.Locations.LocalDatabasePath, StorageScope.LocalUser, null) };
        var original = preferences.Value;
        var app = new CliApplication(TextWriter.Null, TextWriter.Null, locations: fixture.Locations, preferences: preferences);
        Assert.Equal(0, await app.InvokeAsync(fixture.RunArguments()));
        Assert.Equal(original, preferences.Value);
        Assert.Equal(0, preferences.Writes);
    }

    [Fact]
    public async Task AmbiguousDiscoveryRequiresExplicitDirectoryWithoutChangingPreferences()
    {
        using var fixture = new CliFixture();
        await SqliteApplicationStore.OpenOrCreateAsync(fixture.Locations.LocalDatabasePath, StorageScope.LocalUser);
        await SqliteApplicationStore.OpenOrCreateAsync(fixture.Locations.SharedDatabasePath, StorageScope.SharedMachine);
        var preferences = new MemoryPreferences();
        var output = new StringWriter();
        var error = new StringWriter();
        var app = new CliApplication(output, error, locations: fixture.Locations, preferences: preferences);
        Assert.Equal(3, await app.InvokeAsync(["history", "list"]));
        Assert.Contains("--data-dir", error.ToString());
        Assert.Contains(fixture.Locations.LocalDatabasePath, error.ToString());
        Assert.Equal(0, preferences.Writes);
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public async Task DiscoveryWithoutStoreDoesNotCreateOneForQueries()
    {
        using var fixture = new CliFixture();
        var app = new CliApplication(TextWriter.Null, TextWriter.Null,
            locations: fixture.Locations, preferences: new MemoryPreferences());
        Assert.Equal(3, await app.InvokeAsync(["profiles", "list"]));
        Assert.False(File.Exists(fixture.Locations.LocalDatabasePath));
    }

    [Fact]
    public async Task CancellationFinishesPersistedRunAndReleasesLock()
    {
        using var fixture = new CliFixture();
        using var cancellation = new CancellationTokenSource();
        var output = new StringWriter();
        var error = new CancelOnProgressWriter(cancellation);
        var app = new CliApplication(output, error, locations: fixture.Locations, preferences: new MemoryPreferences());
        var args = fixture.RunArguments("--continuous", "true");
        Assert.Equal(130, await app.InvokeAsync(args, cancellation.Token));
        using var json = JsonDocument.Parse(output.ToString());
        var id = json.RootElement.GetProperty("data").GetProperty("runId").GetGuid();
        var store = await fixture.StoreAsync();
        var details = await new RunHistoryRepository(store).GetDetailsAsync(id);
        Assert.Equal(PersistedRunStatus.Stopped, details!.Run.Status);
        Assert.Equal(1, details.Run.CompletedCycles);
        using var lease = RunWriteLease.Acquire(store);
    }

    [Fact]
    public async Task ExistingWriterBlocksCliRunButAllowsQueriesWithoutRecovery()
    {
        using var fixture = new CliFixture();
        var store = await fixture.StoreAsync();
        var settings = new Application.TestSettingsValidator().Validate(
            Application.ProfileSettingsMapper.CreateDefault(DatabaseType.Sqlite) with { SqliteFile = fixture.Target }).Settings!;
        var id = Guid.NewGuid();
        using var lease = RunWriteLease.Acquire(store);
        await new RunRepository(store).BeginAsync(id, settings);
        Assert.Equal(4, (await fixture.ExecuteAsync(fixture.RunArguments())).ExitCode);
        Assert.Equal(0, (await fixture.ExecuteAsync(["history", "list", "--data-dir", fixture.Data])).ExitCode);
        Assert.Equal(PersistedRunStatus.Running, (await new RunHistoryRepository(store).GetDetailsAsync(id))!.Run.Status);
    }

    [Fact]
    public async Task IndependentProcessesConsultCompeteAndRecoverInterruptedRun()
    {
        using var fixture = new CliFixture();
        using var process = fixture.Start(fixture.RunArguments("--continuous", "true"));
        var stdout = process.StandardOutput.ReadToEndAsync();
        try
        {
            var firstProgress = await process.StandardError.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Contains("Ciclos:", firstProgress);
            var store = await fixture.StoreAsync();
            var history = new RunHistoryRepository(store);
            var runs = await history.SearchAsync(new(), new());
            var activeId = Assert.Single(runs.Items).RunId;
            Assert.Equal(4, (await fixture.ExecuteAsync(fixture.RunArguments())).ExitCode);
            Assert.Equal(0, (await fixture.ExecuteAsync(["history", "list", "--data-dir", fixture.Data])).ExitCode);
            Assert.Equal(PersistedRunStatus.Running, (await history.GetDetailsAsync(activeId))!.Run.Status);
            Assert.Equal(2, (await fixture.ExecuteAsync(["export", activeId.ToString(), "--data-dir", fixture.Data,
                "--output", Path.Combine(fixture.Root, "active.json")])).ExitCode);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            // Querying an abandoned session must not change its persisted state.
            Assert.Equal(0, (await fixture.ExecuteAsync(["history", "list", "--data-dir", fixture.Data])).ExitCode);
            Assert.Equal(PersistedRunStatus.Running, (await history.GetDetailsAsync(activeId))!.Run.Status);
            Assert.Equal(0, (await fixture.ExecuteAsync(fixture.RunArguments())).ExitCode);
            Assert.Equal(PersistedRunStatus.Interrupted, (await history.GetDetailsAsync(activeId))!.Run.Status);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await stdout;
        }
    }

    private sealed class CancelOnProgressWriter(CancellationTokenSource source) : StringWriter
    {
        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (value?.StartsWith("Ciclos:", StringComparison.Ordinal) == true) source.Cancel();
        }
    }
}
