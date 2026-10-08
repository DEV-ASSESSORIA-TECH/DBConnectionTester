using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.UI;

public sealed class ProfilesPage : UserControl
{
    private readonly IConnectionProfileRepository repository;
    private readonly ListBox profiles = new() { Dock = DockStyle.Fill, DisplayMember = nameof(SavedConnectionProfile.Name) };
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
    private readonly CheckBox ping = new() { Text = "Ping", Checked = true, AutoSize = true };
    private readonly CheckBox tcp = new() { Text = "TCP", Checked = true, AutoSize = true };
    private readonly CheckBox databaseTest = new() { Text = "Banco", Checked = true, AutoSize = true };
    private readonly CheckBox background = new() { Text = "Iniciar na bandeja", AutoSize = true };
    private readonly Button save = new() { Text = "Salvar", AutoSize = true };
    private readonly Button delete = new() { Text = "Excluir", AutoSize = true };
    private readonly Button use = new() { Text = "Usar em nova execução", AutoSize = true };
    private Guid? selectedId;
    private bool loading;
    private bool editingEnabled = true;
    private bool refreshPending;
    private bool mutationPending;
    private readonly LatestUiRequest refreshRequests = new();
    private readonly LatestUiRequest mutationRequests = new();
    private readonly Dictionary<Control, UiLayout.FieldRow> fields = [];
    private readonly Button create = new() { Text = "Novo", AutoSize = true };
    private readonly TableLayoutPanel editor;


