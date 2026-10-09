using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using Microsoft.Win32;

namespace DBConnectionTester.Tests;

public sealed class StoragePreferenceStoreTests
{
    [Fact]
    public void ReadsLegacySelectionAndReplacesItAsOneValue()
    {
        var path = @"Software\DBConnectionTester.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            var original = new StoragePreference(Guid.NewGuid(), Path.Combine(Path.GetTempPath(), "original.db"), StorageScope.Custom, Guid.NewGuid());
            using (var key = Registry.CurrentUser.CreateSubKey(path))
            {
                key.SetValue("ActiveStoreId", original.StoreId.ToString("D"));
                key.SetValue("ActiveStorePath", original.DatabasePath);
                key.SetValue("ActiveStoreScope", original.Scope.ToString());
                key.SetValue("ObservedPortableStoreId", original.ObservedPortableStoreId!.Value.ToString("D"));
            }
            var repository = new RegistryStoragePreferenceStore(path);
            Assert.Equal(original, repository.Read());
            var next = original with { StoreId = Guid.NewGuid(), DatabasePath = Path.Combine(Path.GetTempPath(), "next.db") };
            repository.Write(next);
            Assert.Equal(next, new RegistryStoragePreferenceStore(path).Read());
            using var saved = Registry.CurrentUser.OpenSubKey(path);
            Assert.IsType<string>(saved!.GetValue("StorageSelection"));
            Assert.Equal(original.DatabasePath, saved.GetValue("ActiveStorePath"));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }
}
