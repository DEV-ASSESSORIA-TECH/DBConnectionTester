using System.Diagnostics;
using System.Data.Common;
using System.Data.Odbc;
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
        DbConnection? connection = null;
        Task? deferredOdbcOpen = null;

        try
        {
            connection = DatabaseConnectionFactory.Create(settings);
            var connect = Stopwatch.StartNew();
            try
            {
                if (connection is OdbcConnection)
                {
                    var openTask = Task.Run(connection.Open, CancellationToken.None);
                    try
                    {
                        await openTask.WaitAsync(settings.Timeout.Value, token);
                    }
                    catch
                    {
                        deferredOdbcOpen = openTask;
                        throw;
                    }
                }
                else
                {
                    await connection.OpenAsync(token).WaitAsync(settings.Timeout.Value, token);
                }
                connect.Stop();
                connectMs = (long)connect.Elapsed.TotalMilliseconds;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                connect.Stop();
                total.Stop();
                connectMs = (long)connect.Elapsed.TotalMilliseconds;
                return new DatabaseResult(StepStatus.Failed, connectMs, StepStatus.Skipped, 0,
                    (long)total.Elapsed.TotalMilliseconds,
                    DiagnosticClassifier.Database(exception, DiagnosticLayer.DatabaseConnect, settings), null);
            }

            var query = Stopwatch.StartNew();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.CommandTimeout = (int)Math.Ceiling(settings.Timeout.Value.TotalSeconds);
                var value = await command.ExecuteScalarAsync(token).WaitAsync(settings.Timeout.Value, token);
                query.Stop();
                total.Stop();
                queryMs = (long)query.Elapsed.TotalMilliseconds;
                var ok = Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
                return new DatabaseResult(StepStatus.Success, connectMs, ok ? StepStatus.Success : StepStatus.Failed, queryMs,
                    (long)total.Elapsed.TotalMilliseconds, null,
                    ok ? null : DiagnosticClassifier.UnexpectedQueryResult(value));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                query.Stop();
                total.Stop();
                queryMs = (long)query.Elapsed.TotalMilliseconds;
                return new DatabaseResult(StepStatus.Success, connectMs, StepStatus.Failed, queryMs,
                    (long)total.Elapsed.TotalMilliseconds, null,
                    DiagnosticClassifier.Database(exception, DiagnosticLayer.DatabaseQuery, settings));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            total.Stop();
            return new DatabaseResult(StepStatus.Failed, connectMs, StepStatus.Skipped, queryMs,
                (long)total.Elapsed.TotalMilliseconds,
                DiagnosticClassifier.Database(exception, DiagnosticLayer.DatabaseConnect, settings), null);
        }
        finally
        {
            if (connection is not null)
            {
                if (deferredOdbcOpen is null)
                    await connection.DisposeAsync();
                else
                    _ = DisposeAfterOpenCompletesAsync(connection, deferredOdbcOpen);
            }
        }
    }

    private static async Task DisposeAfterOpenCompletesAsync(DbConnection connection, Task openTask)
    {
        try
        {
            await openTask.ConfigureAwait(false);
        }
        catch
        {
            // The connection result was already classified as a timeout or cancellation.
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
