using DBConnectionTester.Models;
using Microsoft.Win32;

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
    private const string RegistryPath = @"Software\DBConnectionTester";

    public StoragePreference? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
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
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
        key.SetValue("ActiveStoreId", preference.StoreId.ToString("D"), RegistryValueKind.String);
        key.SetValue("ActiveStorePath", Path.GetFullPath(preference.DatabasePath), RegistryValueKind.String);
        key.SetValue("ActiveStoreScope", preference.Scope.ToString(), RegistryValueKind.String);
        if (preference.ObservedPortableStoreId is Guid observed)
            key.SetValue("ObservedPortableStoreId", observed.ToString("D"), RegistryValueKind.String);
        else
            key.DeleteValue("ObservedPortableStoreId", throwOnMissingValue: false);
    }
}
