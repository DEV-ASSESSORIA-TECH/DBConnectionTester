namespace DBConnectionTester.Models;

public enum LatencyTrendDirection
{
    InsufficientData,
    Stable,
    Improving,
    Degrading
}

public static class LatencyTrendAnalyzer
{
    public static LatencyTrendDirection Analyze(
        IEnumerable<long> successfulLatencies,
        int maximumSamples = 20,
        double changeThreshold = 0.05)
    {
        var samples = successfulLatencies.TakeLast(maximumSamples).ToArray();
        if (samples.Length < 6)
            return LatencyTrendDirection.InsufficientData;

        var split = samples.Length / 2;
        var previousAverage = samples[..split].Average();
        var recentAverage = samples[split..].Average();
        if (previousAverage == 0)
            return recentAverage == 0 ? LatencyTrendDirection.Stable : LatencyTrendDirection.Degrading;

        var change = (recentAverage - previousAverage) / previousAverage;
        if (Math.Abs(change) <= changeThreshold)
            return LatencyTrendDirection.Stable;
        return change < 0 ? LatencyTrendDirection.Improving : LatencyTrendDirection.Degrading;
    }
}
