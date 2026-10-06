using System.Data.Odbc;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using DBConnectionTester.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace DBConnectionTester.Services;

internal static partial class DiagnosticClassifier
{
    public static DiagnosticIssue Dns(Exception exception)
    {
        var source = Unwrap(exception);
        var code = source switch
        {
            TimeoutException => DiagnosticCodes.DnsTimeout,
            SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData } => DiagnosticCodes.DnsHostNotFound,
            SocketException { SocketErrorCode: SocketError.TryAgain } => DiagnosticCodes.DnsTemporaryFailure,
            SocketException { SocketErrorCode: SocketError.TimedOut } => DiagnosticCodes.DnsTimeout,
            _ => DiagnosticCodes.DnsUnknown
        };
        return Create(code, exception, NetworkProvider(source));
    }

    public static DiagnosticIssue Ping(IPStatus status)
    {
        var code = status switch
        {
            IPStatus.TimedOut => DiagnosticCodes.PingTimeout,
            IPStatus.DestinationHostUnreachable or IPStatus.DestinationNetworkUnreachable or
                IPStatus.DestinationPortUnreachable or IPStatus.DestinationUnreachable => DiagnosticCodes.PingUnreachable,
            IPStatus.DestinationProhibited or IPStatus.DestinationProtocolUnreachable => DiagnosticCodes.PingBlocked,
            _ => DiagnosticCodes.PingUnknown
        };
        var provider = new ProviderErrorInfo("ICMP", status.ToString(), null, ((int)status).ToString(),
            nameof(IPStatus), [new(status.ToString(), null, ((int)status).ToString(), status.ToString())]);
        return DiagnosticCatalog.Create(code, status.ToString(), provider);
    }

    public static DiagnosticIssue Ping(Exception exception)
    {
        var source = Unwrap(exception);
        if (source is SocketException socket)
            return TcpFromSocket(socket, DiagnosticLayer.Ping);
        var code = source is TimeoutException ? DiagnosticCodes.PingTimeout : DiagnosticCodes.PingUnknown;
        return Create(code, exception, NetworkProvider(source), layer: DiagnosticLayer.Ping);
    }

    public static DiagnosticIssue Tcp(Exception exception)
    {
        var source = Unwrap(exception);
        if (source is SocketException socket)
            return TcpFromSocket(socket, DiagnosticLayer.Tcp);
        var code = source is TimeoutException ? DiagnosticCodes.TcpTimeout : DiagnosticCodes.TcpUnknown;
        return Create(code, exception, NetworkProvider(source));
    }

    public static DiagnosticIssue Database(Exception exception, DiagnosticLayer layer, TestSettings settings)
    {
        var source = Unwrap(exception);
        var provider = DatabaseProvider(source);
        var exactCode = source switch
        {
            SqlException sql => SqlServerCode(sql.Number, layer),
            PostgresException postgres => PostgreSqlCode(postgres.SqlState, layer),
            MySqlException mysql => MySqlCode(mysql.Number, layer),
            SqliteException sqlite => SqliteCode(sqlite.SqliteErrorCode, layer),
            OdbcException odbc => OdbcCode(odbc.Errors.Cast<OdbcError>().Select(error => error.SQLState), layer),
            TimeoutException => layer == DiagnosticLayer.DatabaseQuery
                ? DiagnosticCodes.DatabaseQueryTimeout
                : DiagnosticCodes.DatabaseConnectTimeout,
            FileNotFoundException when settings.DatabaseType == DatabaseType.Sqlite => DiagnosticCodes.SqliteInvalid,
            _ => null
        };

        if (exactCode is not null)
            return Create(exactCode, exception, provider, secrets: [settings.Password]);

        var heuristicCode = MessageCode(source.Message, layer);
        if (heuristicCode is not null)
            return Create(heuristicCode, exception, provider, DiagnosticConfidence.Heuristic, [settings.Password]);

        return Create(DiagnosticCodes.DatabaseUnknown, exception, provider, DiagnosticConfidence.Fallback,
            [settings.Password], layer);
    }

    public static DiagnosticIssue UnexpectedQueryResult(object? value) => DiagnosticCatalog.Create(
        DiagnosticCodes.DatabaseUnexpectedResult,
        $"SELECT 1 retornou '{Sanitize(Convert.ToString(value) ?? "null")}'.");

    private static DiagnosticIssue TcpFromSocket(SocketException socket, DiagnosticLayer layer)
    {
        string code;
        if (layer == DiagnosticLayer.Ping)
        {
            code = socket.SocketErrorCode switch
            {
                SocketError.TimedOut => DiagnosticCodes.PingTimeout,
                SocketError.AccessDenied => DiagnosticCodes.PingBlocked,
                SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.HostNotFound => DiagnosticCodes.PingUnreachable,
                _ => DiagnosticCodes.PingUnknown
            };
        }
        else
        {
            code = socket.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => DiagnosticCodes.TcpRefused,
                SocketError.TimedOut => DiagnosticCodes.TcpTimeout,
                SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.HostNotFound => DiagnosticCodes.TcpUnreachable,
                SocketError.ConnectionReset or SocketError.ConnectionAborted => DiagnosticCodes.TcpReset,
                SocketError.AddressNotAvailable or SocketError.AddressFamilyNotSupported => DiagnosticCodes.TcpAddressUnavailable,
                _ => DiagnosticCodes.TcpUnknown
            };
        }
        return Create(code, socket, NetworkProvider(socket), layer: layer);
    }

    private static string? SqlServerCode(int number, DiagnosticLayer layer) => number switch
    {
        18456 => DiagnosticCodes.DatabaseAuthentication,
        4060 => DiagnosticCodes.DatabaseNotFound,
        -2 => layer == DiagnosticLayer.DatabaseQuery ? DiagnosticCodes.DatabaseQueryTimeout : DiagnosticCodes.DatabaseConnectTimeout,
        53 or 64 or 233 or 10054 or 10060 or 11001 => DiagnosticCodes.DatabaseUnavailable,
        229 or 230 => DiagnosticCodes.DatabasePermission,
        10928 or 10929 => DiagnosticCodes.DatabaseTooManyConnections,
        _ => null
    };

    private static string? PostgreSqlCode(string sqlState, DiagnosticLayer layer) => sqlState switch
    {
        "28P01" or "28000" => DiagnosticCodes.DatabaseAuthentication,
        "3D000" => DiagnosticCodes.DatabaseNotFound,
        "42501" => DiagnosticCodes.DatabasePermission,
        "53300" => DiagnosticCodes.DatabaseTooManyConnections,
        "57014" => layer == DiagnosticLayer.DatabaseQuery ? DiagnosticCodes.DatabaseQueryTimeout : DiagnosticCodes.DatabaseConnectTimeout,
        _ when sqlState.StartsWith("08", StringComparison.Ordinal) => DiagnosticCodes.DatabaseUnavailable,
        _ => null
    };

    private static string? MySqlCode(int number, DiagnosticLayer layer) => number switch
    {
        1045 => DiagnosticCodes.DatabaseAuthentication,
        1049 => DiagnosticCodes.DatabaseNotFound,
        1040 => DiagnosticCodes.DatabaseTooManyConnections,
        1044 or 1142 or 1143 => DiagnosticCodes.DatabasePermission,
        2002 or 2003 or 2005 or 2013 => DiagnosticCodes.DatabaseUnavailable,
        1205 when layer == DiagnosticLayer.DatabaseQuery => DiagnosticCodes.DatabaseQueryTimeout,
        _ => null
    };

    private static string? SqliteCode(int code, DiagnosticLayer layer) => code switch
    {
        5 or 6 => DiagnosticCodes.SqliteBusy,
        11 => DiagnosticCodes.SqliteCorrupt,
        14 or 26 => DiagnosticCodes.SqliteInvalid,
        23 => DiagnosticCodes.DatabasePermission,
        _ => null
    };

    private static string? OdbcCode(IEnumerable<string> states, DiagnosticLayer layer)
    {
        foreach (var state in states.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (state is "28000") return DiagnosticCodes.DatabaseAuthentication;
            if (state is "3D000") return DiagnosticCodes.DatabaseNotFound;
            if (state is "42501") return DiagnosticCodes.DatabasePermission;
            if (state is "IM002" or "IM003") return DiagnosticCodes.DatabaseDriverMissing;
            if (state is "HYT00" or "HYT01")
                return layer == DiagnosticLayer.DatabaseQuery ? DiagnosticCodes.DatabaseQueryTimeout : DiagnosticCodes.DatabaseConnectTimeout;
            if (state.StartsWith("08", StringComparison.Ordinal)) return DiagnosticCodes.DatabaseUnavailable;
        }
        return null;
    }

    private static string? MessageCode(string message, DiagnosticLayer layer)
    {
        var value = message.ToLowerInvariant();
        if (value.Contains("certificate") || value.Contains("certificado") || value.Contains("ssl") || value.Contains("tls"))
            return DiagnosticCodes.DatabaseTls;
        if (value.Contains("timeout") || value.Contains("tempo limite"))
            return layer == DiagnosticLayer.DatabaseQuery ? DiagnosticCodes.DatabaseQueryTimeout : DiagnosticCodes.DatabaseConnectTimeout;
        if (value.Contains("login failed") || value.Contains("authentication failed") || value.Contains("access denied"))
            return DiagnosticCodes.DatabaseAuthentication;
        if (value.Contains("driver") && (value.Contains("not found") || value.Contains("não encontrado")))
            return DiagnosticCodes.DatabaseDriverMissing;
        return null;
    }

    private static ProviderErrorInfo DatabaseProvider(Exception exception) => exception switch
    {
        SqlException sql => new("SQL Server", sql.Number.ToString(), null, sql.Number.ToString(), sql.GetType().Name,
            sql.Errors.Cast<SqlError>().Select(error => new ProviderErrorEntry(
                error.Number.ToString(), null, error.Number.ToString(), Sanitize(error.Message))).ToArray()),
        PostgresException postgres => new("PostgreSQL", postgres.SqlState, postgres.SqlState, null, postgres.GetType().Name,
            [new(postgres.SqlState, postgres.SqlState, null, Sanitize(postgres.MessageText))]),
        MySqlException mysql => new("MySQL/MariaDB", mysql.Number.ToString(), mysql.SqlState, mysql.Number.ToString(), mysql.GetType().Name,
            [new(mysql.Number.ToString(), mysql.SqlState, mysql.Number.ToString(), Sanitize(mysql.Message))]),
        SqliteException sqlite => new("SQLite", sqlite.SqliteErrorCode.ToString(), null, sqlite.SqliteExtendedErrorCode.ToString(), sqlite.GetType().Name,
            [new(sqlite.SqliteErrorCode.ToString(), null, sqlite.SqliteExtendedErrorCode.ToString(), Sanitize(sqlite.Message))]),
        OdbcException odbc => new("ODBC", odbc.Errors.Count > 0 ? odbc.Errors[0].NativeError.ToString() : null,
            odbc.Errors.Count > 0 ? odbc.Errors[0].SQLState : null,
            odbc.Errors.Count > 0 ? odbc.Errors[0].NativeError.ToString() : null, odbc.GetType().Name,
            odbc.Errors.Cast<OdbcError>().Select(error => new ProviderErrorEntry(
                error.NativeError.ToString(), error.SQLState, error.NativeError.ToString(), Sanitize(error.Message))).ToArray()),
        _ => new(exception.GetType().Name, null, null, null, exception.GetType().Name,
            [new(null, null, null, Sanitize(exception.Message))])
    };

    private static ProviderErrorInfo NetworkProvider(Exception exception) => exception switch
    {
        SocketException socket => new("Socket", socket.SocketErrorCode.ToString(), null, socket.NativeErrorCode.ToString(),
            socket.GetType().Name, [new(socket.SocketErrorCode.ToString(), null, socket.NativeErrorCode.ToString(), Sanitize(socket.Message))]),
        _ => new(".NET", null, null, null, exception.GetType().Name,
            [new(null, null, null, Sanitize(exception.Message))])
    };

    private static DiagnosticIssue Create(
        string code,
        Exception exception,
        ProviderErrorInfo? provider,
        DiagnosticConfidence confidence = DiagnosticConfidence.Exact,
        IEnumerable<string>? secrets = null,
        DiagnosticLayer? layer = null)
    {
        var source = Unwrap(exception);
        var message = $"{source.GetType().Name}: {source.Message}";
        return DiagnosticCatalog.Create(code, Sanitize(message, secrets), Redact(provider, secrets), confidence, layer);
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is AggregateException { InnerExceptions.Count: 1 } aggregate)
            exception = aggregate.InnerExceptions[0];
        return exception.GetBaseException();
    }

    private static ProviderErrorInfo? Redact(ProviderErrorInfo? provider, IEnumerable<string>? secrets)
    {
        if (provider is null) return null;
        return provider with
        {
            Errors = provider.Errors.Select(error => error with { Message = Sanitize(error.Message, secrets) }).ToArray()
        };
    }

    private static string Sanitize(string value, IEnumerable<string>? secrets = null)
    {
        var result = value.Replace('\r', ' ').Replace('\n', ' ');
        if (secrets is not null)
            foreach (var secret in secrets.Where(secret => !string.IsNullOrEmpty(secret)))
                result = result.Replace(secret, "***", StringComparison.Ordinal);
        result = PasswordPattern().Replace(result, "$1=***");
        return result.Trim();
    }

    [GeneratedRegex(@"(?i)(password|pwd)\s*=\s*[^;\s]+")]
    private static partial Regex PasswordPattern();
}
