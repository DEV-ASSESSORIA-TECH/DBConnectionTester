using DBConnectionTester.Application;
using DBConnectionTester.Models;
using DBConnectionTester.Services;
using DBConnectionTester.Services.Output;
using DBConnectionTester.Services.Storage;

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
    private readonly CheckBox chkDns = new() { Text = "DNS", Checked = true, AutoSize = true };
    private readonly CheckBox chkPing = new() { Text = "Ping / ICMP", Checked = true, AutoSize = true };
    private readonly CheckBox chkTcp = new() { Text = "TCP", Checked = true, AutoSize = true };
    private readonly CheckBox chkDatabase = new() { Text = "Banco + SELECT 1", Checked = true, AutoSize = true };
    private readonly CheckBox chkBackground = new() { Text = "Minimizar para a bandeja ao iniciar", AutoSize = true };
    private readonly Button btnTestOnce = new() { Text = "Testar uma vez", AutoSize = true };
    private readonly Button btnStart = new() { Text = "Iniciar teste", AutoSize = true };
    private readonly Button btnStop = new() { Text = "Parar", AutoSize = true, Enabled = false };
    private readonly Button btnOpenCsv = new() { Text = "Abrir CSV", AutoSize = true, Enabled = false };
    private readonly Button btnOpenLog = new() { Text = "Abrir TXT", AutoSize = true, Enabled = false };
    private readonly Button btnOpenFolder = new() { Text = "Abrir pasta", AutoSize = true };
    private readonly Label lblStatus = new() { AutoSize = true, Text = "Pronto." };
    private readonly Label lblRunProgress = new();
    private readonly System.Windows.Forms.Timer elapsedTimer = new() { Interval = 1000 };
    private readonly System.Diagnostics.Stopwatch runElapsed = new();
    private TestSettings? progressSettings;
    private bool showProgressBar;
    private bool showRunProgress;
    private readonly ProgressBar progressBar = new() { Minimum = 0, Maximum = 100, Value = 0, Dock = DockStyle.Fill };
    private readonly ResultsControl resultsControl = new();
    private readonly Panel pageHost = new BufferedPanel() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel navigation = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(8)
    };
    private readonly Label globalStatus = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Dictionary<string, Button> navigationButtons = new(StringComparer.Ordinal);
    private HomePage homePage = null!;
    private UserControl executionPage = null!;
    private HistoryPage historyPage = null!;
    private ProfilesPage profilesPage = null!;
    private SettingsPage settingsPage = null!;

    private readonly NotifyIcon trayIcon = new();
    private readonly ToolStripMenuItem trayStatus = new("Pronto") { Enabled = false };
    private readonly ToolStripMenuItem trayOpenPanel = new("Abrir painel");
    private readonly ToolStripMenuItem trayOpenCsv = new("Abrir CSV") { Enabled = false };
    private readonly ToolStripMenuItem trayOpenLog = new("Abrir log TXT") { Enabled = false };
    private readonly ToolStripMenuItem trayStop = new("Encerrar teste") { Enabled = false };
    private readonly ToolStripMenuItem trayExit = new("Sair");

    private UiLayout.FieldRow hostRow = null!;
    private UiLayout.FieldRow portRow = null!;
    private UiLayout.FieldRow userRow = null!;
    private UiLayout.FieldRow passwordRow = null!;
    private UiLayout.FieldRow databaseRow = null!;
    private UiLayout.FieldRow sqlAuthRow = null!;
    private UiLayout.FieldRow odbcDriverRow = null!;
    private UiLayout.FieldRow sqliteFileRow = null!;

    private RunCoordinator runCoordinator;
    private readonly SqliteApplicationStore applicationStore;
    private ApplicationSettings applicationSettings;
    private readonly TestSettingsValidator settingsValidator = new();
    private RunUiState runUiState = RunUiState.Idle;
    private bool exitRequested;
    private bool configurationEnabled = true;
    private long completedTests;
    private string currentCsvPath = "";
    private string currentTxtPath = "";
    private Guid? selectedProfileId;

    public MainForm(SqliteApplicationStore applicationStore, ApplicationSettings applicationSettings)
    {
        this.applicationStore = applicationStore;
        this.applicationSettings = applicationSettings;
        runCoordinator = CreateRunCoordinator(applicationSettings);
        Text = $"DB Connection Tester {ApplicationInfo.Version}";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = SizeFromClientSize(new Size(800, 600));
        Size = new Size(1120, 900);
        MaximizeBox = true;

        BuildUi();
        ConfigureTray();
        WireEvents();
        ApplyDatabaseType(resetPort: true);
        ApplyRunUiState(RunUiState.Idle);
        ThemeManager.Apply(this, applicationSettings.Theme);
        Shown += async (_, _) =>
        {
            await RefreshProfilesAsync();
            await RefreshHistoryAsync();
        };
    }

    private DatabaseProfile SelectedProfile => cmbDatabaseType.SelectedItem as DatabaseProfile
        ?? DatabaseProfiles.Get(DatabaseType.MySqlMariaDb);

    private SqlServerAuthentication SelectedSqlServerAuthentication =>
        cmbSqlServerAuth.SelectedItem is SqlServerAuthentication authentication
            ? authentication
            : SqlServerAuthentication.Windows;

    private RunCoordinator CreateRunCoordinator(ApplicationSettings settings) => new(new TestRunner(
        new TestCycleExecutor(),
        new RunOutputFactory(applicationStore, settings)));

    internal ApplicationTheme ConfiguredTheme => applicationSettings.Theme;
}

internal enum RunUiState
{
    Idle,
    Running,
    Stopping,
    Completed,
    Failed
}
