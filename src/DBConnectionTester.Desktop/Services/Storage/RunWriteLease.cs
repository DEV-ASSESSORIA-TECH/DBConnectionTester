using System.Text;

namespace DBConnectionTester.Services.Storage;

public sealed class RunAlreadyActiveException : IOException
{
    public RunAlreadyActiveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class RunWriteLease : IDisposable
{
    private readonly FileStream stream;

    private RunWriteLease(FileStream stream)
    {
        this.stream = stream;
    }

    public static RunWriteLease Acquire(SqliteApplicationStore store)
    {
        var directory = Path.GetDirectoryName(store.Descriptor.DatabasePath)!;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ".run.lock");
        try
        {
            var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.ReadWrite,
                Share = FileShare.Read,
                Options = FileOptions.WriteThrough
            });
            stream.SetLength(0);
            var content = Encoding.UTF8.GetBytes(
                $"StoreId={store.Descriptor.StoreId:D}{Environment.NewLine}" +
                $"ProcessId={Environment.ProcessId}{Environment.NewLine}" +
                $"AcquiredAt={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
            stream.Write(content);
            stream.Flush(flushToDisk: true);
            return new RunWriteLease(stream);
        }
        catch (IOException exception)
        {
            throw new RunAlreadyActiveException(
                "Já existe uma execução gravando neste armazenamento. O histórico permanece disponível para consulta.",
                exception);
        }
    }

    public void Dispose() => stream.Dispose();
}
