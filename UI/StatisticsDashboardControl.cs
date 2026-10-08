using System.Drawing.Drawing2D;
using System.ComponentModel;
using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

public sealed class StatisticsDashboardControl : UserControl
{
    private const int MaximumPoints = 100;

    private readonly ComboBox stageSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly Label trendLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly MetricTile successRateTile = new("Taxa de sucesso");
    private readonly MetricTile medianTile = new("Mediana");
    private readonly MetricTile p95Tile = new("P95");
    private readonly MetricTile maximumTile = new("Máximo");
    private readonly MetricTile streakTile = new("Falhas seguidas");
    private readonly MetricTile lastFailureTile = new("Última falha");
    private readonly LatencyTrendControl chart = new();
    private readonly Dictionary<MetricStage, List<TrendPoint>> histories =
        Enum.GetValues<MetricStage>().ToDictionary(stage => stage, _ => new List<TrendPoint>());

    private RunStatisticsSnapshot? latestStatistics;

    public StatisticsDashboardControl()
    {
        Dock = DockStyle.Fill;
        stageSelector.Items.AddRange(new object[]
        {
            new StageOption("DNS", MetricStage.Dns),
            new StageOption("Ping / ICMP", MetricStage.Ping),
            new StageOption("TCP", MetricStage.Tcp),
            new StageOption("Conexão DB", MetricStage.DatabaseConnect),
            new StageOption("SELECT 1", MetricStage.DatabaseQuery)
        });
        stageSelector.SelectedIndexChanged += (_, _) => RefreshDashboard();
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

        var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1 };
        for (var index = 0; index < 6; index++)
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
        metrics.Controls.Add(successRateTile, 0, 0);
        metrics.Controls.Add(medianTile, 1, 0);
        metrics.Controls.Add(p95Tile, 2, 0);
        metrics.Controls.Add(maximumTile, 3, 0);
        metrics.Controls.Add(streakTile, 4, 0);
        metrics.Controls.Add(lastFailureTile, 5, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(metrics, 0, 1);
        layout.Controls.Add(chart, 0, 2);
        Controls.Add(layout);
        VisibleChanged += (_, _) => RefreshDashboard();
        ResetDashboard();
    }

    public void ResetDashboard()
    {
        latestStatistics = null;
        foreach (var history in histories.Values)
            history.Clear();
        RefreshDashboard();
    }

    public void AddCycle(TestCycleResult cycle)
    {
        AddPoint(MetricStage.Dns, cycle.Number, cycle.Dns.Status, cycle.Dns.ElapsedMs);
        AddPoint(MetricStage.Ping, cycle.Number, cycle.Ping.Status, cycle.Ping.ElapsedMs);
        AddPoint(MetricStage.Tcp, cycle.Number, cycle.Tcp.Status, cycle.Tcp.ElapsedMs);
        AddPoint(MetricStage.DatabaseConnect, cycle.Number, cycle.Database.ConnectStatus, cycle.Database.ConnectMs);
        AddPoint(MetricStage.DatabaseQuery, cycle.Number, cycle.Database.QueryStatus, cycle.Database.QueryMs);
    }

