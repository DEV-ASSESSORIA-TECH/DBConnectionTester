using System.Data;
using System.Data.Common;
using DBConnectionTester.Models;
using DBConnectionTester.Services;

namespace DBConnectionTester.Tests;

public sealed class DatabaseTesterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeQueryTimeoutAndCancellationReturnBeforeDriverAndKeepResourcesAlive(bool cancel)
    {
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var connection = new BlockingConnection(release);
        var caller = Environment.CurrentManagedThreadId;
        var running = DatabaseTester.TestAsync(Settings(), cancellation.Token, () => connection);
        try
        {
            Assert.True(SpinWait.SpinUntil(() => connection.QueryStarted.Task.IsCompleted, TimeSpan.FromSeconds(3)));
            Assert.NotEqual(caller, connection.QueryThread);
            Assert.False(running.IsCompleted);
            if (cancel)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(3)));
            }
            else
            {
                var result = await running.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(StepStatus.Success, result.ConnectStatus);
                Assert.Equal(StepStatus.Failed, result.QueryStatus);
                Assert.Equal(DiagnosticCodes.DatabaseQueryTimeout, result.QueryDiagnostic!.DiagnosticCode);
            }
            Assert.False(connection.ResourcesDisposed.Task.IsCompleted);
            Assert.False(connection.CommandDisposed);
        }
        finally { release.Set(); }
        await connection.ResourcesDisposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(connection.CommandDisposed);
    }

    [Fact]
    public async Task NativeQueryCompletesSuccessfullyAndDisposesResources()
    {
        using var release = new ManualResetEventSlim(true);
        var connection = new BlockingConnection(release);
        var result = await DatabaseTester.TestAsync(Settings(), CancellationToken.None, () => connection);
        Assert.Equal(StepStatus.Success, result.QueryStatus);
        Assert.True(connection.CommandDisposed);
        Assert.True(connection.ResourcesDisposed.Task.IsCompleted);
    }

    private static TestSettings Settings() => new(DatabaseType.SapSqlAnywhere, "localhost", NetworkPort.Create(2638),
        "user", "password", "database", "", SqlServerAuthentication.SqlLogin, "driver", RunCount.Create(1), false,
        TestInterval.Create(TimeSpan.Zero), StageTimeout.Create(TimeSpan.FromSeconds(1)), false, false, false, true);

    private sealed class BlockingConnection(ManualResetEventSlim release) : DbConnection
    {
        public TaskCompletionSource QueryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResourcesDisposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int QueryThread;
        public bool CommandDisposed;
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "test";
        public override ConnectionState State => ConnectionState.Open;
        public override void Open() { }
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new BlockingCommand(this, release);
        protected override void Dispose(bool disposing) { ResourcesDisposed.TrySetResult(); base.Dispose(disposing); }
    }

    private sealed class BlockingCommand(BlockingConnection owner, ManualResetEventSlim release) : DbCommand
    {
        public override object ExecuteScalar()
        {
            owner.QueryThread = Environment.CurrentManagedThreadId;
            owner.QueryStarted.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test driver did not return.");
            return 1;
        }
        protected override void Dispose(bool disposing) { owner.CommandDisposed = true; base.Dispose(disposing); }
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
        public override void Cancel() { }
        public override void Prepare() => throw new NotSupportedException();
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }
}

