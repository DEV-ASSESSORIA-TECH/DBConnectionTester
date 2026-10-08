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
    private bool operationsEnabled = true;
    private bool operationPending;
    private bool decisionPending;
    private bool loadingSettings;
    internal Func<DialogResult>? EditDecision;
    private readonly Label settingsState = new() { Visible = false, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(3, 6, 3, 3) };
    private readonly Label operationStatus = new() { Visible = false, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(3, 4, 3, 8) };
    internal Action<string, string, bool>? MessageReporter;
    public event Action<bool>? BusyChanged;
    internal bool IsBusy => operationPending || decisionPending;
    internal bool HasUnsavedChanges => ReadSettings() != currentSettings;


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
        root.Controls.Add(operationStatus);
        root.Controls.Add(BuildAppearanceGroup());
        root.Controls.Add(BuildStorageGroup());
        root.Controls.Add(BuildPackageGroup());
        Controls.Add(root);
        operationStatus.TextChanged += (_, _) => operationStatus.Visible = operationStatus.Text.Length > 0;
        settingsState.TextChanged += (_, _) => settingsState.Visible = settingsState.Text.Length > 0;
        LoadSettings(settings);
        theme.SelectedIndexChanged += (_, _) => UpdateEditState();
        legacyEnabled.CheckedChanged += (_, _) => UpdateEditState();
        legacyDirectory.TextChanged += (_, _) => UpdateEditState();

        browseLegacy.Click += async (_, _) => await RunOperationAsync("Selecionando pasta…", () => Task.FromResult(BrowseLegacyDirectory()));
        save.Click += async (_, _) => await SaveAsync();
        clone.Click += async (_, _) => await CreateOrCloneAsync(copyCurrent: true);
        create.Click += async (_, _) => await CreateOrCloneAsync(copyCurrent: false);
        useExisting.Click += async (_, _) => await UseExistingAsync();
        package.Click += async (_, _) => await CreatePackageAsync();
        restore.Click += async (_, _) => await RestorePackageAsync();
        openFolder.Click += async (_, _) => await RunOperationAsync("Abrindo pasta…", () =>
        {
            Process.Start(new ProcessStartInfo("explorer.exe", Path.GetDirectoryName(store.Descriptor.DatabasePath)!) { UseShellExecute = true });
            return Task.FromResult(true);
        });
    }

    public event Action<ApplicationSettings>? SettingsSaved;
    public event Action<StoreDescriptor>? StorageSelected;
    internal ApplicationTheme SelectedTheme => theme.SelectedItem is ApplicationTheme selected
        ? selected
        : ApplicationTheme.System;

    public void SetOperationsEnabled(bool enabled)
    {
        operationsEnabled = enabled;
        UpdateOperationState();
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
        table.Controls.Add(settingsState, 1, 4);
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

    internal Task<bool> SaveAsync(bool fromDecision = false) => RunOperationAsync("Salvando configurações…", async () =>
    {
        var settings = ReadSettings();
        if (settings.LegacyOutputEnabled)
        {
            if (string.IsNullOrWhiteSpace(settings.LegacyOutputDirectory)) throw new ArgumentException("Informe a pasta para a saída CSV/TXT contínua.");
            settings = settings with { LegacyOutputDirectory = Path.GetFullPath(settings.LegacyOutputDirectory) };
            if (File.Exists(settings.LegacyOutputDirectory)) throw new IOException("A pasta CSV/TXT escolhida está ocupada por um arquivo.");
        }
        await Task.Run(() => settingsRepository.SaveAsync(settings));
        currentSettings = settings;
        LoadSettings(settings);
        settingsState.Text = "Configurações salvas.";
        SettingsSaved?.Invoke(settings);
        operationStatus.Text = "Configurações salvas.";
        return true;
    }, allowDecision: fromDecision);

    private Task<bool> CreateOrCloneAsync(bool copyCurrent) => RunOperationAsync(
        copyCurrent ? "Copiando armazenamento…" : "Criando armazenamento…", async () =>
    {
        var scope = targetScope.SelectedItem is StorageScope selected ? selected : StorageScope.LocalUser;
        var directory = await ResolveTargetDirectoryAsync(scope);
        if (directory is null) return false;
        var selectedStore = await Task.Run(() => copyCurrent
            ? migration.CloneAsync(store, directory, scope)
            : migration.CreateEmptyAsync(directory, scope));
        await ActivateAsync(selectedStore.Descriptor);
        return true;
    });

    private async Task<string?> ResolveTargetDirectoryAsync(StorageScope scope)
    {
        var locations = StorageLocations.CreateDefault();
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
        var directory = Path.GetDirectoryName(databasePath)!;
        if (string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetDirectoryName(store.Descriptor.DatabasePath)!.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new IOException("O destino é a pasta do armazenamento atual. Escolha outro escopo ou uma pasta personalizada vazia.");
        if (scope == StorageScope.SharedMachine)
            await new SharedMachineStorageElevator().PrepareDirectoryAsync();
        return directory;
    }

    private Task<bool> UseExistingAsync() => RunOperationAsync("Verificando armazenamento…", async () =>
    {
        using var dialog = new OpenFileDialog { Filter = "Banco do DB Connection Tester (data.db)|data.db|SQLite (*.db)|*.db" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        var path = dialog.FileName;
        var selected = await Task.Run(() => StorageMigrationService.UseExistingAsync(path));
        await ActivateAsync(selected.Descriptor);
        return true;
    });

    private Task<bool> CreatePackageAsync() => RunOperationAsync("Criando pacote portátil…", async () =>
    {
        using var dialog = new SaveFileDialog { Filter = "Pacote portátil (*.zip)|*.zip", FileName = $"DBConnectionTester-portable-{DateTime.Now:yyyyMMdd}.zip" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        var path = dialog.FileName;
        var include = includeExecutable.Checked;
        await Task.Run(() => new PortablePackageService(store).CreateAsync(path, include));
        ReportMessage("Pacote portátil criado com sucesso.", "Pacote portátil", false);
        return true;
    });

    private Task<bool> RestorePackageAsync() => RunOperationAsync("Restaurando pacote…", async () =>
    {
        using var source = new OpenFileDialog { Filter = "Pacote portátil (*.zip)|*.zip" };
        if (source.ShowDialog(this) != DialogResult.OK) return false;
        using var destination = new FolderBrowserDialog { Description = "Escolha uma pasta vazia para restaurar o pacote" };
        if (destination.ShowDialog(this) != DialogResult.OK) return false;
        var path = source.FileName;
        var directory = destination.SelectedPath;
        var restored = await Task.Run(() => new PortablePackageService(store).RestoreAsync(path, directory));
        await ActivateAsync(restored.Store);
        return true;
    });

    private async Task ActivateAsync(StoreDescriptor descriptor)
    {
        await Task.Run(() => preferences.Write(new StoragePreference(descriptor.StoreId, descriptor.DatabasePath, descriptor.Scope, null)));
        StorageSelected?.Invoke(descriptor);
        ReportMessage("O armazenamento foi selecionado. Reinicie o aplicativo para concluir a troca.", "Armazenamento", false);
    }

    private void UpdateOperationState()
    {
        var enabled = operationsEnabled && !IsBusy;
        foreach (var control in new Control[] { clone, create, useExisting, package, restore, save, openFolder,
            theme, legacyEnabled, legacyDirectory, browseLegacy, targetScope, includeExecutable }) control.Enabled = enabled;
        BusyChanged?.Invoke(IsBusy);
    }

    internal async Task<bool> RunOperationAsync(string status, Func<Task<bool>> operation, bool allowDecision = false)
    {
        if (!operationsEnabled || operationPending || (decisionPending && !allowDecision) || IsDisposed) return false;
        operationPending = true;
        operationStatus.Text = status;
        UpdateOperationState();
        try
        {
            var result = await operation();
            if (!IsDisposed && operationStatus.Text == status) operationStatus.Text = result ? "Operação concluída." : "Operação cancelada.";
            return result;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ApplicationStoreException
            or System.ComponentModel.Win32Exception or System.Security.SecurityException or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException)
        {
            if (!IsDisposed)
            {
                if (exception is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 })
                    operationStatus.Text = "Operação cancelada.";
                else
                {
                    operationStatus.Text = "Não foi possível concluir a operação.";
                    var message = exception switch
                    {
                        UnauthorizedAccessException or System.Security.SecurityException => "Sem permissão para acessar o arquivo, pasta ou preferência selecionada. O armazenamento atual continua ativo.",
                        System.Text.Json.JsonException => "O pacote contém um manifesto inválido e não pode ser restaurado.",
                        _ => exception.Message
                    };
                    ReportMessage(message, "Configurações", true);
                }
            }
            return false;
        }
        finally
        {
            operationPending = false;
            if (!IsDisposed) UpdateOperationState();
        }
    }

    private void ReportMessage(string message, string title, bool error)
    {
        if (MessageReporter is { } reporter) reporter(message, title, error);
        else MessageBox.Show(this, message, title, MessageBoxButtons.OK, error ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    private ApplicationSettings ReadSettings() => new(SelectedTheme, legacyEnabled.Checked, legacyDirectory.Text.Trim());

    private void UpdateEditState()
    {
        if (!loadingSettings) settingsState.Text = HasUnsavedChanges ? "Alterações não salvas." : "";
    }

    internal async Task<bool> TryLeaveAsync()
    {
        if (IsDisposed || IsBusy) return false;
        if (!HasUnsavedChanges) return true;
        decisionPending = true;
        UpdateOperationState();
        try
        {
            var choice = EditDecision?.Invoke() ?? MessageBox.Show(this,
                "Há configurações não salvas.\n\nSim: salvar e continuar.\nNão: descartar e continuar.\nCancelar: continuar editando.",
                "Configurações não salvas", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
            if (choice == DialogResult.Yes) return await SaveAsync(fromDecision: true);
            if (choice != DialogResult.No) return false;
            LoadSettings(currentSettings);
            settingsState.Text = "";
            return true;
        }
        finally { decisionPending = false; if (!IsDisposed) UpdateOperationState(); }
    }

    private void LoadSettings(ApplicationSettings settings)
    {
        loadingSettings = true;
        try
        {
            theme.SelectedIndex = Array.IndexOf(Enum.GetValues<ApplicationTheme>(), settings.Theme);
            legacyEnabled.Checked = settings.LegacyOutputEnabled;
            legacyDirectory.Text = settings.LegacyOutputDirectory;
        }
        finally { loadingSettings = false; }
    }

    private bool BrowseLegacyDirectory()
    {
        using var dialog = new FolderBrowserDialog { InitialDirectory = legacyDirectory.Text };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        legacyDirectory.Text = dialog.SelectedPath;
        return true;
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
