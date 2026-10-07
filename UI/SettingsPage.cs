using System.Diagnostics;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Portability;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.UI;

public sealed class SettingsPage : UserControl
{
    private readonly SqliteApplicationStore store;
    private readonly IApplicationSettingsRepository settingsRepository;
    private readonly IStoragePreferenceStore preferences;
    private readonly StorageMigrationService migration = new();
    private readonly ComboBox theme = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox legacyEnabled = new() { Text = "Gravar CSV e TXT durante a execução", AutoSize = true };
    private readonly TextBox legacyDirectory = new();
    private readonly Button browseLegacy = new() { Text = "Escolher...", AutoSize = true };
    private readonly Button save = new() { Text = "Salvar configurações", AutoSize = true };
    private readonly ComboBox targetScope = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button clone = new() { Text = "Copiar armazenamento atual", AutoSize = true };
    private readonly Button create = new() { Text = "Criar armazenamento vazio", AutoSize = true };
    private readonly Button useExisting = new() { Text = "Usar banco existente", AutoSize = true };
    private readonly Button package = new() { Text = "Criar pacote portátil", AutoSize = true };
    private readonly Button restore = new() { Text = "Restaurar pacote", AutoSize = true };
    private readonly Button openFolder = new() { Text = "Abrir pasta", AutoSize = true };
    private readonly CheckBox includeExecutable = new() { Text = "Incluir EXE single-file", AutoSize = true };
    private ApplicationSettings currentSettings;

    public SettingsPage(
        SqliteApplicationStore store,
        ApplicationSettings settings,
        IApplicationSettingsRepository settingsRepository,
        IStoragePreferenceStore preferences)
    {
        this.store = store;
        currentSettings = settings;
        this.settingsRepository = settingsRepository;
        this.preferences = preferences;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        Padding = new Padding(24);
        theme.Items.AddRange(Enum.GetValues<ApplicationTheme>().Cast<object>().ToArray());
        targetScope.Items.AddRange(new object[]
            { StorageScope.LocalUser, StorageScope.Portable, StorageScope.SharedMachine, StorageScope.Custom });
        targetScope.SelectedIndex = 0;

        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        root.Controls.Add(new Label { Text = "Configurações", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold), Margin = new Padding(3, 3, 3, 16) });
        root.Controls.Add(BuildAppearanceGroup());
        root.Controls.Add(BuildStorageGroup());
        root.Controls.Add(BuildPackageGroup());
        Controls.Add(root);
        LoadSettings(settings);

