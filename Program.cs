using DBConnectionTester.Services.Storage;
using DBConnectionTester.UI;

namespace DBConnectionTester;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var resolver = new StorageResolver(
                StorageLocations.CreateDefault(),
                new RegistryStoragePreferenceStore());
            var resolution = await resolver.ResolveAsync(args);
            var store = resolution.SelectedStore;
            if (resolution.RequiresSelection)
            {
                using var selection = new StorageSelectionForm(resolution.Candidates);
                if (selection.ShowDialog() != DialogResult.OK || selection.SelectedStore is null)
                    return;
                store = await resolver.ActivateAsync(selection.SelectedStore, resolution.Candidates);
            }

            if (store is null)
                throw new ApplicationStoreException("Nenhum armazenamento foi selecionado.");
            var runRepository = new RunRepository(store);
            try
            {
                using var recoveryLease = RunWriteLease.Acquire(store);
                await runRepository.RecoverInterruptedAsync();
            }
            catch (RunAlreadyActiveException)
            {
                // Outra instância está executando testes; esta ainda pode consultar o armazenamento.
            }
            var settings = await new PersistentSettingsRepository(store).GetAsync();
            System.Windows.Forms.Application.Run(new MainForm(store, settings));
        }
        catch (Exception exception) when (exception is ApplicationStoreException or ArgumentException or
                                          UnauthorizedAccessException or IOException)
        {
            MessageBox.Show(
                exception.Message,
                "Falha ao abrir o armazenamento",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
