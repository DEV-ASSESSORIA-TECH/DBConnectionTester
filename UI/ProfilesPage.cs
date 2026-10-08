using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.UI;

public sealed class ProfilesPage : UserControl
{
    private readonly IConnectionProfileRepository repository;
    private readonly ListBox profiles = new() { Dock = DockStyle.Fill, DisplayMember = nameof(SavedConnectionProfile.Name) };
    private readonly Label profilesTitle = new() { Text = "Perfis · 0", AutoSize = true, Margin = new Padding(3, 3, 3, 10) };
    private readonly Label emptyProfiles = new() { Text = "Carregando perfis…", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AccessibleName = "Lista de perfis vazia" };
    private readonly ToolTip profileTip = new();
    private readonly TextBox name = new();
    private readonly ComboBox databaseType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox host = new();
    private readonly NumericUpDown port = new() { Minimum = 1, Maximum = 65535, Value = 3306 };
    private readonly TextBox user = new();
    private readonly TextBox database = new();
    private readonly TextBox sqliteFile = new();
    private readonly ComboBox authentication = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox odbcDriver = new();
    private readonly NumericUpDown testCount = new() { Minimum = 1, Maximum = 10_000_000, Value = 1000 };
    private readonly CheckBox continuous = new() { Text = "Execução contínua", AutoSize = true };
    private readonly NumericUpDown interval = new() { Minimum = 0, Maximum = 3600, DecimalPlaces = 1, Value = 5 };
    private readonly NumericUpDown timeout = new() { Minimum = 1, Maximum = 120, Value = 5 };
    private readonly CheckBox dns = new() { Text = "DNS", Checked = true, AutoSize = true };
    private readonly CheckBox ping = new() { Text = "Ping / ICMP", Checked = true, AutoSize = true };
    private readonly CheckBox tcp = new() { Text = "TCP", Checked = true, AutoSize = true };
    private readonly CheckBox databaseTest = new() { Text = "Banco + SELECT 1", Checked = true, AutoSize = true };
    private readonly CheckBox background = new() { Text = "Minimizar ao iniciar", AutoSize = true };
    private readonly Button save = new() { Text = "Salvar", AutoSize = true };
    private readonly Button delete = new() { Text = "Excluir", AutoSize = true };
    private readonly Button use = new() { Text = "Usar em nova execução", AutoSize = true };
    private Guid? selectedId;
    private ConnectionProfileDraft? editorBaseline;
    private bool decisionPending;
    private bool justSaved;
    private readonly Label editorTitle = new() { Text = "Novo perfil", AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(3, 0, 3, 4) };
    private readonly Label editorState = new() { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(3, 0, 3, 8), AccessibleName = "Estado da edição" };
    internal Func<string, DialogResult>? EditDecision;
    internal Action<string>? ErrorReporter;
    private bool loading;
    private bool editingEnabled = true;
    private bool refreshPending;
    private bool mutationPending;
    private readonly LatestUiRequest refreshRequests = new();
    private readonly LatestUiRequest mutationRequests = new();
    private readonly Dictionary<Control, UiLayout.FieldRow> fields = [];
    private readonly Button create = new() { Text = "Novo perfil", AutoSize = true };
    private readonly TableLayoutPanel editor;
    private readonly List<CompactFieldPair> compactPairs = [];


