namespace DBConnectionTester.Models;

public enum DiagnosticLayer
{
    Dns,
    Ping,
    Tcp,
    DatabaseConnect,
    DatabaseQuery
}

public enum DiagnosticConfidence
{
    Exact,
    Heuristic,
    Fallback
}

public sealed record ProviderErrorEntry(
    string? Code,
    string? SqlState,
    string? NativeCode,
    string Message);

public sealed record ProviderErrorInfo(
    string Provider,
    string? OriginalCode,
    string? SqlState,
    string? NativeCode,
    string ExceptionType,
    IReadOnlyList<ProviderErrorEntry> Errors);

public sealed record DiagnosticIssue(
    string DiagnosticCode,
    string SuggestionCode,
    DiagnosticLayer Layer,
    string UserMessage,
    string TechnicalMessage,
    ProviderErrorInfo? ProviderError,
    DiagnosticConfidence Confidence)
{
    public string Summary => $"[{DiagnosticCode}] {UserMessage}";
}

public sealed record DiagnosticDefinition(
    string Code,
    DiagnosticLayer Layer,
    string Title,
    string DefaultSuggestionCode);

public sealed record SuggestionDefinition(string Code, string Title, string Action);

public static class DiagnosticCodes
{
    public const string DnsHostNotFound = "DBT-DNS-001";
    public const string DnsTemporaryFailure = "DBT-DNS-002";
    public const string DnsTimeout = "DBT-DNS-003";
    public const string DnsNoAddresses = "DBT-DNS-004";
    public const string DnsUnknown = "DBT-DNS-099";

    public const string PingTimeout = "DBT-PING-001";
    public const string PingUnreachable = "DBT-PING-002";
    public const string PingBlocked = "DBT-PING-003";
    public const string PingUnknown = "DBT-PING-099";

    public const string TcpRefused = "DBT-TCP-001";
    public const string TcpTimeout = "DBT-TCP-002";
    public const string TcpUnreachable = "DBT-TCP-003";
    public const string TcpReset = "DBT-TCP-004";
    public const string TcpAddressUnavailable = "DBT-TCP-005";
    public const string TcpUnknown = "DBT-TCP-099";

    public const string DatabaseAuthentication = "DBT-DB-001";
    public const string DatabaseNotFound = "DBT-DB-002";
    public const string DatabaseUnavailable = "DBT-DB-003";
    public const string DatabaseConnectTimeout = "DBT-DB-004";
    public const string DatabaseQueryTimeout = "DBT-DB-005";
    public const string DatabaseTls = "DBT-DB-006";
    public const string DatabasePermission = "DBT-DB-007";
    public const string DatabaseTooManyConnections = "DBT-DB-008";
    public const string DatabaseDriverMissing = "DBT-DB-009";
    public const string SqliteInvalid = "DBT-DB-010";
    public const string SqliteBusy = "DBT-DB-011";
    public const string SqliteCorrupt = "DBT-DB-012";
    public const string DatabaseUnexpectedResult = "DBT-DB-013";
    public const string DatabaseUnknown = "DBT-DB-099";
}

public static class SuggestionCodes
{
    public const string CheckDns = "DBT-FIX-DNS-001";
    public const string CheckNetwork = "DBT-FIX-NET-001";
    public const string CheckIcmp = "DBT-FIX-ICMP-001";
    public const string CheckPort = "DBT-FIX-PORT-001";
    public const string CheckCredentials = "DBT-FIX-AUTH-001";
    public const string CheckDatabase = "DBT-FIX-DB-001";
    public const string CheckTimeout = "DBT-FIX-TIMEOUT-001";
    public const string CheckTls = "DBT-FIX-TLS-001";
    public const string CheckPermission = "DBT-FIX-PERM-001";
    public const string CheckCapacity = "DBT-FIX-CAPACITY-001";
    public const string CheckDriver = "DBT-FIX-DRIVER-001";
    public const string CheckSqliteFile = "DBT-FIX-SQLITE-001";
    public const string CollectDetails = "DBT-FIX-COLLECT-001";
}

