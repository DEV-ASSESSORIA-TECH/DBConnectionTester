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

    public async Task WriteCycleAsync(TestCycleResult cycle, CancellationToken token)
    {
        await writer.WriteLineAsync(BuildLine(cycle).AsMemory(), token);
        foreach (var (stage, diagnostic) in Diagnostics(cycle))
        {
            if (diagnostic is not null)
                await writer.WriteLineAsync(BuildDiagnosticLine(stage, diagnostic).AsMemory(), token);
        }
    }

    public async Task CompleteAsync(RunSummary summary, RunMetrics metrics)
    {
        await writer.WriteLineAsync(new string('-', 120));
        await writer.WriteLineAsync($"Fim: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        await writer.WriteLineAsync($"Motivo do encerramento: {(summary.Stopped ? "Interrompido manualmente / encerramento do aplicativo" : "Quantidade planejada concluída")}");
        await writer.WriteLineAsync($"Testes concluídos: {metrics.Completed}");
        var statistics = metrics.CreateStatistics();
        if (settings.Dns)
            await WriteStatisticsAsync("DNS", statistics.Dns);
        if (settings.Ping)
            await WriteStatisticsAsync("PING", statistics.Ping);
        if (settings.Tcp)
            await WriteStatisticsAsync("TCP", statistics.Tcp);
        if (settings.DatabaseTest)
        {
            await WriteStatisticsAsync("DB CONNECT", statistics.DatabaseConnect);
            await WriteStatisticsAsync("DB SELECT 1", statistics.DatabaseQuery);
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
        await writer.WriteLineAsync($"Camadas: DNS={settings.Dns} | Ping={settings.Ping} | TCP={settings.Tcp} | Banco={settings.DatabaseTest}");
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

    private static IEnumerable<(string Stage, DiagnosticIssue? Diagnostic)> Diagnostics(TestCycleResult cycle)
    {
        yield return ("DNS", cycle.Dns.Diagnostic);
        yield return ("PING", cycle.Ping.Diagnostic);
        yield return ("TCP", cycle.Tcp.Diagnostic);
        yield return ("DB_CONNECT", cycle.Database.ConnectDiagnostic);
        yield return ("DB_QUERY", cycle.Database.QueryDiagnostic);
    }

    private static string BuildDiagnosticLine(string stage, DiagnosticIssue issue)
    {
        var suggestion = DiagnosticCatalog.GetSuggestion(issue.SuggestionCode);
        var provider = issue.ProviderError;
        return $"  DIAGNÓSTICO {stage}: Código={issue.DiagnosticCode} | Sugestão={issue.SuggestionCode} | " +
               $"Ação={suggestion.Action} | Provider={provider?.Provider ?? "N/A"} | " +
               $"Código original={provider?.OriginalCode ?? "N/A"} | SQLSTATE={provider?.SqlState ?? "N/A"} | " +
               $"Código nativo={provider?.NativeCode ?? "N/A"} | Confiança={issue.Confidence} | " +
               $"Detalhe={issue.TechnicalMessage}";
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private async Task WriteStatisticsAsync(string name, StageStatisticsSnapshot statistics)
    {
        var successRate = statistics.SuccessRate is null ? "N/A" : $"{statistics.SuccessRate:0.0}%";
        var sinceFailure = FormatElapsed(statistics.TimeSinceLastFailure(DateTimeOffset.Now));
        await writer.WriteLineAsync(
            $"{name}: OK={statistics.Successes}/{statistics.Attempts} ({successRate}) | Falhas={statistics.Failures} | " +
            $"Média={FormatLatency(statistics.AverageMs)} | Mín={FormatLatency(statistics.MinimumMs)} | " +
            $"Máx={FormatLatency(statistics.MaximumMs)} | Mediana={FormatLatency(statistics.MedianMs)} | " +
            $"P95={FormatLatency(statistics.P95Ms)} | Falhas seguidas={statistics.ConsecutiveFailures} | " +
            $"Maior sequência={statistics.MaximumConsecutiveFailures} | Desde última falha={sinceFailure}");
    }

    private static string FormatLatency(double? milliseconds) =>
        milliseconds is null ? "N/A" : $"{milliseconds:0.0}ms";

    private static string FormatElapsed(TimeSpan? elapsed)
    {
        if (elapsed is null)
            return "nunca";
        if (elapsed.Value.TotalSeconds < 60)
            return $"{Math.Max(0, elapsed.Value.TotalSeconds):0}s";
        if (elapsed.Value.TotalMinutes < 60)
            return $"{elapsed.Value.TotalMinutes:0.0}min";
        if (elapsed.Value.TotalHours < 24)
            return $"{elapsed.Value.TotalHours:0.0}h";
        return $"{elapsed.Value.TotalDays:0.0}d";
    }
}
