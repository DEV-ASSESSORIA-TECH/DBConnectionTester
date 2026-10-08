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
    private readonly Button clone = new() { Text = "Copiar banco atual…", AutoSize = true };
    private readonly Button create = new() { Text = "Criar banco vazio…", AutoSize = true };
    private readonly Button useExisting = new() { Text = "Escolher…", AutoSize = true };
    private readonly Button package = new() { Text = "Criar pacote portátil", AutoSize = true };
    private readonly Button restore = new() { Text = "Restaurar pacote", AutoSize = true };
    private readonly Button openFolder = new() { Text = "Abrir pasta", AutoSize = true };
    private readonly CheckBox includeExecutable = new() { Text = "Incluir aplicativo", AutoSize = true };
    private readonly TextBox currentDatabasePath = new() { ReadOnly = true, AccessibleName = "Caminho do banco atual" };
    private readonly Label bankDetails = new() { AutoSize = true, Dock = DockStyle.Top, Visible = false };
    private readonly Label copyDescription = new() { AutoSize = true, Dock = DockStyle.Top, Text = "Inclui tema e exportação automática. Sem copiar, o destino mantém suas preferências." };
    private readonly Label storageSize = new() { AutoSize = true };
    private readonly Label bankInfo = new() { AutoSize = true, Dock = DockStyle.Top };
    private readonly CheckBox copyPreferences = new() { Text = "Copiar preferências atuais para o banco selecionado", AutoSize = true };
    private readonly Button discard = new() { Text = "Descartar alterações", AutoSize = true };
    private readonly Button cancelSwitch = new() { Text = "Cancelar troca pendente", AutoSize = true, Visible = false };
    private StoreDescriptor selectedBank;
    private StoreDescriptor savedBank;
    private bool savedCopyPreferences;
    private bool clonedBank;
    private readonly SettingsCommitService settingsCommit = new();
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
    internal bool HasUnsavedChanges => ReadSettings() != currentSettings || !SameBank(selectedBank, savedBank) || copyPreferences.Checked != savedCopyPreferences;


    public SettingsPage(
        SqliteApplicationStore store,
        ApplicationSettings settings,
        IApplicationSettingsRepository settingsRepository,
        IStoragePreferenceStore preferences)
    {
        this.store = store;
        currentSettings = settings;
        selectedBank = savedBank = store.Descriptor;
        this.settingsRepository = settingsRepository;
        this.preferences = preferences;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        Padding = new Padding(24);
        theme.Items.AddRange(Enum.GetValues<ApplicationTheme>().Cast<object>().ToArray());
        theme.FormattingEnabled = true;
        theme.Format += (_, e) => e.Value = e.ListItem switch
        { ApplicationTheme.Light => "Claro", ApplicationTheme.Dark => "Escuro", _ => "Sistema" };
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        root.Controls.Add(new Label { Text = "Configurações", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold), Margin = new Padding(3, 3, 3, 16) });
        root.Controls.Add(operationStatus);
        root.Controls.Add(BuildAppearanceGroup());
        root.Controls.Add(BuildStorageGroup());
        root.Controls.Add(BuildSaveArea());
        root.Controls.Add(BuildPackageGroup());
        Controls.Add(root);
        UpdateBankState();
        operationStatus.TextChanged += (_, _) => operationStatus.Visible = operationStatus.Text.Length > 0;
        settingsState.TextChanged += (_, _) => settingsState.Visible = settingsState.Text.Length > 0;
        LoadSettings(settings);
        VisibleChanged += (_, _) => { if (Visible) RefreshStorageSize(); };
        theme.SelectedIndexChanged += (_, _) => UpdateEditState();
        legacyEnabled.CheckedChanged += (_, _) => { UpdateEditState(); UpdateOperationState(); };
        legacyDirectory.TextChanged += (_, _) => UpdateEditState();
        copyPreferences.CheckedChanged += (_, _) => UpdateEditState();
        discard.Click += (_, _) => DiscardChanges();
        cancelSwitch.Click += async (_, _) => await CancelSwitchAsync();

        browseLegacy.Click += async (_, _) => await RunOperationAsync("Selecionando pasta…", () => Task.FromResult(BrowseLegacyDirectory()));
        save.Click += async (_, _) => await SaveAsync();
        clone.Click += async (_, _) => await CreateOrCloneAsync(copyCurrent: true);
        create.Click += async (_, _) => await CreateOrCloneAsync(copyCurrent: false);
        useExisting.Click += async (_, _) => await UseExistingAsync();
        package.Click += async (_, _) => await CreatePackageAsync();
        restore.Click += async (_, _) => await RestorePackageAsync();
        openFolder.Click += async (_, _) => await RunOperationAsync("Abrindo pasta…", () =>
        {
            Process.Start(new ProcessStartInfo("explorer.exe", Path.GetDirectoryName(selectedBank.DatabasePath)!) { UseShellExecute = true });
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
        return Group("Preferências", table);
    }

    private Control BuildSaveArea()
    {
        var table = SettingsTable();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        save.Font = new Font(Font, FontStyle.Bold);
        settingsState.Dock = DockStyle.None;
        actions.Controls.AddRange(new Control[] { save, discard, settingsState });
        AddWide(table, 0, actions);
        AddNote(table, 1, "As preferências permanecem no banco de dados. A cópia para o destino é opcional.");
        return table;
    }

    private Control BuildStorageGroup()
    {
        var table = SettingsTable();
        var path = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        path.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        currentDatabasePath.AccessibleName = "Caminho do banco selecionado";
        currentDatabasePath.Dock = DockStyle.Top;
        path.Controls.Add(currentDatabasePath, 0, 0);
        path.Controls.Add(useExisting, 1, 0);
        AddRow(table, 0, "Caminho:", path);
        AddWide(table, 1, bankInfo);
        var detailsButton = new Button { Text = "Mostrar detalhes", AutoSize = true };
        var details = bankDetails;
        detailsButton.Click += (_, _) =>
        {
            details.Visible = !details.Visible;
            details.Text = $"Identidade: {selectedBank.StoreId:D} · Tamanho em disco: {storageSize.Text}";
            table.RowStyles[3].SizeType = details.Visible ? SizeType.AutoSize : SizeType.Absolute;
            table.RowStyles[3].Height = 0;
            detailsButton.Text = details.Visible ? "Ocultar detalhes" : "Mostrar detalhes";
        };
        var information = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        information.Controls.AddRange(new Control[] { openFolder, detailsButton });
        AddWide(table, 2, information);
        AddWide(table, 3, details);
        table.RowStyles[3].SizeType = SizeType.Absolute; table.RowStyles[3].Height = 0;
        AddWide(table, 4, copyPreferences);
        AddWide(table, 5, copyDescription);
        var preparation = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        preparation.Controls.AddRange(new Control[] { clone, create });
        AddWide(table, 6, preparation);
        AddNote(table, 7, "Essas ações preparam outro banco para seleção. A troca será aplicada ao reiniciar, após salvar.");
        AddWide(table, 8, pendingStorage);
        AddWide(table, 9, cancelSwitch);
        return Group("Banco de dados", table);
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
        var target = selectedBank;
        var copy = copyPreferences.Checked && !SameBank(target, store.Descriptor);
        await Task.Run(() => settingsCommit.SaveAsync(store, settingsRepository, currentSettings, settings, target, copy, preferences));
        currentSettings = settings;
        savedBank = selectedBank;
        savedCopyPreferences = copyPreferences.Checked;
        LoadSettings(settings);
        UpdateBankState();
        settingsState.Text = "Configurações salvas.";
        SettingsSaved?.Invoke(settings);
        StorageSelected?.Invoke(savedBank);
        operationStatus.Text = "Configurações salvas.";
        return true;
    }, allowDecision: fromDecision);

    private Task<bool> CreateOrCloneAsync(bool copyCurrent) => RunOperationAsync(
        copyCurrent ? "Copiando armazenamento…" : "Criando armazenamento…", async () =>
    {
        using var dialog = new FolderBrowserDialog { Description = "Escolha uma pasta vazia para preparar o banco" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        var directory = dialog.SelectedPath;
        var selectedStore = await Task.Run(() => copyCurrent
            ? migration.CloneAsync(store, directory, StorageScope.Custom)
            : migration.CreateEmptyAsync(directory, StorageScope.Custom));
        SetSelectedBank(selectedStore.Descriptor, includesPreferences: copyCurrent);
        return true;
    });

    private Task<bool> UseExistingAsync() => RunOperationAsync("Verificando armazenamento…", async () =>
    {
        using var dialog = new OpenFileDialog { Filter = "Banco do DB Connection Tester (data.db)|data.db|SQLite (*.db)|*.db" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        var path = dialog.FileName;
        var selected = await Task.Run(() => SqliteApplicationStore.InspectAsync(path));
        if (!selected.IsCompatible) throw new ApplicationStoreException($"O banco selecionado não é compatível: {selected.ErrorMessage}");
        SetSelectedBank(selected.Descriptor!);
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
        SetSelectedBank(restored.Store);
        ReportMessage("Pacote restaurado. Salve as configurações para confirmar a troca de banco.", "Pacote portátil", false);
        return true;
    });

    internal void SetSelectedBank(StoreDescriptor descriptor, bool includesPreferences = false)
    {
        selectedBank = descriptor;
        clonedBank = includesPreferences;
        copyPreferences.Checked = includesPreferences;
        UpdateBankState();
        UpdateEditState();
    }

    internal Task<bool> SelectBankAsync(string path) => RunOperationAsync("Verificando banco…", async () =>
    {
        var inspection = await Task.Run(() => SqliteApplicationStore.InspectAsync(path));
        if (!inspection.IsCompatible) throw new ApplicationStoreException($"O banco selecionado não é compatível: {inspection.ErrorMessage}");
        SetSelectedBank(inspection.Descriptor!);
        return true;
    });

    internal Task<bool> CancelSwitchAsync() => RunOperationAsync("Cancelando troca…", async () =>
    {
        await Task.Run(() => preferences.Write(new StoragePreference(store.Descriptor.StoreId, store.Descriptor.DatabasePath, store.Descriptor.Scope, preferences.Read()?.ObservedPortableStoreId)));
        var keepDraft = !SameBank(selectedBank, savedBank);
        savedBank = store.Descriptor;
        savedCopyPreferences = false;
        if (!keepDraft) { selectedBank = store.Descriptor; clonedBank = false; copyPreferences.Checked = false; }
        UpdateBankState(); UpdateEditState();
        StorageSelected?.Invoke(store.Descriptor);
        return true;
    });

    internal void DiscardChanges()
    {
        if (IsBusy) return;
        LoadSettings(currentSettings);
        selectedBank = savedBank; clonedBank = false;
        copyPreferences.Checked = savedCopyPreferences;
        UpdateBankState(); UpdateEditState();
    }

    private static bool SameBank(StoreDescriptor a, StoreDescriptor b) => a.StoreId == b.StoreId
        && string.Equals(a.DatabasePath, b.DatabasePath, StringComparison.OrdinalIgnoreCase);

    private void UpdateBankState()
    {
        currentDatabasePath.Text = selectedBank.DatabasePath;
        var different = !SameBank(selectedBank, store.Descriptor);
        bankInfo.Text = $"Localização: {ScopeName(selectedBank.Scope)} · Banco compatível";
        if (!SameBank(selectedBank, savedBank)) bankInfo.Text += " · Troca não salva";
        copyPreferences.Visible = copyDescription.Visible = different;
        copyPreferences.Text = clonedBank ? "Preferências incluídas na cópia do banco" : "Copiar preferências atuais para o banco selecionado";
        pendingStorage.Visible = cancelSwitch.Visible = !SameBank(savedBank, store.Descriptor);
        pendingStorage.Text = "Troca pendente — reinicie para aplicar. O banco atual continua em uso.";
        RefreshStorageSize();
        bankDetails.Text = $"Identidade: {selectedBank.StoreId:D} · Tamanho em disco: {storageSize.Text}";
        UpdateOperationState();
    }

    private void UpdateOperationState()
    {
        var enabled = operationsEnabled && !IsBusy;
        foreach (var control in new Control[] { clone, create, useExisting, package, restore, save, openFolder,
            theme, legacyEnabled, legacyDirectory, browseLegacy, includeExecutable, discard, cancelSwitch, copyPreferences }) control.Enabled = enabled;
        legacyDirectory.Enabled = browseLegacy.Enabled = enabled && legacyEnabled.Checked;
        includeExecutable.Enabled = enabled && executableAvailable;
        copyPreferences.Enabled = enabled && !clonedBank;
        discard.Enabled = enabled && HasUnsavedChanges;
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
        if (!loadingSettings) { settingsState.Text = HasUnsavedChanges ? "Alterações não salvas." : ""; discard.Enabled = operationsEnabled && !IsBusy && HasUnsavedChanges; }
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
            selectedBank = savedBank; clonedBank = false; copyPreferences.Checked = savedCopyPreferences;
            UpdateBankState(); settingsState.Text = "";
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
        foreach (var path in new[] { selectedBank.DatabasePath, selectedBank.DatabasePath + "-wal", selectedBank.DatabasePath + "-shm" })
        {
            try { var info = new FileInfo(path); if (info.Exists) bytes += info.Length; }
            catch (IOException) { storageSize.Text = "Indisponível"; return; }
            catch (UnauthorizedAccessException) { storageSize.Text = "Indisponível"; return; }
        }
        storageSize.Text = FormatSize(bytes);
    }

    internal static string ScopeName(StorageScope scope) => scope switch
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
