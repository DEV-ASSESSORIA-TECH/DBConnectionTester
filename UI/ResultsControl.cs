using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

public sealed class ResultsControl : UserControl, IThemePaletteAware
{
    private const int MaximumVisibleCycles = 100;

    private readonly DataGridView grid = new();
    private readonly DataGridView statisticsGrid = new();
    private readonly StatisticsDashboardControl statisticsDashboard = new();
    private RunStatisticsSnapshot? latestStatistics;
    private bool statisticsDirty;
    private MetricColors colors = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridViewRow, RowAppearance> rowAppearances = new();
    private sealed class RowAppearance { public bool Failure; }

    private void SetRowAppearance(DataGridViewRow row, bool failure)
    {
        rowAppearances.GetOrCreateValue(row).Failure = failure;
        row.DefaultCellStyle.BackColor = failure ? colors.FailureBackground : colors.NormalStatisticsBackground;
    }

    void IThemePaletteAware.ApplyPalette(ThemePalette palette)
    {
        if (colors == palette.Metrics) return;
        colors = palette.Metrics;
        // Recolor only existing explicit row styles; no data loading or statistics rebuild.
        foreach (var table in new[] { grid, statisticsGrid })
            foreach (DataGridViewRow row in table.Rows)
                if (rowAppearances.TryGetValue(row, out var appearance))
                    row.DefaultCellStyle.BackColor = appearance.Failure ? colors.FailureBackground : colors.NormalStatisticsBackground;
    }

    public ResultsControl()
    {
        Dock = DockStyle.Fill;
        MinimumSize = new Size(0, 150);

        ConfigureGrid();
        ConfigureStatisticsGrid();
        statisticsGrid.VisibleChanged += (_, _) => RenderStatistics();

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var recentTab = new TabPage("Ciclos recentes");
        var statisticsTab = new TabPage("Resumo estatístico");
        var trendTab = new TabPage("Tendência");
        recentTab.Controls.Add(grid);
        statisticsTab.Controls.Add(statisticsGrid);
        trendTab.Controls.Add(statisticsDashboard);
        tabs.TabPages.Add(statisticsTab);
        tabs.TabPages.Add(recentTab);
        tabs.TabPages.Add(trendTab);

        Controls.Add(tabs);
    }

    public void ResetResults()
    {
        grid.Rows.Clear();
        ResetStatistics();
        statisticsDashboard.ResetDashboard();
    }

    public void UpdateStatistics(RunStatisticsSnapshot statistics)
    {
        if (latestStatistics != statistics) statisticsDirty = true;
        latestStatistics = statistics;
        RenderStatistics();
        statisticsDashboard.UpdateStatistics(statistics);
    }

    private void RenderStatistics()
    {
        if (!statisticsGrid.Visible || latestStatistics is not { } statistics) return;
        var stages = new[] { statistics.Dns, statistics.Ping, statistics.Tcp, statistics.DatabaseConnect, statistics.DatabaseQuery };
        for (var i = 0; i < stages.Length; i++)
            statisticsGrid.Rows[i].Cells[10].Value = FormatElapsed(stages[i].TimeSinceLastFailure(DateTimeOffset.Now));
        if (!statisticsDirty) return;
        statisticsDirty = false;
        UpdateStatisticsRow(0, statistics.Dns);
        UpdateStatisticsRow(1, statistics.Ping);
        UpdateStatisticsRow(2, statistics.Tcp);
        UpdateStatisticsRow(3, statistics.DatabaseConnect);
        UpdateStatisticsRow(4, statistics.DatabaseQuery);
    }

