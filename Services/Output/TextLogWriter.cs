using System.Text;
using DBConnectionTester.Models;

namespace DBConnectionTester.Services.Output;

public sealed class TextLogWriter : IAsyncDisposable
{
    private readonly TestSettings settings;
    private readonly StreamWriter writer;

    private TextLogWriter(TestSettings settings, StreamWriter writer)
    {
        this.settings = settings;
        this.writer = writer;
    }

    public static async Task<TextLogWriter> CreateAsync(TestSettings settings)
    {
        var stream = new FileStream(settings.TxtPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)) { AutoFlush = true };
        try
        {
            var result = new TextLogWriter(settings, writer);
            await result.WriteHeaderAsync();
            return result;
        }
        catch
        {
            await writer.DisposeAsync();
            throw;
        }
    }

    public Task WriteCycleAsync(TestCycleResult cycle, CancellationToken token) =>
        writer.WriteLineAsync(BuildLine(cycle).AsMemory(), token);

    public async Task CompleteAsync(RunSummary summary, RunMetrics metrics)
    {
        await writer.WriteLineAsync(new string('-', 120));
        await writer.WriteLineAsync($"Fim: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        await writer.WriteLineAsync($"Motivo do encerramento: {(summary.Stopped ? "Interrompido manualmente / encerramento do aplicativo" : "Quantidade planejada concluída")}");
        await writer.WriteLineAsync($"Testes concluídos: {metrics.Completed}");
        if (settings.Profile.UsesNetwork)
            await writer.WriteLineAsync($"DNS: OK={metrics.DnsOk} | Falhas={metrics.DnsFailures} | Média={RunMetrics.Average(metrics.DnsSum, metrics.DnsOk)}ms");
        if (settings.Ping)
            await writer.WriteLineAsync($"PING: OK={metrics.PingOk} | Falhas={metrics.PingFailures} | Média={RunMetrics.Average(metrics.PingSum, metrics.PingOk)}ms");
        if (settings.Tcp)
            await writer.WriteLineAsync($"TCP: OK={metrics.TcpOk} | Falhas={metrics.TcpFailures} | Média={RunMetrics.Average(metrics.TcpSum, metrics.TcpOk)}ms");
        if (settings.DatabaseTest)
        {
            await writer.WriteLineAsync($"DB CONNECT: OK={metrics.DatabaseConnectOk} | Falhas={metrics.DatabaseConnectFailures} | Média={RunMetrics.Average(metrics.DatabaseConnectSum, metrics.DatabaseConnectOk)}ms");
            await writer.WriteLineAsync($"DB SELECT 1: OK={metrics.DatabaseQueryOk} | Falhas={metrics.DatabaseQueryFailures} | Média={RunMetrics.Average(metrics.DatabaseQuerySum, metrics.DatabaseQueryOk)}ms");
        }
    }

    public ValueTask DisposeAsync() => writer.DisposeAsync();

    private async Task WriteHeaderAsync()
    {
        await writer.WriteLineAsync("CONNECTION TESTER - DNS / PING / TCP / DATABASE");
        await writer.WriteLineAsync($"Início: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        await writer.WriteLineAsync($"Máquina: {Environment.MachineName}");
        await writer.WriteLineAsync($"Tipo: {settings.Profile.DisplayName}");
        await writer.WriteLineAsync($"Destino: {settings.Target}");
        if (settings.DatabaseType != DatabaseType.TcpOnly)
            await writer.WriteLineAsync($"Banco informado: {(string.IsNullOrWhiteSpace(settings.Database) ? "(nenhum)" : settings.Database)}");
        await writer.WriteLineAsync($"Modo: {(settings.Continuous ? "CONTÍNUO - até encerramento manual" : $"LIMITADO - {settings.TestCount} testes")}");
        await writer.WriteLineAsync($"Intervalo: {settings.Interval.TotalSeconds:0.0}s | Timeout: {settings.Timeout.TotalSeconds:0.0}s");
        await writer.WriteLineAsync($"Camadas: DNS={settings.Profile.UsesNetwork} | Ping={settings.Ping} | TCP={settings.Tcp} | Banco={settings.DatabaseTest}");
        await writer.WriteLineAsync(new string('-', 120));
    }

    private string BuildLine(TestCycleResult cycle)
    {
        var line = $"{cycle.StartedAt:yyyy-MM-dd HH:mm:ss.fff} | #{cycle.Number} | DB_TYPE={settings.Profile.DisplayName} | " +
                   $"DNS={cycle.Dns.Status.ToOutputText()} {cycle.Dns.ElapsedMs}ms {cycle.Dns.ResolvedIp} | PING={cycle.Ping.Status.ToOutputText()} {cycle.Ping.ElapsedMs}ms | " +
                   $"TCP={cycle.Tcp.Status.ToOutputText()} {cycle.Tcp.ElapsedMs}ms | DB_CONNECT={cycle.Database.ConnectStatus.ToOutputText()} {cycle.Database.ConnectMs}ms | " +
                   $"DB_QUERY={cycle.Database.QueryStatus.ToOutputText()} {cycle.Database.QueryMs}ms";
        var error = FirstNonEmpty(cycle.Dns.Error, cycle.Ping.Error, cycle.Tcp.Error, cycle.Database.Error);
        return string.IsNullOrWhiteSpace(error) ? line : line + $" | ERRO={error}";
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}
