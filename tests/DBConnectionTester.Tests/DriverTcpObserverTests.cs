using System.Net;
using System.Net.Sockets;
using DBConnectionTester.Models;
using DBConnectionTester.Services;

namespace DBConnectionTester.Tests;

public sealed class DriverTcpObserverTests
{
    [Fact]
    public async Task MySqlCycleOpensOnlyTheDriverConnection()
    {
        // A fake server that accepts and closes: the driver fails the handshake, but TCP connected.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = 0;
        using var stop = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    Interlocked.Increment(ref accepted);
                }
            }
            catch (OperationCanceledException) { }
        });

        var cycle = await new TestCycleExecutor().ExecuteAsync(MySqlSettings(port), 1, CancellationToken.None);
        // Lets a stray extra connection reach the listener before counting.
        await Task.Delay(200);
        stop.Cancel();
        listener.Stop();
        await server;

        Assert.Equal(1, Volatile.Read(ref accepted));
        Assert.Equal(StepStatus.Success, cycle.Tcp.Status);
        Assert.Equal("127.0.0.1", cycle.Tcp.RemoteIp);
        Assert.Equal(StepStatus.Failed, cycle.Database.ConnectStatus);
    }

    [Fact]
    public async Task MySqlCycleReportsRefusedTcpFromTheDriverConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var cycle = await new TestCycleExecutor().ExecuteAsync(MySqlSettings(port), 1, CancellationToken.None);

        Assert.Equal(StepStatus.Failed, cycle.Tcp.Status);
        Assert.Equal(DiagnosticCodes.TcpRefused, cycle.Tcp.Diagnostic?.DiagnosticCode);
        Assert.Equal(StepStatus.Failed, cycle.Database.ConnectStatus);
    }

    [Fact]
    public void FirstConnectedAddressWins()
    {
        var attempts = new[]
        {
            new SocketConnectAttempt(3, "10.0.0.1", SocketError.ConnectionRefused),
            new SocketConnectAttempt(7, "10.0.0.2", null)
        };

        var tcp = DriverTcpObserver.Derive(attempts, true, FailedConnect(), TimeSpan.FromSeconds(5));

        Assert.Equal(StepStatus.Success, tcp.Status);
        Assert.Equal(7, tcp.ElapsedMs);
        Assert.Equal("10.0.0.2", tcp.RemoteIp);
    }

    [Fact]
    public void PendingConnectWhenTheDatabaseStageGaveUpIsATcpTimeout()
    {
        var tcp = DriverTcpObserver.Derive([], true, FailedConnect(), TimeSpan.FromSeconds(5));

        Assert.Equal(StepStatus.Failed, tcp.Status);
        Assert.Equal(5000, tcp.ElapsedMs);
        Assert.Equal(DiagnosticCodes.TcpTimeout, tcp.Diagnostic?.DiagnosticCode);
    }

    [Fact]
    public void NoSocketMeansTheTcpStageDidNotRun()
    {
        var tcp = DriverTcpObserver.Derive([], false, FailedConnect(), TimeSpan.FromSeconds(5));

        Assert.Equal(StepStatus.Skipped, tcp.Status);
    }

    [Theory]
    [InlineData("connection_refused", SocketError.ConnectionRefused)]
    [InlineData("timed_out", SocketError.TimedOut)]
    [InlineData("host_unreachable", SocketError.HostUnreachable)]
    [InlineData("operation_aborted", SocketError.TimedOut)]
    [InlineData("System.OperationCanceledException", SocketError.TimedOut)]
    [InlineData("something_new", SocketError.SocketError)]
    [InlineData(null, SocketError.SocketError)]
    public void SocketErrorTagIsMapped(string? tag, SocketError expected)
    {
        Assert.Equal(expected, SocketConnectAttempt.ParseError(tag));
    }

    [Fact]
    public void OnlyMySqlMeasuresTcpThroughTheDriver()
    {
        var through = DatabaseProfiles.All.Where(profile => profile.TcpStageThroughDriver).Select(profile => profile.Type);

        Assert.Equal([DatabaseType.MySqlMariaDb], through);
    }

    private static DatabaseResult FailedConnect() =>
        new(StepStatus.Failed, 0, StepStatus.Skipped, 0, 0, null, null);

    private static TestSettings MySqlSettings(int port) => new(
        DatabaseType.MySqlMariaDb,
        "127.0.0.1",
        NetworkPort.Create(port),
        "dbct",
        "secret",
        "",
        "",
        SqlServerAuthentication.SqlLogin,
        "",
        RunCount.Create(1),
        false,
        TestInterval.Create(TimeSpan.Zero),
        StageTimeout.Create(TimeSpan.FromSeconds(5)),
        false,
        false,
        true,
        true);
}
