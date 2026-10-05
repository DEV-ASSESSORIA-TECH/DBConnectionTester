using System.Diagnostics;
using DBConnectionTester.Application;
using DBConnectionTester.Models;
using DBConnectionTester.Services;

namespace DBConnectionTester.UI;

public sealed class MainForm : Form
{
    private readonly ComboBox cmbDatabaseType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtHost = new() { Text = "127.0.0.1" };
    private readonly NumericUpDown numPort = new() { Minimum = 1, Maximum = 65535, Value = 3306 };
    private readonly TextBox txtUser = new();
    private readonly TextBox txtPassword = new() { UseSystemPasswordChar = true };
    private readonly TextBox txtDatabase = new();
    private readonly ComboBox cmbSqlServerAuth = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtOdbcDriver = new() { Text = "SQL Anywhere 17" };
    private readonly TextBox txtSqliteFile = new();
    private readonly Button btnBrowseSqlite = new() { Text = "Escolher...", AutoSize = true };
    private readonly NumericUpDown numTests = new() { Minimum = 1, Maximum = 10_000_000, Value = 1000, ThousandsSeparator = true };
    private readonly CheckBox chkContinuous = new() { Text = "Execução contínua (até encerrar manualmente)", AutoSize = true };
    private readonly NumericUpDown numInterval = new() { Minimum = 0, Maximum = 3600, Value = 5, DecimalPlaces = 1, Increment = 0.5M };
    private readonly NumericUpDown numTimeout = new() { Minimum = 1, Maximum = 120, Value = 5 };
    private readonly CheckBox chkPing = new() { Text = "Ping / ICMP", Checked = true, AutoSize = true };
    private readonly CheckBox chkTcp = new() { Text = "TCP", Checked = true, AutoSize = true };
    private readonly CheckBox chkDatabase = new() { Text = "Banco + SELECT 1", Checked = true, AutoSize = true };
    private readonly CheckBox chkBackground = new() { Text = "Minimizar para a bandeja ao iniciar", Checked = true, AutoSize = true };
    private readonly TextBox txtOutput = new();
    private readonly Button btnBrowseOutput = new() { Text = "Escolher..." };
    private readonly Button btnStart = new() { Text = "Iniciar teste", AutoSize = true };
    private readonly Button btnStop = new() { Text = "Parar", AutoSize = true, Enabled = false };
    private readonly Button btnOpenCsv = new() { Text = "Abrir CSV", AutoSize = true, Enabled = false };
    private readonly Button btnOpenLog = new() { Text = "Abrir TXT", AutoSize = true, Enabled = false };
    private readonly Button btnOpenFolder = new() { Text = "Abrir pasta", AutoSize = true };
    private readonly Label lblStatus = new() { AutoSize = true, Text = "Pronto." };
    private readonly ProgressBar progressBar = new() { Minimum = 0, Maximum = 100, Value = 0, Dock = DockStyle.Fill };

    private readonly NotifyIcon trayIcon = new();
    private readonly ToolStripMenuItem trayStatus = new("Pronto") { Enabled = false };
    private readonly ToolStripMenuItem trayOpenPanel = new("Abrir painel");
    private readonly ToolStripMenuItem trayOpenCsv = new("Abrir CSV") { Enabled = false };
    private readonly ToolStripMenuItem trayOpenLog = new("Abrir log TXT") { Enabled = false };
    private readonly ToolStripMenuItem trayStop = new("Encerrar teste") { Enabled = false };
    private readonly ToolStripMenuItem trayExit = new("Sair");

    private RowBinding hostRow = null!;
    private RowBinding portRow = null!;
    private RowBinding userRow = null!;
    private RowBinding passwordRow = null!;
    private RowBinding databaseRow = null!;
    private RowBinding sqlAuthRow = null!;
    private RowBinding odbcDriverRow = null!;
    private RowBinding sqliteFileRow = null!;

    private readonly RunCoordinator runCoordinator = new(new TestRunner());
    private readonly TestSettingsValidator settingsValidator = new(new OutputPathPolicy());
    private bool exitRequested;
    private bool configurationEnabled = true;
    private long completedTests;
    private string currentCsvPath = "";
    private string currentTxtPath = "";

    public MainForm()
    {
        Text = "DB Connection Tester";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 720);
        Size = new Size(860, 800);
        MaximizeBox = false;

