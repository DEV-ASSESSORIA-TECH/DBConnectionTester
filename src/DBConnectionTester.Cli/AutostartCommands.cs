using System.CommandLine;
using System.Diagnostics;
using DBConnectionTester.Application;
using DBConnectionTester.Cli.Background;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Cli;

public sealed partial class CliApplication
{
    /// <summary>
    /// dbct run ... --autostart: saves the run parameters next to the store, registers the
    /// Windows task and starts it now. The task runs "dbct autostart run", which keeps the
    /// execution going and starts again with Windows.
    /// </summary>
    private async Task<int> EnableAutostartAsync(ParseResult parse, SqliteApplicationStore store,
        TestSettingsInput input, string? password, CancellationToken token)
    {
        var storeDirectory = StoreDirectory(store);
        var scope = parse.GetValue<string?>("--autostart-scope") switch
        {
            "system" => AutostartScope.System,
            "user" => AutostartScope.User,
            _ => scheduler.IsElevated ? AutostartScope.System : AutostartScope.User
        };
        if (scope == AutostartScope.System && !scheduler.IsElevated)
            throw new CliException(2,
                "A execução automática no início do Windows (SYSTEM) exige um terminal de administrador. " +
                "Sem administrador, use --autostart-scope user para iniciar no logon deste usuário.");
        var autostart = new AutostartStore(storeDirectory);
        var taskName = AutostartStore.TaskNameFor(storeDirectory);
        if (autostart.Load() is not null || TaskExists(taskName))
            throw new CliException(2, "Este banco já tem execução automática. Use dbct autostart disable antes de configurar outra.");
        var executable = Environment.ProcessPath ?? throw new CliException(5, "Caminho do dbct indisponível.");

        var config = new AutostartConfig(1, scope, scope == AutostartScope.User ? scheduler.CurrentUser : null, taskName,
            executable, password is not null, DateTimeOffset.UtcNow, AutostartSettings.From(input));
        autostart.Save(config, password);
        try
        {
            scheduler.Register(taskName, AutostartTask.Build(scope, scheduler.CurrentUser, executable, storeDirectory));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            autostart.Delete();
            throw new CliException(5, "Não foi possível registrar a execução automática: " + exception.Message);
        }

        var busy = WriterInfo.Read(storeDirectory) is not null;
        var started = DateTimeOffset.UtcNow;
        scheduler.Run(taskName);
        Guid? runId = null;
        if (!busy)
        {
            runId = await WaitForNewRunAsync(store, started, () => { }, token);
            if (runId is null)
                throw new CliException(5, "Execução automática registrada, mas a execução não começou em 30 s. Confira com: dbct autostart show");
        }
        WriteResult(parse, new
        {
            runId, taskName, scope, storeDirectory,
            startsWith = scope == AutostartScope.System ? "boot" : "logon",
            waitingForActiveRun = busy
        });
        error.WriteLine(busy
            ? "Execução automática configurada. Outra execução está gravando neste banco; a automática começa quando ela terminar."
            : scope == AutostartScope.System
                ? "Execução automática iniciada; volta a cada início do Windows, sem logon. Acompanhe com: dbct run status"
                : "Execução automática iniciada; volta a cada logon deste usuário. Acompanhe com: dbct run status");
        return 0;
    }

    private async Task<int> AutostartShowAsync(ParseResult parse, CancellationToken token)
    {
        var store = await OpenStoreAsync(parse, false, token);
        var storeDirectory = StoreDirectory(store);
        var autostart = new AutostartStore(storeDirectory);
        var config = autostart.Load();
        var taskName = config?.TaskName ?? AutostartStore.TaskNameFor(storeDirectory);
        var state = autostart.LoadState();
        WriteResult(parse, new
        {
            enabled = config is not null,
            taskName,
            taskRegistered = TaskExists(taskName),
            config?.Scope,
            config?.UserName,
            settings = config?.Settings,
            config?.HasPassword,
            loopRunning = IsCliProcess(state?.ProcessId),
            state,
            storeDirectory
        });
        return 0;
    }

