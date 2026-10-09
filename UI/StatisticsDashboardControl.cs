using System.Drawing.Drawing2D;
using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

public sealed class StatisticsDashboardControl : UserControl, IThemePaletteAware
{
    private const int MaximumPoints = 100;

    private readonly ComboBox stageSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly Label trendLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly LatencyTrendControl chart = new();
    private readonly Dictionary<MetricStage, List<TrendPoint>> histories =
        Enum.GetValues<MetricStage>().ToDictionary(stage => stage, _ => new List<TrendPoint>());

    private RunStatisticsSnapshot? latestStatistics;
    private bool dashboardDirty = true;
    private MetricColors colors = new();

    private enum TrendTone { Waiting, Attention, Stable, Improving, Degrading }
    private TrendTone trendTone;

    void IThemePaletteAware.ApplyPalette(ThemePalette palette)
    {
        colors = palette.Metrics;
        // The generic theme pass also styles the label; restore its cached trend tone.
        ApplyTrendColor();
    }

    private void ApplyTrendColor() => trendLabel.ForeColor = trendTone switch
    {
        TrendTone.Attention => colors.Attention,
        TrendTone.Stable => colors.Stable,
        TrendTone.Improving => colors.Improving,
        TrendTone.Degrading => colors.Degrading,
        _ => colors.Muted
    };

