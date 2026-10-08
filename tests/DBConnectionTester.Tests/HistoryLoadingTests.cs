using System.Drawing;
using DBConnectionTester.Services.Export;
using System.Reflection;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class HistoryLoadingTests
{
    [Fact]
    public Task BlockingQueryLeavesUiResponsiveAndOlderSearchCannotReplaceNewerSearch() => RunUi(async (page, repository) =>
    {
        var uiThread = Environment.CurrentManagedThreadId;
        using var release = new ManualResetEventSlim();
        var started = Signal();
        var calls = 0;
        repository.Search = (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                Assert.NotEqual(uiThread, Environment.CurrentManagedThreadId);
                started.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                return Task.FromResult(Page(repository.A));
            }
            return Task.FromResult(Page(repository.B));
        };
        var older = page.RefreshAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Reaching here on the UI thread while the query remains blocked
            // proves that a WinForms message-loop continuation can run.
            Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
            await page.RefreshAsync();
            release.Set();
            await older;
            Assert.Equal(repository.B.RunId, Field<DataGridView>(page, "runs").Rows[0].Cells[0].Value);
        }
        finally { release.Set(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OlderSelectionCannotReplaceDetailsOrCycles(bool blockCycles) => RunUi(async (page, repository) =>
    {
        ShowCycles(page);
        var started = Signal();
        var release = Signal();
        repository.Details = async id =>
        {
            if (id == repository.A.RunId && !blockCycles)
            {
                started.TrySetResult();
                await release.Task;
            }
            return new RunHistoryDetails(id == repository.A.RunId ? repository.A : repository.B, "{}", [], []);
        };
        repository.Cycles = async (id, _) =>
        {
            if (id == repository.A.RunId && blockCycles)
            {
                started.TrySetResult();
                await release.Task;
            }
            var cycle = new PersistedCycle(id == repository.A.RunId ? 11 : 22, DateTimeOffset.Now, "", "", "", "", 0, []);
            return new PagedResult<PersistedCycle>([cycle], 1, 1, 100);
        };
        var loading = page.RefreshAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var runs = Field<DataGridView>(page, "runs");
            runs.ClearSelection();
            runs.Rows[1].Selected = true;
            await WaitUntil(() => Field<DataGridView>(page, "cycles").Rows.Count == 1);
            release.TrySetResult();
            await loading;
            Assert.Contains(repository.B.RunId.ToString("D"), Field<TextBox>(page, "details").Text);
            Assert.Equal(22L, Field<DataGridView>(page, "cycles").Rows[0].Cells[0].Value);
        }
        finally { release.TrySetResult(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PagerCommitsPageOnlyAfterLoadingAndIgnoresDoubleClick(bool cyclePager) => RunUi(async (page, repository) =>
    {
        var started = Signal();
        var release = Signal();
        var requests = 0;
        repository.Search = async (_, request) =>
        {
            if (!cyclePager && Interlocked.Increment(ref requests) == 2)
            {
                started.TrySetResult();
                await release.Task;
            }
            return new PagedResult<RunHistoryItem>([repository.A], 75, request.PageNumber, 25);
        };
        repository.Cycles = async (_, request) =>
        {
            if (cyclePager && Interlocked.Increment(ref requests) == 2)
            {
                started.TrySetResult();
                await release.Task;
            }
            return new PagedResult<PersistedCycle>([], 300, request.PageNumber, 100);
        };
        await page.RefreshAsync();
        if (cyclePager)
        {
            ShowCycles(page);
            await WaitUntil(() => Field<Button>(page, "nextCyclePage").Enabled);
        }
        var next = Field<Button>(page, cyclePager ? "nextCyclePage" : "nextRunPage");
        var label = Field<Label>(page, cyclePager ? "cyclePageLabel" : "runPageLabel");
        next.PerformClick();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(next.Enabled);
            next.PerformClick();
            Assert.Equal(2, requests);
            Assert.StartsWith("Página 1", label.Text);
            release.TrySetResult();
            await WaitUntil(() => label.Text.StartsWith("Página 2"));
            Assert.Equal(2, requests);
        }
        finally { release.TrySetResult(); }
    });

    [Fact]
    public Task DisposingPageDiscardsPendingQueryWithoutUpdatingDisposedControls() => RunUi(async (page, repository) =>
    {
        var started = Signal();
        var release = Signal();
        repository.Search = async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
            return Page(repository.A);
        };
        var loading = page.RefreshAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            release.TrySetResult();
            await loading;
        }
        finally { release.TrySetResult(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CyclesAreLoadedOnDemandAndReusedAcrossTabs(bool empty) => RunUi(async (page, repository) =>
    {
        var queries = 0;
        repository.Cycles = (_, request) =>
        {
            Interlocked.Increment(ref queries);
            var cycle = new PersistedCycle(1, DateTimeOffset.Now, "", "", "", "", 0, []);
            return Task.FromResult(new PagedResult<PersistedCycle>(empty ? [] : [cycle], empty ? 0 : 1, request.PageNumber, 100));
        };
        await page.RefreshAsync();
        Assert.Equal(0, queries);
        ShowCycles(page);
        await WaitUntil(() => Field<Label>(page, "cyclePageLabel").Text.Length > 0);
        var tabs = (TabControl)Field<DataGridView>(page, "cycles").Parent!.Parent!.Parent!;
        tabs.SelectedIndex = 0;
        tabs.SelectedIndex = 2;
        var chart = Field<Control>(page, "chart");
        var pointsField = chart.GetType().GetField("points", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var points = pointsField.GetValue(chart);
        tabs.SelectedIndex = 0;
        tabs.SelectedIndex = 2;
        Assert.Same(points, pointsField.GetValue(chart));
        page.Visible = false;
        page.Visible = true;
        await Task.Delay(30);
        Assert.Equal(1, queries);
        Assert.Equal(empty ? 0 : 1, Field<DataGridView>(page, "cycles").Rows.Count);
        await page.RefreshAsync();
        Assert.Equal(2, queries);
    });

    [Theory]
    [InlineData("csv", false)]
    [InlineData("txt", false)]
    [InlineData("json", false)]
    [InlineData("zip", false)]
    [InlineData("csv", true)]
    public Task ExportKeepsUiResponsivePreventsDuplicatesAndSurvivesSelectionChanges(string extension, bool runningSelection) => RunUi(async (page, repository) =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "DBCT-export-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var release = new ManualResetEventSlim();
        var started = Signal();
        var uiThread = Environment.CurrentManagedThreadId;
        if (runningSelection) repository.B = repository.B with { Status = PersistedRunStatus.Running, TerminationReason = null, FinishedAt = null };
        repository.Cycles = (id, request) =>
        {
            Assert.Equal(repository.A.RunId, id);
            Assert.NotEqual(uiThread, Environment.CurrentManagedThreadId);
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return Task.FromResult(new PagedResult<PersistedCycle>([new(1, DateTimeOffset.Now, "", "", "", "", 0, [])], 1, 1, request.PageSize));
        };
        Task? exporting = null;
        try
        {
            await page.RefreshAsync();
            var runGrid = Field<DataGridView>(page, "runs");
            Assert.Equal("Concluída", runGrid.Rows[0].Cells["Status"].Value);
            Assert.Equal(DatabaseProfiles.Get(DatabaseType.MySqlMariaDb).DisplayName, runGrid.Rows[0].Cells["Type"].Value);
            Assert.Contains("Quantidade de testes concluída", Field<TextBox>(page, "details").Text);
            var exporter = Field<RunExportService>(page, "exporter");
            var destination = Path.Combine(directory, "run." + extension);
            Func<Task<RunExportResult>> action = extension == "zip"
                ? () => exporter.ExportZipAsync(repository.A.RunId, destination)
                : () => exporter.ExportAsync(repository.A.RunId, extension switch { "csv" => RunExportFormat.Csv, "txt" => RunExportFormat.Text, _ => RunExportFormat.Json }, destination);
            exporting = StartExport(page, action);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
            Assert.Contains("Exportando", Field<Label>(page, "exportFeedback").Text);
            var actions = Field<FlowLayoutPanel>(page, "exportActions");
            Assert.All(actions.Controls.OfType<Button>(), b => Assert.False(b.Enabled));
            var duplicateCalled = false;
            await StartExport(page, () => { duplicateCalled = true; return Task.FromResult(new RunExportResult(repository.B.RunId, [])); });
            Assert.False(duplicateCalled);
            runGrid.ClearSelection();
            runGrid.Rows[1].Selected = true;
            await WaitUntil(() => Field<RunHistoryDetails?>(page, "selectedDetails")?.Run.RunId == repository.B.RunId);
            Assert.Contains("B", Field<Label>(page, "exportScope").Text);
            Assert.All(actions.Controls.OfType<Button>(), b => Assert.False(b.Enabled));
            release.Set();
            await exporting;
            Assert.True(new FileInfo(destination).Length > 0);
            Assert.Contains("Exportação concluída", Field<Label>(page, "exportFeedback").Text);
            Assert.True(Field<Button>(page, "openExportFolder").Visible);
            Assert.True(Field<Button>(page, "openExportFolder").Enabled);
            Assert.All(actions.Controls.OfType<Button>().Where(b => b.Text != "Abrir pasta"), b => Assert.Equal(!runningSelection, b.Enabled));
        }
        finally
        {
            release.Set();
            if (exporting is not null) await exporting;
            Directory.Delete(directory, true);
        }
    });

    [Fact]
    public Task ExportFailureRestoresButtonsAndDoesNotOfferFolder() => RunUi(async (page, repository) =>
    {
        await page.RefreshAsync();
        await StartExport(page, () => Task.FromException<RunExportResult>(new UnauthorizedAccessException("Sem permissão para salvar.")));
        Assert.Contains("Sem permissão", Field<Label>(page, "exportFeedback").Text);
        Assert.False(Field<Button>(page, "openExportFolder").Visible);
        Assert.All(Field<FlowLayoutPanel>(page, "exportActions").Controls.OfType<Button>().Where(b => b.Text != "Abrir pasta"), b => Assert.True(b.Enabled));
    });

    [Fact]
    public Task ClosingHistoryWhileExportingDoesNotUpdateDisposedControls() => RunUi(async (page, repository) =>
    {
        await page.RefreshAsync();
        var started = Signal();
        var release = Signal();
        var exporting = StartExport(page, async () =>
        {
            started.TrySetResult();
            await release.Task;
            return new RunExportResult(repository.A.RunId, [Path.Combine(Path.GetTempPath(), "run.json")]);
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
        }
        finally { release.TrySetResult(); }
        await exporting;
    });

    [Fact]
    public Task HistorySeparatesPaginationAndExportsAtNarrowWidthsAndExplainsChartScope() => RunUi(async (page, repository) =>
    {
        var latency = new PersistedStageResult("Dns", StepStatus.Success, 5, "", null, null, null, null, null, null, null, null, null, null);
        repository.Cycles = (_, request) => Task.FromResult(new PagedResult<PersistedCycle>(
            [new(1, DateTimeOffset.Now, "", "", "", "", 0, [latency]), new(100, DateTimeOffset.Now, "", "", "", "", 0, [latency])], 250, request.PageNumber, request.PageSize));
        await page.RefreshAsync();
        var form = page.FindForm()!;
        var previous = Field<Button>(page, "previousRunPage");
        var next = Field<Button>(page, "nextRunPage");
        var count = Field<Label>(page, "runPageLabel");
        var scope = Field<Label>(page, "exportScope");
        var actions = Field<FlowLayoutPanel>(page, "exportActions");
        foreach (var size in new[] { new Size(650, 570), new Size(840, 600), new Size(1100, 850), new Size(650, 570) })
        {
            form.ClientSize = size;
            await Task.Delay(40);
            int Top(Control c) => c.PointToScreen(Point.Empty).Y;
            Assert.True(Math.Abs(Top(count) + count.Height / 2 - (Top(previous) + previous.Height / 2)) <= 2);
            Assert.True(Top(scope) > Top(next) + next.Height);
            Assert.True(Top(actions) >= Top(scope) + scope.Height);
            foreach (Control child in actions.Controls)
                if (child.Visible) Assert.True(child.Right <= actions.ClientSize.Width && child.Bottom <= actions.ClientSize.Height);
            var tabs = Field<TabControl>(page, "detailTabs");
            Assert.True(Top(tabs) >= Top(actions) + actions.Height);
            SaveSnapshot(form, $"history-actions-{size.Width}x{size.Height}");
            Assert.True(tabs.Height >= 90, $"Details height {tabs.Height} at {size}; actions {actions.Bounds}, scope {scope.Bounds}");
        }
        Field<TabControl>(page, "detailTabs").SelectedIndex = 2;
        await WaitUntil(() => Field<Label>(page, "chartRange").Text.Contains("250"));
        Assert.Contains("1 a 100 de 250", Field<Label>(page, "chartRange").Text);
        Assert.Contains("execução inteira", Field<Label>(page, "chartRange").Text);
        Assert.Contains("DNS", Field<ComboBox>(page, "chartStage").SelectedItem!.ToString());
        SaveSnapshot(form, "history-chart-scope");
        Field<TabControl>(page, "detailTabs").SelectedIndex = 0;
        await StartExport(page, () => Task.FromResult(new RunExportResult(repository.A.RunId, [Path.Combine(Path.GetTempPath(), $"run-{repository.A.RunId:D}.json")])));
        await Task.Delay(40);
        SaveSnapshot(form, "history-export-success-narrow");
        Assert.True(Field<TabControl>(page, "detailTabs").Height >= 120, $"Details: {Field<TabControl>(page, "detailTabs").Bounds}; feedback: {Field<Label>(page, "exportFeedback").Bounds}; actions: {actions.Parent!.Bounds}");
    });

    private static Task StartExport(HistoryPage page, Func<Task<RunExportResult>> action) =>
        (Task)typeof(HistoryPage).GetMethod("RunExportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [action])!;

    private static void SaveSnapshot(Form form, string name)
    {
        var output = Environment.GetEnvironmentVariable("DBCT_UI_SNAPSHOT_DIR");
        if (output is null) return;
        Directory.CreateDirectory(output);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(output, name + ".png"));
    }

    private static void ShowCycles(HistoryPage page)
    {
        var cycleTab = (TabPage)Field<DataGridView>(page, "cycles").Parent!.Parent!;
        ((TabControl)cycleTab.Parent!).SelectedTab = cycleTab;
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private static Task RunUi(Func<HistoryPage, Repository, Task> action)
    {
        var finished = Signal();
        var thread = new Thread(() =>
        {
            try
            {
                var repository = new Repository();
                using var page = new HistoryPage(repository, repository);
                using var form = new Form { Width = 1100, Height = 850 };
                form.Controls.Add(page);
                form.Shown += async (_, _) =>
                {
                    try { await action(page, repository); finished.TrySetResult(); }
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
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static T Field<T>(HistoryPage page, string name) =>
        (T)typeof(HistoryPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
    private static PagedResult<RunHistoryItem> Page(params RunHistoryItem[] items) => new(items, items.Length, 1, 25);

    private sealed class Repository : IRunHistoryRepository, IConnectionProfileRepository
    {
        public RunHistoryItem A { get; } = Item("A");
        public RunHistoryItem B { get; set; } = Item("B");
        public Func<RunHistoryFilter, PageRequest, Task<PagedResult<RunHistoryItem>>>? Search;
        public Func<Guid, Task<RunHistoryDetails?>>? Details;
        public Func<Guid, PageRequest, Task<PagedResult<PersistedCycle>>>? Cycles;

        public Task<PagedResult<RunHistoryItem>> SearchAsync(RunHistoryFilter filter, PageRequest page, CancellationToken token = default) =>
            Search?.Invoke(filter, page) ?? Task.FromResult(Page(A, B));
        public Task<RunHistoryDetails?> GetDetailsAsync(Guid id, CancellationToken token = default) =>
            Details?.Invoke(id) ?? Task.FromResult<RunHistoryDetails?>(new(id == A.RunId ? A : B, "{}", [], []));
        public Task<PagedResult<PersistedCycle>> GetCyclesAsync(Guid id, PageRequest page, CancellationToken token = default) =>
            Cycles?.Invoke(id, page) ?? Task.FromResult(new PagedResult<PersistedCycle>([], 0, page.PageNumber, page.PageSize));
        public Task<IReadOnlyList<SavedConnectionProfile>> ListAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<SavedConnectionProfile>>([]);
        public Task<SavedConnectionProfile?> GetAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<SavedConnectionProfile> SaveAsync(ConnectionProfileDraft draft, CancellationToken token = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        private static RunHistoryItem Item(string target) => new(Guid.NewGuid(), null, null, PersistedRunStatus.Completed,
            RunTerminationReason.PlannedCountCompleted, DateTimeOffset.Now, DateTimeOffset.Now, "test", "test",
            DatabaseType.MySqlMariaDb, target, 1, null);
    }
}
