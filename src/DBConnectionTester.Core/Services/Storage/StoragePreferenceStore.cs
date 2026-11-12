using DBConnectionTester.Models;
using Microsoft.Win32;
using System.Text.Json;

namespace DBConnectionTester.Services.Storage;

public sealed record StoragePreference(
    Guid StoreId,
    string DatabasePath,
    StorageScope Scope,
    Guid? ObservedPortableStoreId);

public interface IStoragePreferenceStore
{
    StoragePreference? Read();
    void Write(StoragePreference preference);
}

public sealed class RegistryStoragePreferenceStore : IStoragePreferenceStore
{
    private readonly string registryPath;

    public RegistryStoragePreferenceStore() : this(@"Software\DBConnectionTester") { }
    internal RegistryStoragePreferenceStore(string registryPath) => this.registryPath = registryPath;

    public StoragePreference? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
        if (key?.GetValue("StorageSelection") is string selection)
        {
            var preference = JsonSerializer.Deserialize<StoragePreference>(selection)
                ?? throw new ApplicationStoreException("A seleção de armazenamento é inválida.");
            return preference with { DatabasePath = Path.GetFullPath(preference.DatabasePath) };
        }
        var storeIdText = key?.GetValue("ActiveStoreId") as string;
        var databasePath = key?.GetValue("ActiveStorePath") as string;
        var scopeText = key?.GetValue("ActiveStoreScope") as string;
        var observedText = key?.GetValue("ObservedPortableStoreId") as string;
        if (!Guid.TryParse(storeIdText, out var storeId) ||
            string.IsNullOrWhiteSpace(databasePath) ||
            !Enum.TryParse<StorageScope>(scopeText, out var scope))
            return null;

        Guid? observed = Guid.TryParse(observedText, out var observedId) ? observedId : null;
        return new StoragePreference(storeId, Path.GetFullPath(databasePath), scope, observed);
    }

    public void Write(StoragePreference preference)
    {
        using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
        // One registry value prevents a partially updated path/identity selection.
        key.SetValue("StorageSelection", JsonSerializer.Serialize(preference with
        { DatabasePath = Path.GetFullPath(preference.DatabasePath) }), RegistryValueKind.String);
    }
}
