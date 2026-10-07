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

    public ProfilesPage(IConnectionProfileRepository repository)
    {
        this.repository = repository;
        Dock = DockStyle.Fill;
        Padding = new Padding(20);
        databaseType.DataSource = DatabaseProfiles.All.ToList();
        databaseType.DisplayMember = nameof(DatabaseProfile.DisplayName);
        authentication.DataSource = Enum.GetValues<SqlServerAuthentication>();

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(10) };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
        editor.Controls.Add(new Label
        {
            Text = "A senha nunca é salva no perfil e será solicitada na execução.",
            AutoSize = true,
            Margin = new Padding(3, 10, 3, 10)
        }, 0, row);
        editor.SetColumnSpan(editor.GetControlFromPosition(0, row)!, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var create = new Button { Text = "Novo", AutoSize = true };
        buttons.Controls.AddRange(new Control[] { create, save, delete, use });
        editor.Controls.Add(buttons, 0, ++row);
        editor.SetColumnSpan(buttons, 2);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 260, FixedPanel = FixedPanel.Panel1 };
        var listPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        listPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        listPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listPanel.Controls.Add(new Label { Text = "Perfis", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold), Margin = new Padding(3, 3, 3, 14) });
        listPanel.Controls.Add(profiles, 0, 1);
        split.Panel1.Padding = new Padding(0, 0, 12, 0);
        split.Panel1.Controls.Add(listPanel);
        split.Panel2.Controls.Add(editor);
        Controls.Add(split);

        profiles.SelectedIndexChanged += (_, _) => LoadSelection();
        create.Click += (_, _) => ClearEditor();
        save.Click += async (_, _) => await SaveAsync();
        delete.Click += async (_, _) => await DeleteAsync();
        use.Click += (_, _) =>
        {
            if (profiles.SelectedItem is SavedConnectionProfile profile)
                UseRequested?.Invoke(profile);
        };
    }

    public event Action<SavedConnectionProfile>? UseRequested;

    public async Task RefreshAsync(CancellationToken token = default)
    {
        var items = await repository.ListAsync(token);
        profiles.DataSource = items.ToList();
        ClearEditor();
    }

    public void SetEditingEnabled(bool enabled) => save.Enabled = delete.Enabled = enabled;

    private async Task SaveAsync()
    {
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
            var saved = await repository.SaveAsync(draft);
            await RefreshAsync();
            profiles.SelectedItem = profiles.Items.Cast<SavedConnectionProfile>().FirstOrDefault(item => item.ProfileId == saved.ProfileId);
        }
        catch (Exception exception) when (exception is ArgumentException or ApplicationStoreException)
        {
            MessageBox.Show(this, exception.Message, "Perfil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task DeleteAsync()
    {
        if (selectedId is not Guid id || MessageBox.Show(this, "Excluir este perfil? O histórico será preservado.",
                "Excluir perfil", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        await repository.DeleteAsync(id);
        await RefreshAsync();
    }

    private void LoadSelection()
    {
        if (profiles.SelectedItem is not SavedConnectionProfile profile)
            return;
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
    }

    private void ClearEditor()
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
    }

    private static void AddRow(TableLayoutPanel table, int row, string caption, Control control)
    {
        table.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 1, row);
    }
}
