using System.Diagnostics;
using System.Globalization;
using DBConnectionTester.Models;

namespace DBConnectionTester.Services;

internal static class DatabaseTester
{
    public static async Task<DatabaseResult> TestAsync(TestSettings settings, CancellationToken token)
    {
        long connectMs = 0;
        long queryMs = 0;
        var total = Stopwatch.StartNew();

        try
        {
            await using var connection = DatabaseConnectionFactory.Create(settings);
            var connect = Stopwatch.StartNew();
            try
            {
                await connection.OpenAsync(token).WaitAsync(settings.Timeout, token);
                connect.Stop();
                connectMs = (long)connect.Elapsed.TotalMilliseconds;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                connect.Stop();
                total.Stop();
                connectMs = (long)connect.Elapsed.TotalMilliseconds;
                return new DatabaseResult(false, "FALHA", connectMs, false, "N/A", 0,
                    (long)total.Elapsed.TotalMilliseconds, "CONNECT: " + ErrorFormatter.Short(exception));
            }

            var query = Stopwatch.StartNew();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(settings.Timeout.TotalSeconds));
                var value = await command.ExecuteScalarAsync(token).WaitAsync(settings.Timeout, token);
                query.Stop();
                total.Stop();
                queryMs = (long)query.Elapsed.TotalMilliseconds;
                var ok = Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
                return new DatabaseResult(true, "OK", connectMs, ok, ok ? "OK" : "FALHA", queryMs,
                    (long)total.Elapsed.TotalMilliseconds, ok ? "" : "SELECT 1 retornou valor inesperado");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                query.Stop();
                total.Stop();
                queryMs = (long)query.Elapsed.TotalMilliseconds;
                return new DatabaseResult(true, "OK", connectMs, false, "FALHA", queryMs,
                    (long)total.Elapsed.TotalMilliseconds, "QUERY: " + ErrorFormatter.Short(exception));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            total.Stop();
            return new DatabaseResult(false, "FALHA", connectMs, false, "N/A", queryMs,
                (long)total.Elapsed.TotalMilliseconds, ErrorFormatter.Short(exception));
        }
    }
}
