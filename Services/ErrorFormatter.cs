namespace DBConnectionTester.Services;

internal static class ErrorFormatter
{
    public static string Short(Exception exception)
    {
        var baseException = exception.GetBaseException();
        var message = baseException.Message.Replace("\r", " ").Replace("\n", " ");
        return $"{baseException.GetType().Name}: {message}";
    }
}
