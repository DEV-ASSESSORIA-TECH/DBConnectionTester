using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DBConnectionTester.Cli.Tests;

// VSTest loads this assembly directly. Its entry point is a test-only subprocess
// that signals an isolated console without starting PowerShell or compiling C# at runtime.
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--signal-console" || !int.TryParse(args[1], out var id)) return 2;
        using var target = Process.GetProcessById(id);
        FreeConsole();
        if (!AttachConsole((uint)id)) return 10;
        try
        {
            if (!SetConsoleCtrlHandler(IntPtr.Zero, true)) return 11;
            if (!GenerateConsoleCtrlEvent(0, 0)) return 12;
            // Stay attached until the target handles the signal and flushes its output.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await target.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { return 13; }
            return 0;
        }
        finally { FreeConsole(); }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint id);
    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll")]
    private static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();
}
