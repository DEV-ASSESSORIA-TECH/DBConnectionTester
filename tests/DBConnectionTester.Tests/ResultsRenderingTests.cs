using System.Reflection;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class ResultsRenderingTests
{
    [Fact]
    public async Task RevisitingStatisticsReusesValuesButRefreshesElapsedTimeAndPendingChanges()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var results = new ResultsControl();
                using var form = new Form { Width = 1100, Height = 850 };
                form.Controls.Add(results);
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var grid = (DataGridView)typeof(ResultsControl).GetField("statisticsGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(results)!;
                        var tabs = (TabControl)grid.Parent!.Parent!;
                        Assert.Equal("Resumo estatístico", tabs.SelectedTab!.Text);
                        Assert.Empty(results.Controls.OfType<GroupBox>());
                        var stage = new StageStatisticsSnapshot(2, 1, 1, 10, 10, 10, 10, 10, 0, 1, DateTimeOffset.Now.AddSeconds(-10));
                        results.UpdateStatistics(new(stage, stage, stage, stage, stage));
                        tabs.SelectedIndex = 0;
                        var changes = 0;
                        grid.CellValueChanged += (_, e) => { if (e.ColumnIndex < 10) changes++; };
                        var elapsed = grid.Rows[0].Cells[10].Value;
                        tabs.SelectedIndex = 1;
                        await Task.Delay(1100);
                        tabs.SelectedIndex = 0;
                        Assert.Equal(0, changes);
                        Assert.NotEqual(elapsed, grid.Rows[0].Cells[10].Value);
                        tabs.SelectedIndex = 1;
                        var changed = stage with { AverageMs = 20 };
                        results.UpdateStatistics(new(changed, changed, changed, changed, changed));
                        Assert.Equal(0, changes);
                        tabs.SelectedIndex = 0;
                        Assert.True(changes > 0);
                        Assert.StartsWith("20", grid.Rows[0].Cells[3].Value!.ToString());
                        results.ResetResults();
                        Assert.Equal("—", grid.Rows[0].Cells[3].Value);
                        finished.TrySetResult();
                    }
                    catch (Exception error) { finished.TrySetException(error); }
                    finally { form.Close(); }
                };
                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception error) { finished.TrySetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
