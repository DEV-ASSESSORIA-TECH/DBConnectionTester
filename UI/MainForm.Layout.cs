using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

public sealed partial class MainForm
{
    private void BuildUi()
    {
        cmbDatabaseType.DataSource = DatabaseProfiles.All.ToList();
        cmbDatabaseType.DisplayMember = nameof(DatabaseProfile.DisplayName);
        cmbSqlServerAuth.DataSource = Enum.GetValues<SqlServerAuthentication>();
        cmbSqlServerAuth.Format += (_, e) =>
            e.Value = (SqlServerAuthentication?)e.ListItem == SqlServerAuthentication.Windows
                ? "Autenticação do Windows"
                : "Usuário e senha do SQL Server";

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 3,
            AutoScroll = true,
            AutoSize = true
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        var row = 0;
        AddRow(table, row++, "Tipo de banco:", cmbDatabaseType);
        hostRow = AddRow(table, row++, "Servidor / host:", txtHost);
        portRow = AddRow(table, row++, "Porta:", numPort);
        sqlAuthRow = AddRow(table, row++, "Autenticação:", cmbSqlServerAuth);
        userRow = AddRow(table, row++, "Usuário:", txtUser);
        passwordRow = AddRow(table, row++, "Senha:", txtPassword);
        databaseRow = AddRow(table, row++, "Banco (opcional):", txtDatabase);
        odbcDriverRow = AddRow(table, row++, "Driver ODBC:", txtOdbcDriver);

        var sqlitePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
        sqlitePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sqlitePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        txtSqliteFile.Dock = DockStyle.Fill;
        sqlitePanel.Controls.Add(txtSqliteFile, 0, 0);
        sqlitePanel.Controls.Add(btnBrowseSqlite, 1, 0);
        sqliteFileRow = AddRow(table, row++, "Arquivo SQLite (.db):", sqlitePanel);

        AddRow(table, row++, "Quantidade de testes:", numTests);
        AddRow(table, row++, "Modo de execução:", chkContinuous);
        AddRow(table, row++, "Intervalo entre testes (s):", numInterval);
        AddRow(table, row++, "Timeout por etapa (s):", numTimeout);

        var layers = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        layers.Controls.AddRange(new Control[] { chkPing, chkTcp, chkDatabase });
        AddRow(table, row++, "Camadas:", layers);
        AddRow(table, row++, "Comportamento:", chkBackground);

        table.Controls.Add(new Label { Text = "Arquivo CSV:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
        txtOutput.Dock = DockStyle.Fill;
        table.Controls.Add(txtOutput, 1, row);
        table.Controls.Add(btnBrowseOutput, 2, row++);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange(new Control[] { btnStart, btnStop, btnOpenCsv, btnOpenLog, btnOpenFolder });
        table.Controls.Add(new Label { Text = "Controle:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
        table.Controls.Add(buttons, 1, row);
        table.SetColumnSpan(buttons, 2);
        row++;

        table.Controls.Add(new Label { Text = "Progresso:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
        table.Controls.Add(progressBar, 1, row);
        table.SetColumnSpan(progressBar, 2);
        row++;

        table.Controls.Add(new Label { Text = "Status:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
        table.Controls.Add(lblStatus, 1, row);
        table.SetColumnSpan(lblStatus, 2);
        row++;

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = "A senha fica apenas na memória. CSV e TXT não armazenam credenciais nem connection strings. " +
                   "Em execução contínua, o teste termina apenas por ação manual, saída ou desligamento do Windows. " +
                   "O programa não configura inicialização automática."
        };
        table.Controls.Add(note, 0, row);
        table.SetColumnSpan(note, 3);
        Controls.Add(table);
    }

    private static RowBinding AddRow(TableLayoutPanel table, int row, string text, Control control)
    {
        var label = new Label { Text = text, Anchor = AnchorStyles.Left, AutoSize = true };
        table.Controls.Add(label, 0, row);
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 1, row);
        table.SetColumnSpan(control, 2);
        return new RowBinding(label, control);
    }

    private void ConfigureTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(trayStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(trayOpenPanel);
        menu.Items.Add(trayOpenCsv);
        menu.Items.Add(trayOpenLog);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(trayStop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(trayExit);

        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Text = "DB Connection Tester";
        trayIcon.ContextMenuStrip = menu;
        trayIcon.Visible = false;
        trayIcon.DoubleClick += (_, _) => ShowPanel();
    }

    private void WireEvents()
    {
        cmbDatabaseType.SelectedIndexChanged += (_, _) => ApplyDatabaseType(resetPort: true);
        cmbSqlServerAuth.SelectedIndexChanged += (_, _) => ApplyCredentialVisibility();
        chkContinuous.CheckedChanged += (_, _) => numTests.Enabled = configurationEnabled && !chkContinuous.Checked;
        btnBrowseSqlite.Click += (_, _) => BrowseSqliteFile();
        btnBrowseOutput.Click += (_, _) => BrowseOutputFile();
        btnOpenFolder.Click += (_, _) => OpenOutputFolder();
        btnOpenCsv.Click += (_, _) => OpenFile(currentCsvPath);
        btnOpenLog.Click += (_, _) => OpenFile(currentTxtPath);
        btnStart.Click += async (_, _) => await StartAsync();
        btnStop.Click += (_, _) => RequestStop();
        trayOpenPanel.Click += (_, _) => ShowPanel();
        trayOpenCsv.Click += (_, _) => OpenFile(currentCsvPath);
        trayOpenLog.Click += (_, _) => OpenFile(currentTxtPath);
        trayStop.Click += (_, _) => RequestStop();
        trayExit.Click += (_, _) => RequestExit();
        FormClosing += OnFormClosing;
        FormClosed += (_, _) =>
        {
            runCoordinator.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
        };
    }

    private void ApplyDatabaseType(bool resetPort)
    {
        var profile = SelectedProfile;
        var state = DatabaseUiState.Create(profile.Type, SelectedSqlServerAuthentication);
        if (resetPort && profile.DefaultPort.HasValue)
            numPort.Value = profile.DefaultPort.Value;

        hostRow.SetVisible(state.ShowHost);
        portRow.SetVisible(state.ShowPort);
        databaseRow.SetVisible(state.ShowDatabase);
        sqlAuthRow.SetVisible(state.ShowSqlServerAuthentication);
        odbcDriverRow.SetVisible(state.ShowOdbcDriver);
        sqliteFileRow.SetVisible(state.ShowSqliteFile);

        if (resetPort && state.RequireDatabaseTest)
        {
            chkPing.Checked = false;
            chkTcp.Checked = false;
            chkDatabase.Checked = true;
        }
        else if (resetPort && state.RequireTcp)
        {
            chkPing.Checked = true;
            chkTcp.Checked = true;
            chkDatabase.Checked = false;
        }
        else if (resetPort)
        {
            chkPing.Checked = true;
            chkTcp.Checked = true;
            chkDatabase.Checked = true;
        }

        chkPing.Enabled = configurationEnabled && state.AllowPing;
        chkTcp.Enabled = configurationEnabled && state.AllowTcp;
        chkDatabase.Enabled = configurationEnabled && state.AllowDatabaseTest;
        ApplyCredentialVisibility();
    }

    private void ApplyCredentialVisibility()
    {
        var state = DatabaseUiState.Create(SelectedProfile.Type, SelectedSqlServerAuthentication);
        userRow.SetVisible(state.ShowCredentials);
        passwordRow.SetVisible(state.ShowCredentials);
    }

    private sealed record RowBinding(Label Label, Control Control)
    {
        public void SetVisible(bool visible)
        {
            Label.Visible = visible;
            Control.Visible = visible;
        }
    }
}
