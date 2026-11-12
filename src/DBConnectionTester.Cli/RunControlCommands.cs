using System.CommandLine;
using System.Diagnostics;
using DBConnectionTester.Cli.Background;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Cli;

public sealed partial class CliApplication
{
    private static readonly TimeSpan StartConfirmation = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Starts the same run as a background process that does not belong to this terminal and
    /// returns once the run is recorded, with its ID. The run is then followed with
    /// "dbct run status" and ended with "dbct run stop".
    /// </summary>
    private async Task<int> DetachAsync(ParseResult parse, SqliteApplicationStore store, CancellationToken token)
    {
        var storeDirectory = StoreDirectory(store);
        var environment = new Dictionary<string, string>();
        if (secret is not null) environment[DetachedSecretVariable] = secret;
        var executable = Environment.ProcessPath ?? throw new CliException(5, "Caminho do dbct indisponível.");
        var started = DateTimeOffset.UtcNow;
        using var child = DetachedProcess.Start(executable,
            DetachedArguments(arguments, storeDirectory, secret is not null), environment, Environment.CurrentDirectory);
        var runId = await WaitForNewRunAsync(store, started, () =>
        {
            if (child.ExitCode is not { } code) return;
            throw new CliException(code == 4 ? 4 : 5,
                $"A execução em segundo plano terminou antes de começar (código {code}). Execute sem --detach para ver o motivo.");
        }, token);
        if (runId is null)
            throw new CliException(5, "A execução em segundo plano não foi registrada em 30 s. Confira com: dbct run status");
        WriteResult(parse, new { runId, processId = child.Id, background = true, storeDirectory });
        error.WriteLine("Execução em segundo plano. Acompanhe com: dbct run status. Pare com: dbct run stop");
        return 0;
    }

    /// <summary>Same arguments, without what belongs to this terminal.</summary>
    private static List<string> DetachedArguments(IReadOnlyList<string> original, string storeDirectory, bool hasSecret)
    {
        string[] flags = ["--detach", "--password-stdin", "--no-progress"];
        string[] withValue = ["--password-env", "--data-dir", "--format"];
        var result = new List<string>();
        for (var index = 0; index < original.Count; index++)
        {
            var argument = original[index];
            var name = argument.Split('=', ':')[0];
            if (flags.Contains(name))
            {
                if (name == argument && index + 1 < original.Count && original[index + 1] is "true" or "false") index++;
                continue;
            }
            if (withValue.Contains(name))
            {
                if (name == argument) index++;
                continue;
            }
            result.Add(argument);
        }
        result.AddRange(["--no-progress", "--format", "json", "--data-dir", storeDirectory]);
        if (hasSecret) result.AddRange(["--password-env", DetachedSecretVariable]);
        return result;
    }

    /// <summary>Waits for a Running run started after <paramref name="started"/>.</summary>
    private static async Task<Guid?> WaitForNewRunAsync(SqliteApplicationStore store, DateTimeOffset started,
        Action checkFailure, CancellationToken token)
    {
        var history = new RunHistoryRepository(store);
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < StartConfirmation)
        {
            var running = await history.SearchAsync(new RunHistoryFilter(Status: PersistedRunStatus.Running),
                new PageRequest(1, 20), token);
            // Dates are compared as DateTimeOffset: stored text may carry a local offset.
            var run = running.Items.Where(item => item.StartedAt >= started.AddSeconds(-2))
                .OrderByDescending(item => item.StartedAt).FirstOrDefault();
            if (run is not null) return run.RunId;
            checkFailure();
            await Task.Delay(250, token);
        }
        return null;
    }

    private async Task<int> RunStatusAsync(ParseResult parse, CancellationToken token)
    {
        var store = await OpenStoreAsync(parse, false, token);
        var storeDirectory = StoreDirectory(store);
        var history = new RunHistoryRepository(store);
        var writer = WriterInfo.Read(storeDirectory);
        var running = await history.SearchAsync(new RunHistoryFilter(Status: PersistedRunStatus.Running), new PageRequest(1, 20), token);
        var runs = new List<object>();
        foreach (var run in running.Items)
        {
            var last = (await history.GetCyclesAfterAsync(run.RunId, Math.Max(0, run.CompletedCycles - 1), 1, token)).FirstOrDefault();
            runs.Add(new
            {
                run.RunId, run.ProfileName, run.Target, run.DatabaseType, run.StartedAt, run.CompletedCycles,
                lastCycleAt = last?.StartedAt,
                // Running without a live writer: ended by force; recovered as Interrupted on the next run.
                abandoned = writer is null
            });
        }
        var autostart = new AutostartStore(storeDirectory);
        var config = autostart.Load();
        var state = autostart.LoadState();
        WriteResult(parse, new
        {
            active = writer is not null,
            writer = writer is null ? null : new
            {
                writer.ProcessId,
                kind = writer.IsDesktop ? "gui" : writer.IsCli ? "cli" : writer.ProcessName,
                since = writer.AcquiredAt,
                autostart = state?.ProcessId is { } loop && loop == writer.ProcessId
            },
            runs,
            autostart = config is null ? null : new
            {
                config.TaskName, config.Scope, loopRunning = IsCliProcess(state?.ProcessId)
            },
            storeDirectory
        });
        return 0;
    }

    private async Task<int> RunStopAsync(ParseResult parse, CancellationToken token)
    {
        var timeout = parse.GetValue<int>("--timeout");
        if (timeout is < 1 or > 600) throw new CliException(2, "--timeout deve estar entre 1 e 600 segundos.");
        var store = await OpenStoreAsync(parse, false, token);
        var storeDirectory = StoreDirectory(store);
        var history = new RunHistoryRepository(store);
        var running = (await history.SearchAsync(new RunHistoryFilter(Status: PersistedRunStatus.Running),
            new PageRequest(1, 20), token)).Items;
        var writer = WriterInfo.Read(storeDirectory);
        if (writer is null)
            throw new CliException(2, running.Count > 0
                ? "Nenhuma execução ativa: o registro em Running é de uma execução encerrada à força e será marcado como Interrupted na próxima execução."
                : "Nenhuma execução ativa neste banco.");
        if (writer.IsDesktop)
            throw new CliException(2, "A execução ativa pertence à interface gráfica; pare-a pela GUI.");
        var requested = parse.GetValue<Guid?>("id");
        var target = requested ?? running.OrderByDescending(item => item.StartedAt).FirstOrDefault()?.RunId
            ?? throw new CliException(2, "Nenhuma execução em Running neste banco.");
        if (requested is not null && running.All(item => item.RunId != requested))
            throw new CliException(2, "A execução informada não está em andamento.");

        StopRequests.Write(storeDirectory, target);
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(timeout))
        {
            var details = await history.GetDetailsAsync(target, token);
            if (details is not null && details.Run.Status != PersistedRunStatus.Running)
            {
                WriteResult(parse, new { runId = target, details.Run.Status, details.Run.CompletedCycles });
                var state = new AutostartStore(storeDirectory).LoadState();
                if (state?.ProcessId == writer.ProcessId)
                    error.WriteLine("A execução automática volta no próximo início do Windows. " +
                        "Para retomar agora: dbct autostart start. Para removê-la: dbct autostart disable");
                return 0;
            }
            await Task.Delay(500, token);
        }
        throw new CliException(5, $"A execução não confirmou a parada em {timeout} s. O pedido continua pendente.");
    }

    private static bool IsCliProcess(int? processId)
    {
        if (processId is not { } id) return false;
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited && WriterInfo.NameOf(process).Equals(WriterInfo.CliProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return false; }
    }
}