        browseLegacy.Click += (_, _) => BrowseLegacyDirectory();
        save.Click += async (_, _) => await SaveAsync();
        clone.Click += async (_, _) => await CreateOrCloneAsync(copyCurrent: true);
        create.Click += async (_, _) => await CreateOrCloneAsync(copyCurrent: false);
        useExisting.Click += async (_, _) => await UseExistingAsync();
        package.Click += async (_, _) => await CreatePackageAsync();
        restore.Click += async (_, _) => await RestorePackageAsync();
        openFolder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", Path.GetDirectoryName(store.Descriptor.DatabasePath)!) { UseShellExecute = true });
    }

    public event Action<ApplicationSettings>? SettingsSaved;
    public event Action<StoreDescriptor>? StorageSelected;
    internal ApplicationTheme SelectedTheme => theme.SelectedItem is ApplicationTheme selected
        ? selected
        : ApplicationTheme.System;

    public void SetOperationsEnabled(bool enabled)
    {
        clone.Enabled = create.Enabled = useExisting.Enabled = package.Enabled = restore.Enabled = save.Enabled = enabled;
    }

    private GroupBox BuildAppearanceGroup()
    {
        var table = SettingsTable();
        AddRow(table, 0, "Tema:", theme);
        AddRow(table, 1, "Saída legada:", legacyEnabled);
        var path = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        path.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        legacyDirectory.Dock = DockStyle.Fill;
        path.Controls.Add(legacyDirectory, 0, 0);
        path.Controls.Add(browseLegacy, 1, 0);
        AddRow(table, 2, "Pasta CSV/TXT:", path);
        table.Controls.Add(save, 1, 3);
        return Group("Aparência e saída contínua", table);
    }

    private GroupBox BuildStorageGroup()
    {
        var table = SettingsTable();
        AddRow(table, 0, "Banco atual:", new Label { Text = store.Descriptor.DatabasePath, AutoSize = true });
        AddRow(table, 1, "Identidade:", new Label { Text = store.Descriptor.StoreId.ToString("D"), AutoSize = true });
        AddRow(table, 2, "Escopo:", new Label { Text = store.Descriptor.Scope.ToString(), AutoSize = true });
        AddRow(table, 3, "Tamanho:", new Label { Text = FormatSize(new FileInfo(store.Descriptor.DatabasePath).Length), AutoSize = true });
        AddRow(table, 4, "Novo escopo:", targetScope);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.AddRange(new Control[] { clone, create, useExisting, openFolder });
        AddRow(table, 5, "Ações:", actions);
        table.Controls.Add(new Label
        {
            Text = "A troca é ativada na próxima inicialização. Nenhum banco anterior é apagado ou mesclado.",
            AutoSize = true,
            Margin = new Padding(3, 10, 3, 6)
        }, 0, 6);
        table.SetColumnSpan(table.GetControlFromPosition(0, 6)!, 2);
        return Group("Armazenamento", table);
    }

    private GroupBox BuildPackageGroup()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        panel.Controls.Add(package);
        panel.Controls.Add(includeExecutable);
        panel.Controls.Add(restore);
        return Group("Pacote portátil", panel);
    }

    private async Task SaveAsync()
    {
        try
        {
            var settings = new ApplicationSettings(
                theme.SelectedItem is ApplicationTheme selected ? selected : ApplicationTheme.System,
                legacyEnabled.Checked,
                legacyDirectory.Text.Trim());
            await settingsRepository.SaveAsync(settings);
            currentSettings = settings;
            SettingsSaved?.Invoke(settings);
        }
        catch (Exception exception) when (exception is ArgumentException or ApplicationStoreException)
        {
            MessageBox.Show(this, exception.Message, "Configurações", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task CreateOrCloneAsync(bool copyCurrent)
    {
        try
        {
            var scope = targetScope.SelectedItem is StorageScope selected ? selected : StorageScope.LocalUser;
            var directory = await ResolveTargetDirectoryAsync(scope);
            if (directory is null)
                return;
            var selectedStore = copyCurrent
                ? await migration.CloneAsync(store, directory, scope)
                : await migration.CreateEmptyAsync(directory, scope);
            Activate(selectedStore.Descriptor);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or ApplicationStoreException)
        {
            MessageBox.Show(this, exception.Message, "Armazenamento", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task<string?> ResolveTargetDirectoryAsync(StorageScope scope)
    {
        var locations = StorageLocations.CreateDefault();
        if (scope == StorageScope.SharedMachine)
            await new SharedMachineStorageElevator().PrepareDirectoryAsync();
        if (scope == StorageScope.Custom)
        {
            using var dialog = new FolderBrowserDialog { Description = "Escolha uma pasta vazia para o armazenamento" };
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
        }
        var databasePath = scope switch
        {
            StorageScope.LocalUser => locations.LocalDatabasePath,
            StorageScope.SharedMachine => locations.SharedDatabasePath,
            _ => locations.PortableDatabasePath
        };
        return Path.GetDirectoryName(databasePath);
    }

    private async Task UseExistingAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Banco do DB Connection Tester (data.db)|data.db|SQLite (*.db)|*.db" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            var selected = await StorageMigrationService.UseExistingAsync(dialog.FileName);
            Activate(selected.Descriptor);
        }
        catch (Exception exception) when (exception is ApplicationStoreException or IOException)
        {
            MessageBox.Show(this, exception.Message, "Armazenamento", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task CreatePackageAsync()
    {
        using var dialog = new SaveFileDialog { Filter = "Pacote portátil (*.zip)|*.zip", FileName = $"DBConnectionTester-portable-{DateTime.Now:yyyyMMdd}.zip" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            await new PortablePackageService(store).CreateAsync(dialog.FileName, includeExecutable.Checked);
            MessageBox.Show(this, "Pacote portátil criado com sucesso.", "Pacote portátil", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ApplicationStoreException)
        {
            MessageBox.Show(this, exception.Message, "Pacote portátil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RestorePackageAsync()
    {
        using var source = new OpenFileDialog { Filter = "Pacote portátil (*.zip)|*.zip" };
        if (source.ShowDialog(this) != DialogResult.OK)
            return;
        using var destination = new FolderBrowserDialog { Description = "Escolha uma pasta vazia para restaurar o pacote" };
        if (destination.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            var restored = await new PortablePackageService(store).RestoreAsync(source.FileName, destination.SelectedPath);
            Activate(restored.Store);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ApplicationStoreException)
        {
            MessageBox.Show(this, exception.Message, "Restaurar pacote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Activate(StoreDescriptor descriptor)
    {
        preferences.Write(new StoragePreference(descriptor.StoreId, descriptor.DatabasePath, descriptor.Scope, null));
        StorageSelected?.Invoke(descriptor);
        MessageBox.Show(this, "O armazenamento foi selecionado. Reinicie o aplicativo para concluir a troca.",
            "Armazenamento", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void LoadSettings(ApplicationSettings settings)
    {
        theme.SelectedIndex = Array.IndexOf(Enum.GetValues<ApplicationTheme>(), settings.Theme);
        legacyEnabled.Checked = settings.LegacyOutputEnabled;
        legacyDirectory.Text = settings.LegacyOutputDirectory;
    }

    private void BrowseLegacyDirectory()
    {
        using var dialog = new FolderBrowserDialog { InitialDirectory = legacyDirectory.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            legacyDirectory.Text = dialog.SelectedPath;
    }

    private static TableLayoutPanel SettingsTable()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static void AddRow(TableLayoutPanel table, int row, string caption, Control value)
    {
        table.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        value.Dock = DockStyle.Fill;
        table.Controls.Add(value, 1, row);
    }

    private static GroupBox Group(string title, Control content)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(3, 4, 3, 12) };
        group.Controls.Add(content);
        return group;
    }

    private static string FormatSize(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024d:0.0} KiB"
        : $"{bytes / 1024d / 1024d:0.0} MiB";
}
