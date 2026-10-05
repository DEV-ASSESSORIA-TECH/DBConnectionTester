using System.Globalization;
using System.Text;
using DBConnectionTester.Models;

namespace DBConnectionTester.Services;

public sealed class TestRunner
{
    public const string CsvHeader = "Timestamp;Test_Number;Machine;DB_Type;Target;Resolved_IP;DNS_Status;DNS_ms;Ping_Status;Ping_ms;TTL;TCP_Status;TCP_ms;TCP_Local_IP;TCP_Remote_IP;DB_Connect_Status;DB_Connect_ms;DB_Query_Status;DB_Query_ms;DB_Total_ms;DNS_Error;Ping_Error;TCP_Error;DB_Error";

    public async Task<RunSummary> RunAsync(
        TestSettings settings,
        IProgress<TestProgress>? progress,
        CancellationToken token)
    {
        var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await using var csv = new StreamWriter(settings.CsvPath, false, utf8Bom) { AutoFlush = true };
        await using var log = new StreamWriter(settings.TxtPath, false, utf8Bom) { AutoFlush = true };

        await csv.WriteLineAsync(CsvHeader);
        await WriteLogHeaderAsync(log, settings);

        var metrics = new RunMetrics();
        var stopped = false;

        try
        {
            for (long number = 1; settings.Continuous || number <= settings.TestCount; number++)
            {
                token.ThrowIfCancellationRequested();
                var timestamp = DateTime.Now;

                var dns = settings.Profile.UsesNetwork
                    ? await NetworkTester.TestDnsAsync(settings.Host, settings.Timeout, token)
                    : DnsResult.Skipped();
                var ping = settings.Ping
                    ? await NetworkTester.TestPingAsync(settings.Host, settings.Timeout, token)
                    : StepResult.Skipped();
                var tcp = settings.Tcp
                    ? await NetworkTester.TestTcpAsync(settings.Host, settings.Port, settings.Timeout, token)
                    : TcpResult.Skipped();
                var database = settings.DatabaseTest
                    ? await DatabaseTester.TestAsync(settings, token)
                    : DatabaseResult.Skipped();

                metrics.Add(settings, ping, tcp, database);
                await csv.WriteLineAsync(BuildCsvRow(settings, timestamp, number, dns, ping, tcp, database));
                await log.WriteLineAsync(BuildLogLine(settings, timestamp, number, dns, ping, tcp, database));

                progress?.Report(new TestProgress(number, metrics.PingFailures, metrics.TcpFailures,
                    metrics.DatabaseConnectFailures + metrics.DatabaseQueryFailures));

                if (!settings.Continuous && number >= settings.TestCount)
                    break;
                if (settings.Interval > TimeSpan.Zero)
                    await Task.Delay(settings.Interval, token);
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }
        finally
        {
            await WriteLogSummaryAsync(log, settings, metrics, stopped);
        }

        return new RunSummary(metrics.Completed, stopped);
    }

    private static async Task WriteLogHeaderAsync(StreamWriter log, TestSettings settings)
    {
        await log.WriteLineAsync("CONNECTION TESTER - DNS / PING / TCP / DATABASE");
        await log.WriteLineAsync($"Início: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        await log.WriteLineAsync($"Máquina: {Environment.MachineName}");
        await log.WriteLineAsync($"Tipo: {settings.Profile.DisplayName}");
        await log.WriteLineAsync($"Destino: {settings.Target}");
        if (settings.DatabaseType != DatabaseType.TcpOnly)
            await log.WriteLineAsync($"Banco informado: {(string.IsNullOrWhiteSpace(settings.Database) ? "(nenhum)" : settings.Database)}");
        await log.WriteLineAsync($"Modo: {(settings.Continuous ? "CONTÍNUO - até encerramento manual" : $"LIMITADO - {settings.TestCount} testes")}");
        await log.WriteLineAsync($"Intervalo: {settings.Interval.TotalSeconds:0.0}s | Timeout: {settings.Timeout.TotalSeconds:0.0}s");
        await log.WriteLineAsync($"Camadas: DNS={settings.Profile.UsesNetwork} | Ping={settings.Ping} | TCP={settings.Tcp} | Banco={settings.DatabaseTest}");
        await log.WriteLineAsync(new string('-', 120));
    }

    private static async Task WriteLogSummaryAsync(StreamWriter log, TestSettings settings, RunMetrics metrics, bool stopped)
    {
        await log.WriteLineAsync(new string('-', 120));
        await log.WriteLineAsync($"Fim: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        await log.WriteLineAsync($"Motivo do encerramento: {(stopped ? "Interrompido manualmente / encerramento do aplicativo" : "Quantidade planejada concluída")}");
        await log.WriteLineAsync($"Testes concluídos: {metrics.Completed}");
        if (settings.Ping)
            await log.WriteLineAsync($"PING: OK={metrics.PingOk} | Falhas={metrics.PingFailures} | Média={Average(metrics.PingSum, metrics.PingOk)}ms");
        if (settings.Tcp)
            await log.WriteLineAsync($"TCP: OK={metrics.TcpOk} | Falhas={metrics.TcpFailures} | Média={Average(metrics.TcpSum, metrics.TcpOk)}ms");
        if (settings.DatabaseTest)
        {
            await log.WriteLineAsync($"DB CONNECT: OK={metrics.DatabaseConnectOk} | Falhas={metrics.DatabaseConnectFailures} | Média={Average(metrics.DatabaseConnectSum, metrics.DatabaseConnectOk)}ms");
            await log.WriteLineAsync($"DB SELECT 1: OK={metrics.DatabaseQueryOk} | Falhas={metrics.DatabaseQueryFailures} | Média={Average(metrics.DatabaseQuerySum, metrics.DatabaseQueryOk)}ms");
        }
    }

    private static string BuildCsvRow(TestSettings settings, DateTime timestamp, long number,
        DnsResult dns, StepResult ping, TcpResult tcp, DatabaseResult database) =>
        string.Join(";", new[]
        {
            Csv(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")),
            number.ToString(CultureInfo.InvariantCulture),
            Csv(Environment.MachineName),
            Csv(settings.Profile.DisplayName),
            Csv(settings.Target),
            Csv(dns.ResolvedIp),
            Csv(dns.Status),
            dns.ElapsedMs.ToString(CultureInfo.InvariantCulture),
            Csv(ping.Status),
            ping.ElapsedMs.ToString(CultureInfo.InvariantCulture),
            Csv(ping.Extra),
            Csv(tcp.Status),
            tcp.ElapsedMs.ToString(CultureInfo.InvariantCulture),
            Csv(tcp.LocalIp),
            Csv(tcp.RemoteIp),
            Csv(database.ConnectStatus),
            database.ConnectMs.ToString(CultureInfo.InvariantCulture),
            Csv(database.QueryStatus),
            database.QueryMs.ToString(CultureInfo.InvariantCulture),
            database.TotalMs.ToString(CultureInfo.InvariantCulture),
            Csv(dns.Error),
            Csv(ping.Error),
            Csv(tcp.Error),
            Csv(database.Error)
        });

    private static string BuildLogLine(TestSettings settings, DateTime timestamp, long number,
        DnsResult dns, StepResult ping, TcpResult tcp, DatabaseResult database)
    {
        var line = $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} | #{number} | DB_TYPE={settings.Profile.DisplayName} | " +
                   $"DNS={dns.Status} {dns.ElapsedMs}ms {dns.ResolvedIp} | PING={ping.Status} {ping.ElapsedMs}ms | " +
                   $"TCP={tcp.Status} {tcp.ElapsedMs}ms | DB_CONNECT={database.ConnectStatus} {database.ConnectMs}ms | " +
                   $"DB_QUERY={database.QueryStatus} {database.QueryMs}ms";
        var error = FirstNonEmpty(dns.Error, ping.Error, tcp.Error, database.Error);
        return string.IsNullOrWhiteSpace(error) ? line : line + $" | ERRO={error}";
    }

    private static long Average(long sum, int count) => count == 0 ? 0 : sum / count;

    private static string Csv(string? value)
    {
        value ??= "";
        return value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private sealed class RunMetrics
    {
        public long Completed { get; private set; }
        public int PingOk { get; private set; }
        public int PingFailures { get; private set; }
        public long PingSum { get; private set; }
        public int TcpOk { get; private set; }
        public int TcpFailures { get; private set; }
        public long TcpSum { get; private set; }
        public int DatabaseConnectOk { get; private set; }
        public int DatabaseConnectFailures { get; private set; }
        public long DatabaseConnectSum { get; private set; }
        public int DatabaseQueryOk { get; private set; }
        public int DatabaseQueryFailures { get; private set; }
        public long DatabaseQuerySum { get; private set; }

        public void Add(TestSettings settings, StepResult ping, TcpResult tcp, DatabaseResult database)
        {
            Completed++;
            if (settings.Ping)
            {
                if (ping.Ok) { PingOk++; PingSum += ping.ElapsedMs; } else PingFailures++;
            }
            if (settings.Tcp)
            {
                if (tcp.Ok) { TcpOk++; TcpSum += tcp.ElapsedMs; } else TcpFailures++;
            }
            if (settings.DatabaseTest)
            {
                if (database.ConnectOk) { DatabaseConnectOk++; DatabaseConnectSum += database.ConnectMs; } else DatabaseConnectFailures++;
                if (database.QueryOk) { DatabaseQueryOk++; DatabaseQuerySum += database.QueryMs; } else DatabaseQueryFailures++;
            }
        }
    }
}
