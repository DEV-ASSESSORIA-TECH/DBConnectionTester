using System.Globalization;
using System.Text;
using DBConnectionTester.Models;
using Microsoft.Data.Sqlite;

namespace DBConnectionTester.Services.Storage;

public interface IRunHistoryRepository
{
    Task<PagedResult<RunHistoryItem>> SearchAsync(RunHistoryFilter filter, PageRequest page, CancellationToken token = default);
    Task<RunHistoryDetails?> GetDetailsAsync(Guid runId, CancellationToken token = default);
    Task<PagedResult<PersistedCycle>> GetCyclesAsync(Guid runId, PageRequest page, CancellationToken token = default);
}

public sealed class RunHistoryRepository : IRunHistoryRepository
{
    private readonly SqliteApplicationStore store;

    public RunHistoryRepository(SqliteApplicationStore store) => this.store = store;

    public async Task<PagedResult<RunHistoryItem>> SearchAsync(
        RunHistoryFilter filter,
        PageRequest page,
        CancellationToken token = default)
    {
        page.Validate();
        ArgumentNullException.ThrowIfNull(filter);

        await using var connection = await store.OpenConnectionAsync(token);
        var where = BuildWhere(filter, out var parameters);
        var total = await CountAsync(connection, where, parameters, token);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT r.run_id, r.profile_id, p.name, r.status, r.termination_reason,
                   r.started_at, r.finished_at, r.application_version, r.machine_name,
                   r.database_type, r.target, r.completed_cycles, r.failure_message
            FROM runs r
            LEFT JOIN connection_profiles p ON p.profile_id = r.profile_id
            {where}
            ORDER BY r.started_at DESC, r.run_id DESC
            LIMIT $limit OFFSET $offset;
            """;
        AddParameters(command, parameters);
        command.Parameters.AddWithValue("$limit", page.PageSize);
        command.Parameters.AddWithValue("$offset", page.Offset);

        var items = new List<RunHistoryItem>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            items.Add(ReadRun(reader));
        return new PagedResult<RunHistoryItem>(items, total, page.PageNumber, page.PageSize);
    }

    public async Task<RunHistoryDetails?> GetDetailsAsync(Guid runId, CancellationToken token = default)
    {
        await using var connection = await store.OpenConnectionAsync(token);
        RunHistoryItem? run;
        string snapshot;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT r.run_id, r.profile_id, p.name, r.status, r.termination_reason,
                       r.started_at, r.finished_at, r.application_version, r.machine_name,
                       r.database_type, r.target, r.completed_cycles, r.failure_message,
                       r.settings_snapshot_json
                FROM runs r
                LEFT JOIN connection_profiles p ON p.profile_id = r.profile_id
                WHERE r.run_id = $runId;
                """;
            command.Parameters.AddWithValue("$runId", runId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
                return null;
            run = ReadRun(reader);
            snapshot = reader.GetString(13);
        }

        var summaries = new List<PersistedStageSummary>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT stage, attempts, successes, failures, average_ms, minimum_ms, maximum_ms,
                       median_ms, p95_ms, consecutive_failures, maximum_consecutive_failures, last_failure_at
                FROM run_stage_summaries WHERE run_id = $runId ORDER BY stage;
                """;
            command.Parameters.AddWithValue("$runId", runId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                summaries.Add(new PersistedStageSummary(reader.GetString(0), new StageStatisticsSnapshot(
                    reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), NullableDouble(reader, 4),
                    NullableInt64(reader, 5), NullableInt64(reader, 6), NullableDouble(reader, 7),
                    NullableInt64(reader, 8), reader.GetInt32(9), reader.GetInt32(10), NullableDate(reader, 11))));
            }
        }

        var warnings = new List<PersistedRunWarning>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT created_at, warning_code, message FROM run_warnings
                WHERE run_id = $runId ORDER BY warning_id;
                """;
            command.Parameters.AddWithValue("$runId", runId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                warnings.Add(new PersistedRunWarning(ParseDate(reader.GetString(0)), reader.GetString(1), reader.GetString(2)));
        }

        return new RunHistoryDetails(run, snapshot, summaries, warnings);
    }

