using System.Diagnostics;
using DBConnectionTester.Models;

namespace DBConnectionTester.Cli;

// Report synchronously: unlike Progress<T> on a console, no queued callback can outlive the run.
internal sealed class ConsoleProgress(TextWriter error) : IProgress<TestProgress>
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private long lastReported = -250;
    public TestProgress? Latest { get; private set; }

    public bool HasFailures => Latest is { } p &&
        (p.DnsFailures > 0 || p.PingFailures > 0 || p.TcpFailures > 0 || p.DatabaseFailures > 0);

    public void Report(TestProgress value)
    {
        Latest = value;
        if (clock.ElapsedMilliseconds - lastReported < 250) return;
        Write(value);
    }

    public void Complete()
    {
        if (Latest is { } value) Write(value);
        error.Flush();
    }

    private void Write(TestProgress value)
    {
        lastReported = clock.ElapsedMilliseconds;
        error.WriteLine($"Ciclos: {value.Completed} | Falhas DNS={value.DnsFailures} Ping={value.PingFailures} TCP={value.TcpFailures} Banco={value.DatabaseFailures}");
    }
}
