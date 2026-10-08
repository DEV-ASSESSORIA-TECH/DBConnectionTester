using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

public sealed class StartupThreadingTests
{
    [Fact]
    public void ActualExecutableEntryPointUsesStaForNativeFileDialogs()
    {
        var entryPoint = typeof(MainForm).Assembly.EntryPoint!;
        Assert.NotNull(entryPoint.GetCustomAttributes(typeof(STAThreadAttribute), false).SingleOrDefault());
        Assert.Equal(typeof(int), entryPoint.ReturnType);
    }
}
