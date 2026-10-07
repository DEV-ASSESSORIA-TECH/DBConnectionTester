using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBConnectionTester.Models;
using Microsoft.Data.Sqlite;

namespace DBConnectionTester.Services.Storage;

public interface IRunRepository
{
    Task BeginAsync(Guid runId, TestSettings settings, Guid? profileId = null, CancellationToken token = default);
    Task WriteCycleAsync(Guid runId, TestCycleResult cycle, CancellationToken token = default);
    Task CompleteAsync(Guid runId, RunCompletion completion, RunMetrics metrics, CancellationToken token = default);
    Task AddWarningAsync(Guid runId, string code, string message, CancellationToken token = default);
    Task<int> RecoverInterruptedAsync(CancellationToken token = default);
}

public sealed class RunRepository : IRunRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SqliteApplicationStore store;

    public RunRepository(SqliteApplicationStore store)
    {
        this.store = store;
    }

    public async Task BeginAsync(
        Guid runId,
        TestSettings settings,
        Guid? profileId = null,
        CancellationToken token = default)
    {
        var snapshot = new PersistedTestSettingsSnapshot(
            settings.DatabaseType,
            settings.Host,
            settings.Port?.Value,
            settings.User,
            settings.Database,
            settings.SqliteFile,
            settings.SqlServerAuthentication,
            settings.OdbcDriver,
            settings.TestCount.Value,
            settings.Continuous,
            settings.Interval.Value.TotalSeconds,
            settings.Timeout.Value.TotalSeconds,
            settings.Dns,
            settings.Ping,
            settings.Tcp,
            settings.DatabaseTest);

        await using var connection = await store.OpenConnectionAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO runs
                (run_id, profile_id, status, started_at, application_version, machine_name,
                 database_type, target, settings_snapshot_json, completed_cycles)
            VALUES
                ($runId, $profileId, $status, $startedAt, $version, $machine,
                 $databaseType, $target, $settings, 0);
            """;
        command.Parameters.AddWithValue("$runId", runId.ToString("D"));
        command.Parameters.AddWithValue("$profileId", profileId is null ? DBNull.Value : profileId.Value.ToString("D"));
        command.Parameters.AddWithValue("$status", PersistedRunStatus.Running.ToString());
        command.Parameters.AddWithValue("$startedAt", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$version",
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0");
        command.Parameters.AddWithValue("$machine", Environment.MachineName);
        command.Parameters.AddWithValue("$databaseType", settings.DatabaseType.ToString());
        command.Parameters.AddWithValue("$target", settings.Target);
        command.Parameters.AddWithValue("$settings", JsonSerializer.Serialize(snapshot, JsonOptions));
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task WriteCycleAsync(Guid runId, TestCycleResult cycle, CancellationToken token = default)
    {
        await using var connection = await store.OpenConnectionAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO cycles
                        (run_id, cycle_number, started_at, resolved_ip, ping_extra,
                         tcp_local_ip, tcp_remote_ip, database_total_ms)
                    VALUES
                        ($runId, $number, $startedAt, $resolvedIp, $pingExtra,
                         $tcpLocalIp, $tcpRemoteIp, $databaseTotalMs);
                    """;
                command.Parameters.AddWithValue("$runId", runId.ToString("D"));
                command.Parameters.AddWithValue("$number", cycle.Number);
                command.Parameters.AddWithValue("$startedAt", Format(cycle.StartedAt));
                command.Parameters.AddWithValue("$resolvedIp", cycle.Dns.ResolvedIp);
                command.Parameters.AddWithValue("$pingExtra", cycle.Ping.Extra);
                command.Parameters.AddWithValue("$tcpLocalIp", cycle.Tcp.LocalIp);
                command.Parameters.AddWithValue("$tcpRemoteIp", cycle.Tcp.RemoteIp);
                command.Parameters.AddWithValue("$databaseTotalMs", cycle.Database.TotalMs);
                await command.ExecuteNonQueryAsync(token);
            }

            await InsertStageAsync(connection, transaction, runId, cycle.Number, "Dns",
                cycle.Dns.Status, cycle.Dns.ElapsedMs, cycle.Dns.ResolvedIp, cycle.Dns.Diagnostic, token);
            await InsertStageAsync(connection, transaction, runId, cycle.Number, "Ping",
                cycle.Ping.Status, cycle.Ping.ElapsedMs, cycle.Ping.Extra, cycle.Ping.Diagnostic, token);
            await InsertStageAsync(connection, transaction, runId, cycle.Number, "Tcp",
                cycle.Tcp.Status, cycle.Tcp.ElapsedMs, cycle.Tcp.RemoteIp, cycle.Tcp.Diagnostic, token);
            await InsertStageAsync(connection, transaction, runId, cycle.Number, "DatabaseConnect",
                cycle.Database.ConnectStatus, cycle.Database.ConnectMs, "", cycle.Database.ConnectDiagnostic, token);
            await InsertStageAsync(connection, transaction, runId, cycle.Number, "DatabaseQuery",
                cycle.Database.QueryStatus, cycle.Database.QueryMs, "", cycle.Database.QueryDiagnostic, token);

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE runs
                    SET completed_cycles = $completed
                    WHERE run_id = $runId AND status = $running;
                    """;
                update.Parameters.AddWithValue("$completed", cycle.Number);
                update.Parameters.AddWithValue("$runId", runId.ToString("D"));
                update.Parameters.AddWithValue("$running", PersistedRunStatus.Running.ToString());
                if (await update.ExecuteNonQueryAsync(token) != 1)
                    throw new ApplicationStoreException("A execução não está ativa no histórico.");
            }

            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task CompleteAsync(
        Guid runId,
        RunCompletion completion,
        RunMetrics metrics,
        CancellationToken token = default)
    {
        if (completion.Status == PersistedRunStatus.Running)
            throw new ArgumentException("O estado final não pode ser Running.", nameof(completion));

        await using var connection = await store.OpenConnectionAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE runs
                    SET status = $status,
                        termination_reason = $reason,
                        finished_at = $finishedAt,
                        completed_cycles = $completed,
                        failure_message = $failure
                    WHERE run_id = $runId;
                    """;
                command.Parameters.AddWithValue("$status", completion.Status.ToString());
                command.Parameters.AddWithValue("$reason", completion.Reason.ToString());
                command.Parameters.AddWithValue("$finishedAt", Format(DateTimeOffset.UtcNow));
                command.Parameters.AddWithValue("$completed", completion.CompletedCycles);
                command.Parameters.AddWithValue("$failure", completion.FailureMessage is null
                    ? DBNull.Value
                    : completion.FailureMessage);
                command.Parameters.AddWithValue("$runId", runId.ToString("D"));
                if (await command.ExecuteNonQueryAsync(token) != 1)
                    throw new ApplicationStoreException("A execução não existe no histórico.");
            }

            var statistics = metrics.CreateStatistics();
            await SaveSummaryAsync(connection, transaction, runId, "Dns", statistics.Dns, token);
            await SaveSummaryAsync(connection, transaction, runId, "Ping", statistics.Ping, token);
            await SaveSummaryAsync(connection, transaction, runId, "Tcp", statistics.Tcp, token);
            await SaveSummaryAsync(connection, transaction, runId, "DatabaseConnect", statistics.DatabaseConnect, token);
            await SaveSummaryAsync(connection, transaction, runId, "DatabaseQuery", statistics.DatabaseQuery, token);
            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task AddWarningAsync(
        Guid runId,
        string code,
        string message,
        CancellationToken token = default)
    {
        await using var connection = await store.OpenConnectionAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO run_warnings (run_id, created_at, warning_code, message)
            VALUES ($runId, $createdAt, $code, $message);
            """;
        command.Parameters.AddWithValue("$runId", runId.ToString("D"));
        command.Parameters.AddWithValue("$createdAt", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$message", message);
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task<int> RecoverInterruptedAsync(CancellationToken token = default)
    {
        await using var connection = await store.OpenConnectionAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE runs
            SET status = $interrupted,
                termination_reason = $reason,
                finished_at = $finishedAt
            WHERE status = $running;
            """;
        command.Parameters.AddWithValue("$interrupted", PersistedRunStatus.Interrupted.ToString());
        command.Parameters.AddWithValue("$reason", RunTerminationReason.ProcessInterrupted.ToString());
        command.Parameters.AddWithValue("$finishedAt", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$running", PersistedRunStatus.Running.ToString());
        return await command.ExecuteNonQueryAsync(token);
    }

    private static async Task InsertStageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid runId,
        long cycleNumber,
        string stage,
        StepStatus status,
        long elapsedMs,
        string extra,
        DiagnosticIssue? diagnostic,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO stage_results
                (run_id, cycle_number, stage, status, elapsed_ms, extra,
                 diagnostic_code, suggestion_code, confidence, user_message, technical_message,
                 provider, provider_code, sql_state, native_code, provider_errors_json)
            VALUES
                ($runId, $cycle, $stage, $status, $elapsed, $extra,
                 $diagnosticCode, $suggestionCode, $confidence, $userMessage, $technicalMessage,
                 $provider, $providerCode, $sqlState, $nativeCode, $providerErrors);
            """;
        command.Parameters.AddWithValue("$runId", runId.ToString("D"));
        command.Parameters.AddWithValue("$cycle", cycleNumber);
        command.Parameters.AddWithValue("$stage", stage);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$elapsed", elapsedMs);
        command.Parameters.AddWithValue("$extra", extra);
        AddNullable(command, "$diagnosticCode", diagnostic?.DiagnosticCode);
        AddNullable(command, "$suggestionCode", diagnostic?.SuggestionCode);
        AddNullable(command, "$confidence", diagnostic?.Confidence.ToString());
        AddNullable(command, "$userMessage", diagnostic?.UserMessage);
        AddNullable(command, "$technicalMessage", diagnostic?.TechnicalMessage);
        AddNullable(command, "$provider", diagnostic?.ProviderError?.Provider);
        AddNullable(command, "$providerCode", diagnostic?.ProviderError?.OriginalCode);
        AddNullable(command, "$sqlState", diagnostic?.ProviderError?.SqlState);
        AddNullable(command, "$nativeCode", diagnostic?.ProviderError?.NativeCode);
        AddNullable(command, "$providerErrors", diagnostic?.ProviderError is null
            ? null
            : JsonSerializer.Serialize(diagnostic.ProviderError.Errors, JsonOptions));
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task SaveSummaryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid runId,
        string stage,
        StageStatisticsSnapshot statistics,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO run_stage_summaries
                (run_id, stage, attempts, successes, failures, average_ms, minimum_ms, maximum_ms,
                 median_ms, p95_ms, consecutive_failures, maximum_consecutive_failures, last_failure_at)
            VALUES
                ($runId, $stage, $attempts, $successes, $failures, $average, $minimum, $maximum,
                 $median, $p95, $consecutive, $maximumConsecutive, $lastFailure);
            """;
        command.Parameters.AddWithValue("$runId", runId.ToString("D"));
        command.Parameters.AddWithValue("$stage", stage);
        command.Parameters.AddWithValue("$attempts", statistics.Attempts);
        command.Parameters.AddWithValue("$successes", statistics.Successes);
        command.Parameters.AddWithValue("$failures", statistics.Failures);
        AddNullable(command, "$average", statistics.AverageMs);
        AddNullable(command, "$minimum", statistics.MinimumMs);
        AddNullable(command, "$maximum", statistics.MaximumMs);
        AddNullable(command, "$median", statistics.MedianMs);
        AddNullable(command, "$p95", statistics.P95Ms);
        command.Parameters.AddWithValue("$consecutive", statistics.ConsecutiveFailures);
        command.Parameters.AddWithValue("$maximumConsecutive", statistics.MaximumConsecutiveFailures);
        AddNullable(command, "$lastFailure", statistics.LastFailureAt is null
            ? null
            : Format(statistics.LastFailureAt.Value));
        await command.ExecuteNonQueryAsync(token);
    }

    private static void AddNullable(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
