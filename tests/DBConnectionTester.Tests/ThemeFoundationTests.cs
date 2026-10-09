using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class ThemeFoundationTests
{
    [Fact]
    public Task SemanticRolesUseModernPaletteAndPreserveGeometryAcrossThemeChanges() => UiStyleTests.OnUiThread(() =>
    {
        using var form = new Form();
        using var button = new ThemedButton { Text = "Salvar", AutoSize = true };
        form.Controls.Add(button);
        foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
        {
            var palette = ThemeManager.PaletteFor(theme);
            UiStyle.SetRole(button, UiRole.PrimaryAction);
            ThemeManager.ApplyPalette(form, palette);
            Assert.Equal(palette.Accent, button.BackColor);
            Assert.Equal(Color.White, button.ForeColor);
            Assert.True(button.Height >= 32);
            UiStyle.SetRole(button, UiRole.DestructiveAction);
            Assert.NotEqual(palette.Accent, button.BackColor);
            UiStyle.SetRole(button, UiRole.NeutralAction);
            Assert.Equal(palette.SurfaceAlternative, button.BackColor);
            Assert.Equal(palette.Text, button.ForeColor);
            var bounds = button.Bounds;
            ThemeManager.ApplyPalette(form, palette);
            Assert.Equal(bounds, button.Bounds);
            button.Enabled = false;
            Assert.Equal(palette.DisabledBackground, button.BackColor);
            Assert.Equal(palette.DisabledText, button.ForeColor);
            button.Enabled = true;
        }
    });

    [Fact]
    public Task PaletteCanStyleRoleAndStateWithoutChangingOtherControls() => UiStyleTests.OnUiThread(() =>
    {
        using var root = new Panel();
        using var primary = UiStyle.WithRole(new Button(), UiRole.PrimaryAction);
        using var neutral = new Button();
        root.Controls.AddRange([primary, neutral]);
        UiStyle.SetState(primary, UiState.Busy);
        var palette = ThemeManager.PaletteFor(ApplicationTheme.Light);
        palette = palette with { Roles = palette.Roles
            .SetItem((UiRole.PrimaryAction, UiState.Normal), new(Color.Blue, Color.White))
            .Add((UiRole.PrimaryAction, UiState.Busy), new(Color.Gold, Color.Black))
            .Add((UiRole.NeutralAction, UiState.Error), new(Color.Red, Color.White)) };
        ThemeManager.ApplyPalette(root, palette);
        Assert.Equal(Color.Gold, primary.BackColor);
        Assert.Equal(palette.SurfaceAlternative, neutral.BackColor);
        UiStyle.SetState(primary, UiState.Normal);
        Assert.Equal(Color.Blue, primary.BackColor);
        UiStyle.SetState(neutral, UiState.Error);
        Assert.Equal(Color.Red, neutral.BackColor);
    });

    [Fact]
    public Task PaletteRecolorsExistingFailureRowsWithoutUpdatingStatistics() => UiStyleTests.OnUiThread(() =>
    {
        using var form = new Form { ClientSize = new Size(800, 400) };
        using var results = new ResultsControl();
        form.Controls.Add(results);
        form.Show();
        var cycle = new TestCycleResult(1, DateTimeOffset.Now,
            new DnsResult(StepStatus.Failed, "", 10, null),
            new StepResult(StepStatus.Success, 12, "", null),
            new TcpResult(StepStatus.Success, 15, "", "", null),
            new DatabaseResult(StepStatus.Success, 20, StepStatus.Success, 5, 25, null, null));
        results.AddCycle(cycle);
        var stage = new StageStatisticsSnapshot(1, 0, 1, null, null, null, null, null, 1, 1, DateTimeOffset.Now);
        var snapshot = new RunStatisticsSnapshot(stage, stage, stage, stage, stage);
        results.UpdateStatistics(snapshot);
        var grids = Descendants(results).OfType<DataGridView>().ToArray();
        var palette = ThemeManager.PaletteFor(ApplicationTheme.Light);
        ThemeManager.ApplyPalette(form, palette with { Metrics = palette.Metrics with { FailureBackground = Color.Magenta } });
        Assert.Same(snapshot, typeof(ResultsControl).GetField("latestStatistics", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(results));
        Assert.All(grids, grid => Assert.Equal(Color.Magenta, grid.Rows[0].DefaultCellStyle.BackColor));
        Assert.Equal(2, grids.Length);
        results.ResetResults();
        var statisticsGrid = grids.Single(grid => grid.Columns.Contains("Median"));
        Assert.All(statisticsGrid.Rows.Cast<DataGridViewRow>(), row => Assert.Equal(palette.Metrics.NormalStatisticsBackground, row.DefaultCellStyle.BackColor));
        form.Close();
    });

    [Fact]
    public Task ChartUsesNewPaletteWithExistingPointsAndPreservesGeometry() => UiStyleTests.OnUiThread(() =>
    {
        using var chart = new LatencyTrendControl { Size = new Size(480, 180) };
        chart.SetData([new(1, StepStatus.Success, 10), new(2, StepStatus.Success, 20), new(3, StepStatus.Success, 15)], 15, 20);
        var points = typeof(LatencyTrendControl).GetField("points", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chart);
        var bounds = chart.Bounds;
        var palette = ThemeManager.PaletteFor(ApplicationTheme.Light);
        ThemeManager.ApplyPalette(chart, palette with { Metrics = palette.Metrics with { Series = Color.Magenta } });
        Assert.Same(points, typeof(LatencyTrendControl).GetField("points", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chart));
        Assert.Equal(bounds, chart.Bounds);
        using var image = new Bitmap(chart.Width, chart.Height);
        chart.DrawToBitmap(image, chart.ClientRectangle);
        var seriesPixels = 0;
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                if (image.GetPixel(x, y).ToArgb() == Color.Magenta.ToArgb()) seriesPixels++;
        Assert.True(seriesPixels > 20, $"Expected recolored series, found {seriesPixels} pixels.");
    });

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
