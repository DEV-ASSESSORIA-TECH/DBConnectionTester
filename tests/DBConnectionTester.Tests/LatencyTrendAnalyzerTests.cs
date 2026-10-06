using DBConnectionTester.Models;

namespace DBConnectionTester.Tests;

public sealed class LatencyTrendAnalyzerTests
{
    [Fact]
    public void RequiresEnoughSamples()
    {
        Assert.Equal(LatencyTrendDirection.InsufficientData,
            LatencyTrendAnalyzer.Analyze(new long[] { 10, 11, 12, 13, 14 }));
    }

    [Fact]
    public void DetectsStableImprovingAndDegradingSeries()
    {
        Assert.Equal(LatencyTrendDirection.Stable,
            LatencyTrendAnalyzer.Analyze(new long[] { 100, 101, 99, 102, 100, 98 }));
        Assert.Equal(LatencyTrendDirection.Improving,
            LatencyTrendAnalyzer.Analyze(new long[] { 100, 110, 105, 70, 75, 72 }));
        Assert.Equal(LatencyTrendDirection.Degrading,
            LatencyTrendAnalyzer.Analyze(new long[] { 50, 52, 48, 80, 85, 82 }));
    }
}
