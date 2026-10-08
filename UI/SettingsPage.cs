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
    private readonly Control[] layoutContainers;
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
    private readonly CheckBox includeExecutable = new() { Text = "Incluir aplicativo", AutoSize = true };
    private readonly TextBox currentDatabasePath = new() { ReadOnly = true, AccessibleName = "Caminho do banco atual" };
    private readonly Label storageSize = new() { AutoSize = true };
    private readonly TextBox targetPath = new() { ReadOnly = true, AccessibleName = "Destino previsto" };
    private readonly Label targetDescription = new() { AutoSize = true, Dock = DockStyle.Top };
    private readonly Label pendingStorage = new() { AutoSize = true, Dock = DockStyle.Top, Visible = false, Margin = new Padding(3, 10, 3, 6) };
    private readonly bool executableAvailable = new CurrentApplicationBinaryProvider().GetSingleFileExecutablePath() is { } executable && File.Exists(executable);
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
        theme.FormattingEnabled = true;
        theme.Format += (_, e) => e.Value = e.ListItem switch
        { ApplicationTheme.Light => "Claro", ApplicationTheme.Dark => "Escuro", _ => "Sistema" };
        targetScope.FormattingEnabled = true;
        targetScope.Format += (_, e) => e.Value = e.ListItem is StorageScope scope ? ScopeName(scope) : "";
        targetScope.SelectedItem = store.Descriptor.Scope;
        targetScope.SelectedIndexChanged += (_, _) => UpdateTargetDescription();

        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        root.Controls.Add(new Label { Text = "Configurações", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold), Margin = new Padding(3, 3, 3, 16) });
        root.Controls.Add(operationStatus);
        root.Controls.Add(BuildAppearanceGroup());
        root.Controls.Add(BuildStorageGroup());
        root.Controls.Add(BuildPackageGroup());
        Controls.Add(root);
        UpdateTargetDescription();
        operationStatus.TextChanged += (_, _) => operationStatus.Visible = operationStatus.Text.Length > 0;
        settingsState.TextChanged += (_, _) => settingsState.Visible = settingsState.Text.Length > 0;
        LoadSettings(settings);
        VisibleChanged += (_, _) => { if (Visible) RefreshStorageSize(); };
        theme.SelectedIndexChanged += (_, _) => UpdateEditState();
        legacyEnabled.CheckedChanged += (_, _) => { UpdateEditState(); UpdateOperationState(); };
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
        layoutContainers = LayoutContainers(this).ToArray();
    }

    // Visibility changes otherwise remeasure each nested AutoSize container repeatedly.
    // Keep native layout active after the batch so resize, DPI and wrapped text still update.
    internal void BatchLayout(Action change)
    {
        foreach (var container in layoutContainers) container.SuspendLayout();
        try { change(); }
        finally
        {
            for (var index = layoutContainers.Length - 1; index >= 0; index--)
                layoutContainers[index].ResumeLayout(true);
        }
    }

    private static IEnumerable<Control> LayoutContainers(Control root)
    {
        if (root is UserControl or GroupBox or TableLayoutPanel or FlowLayoutPanel) yield return root;
        foreach (Control child in root.Controls)
            foreach (var container in LayoutContainers(child)) yield return container;
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
        AddRow(table, 1, "Tema:", theme);
        AddNote(table, 2, "Exportação automática", bold: true);
        AddRow(table, 3, "CSV e TXT:", legacyEnabled);
        var path = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        path.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        legacyDirectory.Dock = DockStyle.Top;
        path.Controls.Add(legacyDirectory, 0, 0);
        path.Controls.Add(browseLegacy, 1, 0);
        AddRow(table, 4, "Pasta de destino:", path);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        save.Font = new Font(Font, FontStyle.Bold);
        settingsState.Dock = DockStyle.None;
        actions.Controls.AddRange(new Control[] { save, settingsState });
        AddWide(table, 5, actions);
        return Group("Preferências", table);
    }

    private Control BuildStorageGroup()
    {
        var table = SettingsTable();
        currentDatabasePath.Text = store.Descriptor.DatabasePath;
        AddRow(table, 0, "Localização:", new Label { Text = ScopeName(store.Descriptor.Scope), AutoSize = true });
        AddRow(table, 1, "Banco atual:", currentDatabasePath);
        AddRow(table, 2, "Tamanho em disco:", storageSize);
        RefreshStorageSize();
        openFolder.Dock = DockStyle.None;
        var detailsButton = new Button { Text = "Mostrar detalhes", AutoSize = true };
        var details = new TextBox { ReadOnly = true, Text = store.Descriptor.StoreId.ToString("D"), Dock = DockStyle.Top, Visible = false,
            AccessibleName = "Identidade do armazenamento", Margin = new Padding(3, 4, 3, 4) };
        detailsButton.Click += (_, _) =>
        {
            details.Visible = !details.Visible;
            table.RowStyles[4].SizeType = details.Visible ? SizeType.AutoSize : SizeType.Absolute;
            table.RowStyles[4].Height = 0;
            detailsButton.Text = details.Visible ? "Ocultar detalhes" : "Mostrar detalhes";
        };
        var storageActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        storageActions.Controls.AddRange(new Control[] { openFolder, detailsButton });
        AddRow(table, 3, "Ações:", storageActions);
        AddWide(table, 4, details);
        table.RowStyles[4].SizeType = SizeType.Absolute;
        table.RowStyles[4].Height = 0;
        var destination = SettingsTable();
        AddRow(destination, 0, "Localização de destino:", targetScope);
        AddRow(destination, 1, "Destino previsto:", targetPath);
        AddWide(destination, 2, targetDescription);
        AddWide(destination, 3, StorageAction(clone, "Copia histórico, perfis e configurações para o destino vazio."));
        AddWide(destination, 4, StorageAction(create, "Cria um banco sem histórico ou perfis, com configurações padrão."));
        AddWide(destination, 5, StorageAction(useExisting, "Escolhe um banco compatível já existente; a localização acima não se aplica."));
        AddNote(destination, 6, "A troca será aplicada após reiniciar. O banco atual é preservado.");
        AddWide(destination, 7, pendingStorage);
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(Group("Armazenamento atual", table));
        layout.Controls.Add(Group("Trocar armazenamento", destination));
        return layout;
    }

    private GroupBox BuildPackageGroup()
    {
        var table = SettingsTable();
        AddNote(table, 0, "O pacote contém histórico, perfis e configurações do banco atual.");
        var creation = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        creation.Controls.AddRange(new Control[] { package, includeExecutable });
        AddWide(table, 1, creation);
        if (!executableAvailable) AddNote(table, 2, "Incluir aplicativo está disponível na versão publicada como executável único.");
        AddWide(table, 3, StorageAction(restore, "Escolha um pacote e uma pasta vazia. O banco atual será preservado."));
        return Group("Pacote portátil", table);
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
        pendingStorage.Text = $"Será usado após reiniciar: {ScopeName(descriptor.Scope)}\n{descriptor.DatabasePath}";
        pendingStorage.Visible = true;
        StorageSelected?.Invoke(descriptor);
        ReportMessage("O armazenamento foi selecionado. Reinicie o aplicativo para concluir a troca.", "Armazenamento", false);
    }

    private void UpdateOperationState()
    {
        var enabled = operationsEnabled && !IsBusy;
        foreach (var control in new Control[] { clone, create, useExisting, package, restore, save, openFolder,
            theme, legacyEnabled, legacyDirectory, browseLegacy, targetScope, includeExecutable }) control.Enabled = enabled;
        legacyDirectory.Enabled = browseLegacy.Enabled = enabled && legacyEnabled.Checked;
        includeExecutable.Enabled = enabled && executableAvailable;
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
        finally { loadingSettings = false; UpdateOperationState(); }
    }

    private bool BrowseLegacyDirectory()
    {
        using var dialog = new FolderBrowserDialog { InitialDirectory = legacyDirectory.Text };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        legacyDirectory.Text = dialog.SelectedPath;
        return true;
    }

    private void UpdateTargetDescription()
    {
        var scope = targetScope.SelectedItem is StorageScope selected ? selected : StorageScope.LocalUser;
        var locations = StorageLocations.CreateDefault();
        targetPath.Text = scope switch
        {
            StorageScope.LocalUser => locations.LocalDatabasePath,
            StorageScope.SharedMachine => locations.SharedDatabasePath,
            StorageScope.Portable => locations.PortableDatabasePath,
            _ => "A pasta será escolhida ao copiar ou criar o banco."
        };
        targetDescription.Text = scope switch
        {
            StorageScope.SharedMachine => "Disponível aos usuários deste computador. Pode solicitar autorização do Windows.",
            StorageScope.Portable => "O banco fica na pasta do aplicativo, para uso portátil.",
            StorageScope.Custom => "Escolha uma pasta vazia para criar ou copiar o armazenamento.",
            _ => "O banco fica na pasta de dados do seu usuário do Windows."
        };
        if (scope != StorageScope.Custom && string.Equals(Path.GetFullPath(targetPath.Text), store.Descriptor.DatabasePath, StringComparison.OrdinalIgnoreCase))
            targetDescription.Text += " Este é o banco atual; escolha outro destino para copiar ou criar.";
    }

    private static Control StorageAction(Button button, string description)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(3, 4, 3, 4) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        button.Dock = DockStyle.None;
        table.Controls.Add(button, 0, 0);
        table.Controls.Add(new Label { Text = description, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(8, 6, 3, 2) }, 1, 0);
        return table;
    }

    private void RefreshStorageSize()
    {
        long bytes = 0;
        foreach (var path in new[] { store.Descriptor.DatabasePath, store.Descriptor.DatabasePath + "-wal", store.Descriptor.DatabasePath + "-shm" })
        {
            try { var info = new FileInfo(path); if (info.Exists) bytes += info.Length; }
            catch (IOException) { storageSize.Text = "Indisponível"; return; }
            catch (UnauthorizedAccessException) { storageSize.Text = "Indisponível"; return; }
        }
        storageSize.Text = FormatSize(bytes);
    }

    private static string ScopeName(StorageScope scope) => scope switch
    {
        StorageScope.LocalUser => "Usuário atual",
        StorageScope.Portable => "Portátil",
        StorageScope.SharedMachine => "Compartilhado neste computador",
        _ => "Pasta personalizada"
    };

    private static TableLayoutPanel SettingsTable()
    {
        var table = UiLayout.Fields(140);
        table.Padding = new Padding(4);
        return table;
    }

    private static void AddRow(TableLayoutPanel table, int row, string caption, Control value)
    {
        var field = UiLayout.AddField(table, row, caption, value);
        field.Label.Margin = new Padding(3, 3, 6, 3);
        value.Margin = new Padding(3, 2, 3, 2);
    }

    private static void AddWide(TableLayoutPanel table, int row, Control control)
    {
        table.RowCount = Math.Max(table.RowCount, row + 1);
        while (table.RowStyles.Count <= row) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
    }

    private static void AddNote(TableLayoutPanel table, int row, string text, bool bold = false)
    {
        var label = new Label { Text = text, Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(3, bold ? 6 : 3, 3, 4) };
        if (bold) label.Font = new Font(table.Font, FontStyle.Bold);
        AddWide(table, row, label);
    }

    private static GroupBox Group(string title, Control content)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 3, 6, 18), Margin = new Padding(3, 4, 3, 12) };
        group.Controls.Add(content);
        return group;
    }

    private static string FormatSize(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024d:0.0} KiB"
        : $"{bytes / 1024d / 1024d:0.0} MiB";
}
