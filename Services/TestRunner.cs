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
        await using var csvStream = new FileStream(settings.CsvPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        await using var logStream = new FileStream(settings.TxtPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        await using var csv = new StreamWriter(csvStream, utf8Bom) { AutoFlush = true };
        await using var log = new StreamWriter(logStream, utf8Bom) { AutoFlush = true };

        await csv.WriteLineAsync(CsvHeader);
        await WriteLogHeaderAsync(log, settings);

        var metrics = new RunMetrics();
        var stopped = false;

        try
        {
            for (long number = 1; settings.Continuous || number <= settings.TestCount; number++)
            {
                token.ThrowIfCancellationRequested();
                var startedAt = DateTimeOffset.Now;

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

                var cycle = new TestCycleResult(number, startedAt, dns, ping, tcp, database);
                metrics.Add(settings, cycle);
                await csv.WriteLineAsync(BuildCsvRow(settings, cycle));
                await log.WriteLineAsync(BuildLogLine(settings, cycle));

                progress?.Report(metrics.CreateProgress());

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
        if (settings.Profile.UsesNetwork)
            await log.WriteLineAsync($"DNS: OK={metrics.DnsOk} | Falhas={metrics.DnsFailures} | Média={RunMetrics.Average(metrics.DnsSum, metrics.DnsOk)}ms");
        if (settings.Ping)
            await log.WriteLineAsync($"PING: OK={metrics.PingOk} | Falhas={metrics.PingFailures} | Média={RunMetrics.Average(metrics.PingSum, metrics.PingOk)}ms");
        if (settings.Tcp)
            await log.WriteLineAsync($"TCP: OK={metrics.TcpOk} | Falhas={metrics.TcpFailures} | Média={RunMetrics.Average(metrics.TcpSum, metrics.TcpOk)}ms");
        if (settings.DatabaseTest)
        {
            await log.WriteLineAsync($"DB CONNECT: OK={metrics.DatabaseConnectOk} | Falhas={metrics.DatabaseConnectFailures} | Média={RunMetrics.Average(metrics.DatabaseConnectSum, metrics.DatabaseConnectOk)}ms");
            await log.WriteLineAsync($"DB SELECT 1: OK={metrics.DatabaseQueryOk} | Falhas={metrics.DatabaseQueryFailures} | Média={RunMetrics.Average(metrics.DatabaseQuerySum, metrics.DatabaseQueryOk)}ms");
        }
    }

    private static string BuildCsvRow(TestSettings settings, TestCycleResult cycle) =>
        string.Join(";", new[]
        {
            Csv(cycle.StartedAt.ToString("yyyy-MM-dd HH:mm:ss.fff")),
            cycle.Number.ToString(CultureInfo.InvariantCulture),
            Csv(Environment.MachineName),
            Csv(settings.Profile.DisplayName),
            Csv(settings.Target),
            Csv(cycle.Dns.ResolvedIp),
            Csv(cycle.Dns.Status.ToOutputText()),
            cycle.Dns.ElapsedMs.ToString(CultureInfo.InvariantCulture),
            Csv(cycle.Ping.Status.ToOutputText()),
            cycle.Ping.ElapsedMs.ToString(CultureInfo.InvariantCulture),
            Csv(cycle.Ping.Extra),
            Csv(cycle.Tcp.Status.ToOutputText()),
            cycle.Tcp.ElapsedMs.ToString(CultureInfo.InvariantCulture),
            Csv(cycle.Tcp.LocalIp),
            Csv(cycle.Tcp.RemoteIp),
            Csv(cycle.Database.ConnectStatus.ToOutputText()),
            cycle.Database.ConnectMs.ToString(CultureInfo.InvariantCulture),
            Csv(cycle.Database.QueryStatus.ToOutputText()),
            cycle.Database.QueryMs.ToString(CultureInfo.InvariantCulture),
            cycle.Database.TotalMs.ToString(CultureInfo.InvariantCulture),
            Csv(cycle.Dns.Error),
            Csv(cycle.Ping.Error),
            Csv(cycle.Tcp.Error),
            Csv(cycle.Database.Error)
        });

    private static string BuildLogLine(TestSettings settings, TestCycleResult cycle)
    {
        var line = $"{cycle.StartedAt:yyyy-MM-dd HH:mm:ss.fff} | #{cycle.Number} | DB_TYPE={settings.Profile.DisplayName} | " +
                   $"DNS={cycle.Dns.Status.ToOutputText()} {cycle.Dns.ElapsedMs}ms {cycle.Dns.ResolvedIp} | PING={cycle.Ping.Status.ToOutputText()} {cycle.Ping.ElapsedMs}ms | " +
                   $"TCP={cycle.Tcp.Status.ToOutputText()} {cycle.Tcp.ElapsedMs}ms | DB_CONNECT={cycle.Database.ConnectStatus.ToOutputText()} {cycle.Database.ConnectMs}ms | " +
                   $"DB_QUERY={cycle.Database.QueryStatus.ToOutputText()} {cycle.Database.QueryMs}ms";
        var error = FirstNonEmpty(cycle.Dns.Error, cycle.Ping.Error, cycle.Tcp.Error, cycle.Database.Error);
        return string.IsNullOrWhiteSpace(error) ? line : line + $" | ERRO={error}";
    }

    private static string Csv(string? value)
    {
        value ??= "";
        return value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

}
