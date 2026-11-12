using System.Diagnostics;
using System.Text.Json;
using DBConnectionTester.Cli.Background;
using Xunit;

namespace DBConnectionTester.Cli.Tests;

public sealed class BackgroundRunTests
{
    [Fact]
    public async Task DetachedRunReturnsImmediatelyAndStopsFromAnotherCommand()
    {
        using var fixture = new CliFixture();
        try
        {
            var started = await fixture.ExecuteAsync(ContinuousArguments(fixture, "--detach", "--format", "json"));
            Assert.Equal(0, started.ExitCode);
            using var startedJson = JsonDocument.Parse(started.Output);
            var id = startedJson.RootElement.GetProperty("data").GetProperty("runId").GetGuid();

            var status = await fixture.ExecuteAsync(["run", "status", "--data-dir", fixture.Data, "--format", "json"]);
            Assert.Equal(0, status.ExitCode);
            using (var statusJson = JsonDocument.Parse(status.Output))
            {
                var data = statusJson.RootElement.GetProperty("data");
                Assert.True(data.GetProperty("active").GetBoolean());
                Assert.Equal("cli", data.GetProperty("writer").GetProperty("kind").GetString());
                Assert.Equal(id, Assert.Single(data.GetProperty("runs").EnumerateArray()).GetProperty("runId").GetGuid());
            }

            var stop = await fixture.ExecuteAsync(["run", "stop", "--data-dir", fixture.Data, "--format", "json"]);
            Assert.Equal(0, stop.ExitCode);
            using var stopJson = JsonDocument.Parse(stop.Output);
            Assert.Equal("Stopped", stopJson.RootElement.GetProperty("data").GetProperty("status").GetString());
            await WaitUntilAsync(async () => !(await IsActiveAsync(fixture)));
        }
        finally
        {
            await fixture.ExecuteAsync(["run", "stop", "--data-dir", fixture.Data]);
        }
    }

    [Fact]
    public async Task StopWithoutActiveRunIsAUsageError()
    {
        using var fixture = new CliFixture();
        Assert.Equal(0, (await fixture.ExecuteAsync(fixture.RunArguments())).ExitCode);
        var stop = await fixture.ExecuteAsync(["run", "stop", "--data-dir", fixture.Data]);
        Assert.Equal(2, stop.ExitCode);
        Assert.Contains("Nenhuma execução ativa", stop.Error);
    }

    [Fact]
    public async Task AutostartRunsInBackgroundEndsOnStopAndIsRemovedOnDisable()
    {
        using var fixture = new CliFixture();
        var scheduler = new LoopStartingScheduler(fixture);
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var app = new CliApplication(output, error, locations: fixture.Locations,
                preferences: new MemoryPreferences(), scheduler: scheduler);
            var enable = ContinuousArguments(fixture, "--autostart", "--autostart-scope", "user", "--format", "json")
                .Where(argument => argument != "--continuous" && argument != "true").ToArray();
            var code = await app.InvokeAsync(enable);
            Assert.True(code == 0, $"Código {code}: {error}");
            using (var json = JsonDocument.Parse(output.ToString()))
                Assert.NotEqual(JsonValueKind.Null, json.RootElement.GetProperty("data").GetProperty("runId").ValueKind);
            Assert.Contains("LogonTrigger", scheduler.TaskXml);
            Assert.Contains("autostart run --data-dir", scheduler.TaskXml);
            Assert.True(File.Exists(Path.Combine(fixture.Data, "autostart", "config.json")));

            // A second configuration for the same store is refused.
            Assert.Equal(2, await new CliApplication(TextWriter.Null, TextWriter.Null, locations: fixture.Locations,
                preferences: new MemoryPreferences(), scheduler: scheduler).InvokeAsync(enable));

            // "run stop" ends the run and the loop until the next start of Windows.
            Assert.Equal(0, (await fixture.ExecuteAsync(["run", "stop", "--data-dir", fixture.Data])).ExitCode);
            Assert.True(scheduler.Loop!.WaitForExit(30_000));
            Assert.Equal(0, scheduler.Loop.ExitCode);

            var disable = new CliApplication(TextWriter.Null, TextWriter.Null, locations: fixture.Locations,
                preferences: new MemoryPreferences(), scheduler: scheduler);
            Assert.Equal(0, await disable.InvokeAsync(["autostart", "disable", "--data-dir", fixture.Data]));
            Assert.False(scheduler.Registered);
            Assert.False(Directory.Exists(Path.Combine(fixture.Data, "autostart")));
        }
        finally
        {
            scheduler.Stop();
        }
    }

    [Fact]
    public async Task SystemAutostartRequiresAdministrator()
    {
        using var fixture = new CliFixture();
        var error = new StringWriter();
        var app = new CliApplication(TextWriter.Null, error, locations: fixture.Locations,
            preferences: new MemoryPreferences(), scheduler: new LoopStartingScheduler(fixture));
        Assert.Equal(2, await app.InvokeAsync(["run", "--data-dir", fixture.Data, "--type", "sqlite",
            "--sqlite-file", fixture.Target, "--autostart", "--autostart-scope", "system"]));
        Assert.Contains("administrador", error.ToString());
        Assert.False(Directory.Exists(Path.Combine(fixture.Data, "autostart")));
    }

    [Fact]
    public void SystemTaskStartsAtBootAsSystemWithoutTimeLimit()
    {
        var xml = AutostartTask.Build(AutostartScope.System, @"PC\user", @"C:\Program Files\DBConnectionTester\dbct.exe",
            @"C:\Dados DBCT");
        Assert.Contains("BootTrigger", xml);
        Assert.Contains("S-1-5-18", xml);
        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", xml);
        Assert.Contains("autostart run --data-dir \"C:\\Dados DBCT\"", xml);
        Assert.DoesNotContain("LogonTrigger", xml);
    }

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("with space", "\"with space\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void QuotesArgumentsForWindowsCommandLines(string value, string expected) =>
        Assert.Equal(expected, DetachedProcess.Quote(value));

    private static string[] ContinuousArguments(CliFixture fixture, params string[] extra) =>
        ["run", "--data-dir", fixture.Data, "--type", "sqlite", "--sqlite-file", fixture.Target,
         "--continuous", "true", "--interval", "0.2", .. extra];

    private static async Task<bool> IsActiveAsync(CliFixture fixture)
    {
        var status = await fixture.ExecuteAsync(["run", "status", "--data-dir", fixture.Data, "--format", "json"]);
        using var json = JsonDocument.Parse(status.Output);
        return json.RootElement.GetProperty("data").GetProperty("active").GetBoolean();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!await condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Condição não atingida em 30 s.");
            await Task.Delay(200);
        }
    }

    /// <summary>Task Scheduler double: "running the task" starts the real autostart loop.</summary>
    private sealed class LoopStartingScheduler(CliFixture fixture) : IAutostartScheduler
    {
        public string TaskXml { get; private set; } = "";
        public bool Registered { get; private set; }
        public Process? Loop { get; private set; }
        public bool IsElevated => false;
        public string CurrentUser => @"PC\tester";

        public void Register(string taskName, string taskXml) { TaskXml = taskXml; Registered = true; }

        public void Run(string taskName) => Loop = fixture.Start(["autostart", "run", "--data-dir", fixture.Data]);

        public void End(string taskName) => Stop();

        public void Delete(string taskName) => Registered = false;

        public bool Exists(string taskName) => Registered;

        public void Stop()
        {
            if (Loop is { HasExited: false }) { Loop.Kill(entireProcessTree: true); Loop.WaitForExit(); }
        }
    }
}