    public ProfilesPage(IConnectionProfileRepository repository)
    {
        this.repository = repository;
        Dock = DockStyle.Fill;
        Padding = new Padding(20);
        databaseType.Items.AddRange(DatabaseProfiles.All.Cast<object>().ToArray());
        databaseType.DisplayMember = nameof(DatabaseProfile.DisplayName);
        authentication.Items.AddRange(Enum.GetValues<SqlServerAuthentication>().Cast<object>().ToArray());

        editor = UiLayout.Fields(120);
        var row = 0;
        AddSection(editor, row++, "Identificação");
        AddRow(editor, row++, "Nome:", name);
        AddSection(editor, row++, "Conexão");
        AddRow(editor, row++, "Tipo de banco:", databaseType);
        AddRow(editor, row++, "Servidor / host:", host);
        AddRow(editor, row++, "Porta:", port);
        AddRow(editor, row++, "Usuário:", user);
        AddRow(editor, row++, "Banco (opcional):", database);
        AddRow(editor, row++, "Arquivo SQLite:", sqliteFile);
        AddRow(editor, row++, "Autenticação:", authentication);
        AddRow(editor, row++, "Driver ODBC:", odbcDriver);
        AddNote(editor, row++, "A senha não é salva no perfil. Informe-a em Nova execução.");
        AddSection(editor, row++, "Execução");
        AddRow(editor, row++, "Quantidade:", testCount);
        AddRow(editor, row++, "Modo:", continuous);
        AddRow(editor, row++, "Intervalo (s):", interval);
        AddRow(editor, row++, "Timeout (s):", timeout);
        var layers = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        layers.Controls.AddRange(new Control[] { dns, ping, tcp, databaseTest });
        AddRow(editor, row++, "Etapas:", layers);
        AddRow(editor, row++, "Ao iniciar:", background);
        editor.RowCount = row;
        compactPairs.Add(new CompactFieldPair(editor, fields[host], fields[port], 110));
        compactPairs.Add(new CompactFieldPair(editor, fields[interval], fields[timeout], 90));
        editor.SizeChanged += (_, _) => UpdateCompactFields();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };

