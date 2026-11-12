namespace DBConnectionTester.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
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
        Console.CancelKeyPress += handler;
        try
        {
            return await new CliApplication().InvokeAsync(args, cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}
