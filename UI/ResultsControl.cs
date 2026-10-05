using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

public sealed class ResultsControl : UserControl
{
    private const int MaximumVisibleCycles = 100;

    private readonly StageResultCard dnsCard = new("DNS");
    private readonly StageResultCard pingCard = new("Ping / ICMP");
    private readonly StageResultCard tcpCard = new("TCP");
    private readonly StageResultCard connectCard = new("Conexão DB");
    private readonly StageResultCard queryCard = new("SELECT 1");
    private readonly DataGridView grid = new();

    public ResultsControl()
    {
        Dock = DockStyle.Fill;
        MinimumSize = new Size(0, 280);

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(0, 0, 0, 8)
        };
        for (var index = 0; index < 5; index++)
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        cards.Controls.Add(dnsCard, 0, 0);
        cards.Controls.Add(pingCard, 1, 0);
        cards.Controls.Add(tcpCard, 2, 0);
        cards.Controls.Add(connectCard, 3, 0);
        cards.Controls.Add(queryCard, 4, 0);

        ConfigureGrid();

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(cards, 0, 0);
        content.Controls.Add(grid, 0, 1);

        var group = new GroupBox { Text = "Resultados recentes", Dock = DockStyle.Fill };
        group.Controls.Add(content);
        Controls.Add(group);
    }

    public void ResetResults()
    {
        dnsCard.ResetResult();
        pingCard.ResetResult();
        tcpCard.ResetResult();
        connectCard.ResetResult();
        queryCard.ResetResult();
        grid.Rows.Clear();
    }

    public void AddCycle(TestCycleResult cycle)
    {
        dnsCard.ShowResult(cycle.Dns.Status, cycle.Dns.ElapsedMs, cycle.Dns.Error);
        pingCard.ShowResult(cycle.Ping.Status, cycle.Ping.ElapsedMs, cycle.Ping.Error);
        tcpCard.ShowResult(cycle.Tcp.Status, cycle.Tcp.ElapsedMs, cycle.Tcp.Error);
        connectCard.ShowResult(cycle.Database.ConnectStatus, cycle.Database.ConnectMs,
            cycle.Database.ConnectStatus == StepStatus.Failed ? cycle.Database.Error : "");
        queryCard.ShowResult(cycle.Database.QueryStatus, cycle.Database.QueryMs,
            cycle.Database.QueryStatus == StepStatus.Failed ? cycle.Database.Error : "");

        var error = FirstNonEmpty(cycle.Dns.Error, cycle.Ping.Error, cycle.Tcp.Error, cycle.Database.Error);
        grid.Rows.Insert(0,
            cycle.Number.ToString("N0"),
            cycle.StartedAt.ToString("HH:mm:ss.fff"),
            FormatStep(cycle.Dns.Status, cycle.Dns.ElapsedMs),
            FormatStep(cycle.Ping.Status, cycle.Ping.ElapsedMs),
            FormatStep(cycle.Tcp.Status, cycle.Tcp.ElapsedMs),
            FormatStep(cycle.Database.ConnectStatus, cycle.Database.ConnectMs),
            FormatStep(cycle.Database.QueryStatus, cycle.Database.QueryMs),
            error);

        var row = grid.Rows[0];
        if (HasFailure(cycle))
            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 242, 242);
        if (!string.IsNullOrWhiteSpace(error))
            row.Cells[^1].ToolTipText = error;

        while (grid.Rows.Count > MaximumVisibleCycles)
            grid.Rows.RemoveAt(grid.Rows.Count - 1);
    }

    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.ReadOnly = true;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.Fixed3D;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 242, 245);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(grid.Font, FontStyle.Bold);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cycle", HeaderText = "Ciclo", FillWeight = 48 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Horário", FillWeight = 72 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Dns", HeaderText = "DNS", FillWeight = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ping", HeaderText = "Ping", FillWeight = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tcp", HeaderText = "TCP", FillWeight = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Connect", HeaderText = "Conexão DB", FillWeight = 82 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Query", HeaderText = "SELECT 1", FillWeight = 72 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Error", HeaderText = "Erro", FillWeight = 180 });
    }

    private static string FormatStep(StepStatus status, long elapsedMs) => status == StepStatus.Skipped
        ? "N/A"
        : $"{status.ToOutputText()} · {elapsedMs} ms";

    private static bool HasFailure(TestCycleResult cycle) =>
        cycle.Dns.Status == StepStatus.Failed ||
        cycle.Ping.Status == StepStatus.Failed ||
        cycle.Tcp.Status == StepStatus.Failed ||
        cycle.Database.ConnectStatus == StepStatus.Failed ||
        cycle.Database.QueryStatus == StepStatus.Failed;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}

internal sealed class StageResultCard : Panel
{
    private readonly Label statusLabel;
    private readonly Label elapsedLabel;
    private readonly ToolTip toolTip = new();

    public StageResultCard(string title)
    {
        var baseFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
        Dock = DockStyle.Fill;
        Margin = new Padding(4, 2, 4, 2);
        Padding = new Padding(10, 8, 10, 8);
        BorderStyle = BorderStyle.FixedSingle;

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 22,
            Font = new Font(baseFont, FontStyle.Bold)
        };
        statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(baseFont.FontFamily, 11, FontStyle.Bold)
        };
        elapsedLabel = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 20,
            ForeColor = SystemColors.GrayText
        };

        Controls.Add(statusLabel);
        Controls.Add(elapsedLabel);
        Controls.Add(titleLabel);
        ResetResult();
    }

    public void ResetResult()
    {
        statusLabel.Text = "Aguardando";
        elapsedLabel.Text = "—";
        BackColor = Color.FromArgb(247, 248, 250);
        statusLabel.ForeColor = SystemColors.GrayText;
        toolTip.SetToolTip(this, "");
        toolTip.SetToolTip(statusLabel, "");
    }

    public void ShowResult(StepStatus status, long elapsedMs, string error)
    {
        statusLabel.Text = status switch
        {
            StepStatus.Success => "Sucesso",
            StepStatus.Failed => "Falha",
            _ => "Não executado"
        };
        elapsedLabel.Text = status == StepStatus.Skipped ? "—" : $"{elapsedMs:N0} ms";
        BackColor = status switch
        {
            StepStatus.Success => Color.FromArgb(235, 248, 240),
            StepStatus.Failed => Color.FromArgb(255, 238, 238),
            _ => Color.FromArgb(247, 248, 250)
        };
        statusLabel.ForeColor = status switch
        {
            StepStatus.Success => Color.FromArgb(28, 120, 72),
            StepStatus.Failed => Color.FromArgb(185, 45, 45),
            _ => SystemColors.GrayText
        };
        toolTip.SetToolTip(this, error);
        toolTip.SetToolTip(statusLabel, error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            toolTip.Dispose();
        base.Dispose(disposing);
    }
}