    private async Task<int> AutostartDisableAsync(ParseResult parse, CancellationToken token)
    {
        var timeout = parse.GetValue<int>("--timeout");
        if (timeout is < 1 or > 600) throw new CliException(2, "--timeout deve estar entre 1 e 600 segundos.");
        var store = await OpenStoreAsync(parse, false, token);
        var storeDirectory = StoreDirectory(store);
        var autostart = new AutostartStore(storeDirectory);
        var config = autostart.Load();
        var taskName = config?.TaskName ?? AutostartStore.TaskNameFor(storeDirectory);
        var registered = TaskExists(taskName);
        if (config is null && !registered) throw new CliException(2, "Este banco não tem execução automática.");

        // Ends the loop and its current run (recorded as Stopped) before removing the task.
        var loop = autostart.LoadState()?.ProcessId;
        Guid? stoppedRun = null;
        if (IsCliProcess(loop))
        {
            var writer = WriterInfo.Read(storeDirectory);
            if (writer?.ProcessId == loop)
            {
                stoppedRun = (await new RunHistoryRepository(store).SearchAsync(
                    new RunHistoryFilter(Status: PersistedRunStatus.Running), new PageRequest(1, 1), token)).Items.FirstOrDefault()?.RunId;
            }
            StopRequests.Write(storeDirectory, stoppedRun);
            var deadline = Stopwatch.StartNew();
            while (IsCliProcess(loop) && deadline.Elapsed < TimeSpan.FromSeconds(timeout)) await Task.Delay(500, token);
            if (IsCliProcess(loop))
            {
                error.WriteLine("A execução automática não parou a tempo; encerrando o processo (a execução será marcada como Interrupted).");
                try { using var process = Process.GetProcessById(loop!.Value); process.Kill(); }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { }
            }
        }
        if (registered)
        {
            scheduler.End(taskName);
            scheduler.Delete(taskName);
        }
        autostart.Delete();
        WriteResult(parse, new { disabled = true, taskName, stoppedRun, storeDirectory });
        return 0;
    }

    private bool TaskExists(string taskName)
    {
        try { return scheduler.Exists(taskName); }
        catch (PlatformNotSupportedException) { return false; }
    }

    /// <summary>
    /// "dbct autostart run" (hidden, started by the Windows task): keeps the configured run
    /// going. When it ends (another writer, a failure) it starts again after a pause; a stop
    /// request (dbct run stop, dbct autostart disable) or a disabled configuration ends the
    /// loop until the next start of Windows.
    /// </summary>
    private async Task<int> AutostartLoopAsync(ParseResult parse, CancellationToken token)
    {
        var store = await OpenStoreAsync(parse, false, token);
        var storeDirectory = StoreDirectory(store);
        var autostart = new AutostartStore(storeDirectory);
        StopRequests.Clear(storeDirectory);
        var state = new AutostartState(Environment.ProcessId, DateTimeOffset.UtcNow, 0, null, null, null);
        while (!token.IsCancellationRequested)
        {
            var config = autostart.Load();
            if (config is null) return 0;
            int code;
            string? failure = null;
            var stopRequested = false;
            try
            {
                var password = autostart.ReadPassword(config) ?? "";
                var validation = new TestSettingsValidator().Validate(config.Settings.ToInput(password));
                if (!validation.IsValid)
                {
                    code = 2;
                    failure = validation.ErrorMessage;
                }
                else
                {
                    secret = password.Length > 0 ? password : null;
                    autostart.SaveState(state with { StartedAt = DateTimeOffset.UtcNow });
                    var outcome = await ExecuteRunAsync(store, validation.Settings!, null, quiet: true, token);
                    code = outcome.ExitCode;
                    stopRequested = outcome.StopRequested;
                }
            }
            catch (RunAlreadyActiveException) { code = 4; }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                code = 5;
                failure = Sanitize(exception.Message);
            }
            state = state with
            {
                Restarts = state.Restarts + 1, LastExitCode = code, LastExitAt = DateTimeOffset.UtcNow, LastError = failure
            };
            autostart.SaveState(state);
            if (token.IsCancellationRequested) return 130;
            if (stopRequested) return 0;

            var wait = code switch { 4 => AutostartBusyRetryDelay, 2 => AutostartInvalidRetryDelay, _ => AutostartRetryDelay };
            var waited = Stopwatch.StartNew();
            while (waited.Elapsed < wait)
            {
                if (StopRequests.ConsumeAny(storeDirectory) || autostart.Load() is null) return 0;
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1000, Math.Max(10, wait.TotalMilliseconds))), token);
            }
        }
        return 130;
    }
}
