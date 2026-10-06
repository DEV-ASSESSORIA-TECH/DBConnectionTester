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
            System.Windows.Forms.Application.Run(new MainForm(store));
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
