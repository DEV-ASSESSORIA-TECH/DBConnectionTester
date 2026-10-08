using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class HomePageTests
{
    [Fact]
    public Task RecentLoadingIsOffThreadAndActionsRemainResponsive() => RunUi(async (page, history, _) =>
    {
        var uiThread = Environment.CurrentManagedThreadId;
        var entered = Signal(); var release = new TaskCompletionSource<IReadOnlyList<RunHistoryItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        history.Load = _ =>
        {
            Assert.NotEqual(uiThread, Environment.CurrentManagedThreadId);
            entered.TrySetResult(); return release.Task;
        };
        var loading = page.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var tick = Signal(); page.BeginInvoke((Action)(() => tick.TrySetResult()));
            await tick.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var starts = 0; var profiles = 0; var histories = 0;
            page.NewRunRequested += (_, _) => starts++;
            page.ProfilesRequested += (_, _) => profiles++;
            page.HistoryRequested += (_, _) => histories++;
            foreach (var button in Descendants(page).OfType<Button>().Where(b => b.Visible)) button.PerformClick();
            Assert.Equal(1, starts); Assert.Equal(1, profiles); Assert.Equal(2, histories);
            release.TrySetResult(new[] { Run("first"), Run("second"), Run("third") });
            await loading;
            var grid = Field<DataGridView>(page, "recentRuns");
            Assert.Equal(3, grid.Rows.Count);
            Assert.Equal("Configuração manual", grid.Rows[0].Cells[1].Value);
            Assert.Equal("Concluída", grid.Rows[0].Cells[3].Value);
            Assert.DoesNotContain(Descendants(page).OfType<Label>(), label => label.Text.Contains("data.db"));
            page.UpdateRunStatus("Execução contínua em andamento.");
            page.UpdateRunProgress("25 ciclos concluídos · 00:00:30 decorridos");
            Assert.Contains("25 ciclos", Field<Label>(page, "statusDetail").Text);
        }
        finally { release.TrySetResult([]); }
    });

    [Fact]
    public Task NewestRefreshWinsAndDisposalCancelsOutstandingRequest() => RunUi(async (page, history, _) =>
    {
        var old = new TaskCompletionSource<IReadOnlyList<RunHistoryItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = Signal();
        history.Load = _ => { entered.TrySetResult(); return old.Task; };
        var first = page.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        history.Load = _ => Task.FromResult<IReadOnlyList<RunHistoryItem>>(new[] { Run("newest") });
        await page.RefreshAsync();
        old.TrySetResult(new[] { Run("stale") });
        await first;
        Assert.Equal("newest", Field<DataGridView>(page, "recentRuns").Rows[0].Cells[2].Value);
        var delayed = new TaskCompletionSource<IReadOnlyList<RunHistoryItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = Signal(); CancellationToken observed = default;
        history.Load = token => { observed = token; started.TrySetResult(); return delayed.Task; };
        var refreshing = page.RefreshAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Dispose();
        Assert.True(observed.IsCancellationRequested);
        delayed.TrySetResult(new[] { Run("ignored-after-close") });
        await refreshing;
    });

    [Fact]
    public Task EmptyHistoryAndReadFailureSupportRetry() => RunUi(async (page, history, _) =>
    {
        await page.RefreshAsync();
        Assert.False(Field<DataGridView>(page, "recentRuns").Visible);
        Assert.Contains("Nenhuma execução", Field<Label>(page, "recentFeedback").Text);
        history.Load = _ => Task.FromException<IReadOnlyList<RunHistoryItem>>(new IOException("Read failed"));
        await page.RefreshAsync();
        Assert.True(Field<Button>(page, "retry").Visible);
        Assert.Contains("Não foi possível", Field<Label>(page, "recentFeedback").Text);
        history.Load = _ => Task.FromResult<IReadOnlyList<RunHistoryItem>>(new[] { Run("recovered", PersistedRunStatus.Failed) });
        await page.RefreshAsync();
        Assert.False(Field<Button>(page, "retry").Visible);
        var grid = Field<DataGridView>(page, "recentRuns");
        Assert.True(grid.Visible);
        Assert.Equal("Falhou", grid.Rows[0].Cells[3].Value);
    });

    [Fact]
    public Task LayoutFitsCompactWindowAndCurrentThemes() => RunUi(async (page, history, form) =>
    {
        history.Load = _ => Task.FromResult<IReadOnlyList<RunHistoryItem>>(new[]
        {
            Run("db.exemplo.local:3306") with { ProfileName = "Produção" },
            Run("127.0.0.1:1433", PersistedRunStatus.Interrupted),
            Run("db-teste.local:5432") with { ProfileName = "Homologação" }
        });
        await page.RefreshAsync();
        foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark, ApplicationTheme.System })
        {
            ThemeManager.Apply(form, theme);
            foreach (var size in new[] { new Size(620, 566), new Size(800, 620), new Size(1150, 800) })
            {
                form.ClientSize = size;
                page.AutoScrollPosition = Point.Empty;
                await Task.Delay(40);
                var groups = Descendants(page).OfType<GroupBox>().OrderBy(c => c.Top).ToArray();
                Assert.True(groups[0].Bottom <= groups[1].Top);
                foreach (var group in groups)
                    foreach (var control in Descendants(group).Where(c => c.Visible))
                    {
                        var position = group.PointToClient(control.PointToScreen(Point.Empty));
                        Assert.True(position.X + control.Width <= group.ClientSize.Width, $"{control.Text} exceeds {group.Text}");
                        Assert.True(position.Y + control.Height <= group.ClientSize.Height, $"{control.Text} clipped in {group.Text}");
                    }
                var grid = Field<DataGridView>(page, "recentRuns");
                Assert.True(grid.GetRowDisplayRectangle(2, false).Bottom <= grid.ClientSize.Height);
                var historyButton = Descendants(page).OfType<Button>().Single(c => c.Text == "Ver histórico completo");
                Assert.True(page.PointToClient(historyButton.PointToScreen(Point.Empty)).Y + historyButton.Height <= page.ClientSize.Height);
                var output = Environment.GetEnvironmentVariable("DBCT_UI_SNAPSHOT_DIR");
                if (output is not null)
                {
                    Directory.CreateDirectory(output);
                    using var image = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(Path.Combine(output, $"home-{theme}-{size.Width}x{size.Height}.png"));
                }
            }
        }
    });

    private static RunHistoryItem Run(string target, PersistedRunStatus status = PersistedRunStatus.Completed) => new(
        Guid.NewGuid(), null, null, status, RunTerminationReason.PlannedCountCompleted,
        DateTimeOffset.Now, DateTimeOffset.Now, "2.0.0", "machine", DatabaseType.TcpOnly, target, 1, null);
    private static T Field<T>(HomePage page, string name) => (T)typeof(HomePage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private sealed class History : IRecentRunHistoryRepository
    {
        public Func<CancellationToken, Task<IReadOnlyList<RunHistoryItem>>> Load = _ => Task.FromResult<IReadOnlyList<RunHistoryItem>>([]);
        public Task<IReadOnlyList<RunHistoryItem>> GetRecentAsync(CancellationToken token = default) => Load(token);
    }
    private static Task RunUi(Func<HomePage, History, Form, Task> action)
    {
        var finished = Signal();
        var thread = new Thread(() =>
        {
            try
            {
                var history = new History();
                using var page = new HomePage(history);
                using var form = new Form { ClientSize = new Size(800, 620) };
                form.Controls.Add(page);
                form.Shown += async (_, _) =>
                {
                    try { await action(page, history, form); finished.TrySetResult(); }
                    catch (Exception error) { finished.TrySetException(error); }
                    finally { form.Close(); }
                };
                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception error) { finished.TrySetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