public static class DiagnosticCatalog
{
    public static IReadOnlyList<SuggestionDefinition> Suggestions { get; } =
    [
        new(SuggestionCodes.CheckDns, "Verificar DNS", "Confirme o nome do host, o sufixo DNS e o servidor DNS configurado."),
        new(SuggestionCodes.CheckNetwork, "Verificar rede", "Confirme rota, VPN, firewall e disponibilidade do destino."),
        new(SuggestionCodes.CheckIcmp, "Verificar ICMP", "Confirme se ICMP está permitido no host e nos firewalls do caminho."),
        new(SuggestionCodes.CheckPort, "Verificar porta", "Confirme host, porta, serviço em execução e regras de firewall."),
        new(SuggestionCodes.CheckCredentials, "Verificar credenciais", "Confirme usuário, senha e método de autenticação."),
        new(SuggestionCodes.CheckDatabase, "Verificar banco", "Confirme o nome do banco e se ele está disponível para o usuário."),
        new(SuggestionCodes.CheckTimeout, "Revisar timeout", "Verifique a latência e aumente o timeout somente se o serviço estiver saudável."),
        new(SuggestionCodes.CheckTls, "Verificar TLS", "Revise certificado, cadeia de confiança, nome do servidor e configuração TLS."),
        new(SuggestionCodes.CheckPermission, "Verificar permissão", "Conceda somente as permissões necessárias para conectar e executar SELECT 1."),
        new(SuggestionCodes.CheckCapacity, "Verificar capacidade", "Revise o limite e o consumo de conexões no servidor."),
        new(SuggestionCodes.CheckDriver, "Verificar driver", "Instale ou selecione o driver correto, com arquitetura compatível com o aplicativo."),
        new(SuggestionCodes.CheckSqliteFile, "Verificar arquivo SQLite", "Confirme caminho, acesso, integridade e se o arquivo é um banco SQLite válido."),
        new(SuggestionCodes.CollectDetails, "Coletar detalhes", "Use os dados técnicos e o código original do provedor para aprofundar a análise.")
    ];

