using DBConnectionTester.Services.Storage;
using DBConnectionTester.UI;

namespace DBConnectionTester;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Length == 1 && args[0].Equals(
                    SharedMachineStorageSetup.CommandLineSwitch,
                    StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(() => new SharedMachineStorageSetup().PrepareAsync()).GetAwaiter().GetResult();
                return 0;
            }
            if (args.Length == 1 && args[0].Equals(
                    SharedMachineStorageSetup.DirectoryCommandLineSwitch,
                    StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(() => new SharedMachineStorageSetup().PrepareDirectoryAsync()).GetAwaiter().GetResult();
                return 0;
            }

            var resolver = new StorageResolver(
                StorageLocations.CreateDefault(),
                new RegistryStoragePreferenceStore());
            // Keep the actual entry point and all dialogs on the original STA thread.
            // Startup I/O runs without a UI synchronization context; the message loop starts below.
            var resolution = Task.Run(() => resolver.ResolveAsync(args)).GetAwaiter().GetResult();
            var store = resolution.SelectedStore;
            if (resolution.RequiresSelection)
            {
                using var selection = new StorageSelectionForm(resolution.Candidates);
                if (selection.ShowDialog() != DialogResult.OK || selection.SelectedStore is null)
                    return 0;
                var selectedStore = selection.SelectedStore;
                store = Task.Run(() => resolver.ActivateAsync(selectedStore, resolution.Candidates)).GetAwaiter().GetResult();
            }

            if (store is null)
                throw new ApplicationStoreException("Nenhum armazenamento foi selecionado.");
            var runRepository = new RunRepository(store);
            try
            {
                using var recoveryLease = RunWriteLease.Acquire(store);
                Task.Run(() => runRepository.RecoverInterruptedAsync()).GetAwaiter().GetResult();
            }
            catch (RunAlreadyActiveException)
            {
                // Outra instância está executando testes; esta ainda pode consultar o armazenamento.
            }
            var settings = Task.Run(() => new PersistentSettingsRepository(store).GetAsync()).GetAwaiter().GetResult();
            System.Windows.Forms.Application.Run(new MainForm(store, settings));
            return 0;
        }
        catch (Exception exception) when (exception is ApplicationStoreException or ArgumentException or
                                          UnauthorizedAccessException or IOException)
        {
            MessageBox.Show(
                exception.Message,
                "Falha ao abrir o armazenamento",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }
}
