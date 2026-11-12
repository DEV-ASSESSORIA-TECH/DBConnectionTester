using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DBConnectionTester.Cli.Background;

/// <summary>A background CLI process started by <see cref="DetachedProcess"/>.</summary>
internal sealed class DetachedChild(int id, Func<int?> exitCode, Action dispose) : IDisposable
{
    public int Id { get; } = id;

    /// <summary>The exit code once the process ended, otherwise null.</summary>
    public int? ExitCode => exitCode();

    public void Dispose() => dispose();
}

/// <summary>
/// Starts a copy of the CLI that does not belong to the caller's terminal: closing the
/// window, the tab or the SSH session does not end it. On Windows it gets its own hidden
/// console (so logoff and shutdown still reach it as SIGTERM), its own process group and,
/// when the terminal's job object allows it, leaves that job.
/// </summary>
internal static class DetachedProcess
{
    public static DetachedChild Start(string executable, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, string workingDirectory) =>
        OperatingSystem.IsWindows()
            ? StartWindows(executable, arguments, environment, workingDirectory)
            : StartPortable(executable, arguments, environment, workingDirectory);

    /// <summary>Windows command-line quoting (CommandLineToArgvW rules).</summary>
    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0) return argument;
        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '"')
            {
                builder.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                builder.Append('\\', backslashes).Append(character);
            }
            backslashes = 0;
        }
        builder.Append('\\', backslashes * 2).Append('"');
        return builder.ToString();
    }

    private static DetachedChild StartPortable(string executable, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, string workingDirectory)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workingDirectory,
            // Never share the caller's streams: a reader of this process's output would
            // otherwise wait for the background run to end.
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var (name, value) in environment) info.Environment[name] = value;
        var process = Process.Start(info) ?? throw new InvalidOperationException("Processo não iniciado.");
        process.StandardInput.Close();
        // Drain without blocking: the background run writes only its final result.
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        return new DetachedChild(process.Id, () => process.HasExited ? process.ExitCode : null, process.Dispose);
    }

    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateBreakawayFromJob = 0x01000000;
    private const uint CreateNoWindow = 0x08000000;
    private const int AccessDenied = 5;
    private const uint StillActive = 259;

    private static DetachedChild StartWindows(string executable, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, string workingDirectory)
    {
        var commandLine = new StringBuilder(Quote(executable));
        foreach (var argument in arguments) commandLine.Append(' ').Append(Quote(argument));
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            variables[(string)entry.Key] = (string?)entry.Value ?? "";
        foreach (var (name, value) in environment) variables[name] = value;
        var block = new StringBuilder();
        foreach (var (name, value) in variables) block.Append(name).Append('=').Append(value).Append('\0');
        block.Append('\0');

        var environmentBlock = Marshal.StringToHGlobalUni(block.ToString());
        try
        {
            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
            const uint flags = CreateNewProcessGroup | CreateUnicodeEnvironment | CreateNoWindow;
            // Leave the terminal's job (Windows Terminal, OpenSSH) when it allows breakaway,
            // so ending the session does not end the run; otherwise stay in it.
            var created = CreateProcessW(executable, new StringBuilder(commandLine.ToString()), IntPtr.Zero, IntPtr.Zero,
                false, flags | CreateBreakawayFromJob, environmentBlock, workingDirectory, ref startup, out var information);
            if (!created && Marshal.GetLastWin32Error() == AccessDenied)
                created = CreateProcessW(executable, new StringBuilder(commandLine.ToString()), IntPtr.Zero, IntPtr.Zero,
                    false, flags, environmentBlock, workingDirectory, ref startup, out information);
            if (!created) throw new Win32Exception(Marshal.GetLastWin32Error());
            CloseHandle(information.hThread);
            var handle = new SafeProcessHandle(information.hProcess, ownsHandle: true);
            return new DetachedChild(information.dwProcessId,
                () => GetExitCodeProcess(handle, out var code) && code != StillActive ? unchecked((int)code) : null,
                handle.Dispose);
        }
        finally
        {
            Marshal.FreeHGlobal(environmentBlock);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
