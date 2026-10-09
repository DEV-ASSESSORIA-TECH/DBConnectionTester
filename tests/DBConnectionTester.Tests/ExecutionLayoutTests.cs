using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class ExecutionLayoutTests
{
    [Fact]
    public async Task ExecutionLayoutKeepsConfigurationProgressAndResultsInOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DBCT-layout", Guid.NewGuid().ToString("N"));
        var store = await SqliteApplicationStore.OpenOrCreateAsync(Path.Combine(directory, "data.db"), StorageScope.Custom);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm(store, ApplicationSettings.Default);
                form.MaximumSize = UiTestWindow.MaximumSize;
                Assert.Equal(new Size(1000, 680), form.Size);
                var connection = Descendants(form).OfType<GroupBox>().Single(c => c.Text == "Conexão");
                var status = (Label)typeof(MainForm).GetField("lblStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var home = Field<HomePage>(form, "homePage");
                        foreach (var (buttonText, targetPage) in new[]
                        { ("Nova execução", "executionPage"), ("Usar um perfil", "profilesPage"), ("Abrir histórico", "historyPage"), ("Ver histórico completo", "historyPage") })
                        {
                            form.NavigateTo("Início");
                            // Startup loads are asynchronous; let profile navigation settle.
                            await Task.Delay(80);
                            Descendants(home).OfType<Button>().Single(button => button.Text == buttonText).PerformClick();
                            Assert.True(Field<UserControl>(form, targetPage).Visible);
                            if (targetPage == "profilesPage")
                                Assert.Equal(Orientation.Vertical, Descendants(Field<UserControl>(form, targetPage)).OfType<SplitContainer>().Single().Orientation);
                        }
                        form.NavigateTo("Nova execução");
                        var bar = Field<ProgressBar>(form, "progressBar");
                        var results = Field<ResultsControl>(form, "resultsControl");
                        var tabs = results.Controls.OfType<TabControl>().Single();
                        Assert.Equal("Resumo estatístico", tabs.SelectedTab!.Text);
                        Assert.False(bar.Visible);
                        await Task.Delay(80);
                        Assert.Equal(2, ((TableLayoutPanel)connection.Parent!).ColumnCount);
                        Assert.False(Descendants(form).OfType<Panel>().Single(c => c.Name == "ExecutionConfigurationViewport").VerticalScroll.Visible);
                        Snapshot(form, "execution-compact-initial");
                        tabs.SelectedIndex = 1;
                        form.NavigateTo("Início");
                        form.NavigateTo("Nova execução");
                        Assert.Equal("Ciclos recentes", tabs.SelectedTab!.Text);
                        tabs.SelectedIndex = 0;
                        var settings = new TestSettings(DatabaseType.MySqlMariaDb, "localhost", NetworkPort.Create(3306),
                            "test", "", "", "", SqlServerAuthentication.Windows, "", RunCount.Create(100), false,
                            TestInterval.Create(TimeSpan.Zero), StageTimeout.Create(TimeSpan.FromSeconds(5)), true, true, true, true);
                        Invoke(form, "ApplyRunUiState", RunState("Running"), settings);
                        var stage = new StageStatisticsSnapshot(50, 48, 2, 10, 5, 20, 10, 18, 0, 1, DateTimeOffset.Now.AddMinutes(-1));
                        var cycle = new TestCycleResult(50, DateTimeOffset.Now, new DnsResult(StepStatus.Success, "127.0.0.1", 10, null),
                            new StepResult(StepStatus.Success, 12, "", null), new TcpResult(StepStatus.Success, 15, "", "", null),
                            new DatabaseResult(StepStatus.Success, 20, StepStatus.Success, 5, 25, null, null));
                        Invoke(form, "UpdateProgress", settings, new TestProgress(50, 2, 2, 2, 2, 2, cycle, new(stage, stage, stage, stage, stage)), false);
                        Assert.True(bar.Visible);
                        Assert.Equal(50, bar.Value);
                        Assert.Contains("50 de 100", Field<Label>(form, "lblRunProgress").Text);
                        Assert.Contains(Descendants(home).OfType<Label>(), label => label.Text.Contains("50 de 100"));
                        foreach (var size in new[] { new Size(800, 600), new Size(1100, 860), new Size(1180, 600), new Size(1680, 950), new Size(1100, 860) })
                        {
                            form.ClientSize = size;
                            await Task.Delay(80);
                            var viewport = Descendants(form).OfType<Panel>().Single(c => c.Name == "ExecutionConfigurationViewport");
                            var progress = Descendants(form).Single(c => c.Name == "ExecutionProgressArea");
                            var statusHeight = status.Font.Height * 2 + 8;
                            Assert.InRange(status.Height, 1, statusHeight);
                            Assert.True(connection.PointToScreen(Point.Empty).Y >= viewport.PointToScreen(Point.Empty).Y);
                            Assert.True(progress.PointToScreen(Point.Empty).Y >= viewport.PointToScreen(Point.Empty).Y + viewport.Height);
                            Assert.True(results.PointToScreen(Point.Empty).Y >= progress.PointToScreen(Point.Empty).Y + progress.Height);
                            Assert.True(results.Height >= 170);
                            var statisticsGrid = Descendants(results).OfType<DataGridView>().Single(g => g.Columns.Contains("Median"));
                            Assert.True(statisticsGrid.GetRowDisplayRectangle(4, false).Bottom <= statisticsGrid.ClientSize.Height);
                            Snapshot(form, $"execution-{size.Width}x{size.Height}-running");
                            if (size.Width >= 1100) Assert.False(viewport.VerticalScroll.Visible,
                                $"Requested client={size}; {UiTestWindow.Describe(form)}; " +
                                $"viewport={viewport.Bounds}, AutoScroll={viewport.AutoScroll}, display={viewport.DisplayRectangle}");
                        }
                        Invoke(form, "ApplyRunUiState", RunState("Completed"), null);
                        Assert.False(Field<Button>(form, "btnStop").Enabled);
                        var continuous = settings with { Continuous = true };
                        Invoke(form, "ApplyRunUiState", RunState("Running"), continuous);
                        Invoke(form, "UpdateProgress", continuous, new TestProgress(50, 2, 2, 2, 2, 2), false);
                        Assert.False(bar.Visible);
                        var counter = Field<Label>(form, "lblRunProgress");
                        Assert.Contains("50 ciclos concluídos", counter.Text);
                        Assert.DoesNotContain("%", counter.Text);
                        var before = counter.Text;
                        // Wait for an actual UI timer tick rather than assuming its first tick
                        // falls after the elapsed counter crosses a whole second.
                        var deadline = Environment.TickCount64 + 5000;
                        while (counter.Text == before && Environment.TickCount64 < deadline)
                            await Task.Delay(25);
                        Assert.NotEqual(before, counter.Text);
                        var continuousViewport = Descendants(form).OfType<Panel>().Single(c => c.Name == "ExecutionConfigurationViewport");
                        var background = Field<CheckBox>(form, "chkBackground");
                        Snapshot(form, "execution-continuous");
                        Assert.True(background.PointToScreen(Point.Empty).Y + background.Height <= continuousViewport.PointToScreen(Point.Empty).Y + continuousViewport.Height);
                        Invoke(form, "ApplyRunUiState", RunState("Completed"), null);
                        foreach (var databaseType in new[] { DatabaseType.SqlServer, DatabaseType.Sqlite, DatabaseType.MySqlMariaDb })
                        {
                            Field<ComboBox>(form, "cmbDatabaseType").SelectedItem = DatabaseProfiles.Get(databaseType);
                            Field<ComboBox>(form, "cmbSqlServerAuth").SelectedItem = SqlServerAuthentication.SqlLogin;
                            await Task.Delay(30);
                            Assert.True(results.Height >= 140);
                        }
                        var execution = Descendants(form).OfType<GroupBox>().Single(c => c.Text == "Execução");
                        foreach (var databaseType in Enum.GetValues<DatabaseType>())
                        {
                            Field<ComboBox>(form, "cmbDatabaseType").SelectedItem = DatabaseProfiles.Get(databaseType);
                            Field<ComboBox>(form, "cmbSqlServerAuth").SelectedItem = SqlServerAuthentication.SqlLogin;
                            foreach (var width in new[] { 1000, 950, 920, 900, 886, 885, 870, 850, 885, 886, 900, 1000 })
                            {
                                form.ClientSize = new Size(width, 750);
                                await Task.Delay(80);
                                var settingsArea = (TableLayoutPanel)connection.Parent!;
                                // These widths bracket the transition and exercise the newly reclaimed space.
                                if (width >= 886) Assert.Equal(2, settingsArea.ColumnCount);
                                if (width <= 885) Assert.Equal(1, settingsArea.ColumnCount);
                                if (settingsArea.ColumnCount == 2)
                                {
                                    Assert.Equal(connection.Top, execution.Top);
                                    Assert.True(connection.Right <= execution.Left);
                                }
                                else Assert.True(connection.Bottom <= execution.Top);
                                var quantity = Descendants(execution).OfType<Label>().Single(label => label.Text == "Quantidade:");
                                Assert.True(quantity.Height <= quantity.Font.Height + 6, $"Quantity label wraps: {quantity.Bounds}");
                                var quantityPosition = quantity.PointToScreen(Point.Empty).Y;
                                var inputPosition = Field<NumericUpDown>(form, "numTests").PointToScreen(Point.Empty).Y;
                                Assert.InRange(Math.Abs(quantityPosition - inputPosition), 0, 4);
                                foreach (var flow in Descendants(execution).OfType<FlowLayoutPanel>())
                                    foreach (Control child in flow.Controls)
                                        if (child.Visible)
                                        {
                                            Assert.True(child.Right <= flow.ClientSize.Width, $"{child.Text}: {child.Bounds} outside {flow.ClientSize}");
                                            Assert.True(child.Bottom <= flow.ClientSize.Height);
                                        }
                                foreach (var group in new[] { connection, execution })
                                    foreach (var label in Descendants(group).OfType<Label>().Where(c => c.Visible))
                                        Assert.True(label.Right <= label.Parent!.ClientSize.Width, $"{label.Text}: {label.Bounds}");
                                Snapshot(form, $"execution-breakpoint-{databaseType}-{width}");
                            }
                        }
                        results.ResetResults();
                        for (var number = 1; number <= 50; number++)
                            results.AddCycle(cycle with
                            {
                                Number = number,
                                Dns = new DnsResult(StepStatus.Success, "127.0.0.1", number == 1 ? 73 : number % 7, null)
                            });
                        var trendStage = stage with { MedianMs = 3, P95Ms = 6, MaximumMs = 73 };
                        results.UpdateStatistics(new(trendStage, stage, stage, stage, stage));
                        tabs.SelectedIndex = 2;
                        foreach (var size in new[] { new Size(1040, 600), new Size(800, 600), new Size(1680, 950) })
                        {
                            form.ClientSize = size;
                            await Task.Delay(80);
                            var dashboard = Descendants(results).OfType<StatisticsDashboardControl>().Single();
                            var chart = Descendants(dashboard).Single(c => c.GetType().Name == "LatencyTrendControl");
                            Assert.DoesNotContain(Descendants(dashboard), c => c.GetType().Name == "MetricTile");
                            Assert.True(chart.Height >= dashboard.ClientSize.Height - 50);
                            Snapshot(form, $"execution-trend-{size.Width}x{size.Height}");
                        }
                    }
                    catch (Exception error) { failure = error; }
                    finally { form.Close(); }
                };
                System.Windows.Forms.Application.Run(form);
                if (failure is not null) return;
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        try { if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
        finally { Directory.Delete(directory, true); }
    }

    private static T Field<T>(MainForm form, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static object RunState(string name) => Enum.Parse(typeof(MainForm).GetField("runUiState", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType, name);

    private static void Invoke(MainForm form, string name, params object?[] arguments) =>
        typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, arguments);

    private static void Snapshot(Form form, string name)
    {
        var output = Environment.GetEnvironmentVariable("DBCT_UI_SNAPSHOT_DIR");
        if (output is null) return;
        Directory.CreateDirectory(output);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(output, name + ".png"));
        File.WriteAllLines(Path.Combine(output, name + ".layout"), Descendants(form).Where(c => c.Visible).Select(c => $"{c.GetType().Name} [{c.Name} {c.Text}] {c.Bounds}"));
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
