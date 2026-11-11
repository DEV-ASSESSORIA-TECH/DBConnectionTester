namespace DBConnectionTester.Services.Storage;

public static class RunRecoveryService
{
    public static async Task RecoverAsync(SqliteApplicationStore store, CancellationToken token = default)
    {
        using var lease = RunWriteLease.Acquire(store);
        await new RunRepository(store).RecoverInterruptedAsync(token);
    }
}