    public void AddCycle(TestCycleResult cycle)
    {
        var diagnostic = FirstDiagnostic(
            cycle.Dns.Diagnostic,
            cycle.Ping.Diagnostic,
            cycle.Tcp.Diagnostic,
            cycle.Database.ConnectDiagnostic,
            cycle.Database.QueryDiagnostic);
        var error = DiagnosticFormatting.Compact(diagnostic);
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
            SetRowAppearance(row, failure: true);
        if (!string.IsNullOrWhiteSpace(error))
            row.Cells[^1].ToolTipText = DiagnosticFormatting.Detailed(diagnostic);

        while (grid.Rows.Count > MaximumVisibleCycles)
            grid.Rows.RemoveAt(grid.Rows.Count - 1);
        statisticsDashboard.AddCycle(cycle);
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
        grid.BackgroundColor = colors.GridBackground;
        grid.BorderStyle = BorderStyle.Fixed3D;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = colors.GridHeader;
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

    private void ConfigureStatisticsGrid()
    {
        statisticsGrid.Dock = DockStyle.Fill;
        statisticsGrid.AllowUserToAddRows = false;
        statisticsGrid.AllowUserToDeleteRows = false;
        statisticsGrid.AllowUserToResizeRows = false;
        statisticsGrid.ReadOnly = true;
        statisticsGrid.MultiSelect = false;
        statisticsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        statisticsGrid.RowHeadersVisible = false;
        statisticsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        statisticsGrid.BackgroundColor = colors.GridBackground;
        statisticsGrid.BorderStyle = BorderStyle.Fixed3D;
        statisticsGrid.EnableHeadersVisualStyles = false;
        statisticsGrid.ColumnHeadersDefaultCellStyle.BackColor = colors.GridHeader;
        statisticsGrid.ColumnHeadersDefaultCellStyle.Font = new Font(statisticsGrid.Font, FontStyle.Bold);
        statisticsGrid.Columns.Add("Stage", "Etapa");
        statisticsGrid.Columns.Add("Success", "Resultados");
        statisticsGrid.Columns.Add("Rate", "Taxa");
        statisticsGrid.Columns.Add("Average", "Média");
        statisticsGrid.Columns.Add("Minimum", "Mínimo");
        statisticsGrid.Columns.Add("Maximum", "Máximo");
        statisticsGrid.Columns.Add("Median", "Mediana");
        statisticsGrid.Columns.Add("P95", "P95");
        statisticsGrid.Columns.Add("Streak", "Falhas seguidas");
        statisticsGrid.Columns.Add("MaxStreak", "Maior sequência");
        statisticsGrid.Columns.Add("SinceFailure", "Desde última falha");
        statisticsGrid.Columns[0].DefaultCellStyle.Font = new Font(statisticsGrid.Font, FontStyle.Bold);
        statisticsGrid.Columns[1].FillWeight = 130;
        statisticsGrid.Columns[10].FillWeight = 135;

        foreach (var stage in new[] { "DNS", "Ping / ICMP", "TCP", "Conexão DB", "SELECT 1" })
            statisticsGrid.Rows.Add(stage, "—", "—", "—", "—", "—", "—", "—", "0", "0", "Nunca");
    }

    private void ResetStatistics()
    {
        latestStatistics = null;
        statisticsDirty = false;
        foreach (DataGridViewRow row in statisticsGrid.Rows)
        {
            for (var column = 1; column <= 7; column++)
                row.Cells[column].Value = "—";
            row.Cells[8].Value = "0";
            row.Cells[9].Value = "0";
            row.Cells[10].Value = "Nunca";
        }
    }

    private void UpdateStatisticsRow(int rowIndex, StageStatisticsSnapshot statistics)
    {
        var row = statisticsGrid.Rows[rowIndex];
        row.Cells[1].Value = statistics.Attempts == 0
            ? "—"
            : $"{statistics.Successes:N0} OK · {statistics.Failures:N0} falhas";
        row.Cells[2].Value = statistics.SuccessRate is null ? "—" : $"{statistics.SuccessRate:0.0}%";
        row.Cells[3].Value = FormatLatency(statistics.AverageMs);
        row.Cells[4].Value = FormatLatency(statistics.MinimumMs);
        row.Cells[5].Value = FormatLatency(statistics.MaximumMs);
        row.Cells[6].Value = FormatLatency(statistics.MedianMs);
        row.Cells[7].Value = FormatLatency(statistics.P95Ms);
        row.Cells[8].Value = statistics.ConsecutiveFailures.ToString("N0");
        row.Cells[9].Value = statistics.MaximumConsecutiveFailures.ToString("N0");
        row.Cells[10].Value = FormatElapsed(statistics.TimeSinceLastFailure(DateTimeOffset.Now));
        SetRowAppearance(row, statistics.ConsecutiveFailures > 0);
    }

    private static string FormatLatency(double? milliseconds) =>
        milliseconds is null ? "—" : $"{milliseconds:0.0} ms";

    private static string FormatElapsed(TimeSpan? elapsed)
    {
        if (elapsed is null)
            return "Nunca";
        if (elapsed.Value.TotalSeconds < 60)
            return $"{Math.Max(0, elapsed.Value.TotalSeconds):0} s";
        if (elapsed.Value.TotalMinutes < 60)
            return $"{elapsed.Value.TotalMinutes:0.0} min";
        if (elapsed.Value.TotalHours < 24)
            return $"{elapsed.Value.TotalHours:0.0} h";
        return $"{elapsed.Value.TotalDays:0.0} dias";
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

    private static DiagnosticIssue? FirstDiagnostic(params DiagnosticIssue?[] values) =>
        values.FirstOrDefault(value => value is not null);
}