    public StatisticsDashboardControl()
    {
        Font = UiTypography.Body;
        UiStyle.SetRole(this, UiRole.Card);
        UiStyle.SetRole(trendLabel, UiRole.Status);
        Dock = DockStyle.Fill;
        stageSelector.Items.AddRange(new object[]
        {
            new StageOption("DNS", MetricStage.Dns),
            new StageOption("Ping / ICMP", MetricStage.Ping),
            new StageOption("TCP", MetricStage.Tcp),
            new StageOption("Conexão DB", MetricStage.DatabaseConnect),
            new StageOption("SELECT 1", MetricStage.DatabaseQuery)
        });
        stageSelector.SelectedIndexChanged += (_, _) => { dashboardDirty = true; RefreshDashboard(); };
        stageSelector.SelectedIndex = 0;

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(4, 3, 4, 0)
        };
        toolbar.Controls.Add(new Label { Text = "Etapa:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        toolbar.Controls.Add(stageSelector);
        trendLabel.Margin = new Padding(14, 6, 0, 0);
        toolbar.Controls.Add(trendLabel);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(chart, 0, 1);
        Controls.Add(layout);
        VisibleChanged += (_, _) => RefreshDashboard();
        ResetDashboard();
    }

    public void ResetDashboard()
    {
        latestStatistics = null;
        dashboardDirty = true;
        foreach (var history in histories.Values)
            history.Clear();
        RefreshDashboard();
    }

    public void AddCycle(TestCycleResult cycle)
    {
        dashboardDirty = true;
        AddPoint(MetricStage.Dns, cycle.Number, cycle.Dns.Status, cycle.Dns.ElapsedMs);
        AddPoint(MetricStage.Ping, cycle.Number, cycle.Ping.Status, cycle.Ping.ElapsedMs);
        AddPoint(MetricStage.Tcp, cycle.Number, cycle.Tcp.Status, cycle.Tcp.ElapsedMs);
        AddPoint(MetricStage.DatabaseConnect, cycle.Number, cycle.Database.ConnectStatus, cycle.Database.ConnectMs);
        AddPoint(MetricStage.DatabaseQuery, cycle.Number, cycle.Database.QueryStatus, cycle.Database.QueryMs);
    }

    public void UpdateStatistics(RunStatisticsSnapshot statistics)
    {
        if (latestStatistics != statistics) dashboardDirty = true;
        latestStatistics = statistics;
        RefreshDashboard();
    }

    private void AddPoint(MetricStage stage, long cycle, StepStatus status, long elapsedMs)
    {
        var history = histories[stage];
        history.Add(new TrendPoint(cycle, status, elapsedMs));
        if (history.Count > MaximumPoints)
            history.RemoveAt(0);
    }

    private void RefreshDashboard()
    {
        if (!Visible) return;
        if (stageSelector.SelectedItem is not StageOption option)
            return;

        var statistics = SelectStatistics(option.Stage);
        if (!dashboardDirty) return;
        dashboardDirty = false;
        if (statistics is null)
        {
            trendLabel.Text = "Aguardando dados";
            trendTone = TrendTone.Waiting;
            UiStyle.SetState(trendLabel, UiState.Normal);
            ApplyTrendColor();
            chart.SetData(Array.Empty<TrendPoint>(), null, null);
            return;
        }

        if (statistics.ConsecutiveFailures > 0)
        {
            trendLabel.Text = $"Atenção: {statistics.ConsecutiveFailures:N0} falha(s) consecutiva(s)";
            trendTone = TrendTone.Attention;
            UiStyle.SetState(trendLabel, UiState.Error);
        }
        else
        {
            var direction = LatencyTrendAnalyzer.Analyze(
                histories[option.Stage]
                    .Where(point => point.Status == StepStatus.Success)
                    .Select(point => point.ElapsedMs));
            (trendLabel.Text, trendTone) = direction switch
            {
                LatencyTrendDirection.Stable => ("Tendência estável", TrendTone.Stable),
                LatencyTrendDirection.Improving => ("Latência melhorando", TrendTone.Improving),
                LatencyTrendDirection.Degrading => ("Latência degradando", TrendTone.Degrading),
                _ => ("Dados insuficientes para tendência", TrendTone.Waiting)
            };
        }

        UiStyle.SetState(trendLabel, trendTone switch
        {
            TrendTone.Attention => UiState.Error,
            TrendTone.Degrading => UiState.Warning,
            TrendTone.Improving => UiState.Success,
            _ => UiState.Normal
        });
        ApplyTrendColor();
        chart.SetData(histories[option.Stage], statistics.MedianMs, statistics.P95Ms);
    }

    private StageStatisticsSnapshot? SelectStatistics(MetricStage stage) => latestStatistics is null
        ? null
        : stage switch
        {
            MetricStage.Dns => latestStatistics.Dns,
            MetricStage.Ping => latestStatistics.Ping,
            MetricStage.Tcp => latestStatistics.Tcp,
            MetricStage.DatabaseConnect => latestStatistics.DatabaseConnect,
            _ => latestStatistics.DatabaseQuery
        };

    private sealed record StageOption(string Name, MetricStage Stage)
    {
        public override string ToString() => Name;
    }
}

internal enum MetricStage
{
    Dns,
    Ping,
    Tcp,
    DatabaseConnect,
    DatabaseQuery
}

internal readonly record struct TrendPoint(long Cycle, StepStatus Status, long ElapsedMs);

internal sealed class LatencyTrendControl : Control, IThemePaletteAware
{
    private IReadOnlyList<TrendPoint> points = Array.Empty<TrendPoint>();
    private double? median;
    private double? p95;
    private MetricColors colors = new();

    void IThemePaletteAware.ApplyPalette(ThemePalette palette)
    {
        if (colors == palette.Metrics) return;
        colors = palette.Metrics;
        Invalidate();
    }

    public LatencyTrendControl()
    {
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        BackColor = colors.GridBackground;
        Margin = new Padding(3);
    }