    public void UpdateStatistics(RunStatisticsSnapshot statistics)
    {
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
        if (statistics is null)
        {
            foreach (var tile in MetricTiles())
                tile.Value = "—";
            trendLabel.Text = "Aguardando dados";
            trendLabel.ForeColor = SystemColors.GrayText;
            chart.SetData(Array.Empty<TrendPoint>(), null, null);
            return;
        }

        successRateTile.Value = statistics.SuccessRate is null ? "—" : $"{statistics.SuccessRate:0.0}%";
        medianTile.Value = FormatLatency(statistics.MedianMs);
        p95Tile.Value = FormatLatency(statistics.P95Ms);
        maximumTile.Value = FormatLatency(statistics.MaximumMs);
        streakTile.Value = statistics.ConsecutiveFailures.ToString("N0");
        lastFailureTile.Value = FormatElapsed(statistics.TimeSinceLastFailure(DateTimeOffset.Now));

        if (statistics.ConsecutiveFailures > 0)
        {
            trendLabel.Text = $"Atenção: {statistics.ConsecutiveFailures:N0} falha(s) consecutiva(s)";
            trendLabel.ForeColor = Color.FromArgb(185, 45, 45);
        }
        else
        {
            var direction = LatencyTrendAnalyzer.Analyze(
                histories[option.Stage]
                    .Where(point => point.Status == StepStatus.Success)
                    .Select(point => point.ElapsedMs));
            (trendLabel.Text, trendLabel.ForeColor) = direction switch
            {
                LatencyTrendDirection.Stable => ("Tendência estável", Color.FromArgb(55, 105, 155)),
                LatencyTrendDirection.Improving => ("Latência melhorando", Color.FromArgb(28, 120, 72)),
                LatencyTrendDirection.Degrading => ("Latência degradando", Color.FromArgb(200, 110, 25)),
                _ => ("Dados insuficientes para tendência", SystemColors.GrayText)
            };
        }

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

    private IEnumerable<MetricTile> MetricTiles()
    {
        yield return successRateTile;
        yield return medianTile;
        yield return p95Tile;
        yield return maximumTile;
        yield return streakTile;
        yield return lastFailureTile;
    }

    private static string FormatLatency(double? milliseconds) =>
        milliseconds is null ? "—" : $"{milliseconds:0.0} ms";

    private static string FormatElapsed(TimeSpan? elapsed)
    {
        if (elapsed is null)
            return "Nunca";
        if (elapsed.Value.TotalSeconds < 60)
            return $"{elapsed.Value.TotalSeconds:0} s";
        if (elapsed.Value.TotalMinutes < 60)
            return $"{elapsed.Value.TotalMinutes:0.0} min";
        if (elapsed.Value.TotalHours < 24)
            return $"{elapsed.Value.TotalHours:0.0} h";
        return $"{elapsed.Value.TotalDays:0.0} d";
    }

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

internal sealed class MetricTile : Panel
{
    private readonly Label valueLabel;

    public MetricTile(string caption)
    {
        Dock = DockStyle.Fill;
        Margin = new Padding(3);
        Padding = new Padding(6, 4, 6, 4);
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Color.FromArgb(247, 248, 250);
        var captionLabel = new Label
        {
            Text = caption,
            Dock = DockStyle.Top,
            Height = 18,
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleCenter
        };
        valueLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font, FontStyle.Bold)
        };
        Controls.Add(valueLabel);
        Controls.Add(captionLabel);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => valueLabel.Text;
        set => valueLabel.Text = value;
    }
}

internal sealed class LatencyTrendControl : Control
{
    private IReadOnlyList<TrendPoint> points = Array.Empty<TrendPoint>();
    private double? median;
    private double? p95;

    public LatencyTrendControl()
    {
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        BackColor = SystemColors.Window;
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
        var area = new Rectangle(38, 18, Math.Max(1, Width - 48), Math.Max(1, Height - 34));
        using var borderPen = new Pen(Color.FromArgb(220, 224, 229));
        e.Graphics.DrawRectangle(borderPen, area);

        var successful = points.Where(point => point.Status == StepStatus.Success).ToArray();
        if (successful.Length == 0)
        {
            TextRenderer.DrawText(e.Graphics, "Sem latências para exibir", Font, area,
                SystemColors.GrayText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var maximum = Math.Max(1d, new[]
        {
            successful.Max(point => (double)point.ElapsedMs),
            median ?? 0,
            p95 ?? 0
        }.Max() * 1.1);

        DrawReference(e.Graphics, area, maximum, median, Color.FromArgb(65, 120, 180), "Mediana");
        DrawReference(e.Graphics, area, maximum, p95, Color.FromArgb(220, 135, 45), "P95");

        using var linePen = new Pen(Color.FromArgb(55, 105, 155), 2);
        using var successBrush = new SolidBrush(Color.FromArgb(55, 105, 155));
        using var failurePen = new Pen(Color.FromArgb(190, 50, 50), 2);
        using var skippedPen = new Pen(Color.FromArgb(160, 165, 170));
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

        TextRenderer.DrawText(e.Graphics, $"{maximum:0} ms", Font, new Point(0, area.Top - 7), SystemColors.GrayText);
        TextRenderer.DrawText(e.Graphics, "0", Font, new Point(16, area.Bottom - 8), SystemColors.GrayText);
    }

    private void DrawReference(Graphics graphics, Rectangle area, double maximum, double? value, Color color, string label)
    {
        if (value is null)
            return;
        var y = area.Bottom - (float)(value.Value / maximum * area.Height);
        using var pen = new Pen(color) { DashStyle = DashStyle.Dash };
        graphics.DrawLine(pen, area.Left, y, area.Right, y);
        TextRenderer.DrawText(graphics, label, Font, new Point(area.Right - 55, (int)y - 14), color);
    }
}
