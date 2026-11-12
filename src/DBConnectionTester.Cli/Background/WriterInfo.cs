using System.Diagnostics;
using System.Globalization;

namespace DBConnectionTester.Cli.Background;

/// <summary>Process that currently holds the store's write lease (.run.lock), if alive.</summary>
internal sealed record WriterInfo(int ProcessId, string ProcessName, DateTimeOffset? AcquiredAt)
{
    public const string CliProcessName = "dbct";
    public const string DesktopProcessName = "DBConnectionTester";

    public bool IsCli => ProcessName.Equals(CliProcessName, StringComparison.OrdinalIgnoreCase);
    public bool IsDesktop => ProcessName.Equals(DesktopProcessName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Process name without a trailing ".exe" (present outside Windows when started through it).</summary>
    public static string NameOf(Process process) =>
        process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? process.ProcessName[..^4] : process.ProcessName;

    /// <summary>
    /// Reads the lease metadata without taking the lease, so a query never competes with a
    /// starting run. A recorded process that no longer exists means no live writer.
    /// </summary>
    public static WriterInfo? Read(string storeDirectory)
    {
        var path = Path.Combine(storeDirectory, ".run.lock");
        if (!File.Exists(path)) return null;
        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        int? processId = null;
        DateTimeOffset? acquiredAt = null;
        foreach (var line in text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var value = line[(separator + 1)..];
            switch (line[..separator])
            {
                case "ProcessId" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id):
                    processId = id;
                    break;
                case "AcquiredAt" when DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at):
                    acquiredAt = at;
                    break;
            }
        }
        if (processId is not { } pid) return null;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return null;
            var name = NameOf(process);
            // A recycled PID belongs to an unrelated program: not a writer.
            if (!name.Equals(CliProcessName, StringComparison.OrdinalIgnoreCase) &&
                !name.Equals(DesktopProcessName, StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                return null;
            return new WriterInfo(pid, name, acquiredAt);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                               or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
