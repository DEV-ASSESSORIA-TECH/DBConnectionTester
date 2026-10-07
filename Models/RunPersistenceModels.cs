namespace DBConnectionTester.Models;

public enum PersistedRunStatus
{
    Running,
    Completed,
    Stopped,
    Failed,
    Interrupted
}

public enum RunTerminationReason
{
    PlannedCountCompleted,
    StoppedByUser,
    ExecutionFailed,
    ProcessInterrupted
}

public sealed record RunCompletion(
    PersistedRunStatus Status,
    RunTerminationReason Reason,
    long CompletedCycles,
    string? FailureMessage);

internal sealed record PersistedTestSettingsSnapshot(
    DatabaseType DatabaseType,
    string Host,
    int? Port,
    string User,
    string Database,
    string SqliteFile,
    SqlServerAuthentication SqlServerAuthentication,
    string OdbcDriver,
    long TestCount,
    bool Continuous,
    double IntervalSeconds,
    double TimeoutSeconds,
    bool Dns,
    bool Ping,
    bool Tcp,
    bool DatabaseTest);