    public static IReadOnlyList<DiagnosticDefinition> Diagnostics { get; } =
    [
        new(DiagnosticCodes.DnsHostNotFound, DiagnosticLayer.Dns, "Host não encontrado", SuggestionCodes.CheckDns),
        new(DiagnosticCodes.DnsTemporaryFailure, DiagnosticLayer.Dns, "Falha temporária de DNS", SuggestionCodes.CheckDns),
        new(DiagnosticCodes.DnsTimeout, DiagnosticLayer.Dns, "Timeout de DNS", SuggestionCodes.CheckDns),
        new(DiagnosticCodes.DnsNoAddresses, DiagnosticLayer.Dns, "DNS não retornou endereços", SuggestionCodes.CheckDns),
        new(DiagnosticCodes.DnsUnknown, DiagnosticLayer.Dns, "Falha de DNS não classificada", SuggestionCodes.CollectDetails),
        new(DiagnosticCodes.PingTimeout, DiagnosticLayer.Ping, "Timeout no ping", SuggestionCodes.CheckIcmp),
        new(DiagnosticCodes.PingUnreachable, DiagnosticLayer.Ping, "Destino ou rede inalcançável", SuggestionCodes.CheckNetwork),
        new(DiagnosticCodes.PingBlocked, DiagnosticLayer.Ping, "ICMP bloqueado ou não permitido", SuggestionCodes.CheckIcmp),
        new(DiagnosticCodes.PingUnknown, DiagnosticLayer.Ping, "Falha de ping não classificada", SuggestionCodes.CollectDetails),
        new(DiagnosticCodes.TcpRefused, DiagnosticLayer.Tcp, "Conexão TCP recusada", SuggestionCodes.CheckPort),
        new(DiagnosticCodes.TcpTimeout, DiagnosticLayer.Tcp, "Timeout na conexão TCP", SuggestionCodes.CheckTimeout),
        new(DiagnosticCodes.TcpUnreachable, DiagnosticLayer.Tcp, "Destino ou rede TCP inalcançável", SuggestionCodes.CheckNetwork),
        new(DiagnosticCodes.TcpReset, DiagnosticLayer.Tcp, "Conexão TCP redefinida", SuggestionCodes.CheckPort),
        new(DiagnosticCodes.TcpAddressUnavailable, DiagnosticLayer.Tcp, "Endereço local ou remoto indisponível", SuggestionCodes.CheckNetwork),
        new(DiagnosticCodes.TcpUnknown, DiagnosticLayer.Tcp, "Falha TCP não classificada", SuggestionCodes.CollectDetails),
        new(DiagnosticCodes.DatabaseAuthentication, DiagnosticLayer.DatabaseConnect, "Autenticação rejeitada", SuggestionCodes.CheckCredentials),
        new(DiagnosticCodes.DatabaseNotFound, DiagnosticLayer.DatabaseConnect, "Banco de dados não encontrado", SuggestionCodes.CheckDatabase),
        new(DiagnosticCodes.DatabaseUnavailable, DiagnosticLayer.DatabaseConnect, "Servidor de banco indisponível", SuggestionCodes.CheckPort),
        new(DiagnosticCodes.DatabaseConnectTimeout, DiagnosticLayer.DatabaseConnect, "Timeout ao conectar ao banco", SuggestionCodes.CheckTimeout),
        new(DiagnosticCodes.DatabaseQueryTimeout, DiagnosticLayer.DatabaseQuery, "Timeout ao executar consulta", SuggestionCodes.CheckTimeout),
        new(DiagnosticCodes.DatabaseTls, DiagnosticLayer.DatabaseConnect, "Falha de TLS ou certificado", SuggestionCodes.CheckTls),
        new(DiagnosticCodes.DatabasePermission, DiagnosticLayer.DatabaseQuery, "Permissão insuficiente", SuggestionCodes.CheckPermission),
        new(DiagnosticCodes.DatabaseTooManyConnections, DiagnosticLayer.DatabaseConnect, "Limite de conexões atingido", SuggestionCodes.CheckCapacity),
        new(DiagnosticCodes.DatabaseDriverMissing, DiagnosticLayer.DatabaseConnect, "Driver ausente ou incompatível", SuggestionCodes.CheckDriver),
        new(DiagnosticCodes.SqliteInvalid, DiagnosticLayer.DatabaseConnect, "Arquivo SQLite inválido", SuggestionCodes.CheckSqliteFile),
        new(DiagnosticCodes.SqliteBusy, DiagnosticLayer.DatabaseQuery, "Banco SQLite ocupado ou bloqueado", SuggestionCodes.CheckSqliteFile),
        new(DiagnosticCodes.SqliteCorrupt, DiagnosticLayer.DatabaseQuery, "Banco SQLite corrompido", SuggestionCodes.CheckSqliteFile),
        new(DiagnosticCodes.DatabaseUnexpectedResult, DiagnosticLayer.DatabaseQuery, "SELECT 1 retornou valor inesperado", SuggestionCodes.CollectDetails),
        new(DiagnosticCodes.DatabaseUnknown, DiagnosticLayer.DatabaseConnect, "Falha de banco não classificada", SuggestionCodes.CollectDetails)
    ];

    public static DiagnosticDefinition GetDiagnostic(string code) =>
        Diagnostics.First(item => item.Code == code);

    public static SuggestionDefinition GetSuggestion(string code) =>
        Suggestions.First(item => item.Code == code);

    public static DiagnosticIssue Create(
        string diagnosticCode,
        string technicalMessage,
        ProviderErrorInfo? providerError = null,
        DiagnosticConfidence confidence = DiagnosticConfidence.Exact,
        DiagnosticLayer? layer = null)
    {
        var definition = GetDiagnostic(diagnosticCode);
        return new DiagnosticIssue(
            definition.Code,
            definition.DefaultSuggestionCode,
            layer ?? definition.Layer,
            definition.Title,
            technicalMessage,
            providerError,
            confidence);
    }
}
