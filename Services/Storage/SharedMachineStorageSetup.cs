using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using DBConnectionTester.Models;

namespace DBConnectionTester.Services.Storage;

public interface ISharedDirectorySecurity
{
    void GrantModifyToLocalUsers(string directoryPath);
}

public sealed class WindowsSharedDirectorySecurity : ISharedDirectorySecurity
{
    public void GrantModifyToLocalUsers(string directoryPath)
    {
        var directory = new DirectoryInfo(directoryPath);
        var security = directory.GetAccessControl(AccessControlSections.Access);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            users,
            FileSystemRights.Modify | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        directory.SetAccessControl(security);
    }
}

public sealed class SharedMachineStorageSetup
{
    public const string CommandLineSwitch = "--prepare-shared-store";
    private readonly string sharedDatabasePath;
    private readonly ISharedDirectorySecurity security;

    public SharedMachineStorageSetup()
        : this(StorageLocations.CreateDefault().SharedDatabasePath, new WindowsSharedDirectorySecurity())
    {
    }

    internal SharedMachineStorageSetup(string sharedDatabasePath, ISharedDirectorySecurity security)
    {
        this.sharedDatabasePath = Path.GetFullPath(sharedDatabasePath);
        this.security = security;
    }

    public async Task<SqliteApplicationStore> PrepareAsync(CancellationToken token = default)
    {
        var directory = Path.GetDirectoryName(sharedDatabasePath)
            ?? throw new ApplicationStoreException("O caminho compartilhado é inválido.");
        Directory.CreateDirectory(directory);
        security.GrantModifyToLocalUsers(directory);
        await VerifyWriteAccessAsync(directory, token);
        return await SqliteApplicationStore.OpenOrCreateAsync(
            sharedDatabasePath, StorageScope.SharedMachine, token: token);
    }

    private static async Task VerifyWriteAccessAsync(string directory, CancellationToken token)
    {
        var probe = Path.Combine(directory, ".permission-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(probe, "DBConnectionTester", token);
        }
        finally
        {
            if (File.Exists(probe))
                File.Delete(probe);
        }
    }
}

public sealed class SharedMachineStorageElevator
{
    public async Task PrepareAsync(CancellationToken token = default)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("Não foi possível localizar o executável atual.");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = SharedMachineStorageSetup.CommandLineSwitch,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        }) ?? throw new InvalidOperationException("Não foi possível iniciar a preparação elevada.");
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0)
            throw new ApplicationStoreException($"A preparação do armazenamento compartilhado terminou com o código {process.ExitCode}.");
    }
}
