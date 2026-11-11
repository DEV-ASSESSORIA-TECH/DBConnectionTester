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

            var info = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            info.ArgumentList.Add(typeof(ConsoleSignalTests).Assembly.Location);
            info.ArgumentList.Add("--signal-console");
            info.ArgumentList.Add(process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
