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
                var connection = Descendants(form).OfType<GroupBox>().Single(c => c.Text == "Conexão");
                var status = (Label)typeof(MainForm).GetField("lblStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        form.NavigateTo("Nova execução");
                        var bar = Field<ProgressBar>(form, "progressBar");
                        var results = Field<ResultsControl>(form, "resultsControl");
                        var tabs = results.Controls.OfType<TabControl>().Single();
                        Assert.Equal("Resumo estatístico", tabs.SelectedTab!.Text);
                        Assert.False(bar.Visible);
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
                            Assert.True(results.Height >= 190);
                            var statisticsGrid = Descendants(results).OfType<DataGridView>().Single(g => g.Columns.Contains("Median"));
                            Assert.True(statisticsGrid.GetRowDisplayRectangle(4, false).Bottom <= statisticsGrid.ClientSize.Height);
                            Snapshot(form, $"execution-{size.Width}x{size.Height}-running");
                            if (size.Width >= 1100) Assert.False(viewport.VerticalScroll.Visible, $"AutoScroll={viewport.AutoScroll}, height={viewport.Height}, display={viewport.DisplayRectangle}");
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
                        await Task.Delay(1200);
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
