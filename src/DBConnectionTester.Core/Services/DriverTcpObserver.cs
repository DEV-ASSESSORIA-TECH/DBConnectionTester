using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using DBConnectionTester.Models;

namespace DBConnectionTester.Services;

/// <summary>
/// Measures the TCP stage from the socket that the database driver itself opens.
/// </summary>
/// <remarks>
/// A bare TCP probe (connect and close without the database handshake) is not neutral for every server:
/// MySQL and MariaDB count it in <c>Aborted_connects</c> and in the per-host error counter limited by
/// <c>max_connect_errors</c>, which ends up blocking the client machine. For those servers the cycle does not
/// open an extra socket: it observes the <c>socket connect</c> activity that .NET emits while the driver connects
/// and turns it into the TCP stage result.
/// </remarks>
internal sealed class DriverTcpObserver : IDisposable
{
    internal const string SocketsSourceName = "Experimental.System.Net.Sockets";
    private const string ScopeSourceName = "DBConnectionTester.DriverTcp";
    private const string ScopeOperationName = "DBConnectionTester.DriverTcp.Scope";

    private static readonly ActivitySource ScopeSource = new(ScopeSourceName);
    private static readonly ConcurrentDictionary<string, DriverTcpObserver> ActiveScopes = new();
    private static readonly object ListenerGate = new();
    private static ActivityListener? listener;

    private readonly ConcurrentQueue<SocketConnectAttempt> attempts = new();
    private readonly Activity? scope;
    private readonly Activity? previous;
    private int started;

    private DriverTcpObserver()
    {
        EnsureListener();
        previous = Activity.Current;
        // A root scope: a parent from the caller would leak its sampling decision into ours.
        Activity.Current = null;
        scope = ScopeSource.StartActivity(ScopeOperationName, ActivityKind.Internal, default(ActivityContext));
        if (scope is not null)
            ActiveScopes[scope.Id!] = this;
        else
            Activity.Current = previous;
    }

    /// <summary>False when the runtime did not let us create the scope; the result then is <see cref="TcpResult.Skipped"/>.</summary>
    public bool IsObserving => scope is not null;

    internal IReadOnlyList<SocketConnectAttempt> Attempts => attempts.ToArray();

    internal bool ConnectStarted => Volatile.Read(ref started) > 0;

    public static DriverTcpObserver Start() => new();

    /// <summary>Builds the TCP stage result after the database stage has finished.</summary>
    public TcpResult ToTcpResult(DatabaseResult database, TimeSpan timeout) =>
        Derive(Attempts, ConnectStarted, database, timeout);

    internal static TcpResult Derive(
        IReadOnlyList<SocketConnectAttempt> attempts,
        bool connectStarted,
        DatabaseResult database,
        TimeSpan timeout)
    {
        // The driver may try several addresses; the stage succeeds with the first one that connected.
        var success = attempts.FirstOrDefault(attempt => attempt.Error is null);
        if (success is not null)
            return new TcpResult(StepStatus.Success, success.ElapsedMs, "", success.RemoteAddress, null);

        if (attempts.Count > 0)
        {
            var failure = attempts[^1];
            return new TcpResult(StepStatus.Failed, failure.ElapsedMs, "", "",
                DiagnosticClassifier.Tcp(new SocketException((int)failure.Error!.Value)));
        }

        // A connect still pending when the database stage gave up is a TCP timeout.
        if (connectStarted && !database.ConnectOk)
            return new TcpResult(StepStatus.Failed, (long)timeout.TotalMilliseconds, "", "",
                DiagnosticClassifier.Tcp(new TimeoutException("A conexão TCP do driver não foi concluída dentro do timeout.")));

        // No socket was opened (for example, the name did not resolve; the DNS stage reports it).
        return TcpResult.Skipped();
    }

    public void Dispose()
    {
        if (scope is null) return;
        ActiveScopes.TryRemove(scope.Id!, out _);
        scope.Stop();
        Activity.Current = previous;
    }

    private static void EnsureListener()
    {
        if (listener is not null) return;
        lock (ListenerGate)
        {
            if (listener is not null) return;
            var created = new ActivityListener
            {
                ShouldListenTo = source => source.Name is ScopeSourceName or SocketsSourceName,
                Sample = Sample,
                ActivityStarted = activity =>
                {
                    if (activity.Source.Name == SocketsSourceName && FindScope(activity) is { } observer)
                        Interlocked.Increment(ref observer.started);
                },
                ActivityStopped = activity =>
                {
                    if (activity.Source.Name == SocketsSourceName && FindScope(activity) is { } observer &&
                        SocketConnectAttempt.From(activity) is { } attempt)
                        observer.attempts.Enqueue(attempt);
                }
            };
            ActivitySource.AddActivityListener(created);
            listener = created;
        }
    }

    // Only activities inside an observation scope are recorded; everything else in the process stays untouched.
    private static ActivitySamplingResult Sample(ref ActivityCreationOptions<ActivityContext> options)
    {
        if (options.Source.Name == ScopeSourceName)
            return ActivitySamplingResult.AllDataAndRecorded;
        for (var current = Activity.Current; current is not null; current = current.Parent)
        {
            if (current.Source.Name == ScopeSourceName)
                return ActivitySamplingResult.AllDataAndRecorded;
        }
        return ActivitySamplingResult.None;
    }

    private static DriverTcpObserver? FindScope(Activity activity)
    {
        for (var current = activity.Parent; current is not null; current = current.Parent)
        {
            if (current.Source.Name == ScopeSourceName && current.Id is { } id && ActiveScopes.TryGetValue(id, out var observer))
                return observer;
        }
        return null;
    }
}

internal sealed record SocketConnectAttempt(long ElapsedMs, string RemoteAddress, SocketError? Error)
{
    internal static SocketConnectAttempt? From(Activity activity)
    {
        if (!activity.OperationName.EndsWith(".Connect", StringComparison.Ordinal))
            return null;
        var elapsedMs = (long)activity.Duration.TotalMilliseconds;
        var remote = activity.GetTagItem("network.peer.address") as string ?? "";
        if (activity.Status != ActivityStatusCode.Error)
            return new SocketConnectAttempt(elapsedMs, remote, null);
        return new SocketConnectAttempt(elapsedMs, remote, ParseError(activity.GetTagItem("error.type") as string));
    }

    // .NET reports SocketError in snake_case ("connection_refused", "timed_out"). A connect that the driver
    // aborted (its own connect timeout) is reported as a TCP timeout, like the bare probe did.
    internal static SocketError ParseError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return SocketError.SocketError;
        if (value.Contains("cancel", StringComparison.OrdinalIgnoreCase)) return SocketError.TimedOut;
        var name = value.Replace("_", "", StringComparison.Ordinal);
        if (!Enum.TryParse<SocketError>(name, ignoreCase: true, out var error) || !Enum.IsDefined(error))
            return SocketError.SocketError;
        return error == SocketError.OperationAborted ? SocketError.TimedOut : error;
    }
}
