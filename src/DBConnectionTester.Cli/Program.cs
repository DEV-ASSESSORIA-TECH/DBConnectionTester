using System.Runtime.InteropServices;

namespace DBConnectionTester.Cli;

internal static class Program
{
    // Windows terminates the process shortly after a close/logoff/shutdown handler returns
    // (about 5 s for a closed console). Stay inside the handler while the run finalizes.
    private static readonly TimeSpan TerminationGrace = TimeSpan.FromSeconds(4);

    private static async Task<int> Main(string[] args)
    {
        // JSON is UTF-8 by definition. A redirected stream would otherwise use the console
        // code page and corrupt accented text read by scripts and collectors. Only the
        // redirected streams are replaced: setting Console.*Encoding would also change the
        // code page of the user's console window after dbct exits.
        var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (Console.IsOutputRedirected)
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
        if (Console.IsErrorRedirected)
            Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
        if (Console.IsInputRedirected)
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), utf8));
        using var cancellation = new CancellationTokenSource();
        using var finished = new ManualResetEventSlim();
        var signals = 0;
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            // First Ctrl+C: stop gracefully and record the run as Stopped. A second one
            // terminates the process, e.g. when a driver ignores cancellation while connecting.
            if (Interlocked.Increment(ref signals) > 1)
            {
                e.Cancel = false;
                return;
            }
            e.Cancel = true;
            Console.Error.WriteLine("Cancelando... pressione Ctrl+C novamente para encerrar imediatamente.");
            cancellation.Cancel();
        };
        // Closed console (SIGHUP) and logoff/shutdown or a service/task stop (SIGTERM):
        // without this the run stays "Running" and is only marked Interrupted later.
        Action<PosixSignalContext> terminate = context =>
        {
            context.Cancel = true;
            cancellation.Cancel();
            finished.Wait(TerminationGrace);
        };
        Console.CancelKeyPress += handler;
        using var hangup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, terminate);
        using var termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, terminate);
        try
        {
            return await new CliApplication().InvokeAsync(args, cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
            finished.Set();
        }
    }
}