        txtOutput.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"connection_test_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        BuildUi();
        ConfigureTray();
        WireEvents();
        ApplyDatabaseType(resetPort: true);
    }

    private DatabaseProfile SelectedProfile => cmbDatabaseType.SelectedItem as DatabaseProfile
        ?? DatabaseProfiles.Get(DatabaseType.MySqlMariaDb);

    private SqlServerAuthentication SelectedSqlServerAuthentication =>
        cmbSqlServerAuth.SelectedItem is SqlServerAuthentication authentication
            ? authentication
            : SqlServerAuthentication.Windows;

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

        var outputLabel = new Label { Text = "Arquivo CSV:", Anchor = AnchorStyles.Left, AutoSize = true };
        table.Controls.Add(outputLabel, 0, row);
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

    private void BrowseSqliteFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Bancos SQLite (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
            FileName = Path.GetFileName(txtSqliteFile.Text),
            InitialDirectory = GetExistingDirectory(txtSqliteFile.Text)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            txtSqliteFile.Text = dialog.FileName;
    }

    private void BrowseOutputFile()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv|Todos os arquivos (*.*)|*.*",
            FileName = Path.GetFileName(txtOutput.Text),
            InitialDirectory = GetExistingDirectory(txtOutput.Text)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            txtOutput.Text = dialog.FileName;
    }

    private async Task StartAsync()
    {
        if (runCoordinator.IsRunning)
            return;

        var settings = TryBuildSettings();
        if (settings is null)
            return;

        currentCsvPath = settings.CsvPath;
        currentTxtPath = settings.TxtPath;
        exitRequested = false;
        completedTests = 0;
        SetConfigurationEnabled(false);
        btnStart.Enabled = false;
        btnStop.Enabled = true;
        btnOpenCsv.Enabled = true;
        btnOpenLog.Enabled = true;
        trayStop.Enabled = true;
        trayOpenCsv.Enabled = true;
        trayOpenLog.Enabled = true;

        if (settings.Continuous)
        {
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.MarqueeAnimationSpeed = 30;
        }
        else
        {
            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.Value = 0;
        }

        UpdateTrayStatus(settings, new TestProgress(0, 0, 0, 0, 0, 0));
        if (chkBackground.Checked)
            HideToTray("Teste iniciado em segundo plano.");

        try
        {
            var uiProgress = new Progress<TestProgress>(value => UpdateProgress(settings, value));
            var summary = await runCoordinator.StartAsync(settings, uiProgress);
            completedTests = summary.Completed;
            CompleteRun(settings, summary);
        }
        catch (Exception exception)
        {
            lblStatus.Text = $"Erro: {exception.Message}";
            ShowPanel();
            MessageBox.Show(this, exception.ToString(), "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetConfigurationEnabled(true);
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            trayStop.Enabled = false;
            progressBar.Style = ProgressBarStyle.Blocks;
            if (exitRequested)
            {
                trayIcon.Visible = false;
                Close();
            }
        }
    }

    private TestSettings? TryBuildSettings()
    {
        var input = new TestSettingsInput(
            SelectedProfile.Type,
            txtHost.Text,
            (int)numPort.Value,
            txtUser.Text,
            txtPassword.Text,
            txtDatabase.Text,
            txtSqliteFile.Text,
            SelectedSqlServerAuthentication,
            txtOdbcDriver.Text,
            (long)numTests.Value,
            chkContinuous.Checked,
            TimeSpan.FromSeconds((double)numInterval.Value),
            TimeSpan.FromSeconds((double)numTimeout.Value),
            chkPing.Checked,
            chkTcp.Checked,
            chkDatabase.Checked,
            txtOutput.Text);

        var result = settingsValidator.Validate(input);
        return result.IsValid ? result.Settings : ValidationError(result.ErrorMessage);
    }

    private TestSettings? ValidationError(string message)
    {
        MessageBox.Show(this, message, "Validação", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return null;
    }

    private void UpdateProgress(TestSettings settings, TestProgress value)
    {
        completedTests = value.Completed;
        if (!settings.Continuous)
        {
            var percentage = (int)Math.Round(value.Completed * 100.0 / settings.TestCount);
            progressBar.Value = Math.Clamp(percentage, 0, 100);
        }
        var prefix = settings.Continuous
            ? $"Executando continuamente | {value.Completed:N0} testes"
            : $"Executando {value.Completed:N0}/{settings.TestCount:N0}";
        lblStatus.Text = $"{prefix} | DNS: {value.DnsFailures} | Ping: {value.PingFailures} | " +
                         $"TCP: {value.TcpFailures} | DB conexão: {value.DatabaseConnectFailures} | " +
                         $"DB consulta: {value.DatabaseQueryFailures}";
        UpdateTrayStatus(settings, value);
    }

    private void CompleteRun(TestSettings settings, RunSummary summary)
    {
        if (summary.Stopped)
        {
            lblStatus.Text = $"Teste interrompido. {summary.Completed:N0} verificações gravadas.";
            trayStatus.Text = $"Interrompido - {summary.Completed:N0} testes";
            SafeTrayText($"DB Connection Tester - parado - {summary.Completed:N0}");
            return;
        }

        lblStatus.Text = $"Concluído. CSV: {Path.GetFileName(settings.CsvPath)} | TXT: {Path.GetFileName(settings.TxtPath)}";
        progressBar.Value = 100;
        trayStatus.Text = $"Concluído - {summary.Completed:N0} testes";
        SafeTrayText($"DB Connection Tester - concluído - {summary.Completed:N0}");
        trayIcon.Visible = true;
        trayIcon.BalloonTipTitle = "DB Connection Tester";
        trayIcon.BalloonTipText = $"Teste concluído. {summary.Completed:N0} verificações executadas.";
        trayIcon.BalloonTipIcon = ToolTipIcon.Info;
        trayIcon.ShowBalloonTip(3000);
    }

    private void RequestStop()
    {
        if (!runCoordinator.IsRunning)
            return;
        lblStatus.Text = "Encerrando teste e finalizando o log...";
        trayStatus.Text = "Encerrando teste...";
        trayStop.Enabled = false;
        runCoordinator.Stop();
    }

    private void RequestExit()
    {
        if (runCoordinator.IsRunning)
        {
            var result = MessageBox.Show(
                "Há um teste em andamento. Deseja encerrar o teste, finalizar o log e sair?",
                "Sair do DB Connection Tester",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
                return;
            exitRequested = true;
            trayStop.Enabled = false;
            runCoordinator.Stop();
            return;
        }

        exitRequested = true;
        trayIcon.Visible = false;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            exitRequested = true;
            runCoordinator.Stop();
            trayIcon.Visible = false;
            return;
        }
        if (runCoordinator.IsRunning && !exitRequested)
        {
            e.Cancel = true;
            HideToTray("Teste continua em execução.");
        }
    }

    private void SetConfigurationEnabled(bool enabled)
    {
        configurationEnabled = enabled;
        foreach (var control in new Control[]
                 {
                     cmbDatabaseType, txtHost, numPort, cmbSqlServerAuth, txtUser, txtPassword, txtDatabase,
                     txtOdbcDriver, txtSqliteFile, btnBrowseSqlite, numInterval, numTimeout, chkContinuous,
                     chkBackground, txtOutput, btnBrowseOutput
                 })
            control.Enabled = enabled;
        numTests.Enabled = enabled && !chkContinuous.Checked;
        ApplyDatabaseType(resetPort: false);
    }

    private void UpdateTrayStatus(TestSettings settings, TestProgress value)
    {
        trayIcon.Visible = true;
        trayStatus.Text = settings.Continuous
            ? $"Executando - {value.Completed:N0} testes"
            : $"Executando - {value.Completed:N0}/{settings.TestCount:N0}";
        var tooltip = settings.Continuous
            ? $"DB Tester - {value.Completed:N0} - falhas P:{value.PingFailures} T:{value.TcpFailures} DB:{value.DatabaseFailures}"
            : $"DB Tester - {value.Completed:N0}/{settings.TestCount:N0} - falhas P:{value.PingFailures} T:{value.TcpFailures} DB:{value.DatabaseFailures}";
        SafeTrayText(tooltip);
    }

    private void HideToTray(string? balloonText = null)
    {
        trayIcon.Visible = true;
        Hide();
        ShowInTaskbar = false;
        if (string.IsNullOrWhiteSpace(balloonText))
            return;
        trayIcon.BalloonTipTitle = "DB Connection Tester";
        trayIcon.BalloonTipText = balloonText;
        trayIcon.BalloonTipIcon = ToolTipIcon.Info;
        trayIcon.ShowBalloonTip(2000);
    }

    private void ShowPanel()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void OpenOutputFolder()
    {
        var path = !string.IsNullOrWhiteSpace(currentCsvPath) ? currentCsvPath : txtOutput.Text.Trim();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }

    private static void OpenFile(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string GetExistingDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? directory
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    private void SafeTrayText(string text) => trayIcon.Text = text.Length <= 63 ? text : text[..63];

    private sealed record RowBinding(Label Label, Control Control)
    {
        public void SetVisible(bool visible)
        {
            Label.Visible = visible;
            Control.Visible = visible;
        }
    }
}
