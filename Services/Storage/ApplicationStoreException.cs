namespace DBConnectionTester.Services.Storage;

public sealed class ApplicationStoreException : Exception
{
    public ApplicationStoreException(string message)
        : base(message)
    {
    }

    public ApplicationStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
