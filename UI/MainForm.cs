using DBConnectionTester.Application;
using DBConnectionTester.Models;
using DBConnectionTester.Services;

namespace DBConnectionTester.UI;

public sealed partial class MainForm : Form
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
    private RunUiState runUiState = RunUiState.Idle;
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
        ApplyRunUiState(RunUiState.Idle);
    }

    private DatabaseProfile SelectedProfile => cmbDatabaseType.SelectedItem as DatabaseProfile
        ?? DatabaseProfiles.Get(DatabaseType.MySqlMariaDb);

    private SqlServerAuthentication SelectedSqlServerAuthentication =>
        cmbSqlServerAuth.SelectedItem is SqlServerAuthentication authentication
            ? authentication
            : SqlServerAuthentication.Windows;
}

internal enum RunUiState
{
    Idle,
    Running,
    Stopping,
    Completed,
    Failed
}
