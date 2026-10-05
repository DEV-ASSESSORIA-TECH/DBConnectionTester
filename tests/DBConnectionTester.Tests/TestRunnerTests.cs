using DBConnectionTester.Services;

namespace DBConnectionTester.Tests;

public sealed class TestRunnerTests
{
    [Fact]
    public void CsvSchemaIsGeneric()
    {
        Assert.Contains("DB_Type", TestRunner.CsvHeader);
        Assert.Contains("DB_Connect_Status", TestRunner.CsvHeader);
        Assert.Contains("DB_Query_Status", TestRunner.CsvHeader);
        Assert.Contains("DB_Error", TestRunner.CsvHeader);
        Assert.DoesNotContain("MySQL", TestRunner.CsvHeader, StringComparison.OrdinalIgnoreCase);
    }
}
