using System.Text.Json;

namespace DBConnectionTester.Cli.Background;

/// <summary>
/// Cross-process stop request for the CLI run writing to a store: a small file next to
/// data.db (and to .run.lock) that the running CLI polls between waits. It works for a run
/// in another terminal, in the background (--detach) or started with Windows (--autostart).
/// </summary>
internal static class StopRequests
{
    private const string FileName = ".run.stop";

    private sealed record StopRequest(Guid? RunId, DateTimeOffset RequestedAt);

    public static string PathFor(string storeDirectory) => Path.Combine(storeDirectory, FileName);

    /// <param name="runId">The run to stop, or null for whichever CLI run is active.</param>
    public static void Write(string storeDirectory, Guid? runId)
    {
        var path = PathFor(storeDirectory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new StopRequest(runId, DateTimeOffset.UtcNow)));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// True when a request targets <paramref name="runId"/> (or any run) and was made after the
    /// run started; the request is then removed. Requests older than the run are stale leftovers
    /// and are removed without stopping it.
    /// </summary>
    public static bool Consume(string storeDirectory, Guid runId, DateTimeOffset runStartedAt)
    {
        var path = PathFor(storeDirectory);
        if (!File.Exists(path)) return false;
        StopRequest? request;
        try { request = JsonSerializer.Deserialize<StopRequest>(File.ReadAllText(path)); }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
        if (request is null) return false;
        var stale = request.RequestedAt < runStartedAt.AddSeconds(-1);
        var matches = request.RunId is null || request.RunId == runId;
        if (stale || matches) TryDelete(path);
        return matches && !stale;
    }

    /// <summary>A request for any run, used by the autostart loop between runs.</summary>
    public static bool ConsumeAny(string storeDirectory)
    {
        var path = PathFor(storeDirectory);
        if (!File.Exists(path)) return false;
        TryDelete(path);
        return true;
    }

    public static void Clear(string storeDirectory) => TryDelete(PathFor(storeDirectory));

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
