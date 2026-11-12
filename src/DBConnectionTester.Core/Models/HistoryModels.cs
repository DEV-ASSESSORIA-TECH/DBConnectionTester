namespace DBConnectionTester.Models;

public sealed record PageRequest(int PageNumber = 1, int PageSize = 50)
{
    public int Offset
    {
        get
        {
            Validate();
            return checked((PageNumber - 1) * PageSize);
        }
    }

    public void Validate()
    {
        if (PageNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(PageNumber));
        if (PageSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(PageSize), "O tamanho da página deve estar entre 1 e 500.");
    }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, long TotalItems, int PageNumber, int PageSize)
{
    public long TotalPages => TotalItems == 0 ? 0 : (TotalItems + PageSize - 1) / PageSize;
}

public sealed record RunHistoryFilter(
    DateTimeOffset? StartedFrom = null,
    DateTimeOffset? StartedUntil = null,
    Guid? ProfileId = null,
    string? Target = null,
    PersistedRunStatus? Status = null,
    string? DiagnosticCode = null);

public sealed record RunHistoryItem(
    Guid RunId,
    Guid? ProfileId,
    string? ProfileName,
    PersistedRunStatus Status,
    RunTerminationReason? TerminationReason,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string ApplicationVersion,
    string MachineName,
    DatabaseType DatabaseType,
    string Target,
    long CompletedCycles,
    string? FailureMessage);

public sealed record RunHistoryDetails(
    RunHistoryItem Run,
    string SettingsSnapshotJson,
    IReadOnlyList<PersistedStageSummary> StageSummaries,
    IReadOnlyList<PersistedRunWarning> Warnings);

public sealed record PersistedStageSummary(string Stage, StageStatisticsSnapshot Statistics);

public sealed record PersistedRunWarning(DateTimeOffset CreatedAt, string Code, string Message);

public sealed record PersistedCycle(
    long Number,
    DateTimeOffset StartedAt,
    string ResolvedIp,
    string PingExtra,
    string TcpLocalIp,
    string TcpRemoteIp,
    long DatabaseTotalMs,
    IReadOnlyList<PersistedStageResult> Stages);

public sealed record PersistedStageResult(
    string Stage,
    StepStatus Status,
    long ElapsedMs,
    string Extra,
    string? DiagnosticCode,
    string? SuggestionCode,
    DiagnosticConfidence? Confidence,
    string? UserMessage,
    string? TechnicalMessage,
    string? Provider,
    string? ProviderCode,
    string? SqlState,
    string? NativeCode,
    string? ProviderErrorsJson);
