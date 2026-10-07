using DBConnectionTester.Models;

namespace DBConnectionTester.Services.Storage;

public sealed class StorageMigrationService
{
    public async Task<SqliteApplicationStore> CreateEmptyAsync(
        string destinationDirectory,
        StorageScope scope,
        CancellationToken token = default)
    {
        var destination = PrepareDestination(destinationDirectory);
        var staging = CreateStaging(destination);
        try
        {
            await SqliteApplicationStore.OpenOrCreateAsync(
                Path.Combine(staging, StorageLocations.DatabaseFileName), scope, token: token);
            CommitStaging(staging, destination);
            staging = "";
            return await SqliteApplicationStore.OpenOrCreateAsync(
                Path.Combine(destination, StorageLocations.DatabaseFileName), scope, token: token);
        }
        finally
        {
            Cleanup(staging);
        }
    }

    public async Task<SqliteApplicationStore> CloneAsync(
        SqliteApplicationStore source,
        string destinationDirectory,
        StorageScope scope,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var lease = RunWriteLease.Acquire(source);
        var destination = PrepareDestination(destinationDirectory);
        var staging = CreateStaging(destination);
        try
        {
            await StoreCloneService.CloneAsync(
                source, Path.Combine(staging, StorageLocations.DatabaseFileName), scope, token);
            CommitStaging(staging, destination);
            staging = "";
            return await SqliteApplicationStore.OpenOrCreateAsync(
                Path.Combine(destination, StorageLocations.DatabaseFileName), scope, token: token);
        }
        finally
        {
            Cleanup(staging);
        }
    }

    public static async Task<SqliteApplicationStore> UseExistingAsync(
        string databasePath,
        CancellationToken token = default)
    {
        var inspection = await SqliteApplicationStore.InspectAsync(databasePath, token);
        if (!inspection.IsCompatible)
            throw new ApplicationStoreException(
                inspection.Status == StoreInspectionStatus.Missing
                    ? "O banco selecionado não existe."
                    : $"O banco selecionado não é compatível: {inspection.ErrorMessage}");
        var descriptor = inspection.Descriptor!;
        return await SqliteApplicationStore.OpenOrCreateAsync(
            descriptor.DatabasePath, descriptor.Scope, descriptor.ClonedFromStoreId, token);
    }

    private static string PrepareDestination(string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("Informe o diretório de destino.", nameof(destinationDirectory));
        var destination = Path.GetFullPath(destinationDirectory.Trim());
        if (File.Exists(destination))
            throw new IOException("O destino já está ocupado por um arquivo.");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("O diretório de destino não está vazio.");
        return destination;
    }

    private static string CreateStaging(string destination)
    {
        var parent = Path.GetDirectoryName(destination)
            ?? throw new IOException("Não foi possível determinar a pasta pai do destino.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".dbct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        return staging;
    }

    private static void CommitStaging(string staging, string destination)
    {
        if (Directory.Exists(destination))
            Directory.Delete(destination, recursive: false);
        Directory.Move(staging, destination);
    }

    private static void Cleanup(string path)
    {
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