    public ProfilesPage(IConnectionProfileRepository repository)
    {
        this.repository = repository;
        Dock = DockStyle.Fill;
        Padding = new Padding(20);
        databaseType.Items.AddRange(DatabaseProfiles.All.Cast<object>().ToArray());
        databaseType.DisplayMember = nameof(DatabaseProfile.DisplayName);
        authentication.Items.AddRange(Enum.GetValues<SqlServerAuthentication>().Cast<object>().ToArray());

        editor = UiLayout.Fields(160);
        var row = 0;
        AddRow(editor, row++, "Nome:", name);
        AddRow(editor, row++, "Tipo:", databaseType);
        AddRow(editor, row++, "Servidor:", host);
        AddRow(editor, row++, "Porta:", port);
        AddRow(editor, row++, "Usuário:", user);
        AddRow(editor, row++, "Banco:", database);
        AddRow(editor, row++, "Arquivo SQLite:", sqliteFile);
        AddRow(editor, row++, "Autenticação SQL Server:", authentication);
        AddRow(editor, row++, "Driver ODBC:", odbcDriver);
        AddRow(editor, row++, "Quantidade:", testCount);
        AddRow(editor, row++, "Modo:", continuous);
        AddRow(editor, row++, "Intervalo (s):", interval);
        AddRow(editor, row++, "Timeout (s):", timeout);
        var layers = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        layers.Controls.AddRange(new Control[] { dns, ping, tcp, databaseTest });
        AddRow(editor, row++, "Camadas:", layers);
        AddRow(editor, row++, "Comportamento:", background);
        editor.RowCount = row + 1;
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.Controls.Add(new Label
        {
            Text = "A senha nunca é salva no perfil e será solicitada na execução.",
            AutoSize = true,
            Margin = new Padding(3, 10, 3, 10)
        }, 0, row);
        editor.SetColumnSpan(editor.GetControlFromPosition(0, row)!, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };

        buttons.Controls.AddRange(new Control[] { create, save, delete, use });
        var viewport = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true };
        viewport.Controls.Add(editor);
        var editorHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        editorHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editorHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editorHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editorHost.Controls.Add(buttons, 0, 0);
        editorHost.Controls.Add(viewport, 0, 1);

        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(900, 650), SplitterDistance = 220,
            FixedPanel = FixedPanel.Panel1, Panel1MinSize = 80, Panel2MinSize = 140 };
        split.SizeChanged += (_, _) =>
        {
            var orientation = split.ClientSize.Width * 96d / split.DeviceDpi >= 800 ? Orientation.Vertical : Orientation.Horizontal;
            if (split.Orientation == orientation) return;
            var extent = orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
            if (extent <= split.Panel1MinSize + split.Panel2MinSize + split.SplitterWidth) return;
            split.SuspendLayout();
            split.SplitterDistance = split.Panel1MinSize;
            split.Orientation = orientation;
            var preferred = (int)((orientation == Orientation.Vertical ? 220 : 130) * split.DeviceDpi / 96d);
            split.SplitterDistance = Math.Clamp(preferred, split.Panel1MinSize,
                Math.Max(split.Panel1MinSize, extent - split.Panel2MinSize - split.SplitterWidth));
            split.ResumeLayout(true);
        };
        var listPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        listPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        listPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listPanel.Controls.Add(new Label { Text = "Perfis", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold), Margin = new Padding(3, 3, 3, 14) });
        listPanel.Controls.Add(profiles, 0, 1);
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
        profiles.SelectedIndexChanged += (_, _) => LoadSelection();
        create.Click += (_, _) => ClearEditor();
        save.Click += async (_, _) => await SaveAsync();
        delete.Click += async (_, _) => await DeleteAsync();
        use.Click += (_, _) =>
        {
            if (profiles.SelectedItem is SavedConnectionProfile profile)
                UseRequested?.Invoke(profile);
        };
        ClearEditor();
    }

    public event Action<SavedConnectionProfile>? UseRequested;
    public event Action<IReadOnlyList<SavedConnectionProfile>>? ProfilesChanged;

    public void SelectProfile(Guid? id)
    {
        var item = profiles.Items.Cast<SavedConnectionProfile>().FirstOrDefault(p => p.ProfileId == id);
        if (item is null) ClearEditor();
        else profiles.SelectedItem = item;
    }

    public Task RefreshAsync(CancellationToken token = default) => LoadProfilesAsync(null, token);

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
            else profiles.SelectedItem = selected;
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
        if (disposing) { refreshRequests.Dispose(); mutationRequests.Dispose(); }
        base.Dispose(disposing);
    }

    private void ApplyVisibility()
    {
        var canEdit = editingEnabled && !refreshPending && !mutationPending;
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
            testCount.Enabled = canEdit && !continuous.Checked;
            delete.Enabled = use.Enabled = canEdit && selectedId is not null;
        }
        finally { editor.ResumeLayout(true); }
    }

    private async Task SaveAsync()
    {
        if (!editingEnabled || refreshPending || mutationPending || IsDisposed) return;
        using var request = mutationRequests.Start();
        mutationPending = true;
        ApplyVisibility();
        try
        {
            var selectedType = (databaseType.SelectedItem as DatabaseProfile)?.Type ?? DatabaseType.MySqlMariaDb;
            var draft = new ConnectionProfileDraft(selectedId, name.Text, selectedType, host.Text,
                DatabaseProfiles.Get(selectedType).UsesNetwork ? (int)port.Value : null,
                user.Text, database.Text, sqliteFile.Text,
                authentication.SelectedItem is SqlServerAuthentication auth ? auth : SqlServerAuthentication.Windows,
                odbcDriver.Text,
                new ProfileExecutionDefaults((long)testCount.Value, continuous.Checked, (double)interval.Value,
                    (int)timeout.Value, dns.Checked, ping.Checked, tcp.Checked, databaseTest.Checked, background.Checked));
            var saved = await Task.Run(() => repository.SaveAsync(draft, request.Token), request.Token);
            if (request.IsCurrent) await LoadProfilesAsync(saved.ProfileId, request.Token);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is ArgumentException or ApplicationStoreException)
        {
            if (request.IsCurrent) MessageBox.Show(this, exception.Message, "Perfil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { if (!IsDisposed) { mutationPending = false; ApplyVisibility(); } }
    }

    private async Task DeleteAsync()
    {
        if (!editingEnabled || refreshPending || mutationPending || selectedId is not Guid id || MessageBox.Show(this, "Excluir este perfil? O histórico será preservado.",
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
        try { update(); ApplyVisibility(); }
        finally { loading = wasLoading; editor.ResumeLayout(true); }
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

    private void AddRow(TableLayoutPanel table, int row, string caption, Control control)
        => fields[control] = UiLayout.AddField(table, row, caption, control);
}