        save.Font = new Font(Font, FontStyle.Bold);
        delete.Margin = new Padding(24, 3, 3, 3);
        buttons.Controls.AddRange(new Control[] { save, use, delete });
        var viewport = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true };
        viewport.Controls.Add(editor);
        var editorHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        editorHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editorHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editorHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editorHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editorHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editorTitle.Font = new Font(Font.FontFamily, 12, FontStyle.Bold);
        editorHost.Controls.Add(editorTitle, 0, 0);
        editorHost.Controls.Add(editorState, 0, 1);
        editorHost.Controls.Add(buttons, 0, 2);
        editorHost.Controls.Add(viewport, 0, 3);

        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(900, 650), SplitterDistance = 200,
            FixedPanel = FixedPanel.Panel1, Panel1MinSize = 80, Panel2MinSize = 140 };
        split.SizeChanged += (_, _) =>
        {
            var width = split.ClientSize.Width * 96d / split.DeviceDpi;
            var orientation = width >= 680 ? Orientation.Vertical : Orientation.Horizontal;
            var extent = orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
            if (extent <= split.Panel1MinSize + split.Panel2MinSize + split.SplitterWidth) return;
            // Reclaim list width gradually before stacking the list above the editor.
            var listSize = orientation == Orientation.Vertical ? Math.Clamp(width - 500, 180, 200) : 130;
            var preferred = (int)(listSize * split.DeviceDpi / 96d);
            var distance = Math.Clamp(preferred, split.Panel1MinSize,
                Math.Max(split.Panel1MinSize, extent - split.Panel2MinSize - split.SplitterWidth));
            if (split.Orientation == orientation && split.SplitterDistance == distance) return;
            split.SuspendLayout();
            if (split.Orientation != orientation)
            {
                split.SplitterDistance = split.Panel1MinSize;
                split.Orientation = orientation;
            }
            split.Panel1.Padding = orientation == Orientation.Vertical ? new Padding(0, 0, 12, 0) : new Padding(0, 0, 0, 10);
            split.SplitterDistance = distance;
            split.ResumeLayout(true);
        };
        var listPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        listPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        listPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        profilesTitle.Font = new Font(Font.FontFamily, 14, FontStyle.Bold);
        listPanel.Controls.Add(profilesTitle);
        listPanel.RowStyles.Insert(1, new RowStyle(SizeType.AutoSize));
        listPanel.Controls.Add(create, 0, 1);
        create.Margin = new Padding(3, 0, 3, 10);
        var listHost = new BufferedPanel { Dock = DockStyle.Fill };
        listHost.Controls.Add(profiles);
        listHost.Controls.Add(emptyProfiles);
        emptyProfiles.BringToFront();
        listPanel.Controls.Add(listHost, 0, 2);
        profiles.MouseMove += (_, e) =>
        {
            var index = profiles.IndexFromPoint(e.Location);
            var text = index >= 0 && profiles.Items[index] is SavedConnectionProfile item
                && TextRenderer.MeasureText(item.Name, profiles.Font).Width > profiles.ClientSize.Width - 8 ? item.Name : "";
            if (profileTip.GetToolTip(profiles) != text) profileTip.SetToolTip(profiles, text);
        };
        profiles.MouseLeave += (_, _) => profileTip.SetToolTip(profiles, "");
        split.Panel1.Padding = new Padding(0, 0, 12, 0);
        split.Panel1.Controls.Add(listPanel);
        split.Panel2.Controls.Add(editorHost);
        Controls.Add(split);

        databaseType.SelectedIndexChanged += (_, _) =>
        {
            if (loading) return;
            var type = (databaseType.SelectedItem as DatabaseProfile)?.Type ?? DatabaseType.MySqlMariaDb;
            port.Value = DatabaseProfiles.Get(type).DefaultPort ?? 1;
            dns.Checked = ping.Checked = tcp.Checked = DatabaseProfiles.Get(type).UsesNetwork;
            databaseTest.Checked = DatabaseProfiles.Get(type).SupportsDatabaseTest;
            if (type == DatabaseType.SapSqlAnywhere && string.IsNullOrWhiteSpace(odbcDriver.Text)) odbcDriver.Text = "SQL Anywhere 17";
            ApplyVisibility();
        };
        authentication.FormattingEnabled = true;
        authentication.Format += (_, e) => e.Value = e.ListItem is SqlServerAuthentication.Windows ? "Windows" : "Usuário e senha";
        authentication.SelectedIndexChanged += (_, _) => { if (!loading) ApplyVisibility(); };
        continuous.CheckedChanged += (_, _) => testCount.Enabled = editingEnabled && !continuous.Checked;
        profiles.SelectedIndexChanged += async (_, _) => await ChangeSelectionAsync();
        create.Click += async (_, _) => { if (await TryLeaveAsync()) ClearEditor(); };
        save.Click += async (_, _) => await SaveAsync();
        delete.Click += async (_, _) => await DeleteAsync();
        use.Click += async (_, _) => await UseSelectionAsync();
        ClearEditor();
        foreach (var control in new Control[] { name, databaseType, host, port, user, database, sqliteFile, authentication,
            odbcDriver, testCount, continuous, interval, timeout, dns, ping, tcp, databaseTest, background })
        {
            void Changed(object? sender, EventArgs args) { if (!loading) { justSaved = false; UpdateEditState(); } }
            if (control is TextBox text) text.TextChanged += Changed;
            else if (control is ComboBox combo) combo.SelectedIndexChanged += Changed;
            else if (control is NumericUpDown number) number.ValueChanged += Changed;
            else if (control is CheckBox check) check.CheckedChanged += Changed;
        }
    }

    public event Action<SavedConnectionProfile>? UseRequested;
    public event Action<IReadOnlyList<SavedConnectionProfile>>? ProfilesChanged;

    public void SelectProfile(Guid? id)
    {
        var item = profiles.Items.Cast<SavedConnectionProfile>().FirstOrDefault(p => p.ProfileId == id);
        if (item is null) create.PerformClick();
        else profiles.SelectedItem = item;
    }

    public async Task RefreshAsync(CancellationToken token = default)
    {
        if (await TryLeaveAsync()) await LoadProfilesAsync(selectedId, token);
    }

    private async Task LoadProfilesAsync(Guid? selectId, CancellationToken token = default)
    {
        using var request = refreshRequests.Start(token);
        refreshPending = true;
        ApplyVisibility();
        try
        {
            var items = await Task.Run(() => repository.ListAsync(request.Token), request.Token);
            if (!request.IsCurrent) return;
            profiles.BeginUpdate();
            loading = true;
            try
            {
                profiles.DataSource = items.ToList();
                profiles.ClearSelected();
            }
            finally { loading = false; profiles.EndUpdate(); }
            var selected = items.FirstOrDefault(item => item.ProfileId == selectId);
            if (selected is null) ClearEditor();
            else
            {
                SetListSelection(selected);
                LoadSelection();
            }
            profilesTitle.Text = $"Perfis · {items.Count}";
            emptyProfiles.Text = "Nenhum perfil salvo.\nClique em Novo perfil para criar.";
            emptyProfiles.Visible = items.Count == 0;
            ProfilesChanged?.Invoke(items);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (ApplicationStoreException) when (!request.IsCurrent) { }
        finally
        {
            if (request.IsCurrent) { refreshPending = false; ApplyVisibility(); }
        }
    }

    public void SetEditingEnabled(bool enabled)
    {
        editingEnabled = enabled;
        ApplyVisibility();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { refreshRequests.Dispose(); mutationRequests.Dispose(); profileTip.Dispose(); }
        base.Dispose(disposing);
    }

    private void ApplyVisibility()
    {
        var canEdit = editingEnabled && !refreshPending && !mutationPending && !decisionPending;
        editor.Enabled = profiles.Enabled = create.Enabled = save.Enabled = canEdit;
        var type = (databaseType.SelectedItem as DatabaseProfile)?.Type ?? DatabaseType.MySqlMariaDb;
        var auth = authentication.SelectedItem is SqlServerAuthentication selected ? selected : SqlServerAuthentication.Windows;
        var state = DatabaseUiState.Create(type, auth);
        editor.SuspendLayout();
        try
        {
            fields[host].SetVisible(state.ShowHost); fields[port].SetVisible(state.ShowPort);
            fields[user].SetVisible(state.ShowCredentials); fields[database].SetVisible(state.ShowDatabase);
            fields[sqliteFile].SetVisible(state.ShowSqliteFile); fields[authentication].SetVisible(state.ShowSqlServerAuthentication);
            fields[odbcDriver].SetVisible(state.ShowOdbcDriver);
            dns.Enabled = canEdit && state.AllowDns; ping.Enabled = canEdit && state.AllowPing;
            tcp.Enabled = canEdit && state.AllowTcp; databaseTest.Enabled = canEdit && state.AllowDatabaseTest;
            if (state.RequireDatabaseTest) databaseTest.Checked = true;
            if (state.RequireTcp) tcp.Checked = true;
            UpdateCompactFields();
            testCount.Enabled = canEdit && !continuous.Checked;
            delete.Enabled = canEdit && selectedId is not null;
            use.Enabled = canEdit && (selectedId is not null || HasUnsavedChanges);
        }
        finally { editor.ResumeLayout(true); }
    }

    private async Task<bool> SaveAsync()
    {
        if (!editingEnabled || refreshPending || mutationPending || IsDisposed) return false;
        using var request = mutationRequests.Start();
        mutationPending = true;
        ApplyVisibility();
        try
        {
            var draft = ReadDraft();
            var saved = await Task.Run(() => repository.SaveAsync(draft, request.Token), request.Token);
            if (!request.IsCurrent) return false;
            await LoadProfilesAsync(saved.ProfileId, request.Token);
            var success = !IsDisposed && selectedId == saved.ProfileId && !HasUnsavedChanges;
            if (success) { justSaved = true; UpdateEditState(); }
            return success;
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is ArgumentException or ApplicationStoreException)
        {
            if (request.IsCurrent) ReportError(exception.Message);
        }
        finally { if (!IsDisposed) { mutationPending = false; ApplyVisibility(); } }
        return false;
    }

    private async Task DeleteAsync()
    {
        if (!editingEnabled || refreshPending || mutationPending || selectedId is null || !await TryLeaveAsync()) return;
        if (selectedId is not Guid id || MessageBox.Show(this, "Excluir este perfil? O histórico será preservado.",
                "Excluir perfil", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        using var request = mutationRequests.Start();
        mutationPending = true;
        ApplyVisibility();
        try
        {
            await Task.Run(() => repository.DeleteAsync(id, request.Token), request.Token);
            if (request.IsCurrent) await LoadProfilesAsync(null, request.Token);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is ApplicationStoreException or Microsoft.Data.Sqlite.SqliteException)
        { if (request.IsCurrent) MessageBox.Show(this, exception.Message, "Excluir perfil", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { if (!IsDisposed) { mutationPending = false; ApplyVisibility(); } }
    }

    private void UpdateEditor(Action update)
    {
        var wasLoading = loading;
        loading = true;
        editor.SuspendLayout();
        try { justSaved = false; update(); ApplyVisibility(); editorBaseline = ReadDraft(); }
        finally { loading = wasLoading; editor.ResumeLayout(true); UpdateEditState(); }
    }

    internal bool IsBusy => decisionPending || mutationPending || refreshPending;

    internal bool HasUnsavedChanges => editorBaseline is not null && ReadDraft() != editorBaseline;

    private ConnectionProfileDraft ReadDraft()
    {
        var type = (databaseType.SelectedItem as DatabaseProfile)?.Type ?? DatabaseType.MySqlMariaDb;
        return new(selectedId, name.Text, type, host.Text, DatabaseProfiles.Get(type).UsesNetwork ? (int)port.Value : null,
            user.Text, database.Text, sqliteFile.Text,
            authentication.SelectedItem is SqlServerAuthentication auth ? auth : SqlServerAuthentication.Windows, odbcDriver.Text,
            new((long)testCount.Value, continuous.Checked, (double)interval.Value, (int)timeout.Value,
                dns.Checked, ping.Checked, tcp.Checked, databaseTest.Checked, background.Checked));
    }

    private void UpdateEditState()
    {
        editorTitle.Text = selectedId is null ? "Novo perfil" : (string.IsNullOrWhiteSpace(name.Text) ? "Editar perfil" : name.Text);
        editorState.Text = HasUnsavedChanges ? "Alterações não salvas" : justSaved ? "Perfil salvo" : "";
        editorState.Visible = editorState.Text.Length > 0;
        use.Enabled = editingEnabled && !refreshPending && !mutationPending && !decisionPending
            && (selectedId is not null || HasUnsavedChanges);
    }

    private void SetListSelection(SavedConnectionProfile? profile)
    {
        var previous = loading;
        loading = true;
        try { if (profile is null) profiles.ClearSelected(); else profiles.SelectedItem = profile; }
        finally { loading = previous; }
    }

    private void RestoreSavedEditor()
    {
        var item = profiles.Items.Cast<SavedConnectionProfile>().FirstOrDefault(p => p.ProfileId == selectedId);
        SetListSelection(item);
        if (item is null) ClearEditor(); else LoadSelection();
    }

    private async Task ChangeSelectionAsync()
    {
        if (loading || decisionPending || mutationPending || refreshPending) return;
        var requested = profiles.SelectedItem as SavedConnectionProfile;
        if (requested?.ProfileId == selectedId) return;
        var old = profiles.Items.Cast<SavedConnectionProfile>().FirstOrDefault(p => p.ProfileId == selectedId);
        SetListSelection(old);
        if (!await TryLeaveAsync() || IsDisposed) return;
        var next = profiles.Items.Cast<SavedConnectionProfile>().FirstOrDefault(p => p.ProfileId == requested?.ProfileId);
        SetListSelection(next);
        if (next is null) ClearEditor(); else LoadSelection();
    }

    internal async Task<bool> TryLeaveAsync()
    {
        if (decisionPending || mutationPending || refreshPending || IsDisposed) return false;
        if (!HasUnsavedChanges) return true;
        decisionPending = true;
        ApplyVisibility();
        try
        {
            var result = AskEdits("Há alterações não salvas neste perfil.\n\nSim: salvar e continuar.\nNão: descartar alterações e continuar.\nCancelar: continuar editando.");
            if (result == DialogResult.Yes) return await SaveAsync();
            if (result != DialogResult.No) return false;
            RestoreSavedEditor();
            return true;
        }
        finally { decisionPending = false; if (!IsDisposed) ApplyVisibility(); }
    }

    private async Task UseSelectionAsync()
    {
        if (!editingEnabled || refreshPending || mutationPending || decisionPending || IsDisposed) return;
        decisionPending = true;
        ApplyVisibility();
        try
        {
            if (HasUnsavedChanges)
            {
                var result = AskEdits(selectedId is null
                    ? "Este perfil ainda não foi salvo.\n\nSim: salvar e usar.\nCancelar ou Não: continuar editando."
                    : "Há alterações não salvas.\n\nSim: salvar e usar.\nNão: descartar alterações e usar a versão salva.\nCancelar: continuar editando.");
                if (result == DialogResult.Yes) { if (!await SaveAsync()) return; }
                else if (result == DialogResult.No && selectedId is not null) RestoreSavedEditor();
                else return;
            }
            decisionPending = false;
            ApplyVisibility();
            if (profiles.SelectedItem is SavedConnectionProfile profile) UseRequested?.Invoke(profile);
        }
        finally { decisionPending = false; if (!IsDisposed) ApplyVisibility(); }
    }

    private DialogResult AskEdits(string message) => EditDecision?.Invoke(message)
        ?? MessageBox.Show(this, message, "Alterações no perfil", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);

    private void ReportError(string message)
    {
        if (ErrorReporter is { } reporter) reporter(message);
        else MessageBox.Show(this, message, "Perfil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void LoadSelection()
    {
        if (loading || profiles.SelectedItem is not SavedConnectionProfile profile) return;
        UpdateEditor(() =>
        {
            selectedId = profile.ProfileId;
            name.Text = profile.Name;
            databaseType.SelectedItem = DatabaseProfiles.All.First(item => item.Type == profile.DatabaseType);
            host.Text = profile.Host;
            port.Value = Math.Clamp(profile.Port ?? DatabaseProfiles.Get(profile.DatabaseType).DefaultPort ?? 1, 1, 65535);
            user.Text = profile.UserName;
            database.Text = profile.DatabaseName;
            sqliteFile.Text = profile.SqliteFile;
            authentication.SelectedItem = profile.SqlServerAuthentication;
            odbcDriver.Text = profile.OdbcDriver;
            testCount.Value = profile.ExecutionDefaults.TestCount;
            continuous.Checked = profile.ExecutionDefaults.Continuous;
            interval.Value = (decimal)profile.ExecutionDefaults.IntervalSeconds;
            timeout.Value = profile.ExecutionDefaults.TimeoutSeconds;
            dns.Checked = profile.ExecutionDefaults.Dns;
            ping.Checked = profile.ExecutionDefaults.Ping;
            tcp.Checked = profile.ExecutionDefaults.Tcp;
            databaseTest.Checked = profile.ExecutionDefaults.DatabaseTest;
            background.Checked = profile.ExecutionDefaults.StartInBackground;
        });
    }

    private void ClearEditor() => UpdateEditor(() =>
    {
        selectedId = null;
        profiles.ClearSelected();
        name.Clear(); host.Clear(); user.Clear(); database.Clear(); sqliteFile.Clear(); odbcDriver.Clear();
        databaseType.SelectedIndex = 0; authentication.SelectedItem = SqlServerAuthentication.Windows;
        var defaults = ProfileExecutionDefaults.Default;
        testCount.Value = defaults.TestCount; continuous.Checked = defaults.Continuous;
        interval.Value = (decimal)defaults.IntervalSeconds; timeout.Value = defaults.TimeoutSeconds;
        dns.Checked = defaults.Dns; ping.Checked = defaults.Ping; tcp.Checked = defaults.Tcp;
        databaseTest.Checked = defaults.DatabaseTest; background.Checked = defaults.StartInBackground;
    });

    private void UpdateCompactFields()
    {
        if (compactPairs.Count == 0) return;
        var compact = editor.ClientSize.Width * 96d / editor.DeviceDpi >= 500;
        var type = (databaseType.SelectedItem as DatabaseProfile)?.Type ?? DatabaseType.MySqlMariaDb;
        compactPairs[0].Update(compact, DatabaseUiState.Create(type, SqlServerAuthentication.Windows).ShowHost);
        compactPairs[1].Update(compact, true);
    }

    private sealed class CompactFieldPair
    {
        private readonly TableLayoutPanel table;
        private readonly UiLayout.FieldRow first;
        private readonly UiLayout.FieldRow second;
        private readonly TableLayoutPanel line;
        private bool compact;

        public CompactFieldPair(TableLayoutPanel table, UiLayout.FieldRow first, UiLayout.FieldRow second, int inputWidth)
        {
            this.table = table; this.first = first; this.second = second;
            line = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, inputWidth * table.DeviceDpi / 96f));
        }

        public void Update(bool requested, bool visible)
        {
            requested &= visible;
            if (requested == compact) return;
            table.SuspendLayout(); line.SuspendLayout();
            try
            {
                compact = requested;
                if (compact)
                {
                    table.Controls.Remove(first.Input);
                    table.Controls.Remove(second.Label);
                    table.Controls.Remove(second.Input);
                    line.Controls.Add(first.Input, 0, 0);
                    line.Controls.Add(second.Label, 1, 0);
                    line.Controls.Add(second.Input, 2, 0);
                    table.Controls.Add(line, 1, first.Row);
                    table.RowStyles[second.Row].SizeType = SizeType.Absolute;
                    table.RowStyles[second.Row].Height = 0;
                }
                else
                {
                    table.Controls.Remove(line);
                    line.Controls.Remove(first.Input);
                    line.Controls.Remove(second.Label);
                    line.Controls.Remove(second.Input);
                    table.Controls.Add(first.Input, 1, first.Row);
                    table.Controls.Add(second.Label, 0, second.Row);
                    table.Controls.Add(second.Input, 1, second.Row);
                    table.RowStyles[second.Row].SizeType = visible ? SizeType.AutoSize : SizeType.Absolute;
                    table.RowStyles[second.Row].Height = 0;
                }
            }
            finally { line.ResumeLayout(true); table.ResumeLayout(true); }
        }
    }

    private static void AddSection(TableLayoutPanel table, int row, string title)
    {
        AddNote(table, row, title);
        var label = (Label)table.GetControlFromPosition(0, row)!;
        label.Font = new Font(table.Font, FontStyle.Bold);
        label.Margin = new Padding(3, row == 0 ? 0 : 14, 3, 6);
    }

    private static void AddNote(TableLayoutPanel table, int row, string text)
    {
        table.RowCount = Math.Max(table.RowCount, row + 1);
        while (table.RowStyles.Count <= row) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(3, 6, 3, 6) };
        table.Controls.Add(label, 0, row);
        table.SetColumnSpan(label, 2);
    }

    private void AddRow(TableLayoutPanel table, int row, string caption, Control control)
        => fields[control] = UiLayout.AddField(table, row, caption, control);
}
