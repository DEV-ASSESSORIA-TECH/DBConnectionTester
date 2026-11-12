using System.Reflection;

namespace DBConnectionTester.Models;

public static class ApplicationInfo
{
    public static string Version =>
        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
        .GetName().Version?.ToString(3) ?? "0.0.0";
}
