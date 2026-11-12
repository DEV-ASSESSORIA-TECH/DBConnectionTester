namespace DBConnectionTester.UI;

// Owned by the UI thread. Cancellation reduces wasted work; identity also
// rejects results from providers that cannot interrupt an in-flight query.
internal sealed class LatestUiRequest : IDisposable
{
    private Request? current;
    private bool disposed;

    public Request Start(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Cancel();
        return current = new Request(this, token);
    }

    public void Cancel()
    {
        var previous = current;
        current = null;
        previous?.Cancel();
    }

    public void Dispose() { disposed = true; Cancel(); }

    internal sealed class Request : IDisposable
    {
        private readonly LatestUiRequest owner;
        private readonly CancellationTokenSource cancellation;
        public CancellationToken Token { get; }
        public bool IsCurrent => ReferenceEquals(owner.current, this) && !Token.IsCancellationRequested;

        public Request(LatestUiRequest owner, CancellationToken token)
        {
            this.owner = owner;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Token = cancellation.Token;
        }

        public void Cancel() => cancellation.Cancel();

        public void Dispose()
        {
            if (ReferenceEquals(owner.current, this)) owner.current = null;
            cancellation.Dispose();
        }
    }
}
