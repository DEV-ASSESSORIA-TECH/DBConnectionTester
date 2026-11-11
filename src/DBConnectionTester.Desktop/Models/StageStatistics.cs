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
    internal static readonly int MaximumExactLatencyMs = checked((int)StageTimeout.Maximum.TotalMilliseconds);
    private static readonly int OverflowBucket = MaximumExactLatencyMs + 1;

    private long[]? latencyTree;
    private double latencyAverage;
    private long? minimumLatency;
    private long? maximumLatency;

    public long Attempts { get; private set; }
    public long Successes { get; private set; }
    public long Failures { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public int MaximumConsecutiveFailures { get; private set; }
    public DateTimeOffset? LastFailureAt { get; private set; }
    internal int DistributionBucketCount => latencyTree?.Length ?? 0;

    public void Record(StepStatus status, long elapsedMs, DateTimeOffset timestamp)
    {
        if (status == StepStatus.Skipped)
            return;

        Attempts++;
        if (status == StepStatus.Success)
        {
            Successes++;
            ConsecutiveFailures = 0;
            latencyAverage += (elapsedMs - latencyAverage) / Successes;
            minimumLatency = minimumLatency is null ? elapsedMs : Math.Min(minimumLatency.Value, elapsedMs);
            maximumLatency = maximumLatency is null ? elapsedMs : Math.Max(maximumLatency.Value, elapsedMs);
            AddLatency(elapsedMs);
            return;
        }

        Failures++;
        ConsecutiveFailures++;
        MaximumConsecutiveFailures = Math.Max(MaximumConsecutiveFailures, ConsecutiveFailures);
        LastFailureAt = timestamp;
    }

    public StageStatisticsSnapshot CreateSnapshot()
    {
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
            Successes == 0 ? null : latencyAverage,
            minimumLatency,
            maximumLatency,
            median,
            p95,
            ConsecutiveFailures,
            MaximumConsecutiveFailures,
            LastFailureAt);
    }

    private long ValueAtIndex(long targetIndex)
    {
        var tree = latencyTree ?? throw new InvalidOperationException("Não há latências registradas.");
        var rank = targetIndex + 1;
        var index = 0;
        for (var bit = HighestPowerOfTwoAtMost(tree.Length - 1); bit != 0; bit >>= 1)
        {
            var next = index + bit;
            if (next < tree.Length && tree[next] < rank)
            {
                index = next;
                rank -= tree[next];
            }
        }

        return index == OverflowBucket ? maximumLatency!.Value : index;
    }

    private void AddLatency(long elapsedMs)
    {
        latencyTree ??= new long[OverflowBucket + 2];
        var bucket = (int)Math.Clamp(elapsedMs, 0, OverflowBucket);
        for (var index = bucket + 1; index < latencyTree.Length; index += index & -index)
            latencyTree[index]++;
    }

    private static int HighestPowerOfTwoAtMost(int value)
    {
        var result = 1;
        while (result <= value / 2)
            result <<= 1;
        return result;
    }
}
