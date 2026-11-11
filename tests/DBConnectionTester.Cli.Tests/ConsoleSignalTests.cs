using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using Xunit;

namespace DBConnectionTester.Cli.Tests;

public sealed class ConsoleSignalTests
{
    [Fact]
    public async Task RealCtrlCFinalizesCliProcessAndReleasesWriter()
    {
        using var fixture = new CliFixture();
        var store = await fixture.StoreAsync();
        var history = new RunHistoryRepository(store);
        var arguments = fixture.RunArguments("--continuous", "true");
        var commandLine = new StringBuilder('"' + CliFixture.ExecutablePath + "\" " +
            string.Join(" ", arguments.Select(value => '"' + value + '"')));
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Flags = 1, ShowWindow = 0 };
        // A separate hidden console lets the helper signal only this CLI, never the test runner.
        if (!CreateProcess(CliFixture.ExecutablePath, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                0x10, IntPtr.Zero, fixture.Root, ref startup, out var native))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using var process = Process.GetProcessById(native.ProcessId);
        CloseHandle(native.Thread);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            RunHistoryItem? active;
            do
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, timeout.Token);
                active = (await history.SearchAsync(new(), new(), timeout.Token)).Items.FirstOrDefault();
            } while (active is null || active.CompletedCycles < 1);

            var script = """
                Add-Type -TypeDefinition @'
                using System;
                using System.Runtime.InteropServices;
                public static class Signal {
                    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AttachConsole(uint id);
                    [DllImport("kernel32.dll")] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
                    [DllImport("kernel32.dll")] public static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
                    [DllImport("kernel32.dll")] public static extern bool FreeConsole();
                }
                '@
                """ + Environment.NewLine + $$"""
                [void][Signal]::FreeConsole()
                if (-not [Signal]::AttachConsole({{process.Id}})) {
                    [Console]::Error.WriteLine([Runtime.InteropServices.Marshal]::GetLastWin32Error())
                    exit 10
                }
                [void][Signal]::SetConsoleCtrlHandler([IntPtr]::Zero, $true)
                if (-not [Signal]::GenerateConsoleCtrlEvent(0, 0)) { exit 11 }
                Start-Sleep -Milliseconds 200
                [void][Signal]::FreeConsole()
                exit 0
                """;
            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script }) info.ArgumentList.Add(argument);
            using var helper = Process.Start(info)!;
            var output = helper.StandardOutput.ReadToEndAsync();
            var error = helper.StandardError.ReadToEndAsync();
            try { await helper.WaitForExitAsync(timeout.Token); }
            finally { if (!helper.HasExited) helper.Kill(entireProcessTree: true); }
            Assert.True(helper.ExitCode == 0, $"Signal helper exit={helper.ExitCode}: " + await output + await error);
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(GetExitCodeProcess(native.Process, out var exitCode));
            Assert.Equal(130u, exitCode);
            Assert.Equal(PersistedRunStatus.Stopped, (await history.GetDetailsAsync(active.RunId))!.Run.Status);
            using var lease = RunWriteLease.Acquire(store);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            CloseHandle(native.Process);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, Width, Height, XChars, YChars, Fill;
        public int Flags;
        public short ShowWindow, ReservedSize;
        public IntPtr ReservedPointer, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string currentDirectory,
        ref StartupInfo startup, out ProcessInfo process);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
}