    public async Task<PagedResult<PersistedCycle>> GetCyclesAsync(
        Guid runId,
        PageRequest page,
        CancellationToken token = default)
    {
        page.Validate();
        await using var connection = await store.OpenConnectionAsync(token);
        long total;
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM cycles WHERE run_id = $runId;";
            count.Parameters.AddWithValue("$runId", runId.ToString("D"));
            total = Convert.ToInt64(await count.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        }

        var cycles = new List<PersistedCycle>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH selected AS (
                SELECT * FROM cycles WHERE run_id = $runId
                ORDER BY cycle_number LIMIT $limit OFFSET $offset
            )
            SELECT c.cycle_number, c.started_at, c.resolved_ip, c.ping_extra,
                   c.tcp_local_ip, c.tcp_remote_ip, c.database_total_ms,
                   s.stage, s.status, s.elapsed_ms, s.extra, s.diagnostic_code,
                   s.suggestion_code, s.confidence, s.user_message, s.technical_message,
                   s.provider, s.provider_code, s.sql_state, s.native_code, s.provider_errors_json
            FROM selected c
            LEFT JOIN stage_results s ON s.run_id = c.run_id AND s.cycle_number = c.cycle_number
            ORDER BY c.cycle_number, s.stage;
            """;
        command.Parameters.AddWithValue("$runId", runId.ToString("D"));
        command.Parameters.AddWithValue("$limit", page.PageSize);
        command.Parameters.AddWithValue("$offset", page.Offset);
        await using var reader = await command.ExecuteReaderAsync(token);
        CycleBuilder? current = null;
        while (await reader.ReadAsync(token))
        {
            var number = reader.GetInt64(0);
            if (current is null || current.Number != number)
            {
                if (current is not null)
                    cycles.Add(current.Build());
                current = new CycleBuilder(number, ParseDate(reader.GetString(1)), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetInt64(6));
            }
            if (!reader.IsDBNull(7))
                current.Stages.Add(ReadStage(reader));
        }
        if (current is not null)
            cycles.Add(current.Build());
        return new PagedResult<PersistedCycle>(cycles, total, page.PageNumber, page.PageSize);
    }

    private static string BuildWhere(RunHistoryFilter filter, out Dictionary<string, object> parameters)
    {
        var values = new Dictionary<string, object>();
        var clauses = new List<string>();
        void Add(string sql, string name, object value) { clauses.Add(sql); values.Add(name, value); }
        if (filter.StartedFrom is not null) Add("r.started_at >= $from", "$from", Format(filter.StartedFrom.Value));
        if (filter.StartedUntil is not null) Add("r.started_at <= $until", "$until", Format(filter.StartedUntil.Value));
        if (filter.ProfileId is not null) Add("r.profile_id = $profile", "$profile", filter.ProfileId.Value.ToString("D"));
        if (!string.IsNullOrWhiteSpace(filter.Target)) Add("r.target LIKE $target ESCAPE '\\'", "$target", $"%{EscapeLike(filter.Target.Trim())}%");
        if (filter.Status is not null) Add("r.status = $status", "$status", filter.Status.Value.ToString());
        if (!string.IsNullOrWhiteSpace(filter.DiagnosticCode))
            Add("EXISTS (SELECT 1 FROM stage_results s WHERE s.run_id = r.run_id AND s.diagnostic_code = $diagnostic)", "$diagnostic", filter.DiagnosticCode.Trim());
        parameters = values;
        return clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses);
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string where, Dictionary<string, object> parameters, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM runs r {where};";
        AddParameters(command, parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }

    private static void AddParameters(SqliteCommand command, Dictionary<string, object> parameters)
    {
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Key, parameter.Value);
    }

    private static RunHistoryItem ReadRun(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)), reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
        reader.IsDBNull(2) ? null : reader.GetString(2), Enum.Parse<PersistedRunStatus>(reader.GetString(3)),
        reader.IsDBNull(4) ? null : Enum.Parse<RunTerminationReason>(reader.GetString(4)), ParseDate(reader.GetString(5)),
        reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6)), reader.GetString(7), reader.GetString(8),
        Enum.Parse<DatabaseType>(reader.GetString(9)), reader.GetString(10), reader.GetInt64(11),
        reader.IsDBNull(12) ? null : reader.GetString(12));

    private static PersistedStageResult ReadStage(SqliteDataReader reader) => new(
        reader.GetString(7), Enum.Parse<StepStatus>(reader.GetString(8)), reader.GetInt64(9), reader.GetString(10),
        NullableString(reader, 11), NullableString(reader, 12),
        reader.IsDBNull(13) ? null : Enum.Parse<DiagnosticConfidence>(reader.GetString(13)),
        NullableString(reader, 14), NullableString(reader, 15), NullableString(reader, 16),
        NullableString(reader, 17), NullableString(reader, 18), NullableString(reader, 19), NullableString(reader, 20));

    private static string? NullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static long? NullableInt64(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    private static double? NullableDouble(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    private static DateTimeOffset? NullableDate(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));
    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private sealed record CycleBuilder(long Number, DateTimeOffset StartedAt, string ResolvedIp, string PingExtra,
        string TcpLocalIp, string TcpRemoteIp, long DatabaseTotalMs)
    {
        public List<PersistedStageResult> Stages { get; } = [];
        public PersistedCycle Build() => new(Number, StartedAt, ResolvedIp, PingExtra, TcpLocalIp, TcpRemoteIp, DatabaseTotalMs, Stages);
    }
}
