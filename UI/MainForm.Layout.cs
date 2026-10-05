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

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 5,
            AutoScroll = true
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var settingsArea = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        settingsArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        settingsArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        settingsArea.Controls.Add(BuildConnectionGroup(), 0, 0);
        settingsArea.Controls.Add(BuildExecutionGroup(), 1, 0);

        root.Controls.Add(settingsArea, 0, 0);
        root.Controls.Add(BuildOutputGroup(), 0, 1);
        root.Controls.Add(BuildControlArea(), 0, 2);
        root.Controls.Add(resultsControl, 0, 3);
        root.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(6, 8, 6, 0),
            Text = "Credenciais ficam somente na memória e não são gravadas nos relatórios. " +
                   "A grade mantém os 100 ciclos mais recentes; CSV e TXT preservam toda a execução."
        }, 0, 4);

        Controls.Add(root);
    }

    private GroupBox BuildConnectionGroup()
    {
        var table = CreateSettingsTable(labelWidth: 145);
        var row = 0;
        AddRow(table, row++, "Tipo de banco:", cmbDatabaseType);
        hostRow = AddRow(table, row++, "Servidor / host:", txtHost);
        portRow = AddRow(table, row++, "Porta:", numPort);
        sqlAuthRow = AddRow(table, row++, "Autenticação:", cmbSqlServerAuth);
        userRow = AddRow(table, row++, "Usuário:", txtUser);
        passwordRow = AddRow(table, row++, "Senha:", txtPassword);
        databaseRow = AddRow(table, row++, "Banco (opcional):", txtDatabase);
        odbcDriverRow = AddRow(table, row++, "Driver ODBC:", txtOdbcDriver);

        var sqlitePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Margin = Padding.Empty
        };
        sqlitePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sqlitePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        txtSqliteFile.Dock = DockStyle.Fill;
        sqlitePanel.Controls.Add(txtSqliteFile, 0, 0);
        sqlitePanel.Controls.Add(btnBrowseSqlite, 1, 0);
        sqliteFileRow = AddRow(table, row, "Arquivo SQLite:", sqlitePanel);

        return CreateGroup("Conexão", table);
    }

    private GroupBox BuildExecutionGroup()
    {
        var table = CreateSettingsTable(labelWidth: 170);
        var row = 0;
        AddRow(table, row++, "Quantidade de testes:", numTests);
        AddRow(table, row++, "Modo de execução:", chkContinuous);
        AddRow(table, row++, "Intervalo entre testes:", WithSuffix(numInterval, "segundos"));
        AddRow(table, row++, "Timeout por etapa:", WithSuffix(numTimeout, "segundos"));

        var layers = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        layers.Controls.AddRange(new Control[] { chkDns, chkPing, chkTcp, chkDatabase });
        AddRow(table, row++, "Camadas:", layers);
        AddRow(table, row, "Comportamento:", chkBackground);

        return CreateGroup("Execução", table);
    }

    private GroupBox BuildOutputGroup()
    {
        var table = CreateSettingsTable(labelWidth: 145);
        txtOutput.Dock = DockStyle.Fill;
        AddRowWithButton(table, 0, "Arquivo CSV:", txtOutput, btnBrowseOutput);
        return CreateGroup("Saída", table);
    }

    private Control BuildControlArea()
    {
        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 6, 0, 6)
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty
        };
        btnTestOnce.Font = new Font(btnTestOnce.Font, FontStyle.Bold);
        btnStart.Font = new Font(btnStart.Font, FontStyle.Bold);
        buttons.Controls.AddRange(new Control[]
        {
            btnTestOnce, btnStart, btnStop, btnOpenCsv, btnOpenLog, btnOpenFolder
        });

        progressBar.Margin = new Padding(3, 8, 3, 3);
        lblStatus.Margin = new Padding(4, 4, 4, 0);
        area.Controls.Add(buttons, 0, 0);
        area.Controls.Add(progressBar, 0, 1);
        area.Controls.Add(lblStatus, 0, 2);
        return area;
    }

    private static TableLayoutPanel CreateSettingsTable(int labelWidth) => new TableLayoutPanel
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        ColumnCount = 3,
        Padding = new Padding(8)
    }.WithColumns(labelWidth);

    private static GroupBox CreateGroup(string title, Control content)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(4)
        };
        group.Controls.Add(content);
        return group;
    }

    private static Control WithSuffix(Control control, string suffix)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, 0, 0);
        panel.Controls.Add(new Label { Text = suffix, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 4, 0, 0) }, 1, 0);
        return panel;
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

    private static void AddRowWithButton(TableLayoutPanel table, int row, string text, Control control, Control button)
    {
        table.Controls.Add(new Label { Text = text, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 1, row);
        table.Controls.Add(button, 2, row);
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
        btnTestOnce.Click += async (_, _) => await StartAsync(singleRun: true);
        btnStart.Click += async (_, _) => await StartAsync(singleRun: false);
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
            chkDns.Checked = false;
            chkPing.Checked = false;
            chkTcp.Checked = false;
            chkDatabase.Checked = true;
        }
        else if (resetPort && state.RequireTcp)
        {
            chkDns.Checked = true;
            chkPing.Checked = true;
            chkTcp.Checked = true;
            chkDatabase.Checked = false;
        }
        else if (resetPort)
        {
            chkDns.Checked = true;
            chkPing.Checked = true;
            chkTcp.Checked = true;
            chkDatabase.Checked = true;
        }

        chkDns.Enabled = configurationEnabled && state.AllowDns;
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

internal static class TableLayoutExtensions
{
    public static TableLayoutPanel WithColumns(this TableLayoutPanel table, int labelWidth)
    {
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return table;
    }
}
