namespace DBConnectionTester.Models;

public sealed record StageStatisticsSnapshot(
    long Attempts,
    long Successes,
    long Failures,
    double? AverageMs,
    long? MinimumMs,
    long? MaximumMs,
    double? MedianMs,
    long? P95Ms,
    int ConsecutiveFailures,
    int MaximumConsecutiveFailures,
    DateTimeOffset? LastFailureAt)
{
    public double? SuccessRate => Attempts == 0 ? null : Successes * 100.0 / Attempts;

    public TimeSpan? TimeSinceLastFailure(DateTimeOffset now) =>
        LastFailureAt is null ? null : TimeSpan.FromTicks(Math.Max(0, (now - LastFailureAt.Value).Ticks));
}

public sealed record RunStatisticsSnapshot(
    StageStatisticsSnapshot Dns,
    StageStatisticsSnapshot Ping,
    StageStatisticsSnapshot Tcp,
    StageStatisticsSnapshot DatabaseConnect,
    StageStatisticsSnapshot DatabaseQuery);

internal sealed class StageStatistics
{
    private readonly SortedDictionary<long, long> latencyDistribution = new();
    private long latencySum;

    public long Attempts { get; private set; }
    public long Successes { get; private set; }
    public long Failures { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public int MaximumConsecutiveFailures { get; private set; }
    public DateTimeOffset? LastFailureAt { get; private set; }

    public void Record(StepStatus status, long elapsedMs, DateTimeOffset timestamp)
    {
        if (status == StepStatus.Skipped)
            return;

        Attempts++;
        if (status == StepStatus.Success)
        {
            Successes++;
            ConsecutiveFailures = 0;
            latencySum += elapsedMs;
            latencyDistribution[elapsedMs] = latencyDistribution.GetValueOrDefault(elapsedMs) + 1;
            return;
        }

        Failures++;
        ConsecutiveFailures++;
        MaximumConsecutiveFailures = Math.Max(MaximumConsecutiveFailures, ConsecutiveFailures);
        LastFailureAt = timestamp;
    }

    public StageStatisticsSnapshot CreateSnapshot()
    {
        long? minimum = Successes == 0 ? null : latencyDistribution.First().Key;
        long? maximum = Successes == 0 ? null : latencyDistribution.Last().Key;
        double? median = Successes == 0
            ? null
            : (ValueAtIndex((Successes - 1) / 2) + ValueAtIndex(Successes / 2)) / 2.0;
        long? p95 = Successes == 0
            ? null
            : ValueAtIndex(Math.Max(0, (long)Math.Ceiling(Successes * 0.95) - 1));

        return new StageStatisticsSnapshot(
            Attempts,
            Successes,
            Failures,
            Successes == 0 ? null : latencySum / (double)Successes,
            minimum,
            maximum,
            median,
            p95,
            ConsecutiveFailures,
            MaximumConsecutiveFailures,
            LastFailureAt);
    }

    private long ValueAtIndex(long targetIndex)
    {
        long seen = 0;
        foreach (var (latency, count) in latencyDistribution)
        {
            seen += count;
            if (seen > targetIndex)
                return latency;
        }

        return latencyDistribution.Last().Key;
    }
}
