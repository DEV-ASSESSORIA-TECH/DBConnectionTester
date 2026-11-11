using DBConnectionTester.Models;

namespace DBConnectionTester.Services.Storage;

public sealed class SettingsCommitService
{
    public async Task SaveAsync(SqliteApplicationStore source, IApplicationSettingsRepository repository,
        ApplicationSettings previous, ApplicationSettings settings, StoreDescriptor target,
        bool copyPreferences, IStoragePreferenceStore preferences)
    {
        var observedPortable = preferences.Read()?.ObservedPortableStoreId;
        PersistentSettingsRepository? destination = null;
        ApplicationSettings? destinationPrevious = null;
        var different = source.Descriptor.StoreId != target.StoreId ||
            !string.Equals(source.Descriptor.DatabasePath, target.DatabasePath, StringComparison.OrdinalIgnoreCase);
        if (different)
        {
            var inspection = await SqliteApplicationStore.InspectAsync(target.DatabasePath);
            if (!inspection.IsCompatible || inspection.Descriptor!.StoreId != target.StoreId)
                throw new ApplicationStoreException("O banco selecionado foi removido, substituído ou não é compatível. Escolha o banco novamente.");
            if (copyPreferences)
            {
                var store = await StorageMigrationService.UseExistingAsync(target.DatabasePath);
                destination = new PersistentSettingsRepository(store);
                destinationPrevious = await destination.GetAsync();
            }
        }

        var sourceWritten = false;
        var destinationWritten = false;
        try
        {
            await repository.SaveAsync(settings);
            sourceWritten = true;
            if (destination is not null)
            {
                await destination.SaveAsync(settings);
                destinationWritten = true;
            }
            // Only schedule the switch after both preference writes succeed.
            preferences.Write(new StoragePreference(target.StoreId, target.DatabasePath, target.Scope,
                target.Scope == StorageScope.Portable ? target.StoreId : observedPortable));
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            if (destinationWritten)
                try { await destination!.SaveAsync(destinationPrevious!); }
                catch (Exception rollback) { failures.Add(rollback); }
            if (sourceWritten)
                try { await repository.SaveAsync(previous); }
                catch (Exception rollback) { failures.Add(rollback); }
            if (failures.Count > 1)
                throw new ApplicationStoreException("A troca não foi confirmada e não foi possível restaurar todas as preferências. Verifique os bancos antes de tentar novamente.", new AggregateException(failures));
            throw;
        }
    }
}
