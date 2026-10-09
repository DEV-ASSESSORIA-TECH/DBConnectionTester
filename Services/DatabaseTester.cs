using System.Diagnostics;
using System.Data.Common;
using System.Globalization;
using DBConnectionTester.Models;

namespace DBConnectionTester.Services;

internal static class DatabaseTester
{
    public static Task<DatabaseResult> TestAsync(TestSettings settings, CancellationToken token) =>
        TestAsync(settings, token, () => DatabaseConnectionFactory.Create(settings));

    internal static Task<DatabaseResult> TestAsync(TestSettings settings, CancellationToken token, Func<DbConnection> createConnection) =>
        settings.DatabaseType == DatabaseType.SapSqlAnywhere
            ? Task.Run(() => TestCoreAsync(settings, token, createConnection), CancellationToken.None)
            : TestCoreAsync(settings, token, createConnection);

    private static async Task<DatabaseResult> TestCoreAsync(TestSettings settings, CancellationToken token, Func<DbConnection> createConnection)
    {
        long connectMs = 0;
        long queryMs = 0;
        var total = Stopwatch.StartNew();
        DbConnection? connection = null;
        Task? deferredOperation = null;
        DbCommand? command = null;

        try
        {
            token.ThrowIfCancellationRequested();
            connection = createConnection();
            var connect = Stopwatch.StartNew();
            try
            {
                if (settings.DatabaseType == DatabaseType.SapSqlAnywhere)
                {
                    var openTask = Task.Run(connection.Open, CancellationToken.None);
                    try
                    {
                        await openTask.WaitAsync(settings.Timeout.Value, token).ConfigureAwait(false);
                    }
                    catch
                    {
                        deferredOperation = openTask;
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
                command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.CommandTimeout = (int)Math.Ceiling(settings.Timeout.Value.TotalSeconds);
                var queryTask = settings.DatabaseType == DatabaseType.SapSqlAnywhere
                    ? Task.Run(command.ExecuteScalar, CancellationToken.None)
                    : command.ExecuteScalarAsync(token);
                object? value;
                try
                {
                    value = await queryTask.WaitAsync(settings.Timeout.Value, token).ConfigureAwait(false);
                }
                catch
                {
                    // A native ODBC call can outlive the wait. Keep its resources alive until it returns.
                    if (settings.DatabaseType == DatabaseType.SapSqlAnywhere) deferredOperation = queryTask;
                    throw;
                }
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
                if (deferredOperation is null)
                    await DisposeResourcesAsync(connection, command);
                else
                    _ = DisposeAfterOperationCompletesAsync(connection, command, deferredOperation);
            }
        }
    }

    private static async Task DisposeAfterOperationCompletesAsync(DbConnection connection, DbCommand? command, Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch
        {
            // The operation result was already classified as a timeout or cancellation.
        }
        finally
        {
            try { await DisposeResourcesAsync(connection, command); }
            catch { /* Cleanup of an abandoned native operation must not fault an unobserved task. */ }
        }
    }

    private static async Task DisposeResourcesAsync(DbConnection connection, DbCommand? command)
    {
        try { if (command is not null) await command.DisposeAsync().ConfigureAwait(false); }
        finally { await connection.DisposeAsync().ConfigureAwait(false); }
    }
}
