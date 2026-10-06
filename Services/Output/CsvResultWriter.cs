using System.Globalization;
using System.Text;
using DBConnectionTester.Models;

namespace DBConnectionTester.Services.Output;

public sealed class CsvResultWriter : IAsyncDisposable
{
    public const string Header = "Timestamp;Test_Number;Machine;DB_Type;Target;Resolved_IP;DNS_Status;DNS_ms;Ping_Status;Ping_ms;TTL;TCP_Status;TCP_ms;TCP_Local_IP;TCP_Remote_IP;DB_Connect_Status;DB_Connect_ms;DB_Query_Status;DB_Query_ms;DB_Total_ms;DNS_Error;Ping_Error;TCP_Error;DB_Error;" +
        "DNS_Diagnostic_Code;DNS_Suggestion_Code;DNS_Provider;DNS_Provider_Code;DNS_SQL_State;DNS_Native_Code;DNS_Technical_Message;" +
        "Ping_Diagnostic_Code;Ping_Suggestion_Code;Ping_Provider;Ping_Provider_Code;Ping_SQL_State;Ping_Native_Code;Ping_Technical_Message;" +
        "TCP_Diagnostic_Code;TCP_Suggestion_Code;TCP_Provider;TCP_Provider_Code;TCP_SQL_State;TCP_Native_Code;TCP_Technical_Message;" +
        "DB_Connect_Diagnostic_Code;DB_Connect_Suggestion_Code;DB_Connect_Provider;DB_Connect_Provider_Code;DB_Connect_SQL_State;DB_Connect_Native_Code;DB_Connect_Technical_Message;" +
        "DB_Query_Diagnostic_Code;DB_Query_Suggestion_Code;DB_Query_Provider;DB_Query_Provider_Code;DB_Query_SQL_State;DB_Query_Native_Code;DB_Query_Technical_Message";

    private readonly TestSettings settings;
    private readonly StreamWriter writer;

    private CsvResultWriter(TestSettings settings, StreamWriter writer)
    {
        this.settings = settings;
        this.writer = writer;
    }

    public static async Task<CsvResultWriter> CreateAsync(TestSettings settings)
    {
        var stream = CreateFile(settings.CsvPath);
        var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)) { AutoFlush = true };
        try
        {
            await writer.WriteLineAsync(Header);
            return new CsvResultWriter(settings, writer);
        }
        catch
        {
            await writer.DisposeAsync();
            throw;
        }
    }

    public Task WriteCycleAsync(TestCycleResult cycle, CancellationToken token) =>
        writer.WriteLineAsync(BuildRow(cycle).AsMemory(), token);

    public ValueTask DisposeAsync() => writer.DisposeAsync();

    private string BuildRow(TestCycleResult cycle)
    {
        var values = new List<string>
        {
        Escape(cycle.StartedAt.ToString("yyyy-MM-dd HH:mm:ss.fff")),
        cycle.Number.ToString(CultureInfo.InvariantCulture),
        Escape(Environment.MachineName),
        Escape(settings.Profile.DisplayName),
        Escape(settings.Target),
        Escape(cycle.Dns.ResolvedIp),
        Escape(cycle.Dns.Status.ToOutputText()),
        cycle.Dns.ElapsedMs.ToString(CultureInfo.InvariantCulture),
        Escape(cycle.Ping.Status.ToOutputText()),
        cycle.Ping.ElapsedMs.ToString(CultureInfo.InvariantCulture),
        Escape(cycle.Ping.Extra),
        Escape(cycle.Tcp.Status.ToOutputText()),
        cycle.Tcp.ElapsedMs.ToString(CultureInfo.InvariantCulture),
        Escape(cycle.Tcp.LocalIp),
        Escape(cycle.Tcp.RemoteIp),
        Escape(cycle.Database.ConnectStatus.ToOutputText()),
        cycle.Database.ConnectMs.ToString(CultureInfo.InvariantCulture),
        Escape(cycle.Database.QueryStatus.ToOutputText()),
        cycle.Database.QueryMs.ToString(CultureInfo.InvariantCulture),
        cycle.Database.TotalMs.ToString(CultureInfo.InvariantCulture),
        Escape(cycle.Dns.Error),
        Escape(cycle.Ping.Error),
        Escape(cycle.Tcp.Error),
        Escape(cycle.Database.Error)
        };
        AddDiagnostic(values, cycle.Dns.Diagnostic);
        AddDiagnostic(values, cycle.Ping.Diagnostic);
        AddDiagnostic(values, cycle.Tcp.Diagnostic);
        AddDiagnostic(values, cycle.Database.ConnectDiagnostic);
        AddDiagnostic(values, cycle.Database.QueryDiagnostic);
        return string.Join(";", values);
    }

    private static void AddDiagnostic(ICollection<string> values, DiagnosticIssue? issue)
    {
        values.Add(Escape(issue?.DiagnosticCode));
        values.Add(Escape(issue?.SuggestionCode));
        values.Add(Escape(issue?.ProviderError?.Provider));
        values.Add(Escape(issue?.ProviderError?.OriginalCode));
        values.Add(Escape(issue?.ProviderError?.SqlState));
        values.Add(Escape(issue?.ProviderError?.NativeCode));
        values.Add(Escape(issue?.TechnicalMessage));
    }

    internal static string Escape(string? value)
    {
        value ??= "";
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
            value = "'" + value;
        return value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
    }

    private static FileStream CreateFile(string path) => new(path, new FileStreamOptions
    {
        Mode = FileMode.CreateNew,
        Access = FileAccess.Write,
        Share = FileShare.Read,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan
    });
}
