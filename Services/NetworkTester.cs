using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DBConnectionTester.Models;

namespace DBConnectionTester.Services;

internal static class NetworkTester
{
    public static async Task<DnsResult> TestDnsAsync(string host, TimeSpan timeout, CancellationToken token)
    {
        if (IPAddress.TryParse(host, out var parsed))
            return new DnsResult(StepStatus.Success, parsed.ToString(), 0, null);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host).WaitAsync(timeout, token);
            stopwatch.Stop();
            var ip = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)?.ToString()
                     ?? addresses.FirstOrDefault()?.ToString()
                     ?? "";
            var diagnostic = addresses.Length > 0
                ? null
                : DiagnosticCatalog.Create(DiagnosticCodes.DnsNoAddresses, "A resolução foi concluída sem endereços.");
            return new DnsResult(addresses.Length > 0 ? StepStatus.Success : StepStatus.Failed, ip,
                (long)stopwatch.Elapsed.TotalMilliseconds, diagnostic);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new DnsResult(StepStatus.Failed, "", (long)stopwatch.Elapsed.TotalMilliseconds,
                DiagnosticClassifier.Dns(exception));
        }
    }

    public static async Task<StepResult> TestPingAsync(string host, TimeSpan timeout, CancellationToken token)
    {
        using var ping = new Ping();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var reply = await ping.SendPingAsync(host, (int)timeout.TotalMilliseconds)
                .WaitAsync(timeout + TimeSpan.FromSeconds(1), token);
            stopwatch.Stop();
            return reply.Status == IPStatus.Success
                ? new StepResult(StepStatus.Success, reply.RoundtripTime, reply.Options?.Ttl.ToString() ?? "", null)
                : new StepResult(StepStatus.Failed, (long)stopwatch.Elapsed.TotalMilliseconds, "",
                    DiagnosticClassifier.Ping(reply.Status));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new StepResult(StepStatus.Failed, (long)stopwatch.Elapsed.TotalMilliseconds, "",
                DiagnosticClassifier.Ping(exception));
        }
    }

    public static async Task<TcpResult> TestTcpAsync(string host, int port, TimeSpan timeout, CancellationToken token)
    {
        using var client = new TcpClient();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(host, port).WaitAsync(timeout, token);
            stopwatch.Stop();
            var local = (client.Client.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "";
            var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
            return new TcpResult(StepStatus.Success, (long)stopwatch.Elapsed.TotalMilliseconds, local, remote, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new TcpResult(StepStatus.Failed, (long)stopwatch.Elapsed.TotalMilliseconds, "", "",
                DiagnosticClassifier.Tcp(exception));
        }
    }
}