    public void SetData(IReadOnlyList<TrendPoint> values, double? medianMs, double? p95Ms)
    {
        points = values.ToArray();
        median = medianMs;
        p95 = p95Ms;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var successful = points.Where(point => point.Status == StepStatus.Success).ToArray();
        var maximum = Math.Max(1d, new[]
        {
            successful.Length == 0 ? 0 : successful.Max(point => (double)point.ElapsedMs),
            median ?? 0,
            p95 ?? 0
        }.Max() * 1.1);
        var labelHeight = Font.Height + 4;
        var axisWidth = TextRenderer.MeasureText($"{maximum:0} ms", Font).Width + 6;
        var area = new Rectangle(axisWidth, labelHeight + 6,
            Math.Max(1, Width - axisWidth - 12), Math.Max(1, Height - labelHeight * 2 - 12));
        if (Width <= axisWidth + 12 || Height <= labelHeight * 2 + 12) return;
        using var borderPen = new Pen(colors.ChartGrid);
        e.Graphics.DrawRectangle(borderPen, area);

        if (successful.Length == 0)
        {
            TextRenderer.DrawText(e.Graphics, "Sem latências para exibir", Font, area,
                colors.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        using var gridPen = new Pen(colors.ChartGrid) { DashStyle = DashStyle.Dot };
        e.Graphics.DrawLine(gridPen, area.Left, area.Top + area.Height / 2f, area.Right, area.Top + area.Height / 2f);
        DrawReference(e.Graphics, area, maximum, median, colors.Median);
        DrawReference(e.Graphics, area, maximum, p95, colors.P95);
        // Keep reference labels outside the plot so nearby percentiles cannot overlap.
        var legendX = area.Left;
        foreach (var reference in new[] { ("Mediana", median, colors.Median), ("P95", p95, colors.P95) })
        {
            if (reference.Item2 is null) continue;
            var text = $"{reference.Item1}: {reference.Item2:0.0} ms";
            var width = TextRenderer.MeasureText(text, Font).Width;
            TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(legendX, 0, Math.Max(0, area.Right - legendX), labelHeight),
                reference.Item3, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            legendX += width + 16;
        }

        using var linePen = new Pen(colors.Series, 2);
        using var successBrush = new SolidBrush(colors.Series);
        using var failurePen = new Pen(colors.Failure, 2);
        using var skippedPen = new Pen(colors.Skipped);
        PointF? previous = null;
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var x = area.Left + (points.Count == 1 ? area.Width / 2f : index * area.Width / (float)(points.Count - 1));
            if (point.Status == StepStatus.Success)
            {
                var y = area.Bottom - (float)(point.ElapsedMs / maximum * area.Height);
                var current = new PointF(x, y);
                if (previous is not null)
                    e.Graphics.DrawLine(linePen, previous.Value, current);
                e.Graphics.FillEllipse(successBrush, x - 2.5f, y - 2.5f, 5, 5);
                previous = current;
            }
            else
            {
                previous = null;
                var markerY = area.Bottom - 5;
                if (point.Status == StepStatus.Failed)
                {
                    e.Graphics.DrawLine(failurePen, x - 3, markerY - 3, x + 3, markerY + 3);
                    e.Graphics.DrawLine(failurePen, x - 3, markerY + 3, x + 3, markerY - 3);
                }
                else
                {
                    e.Graphics.DrawLine(skippedPen, x - 2, markerY, x + 2, markerY);
                }
            }
        }

        TextRenderer.DrawText(e.Graphics, $"{maximum:0} ms", Font, new Point(0, area.Top - Font.Height / 2), colors.Muted);
        TextRenderer.DrawText(e.Graphics, "0", Font, new Point(axisWidth - TextRenderer.MeasureText("0", Font).Width - 6, area.Bottom - Font.Height / 2), colors.Muted);
        if (points.Count > 0)
        {
            var axis = new Rectangle(area.Left, area.Bottom + 4, area.Width, labelHeight);
            TextRenderer.DrawText(e.Graphics, $"Ciclo {points[0].Cycle}", Font, axis, colors.Muted, TextFormatFlags.Left);
            if (points.Count > 1)
                TextRenderer.DrawText(e.Graphics, $"Ciclo {points[^1].Cycle}", Font, axis, colors.Muted, TextFormatFlags.Right);
        }
    }

    private void DrawReference(Graphics graphics, Rectangle area, double maximum, double? value, Color color)
    {
        if (value is null)
            return;
        var y = area.Bottom - (float)(value.Value / maximum * area.Height);
        using var pen = new Pen(color) { DashStyle = DashStyle.Dash };
        graphics.DrawLine(pen, area.Left, y, area.Right, y);
    }
}
