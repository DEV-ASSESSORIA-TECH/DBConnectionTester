using DBConnectionTester.Models;
using DBConnectionTester.Services.Export;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.UI;

public sealed class HistoryPage : UserControl
{
    private const int RunsPageSize = 25;
    private const int CyclesPageSize = 100;
    private readonly IRunHistoryRepository history;
    private readonly IConnectionProfileRepository profilesRepository;
    private readonly RunExportService exporter;
    private readonly DateTimePicker from = new() { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false };
    private readonly DateTimePicker until = new() { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false };
    private readonly ComboBox profile = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox target = new();
    private readonly ComboBox status = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox diagnostic = new();
    private readonly Button search = new() { Text = "Pesquisar", AutoSize = true };
    private readonly DataGridView runs = new();
    private readonly DataGridView cycles = new();
    private readonly Label runPageLabel = new() { AutoSize = true };
    private readonly Label cyclePageLabel = new() { AutoSize = true };
    private readonly Button previousRunPage = new() { Text = "Anterior", AutoSize = true };
    private readonly Button nextRunPage = new() { Text = "Próxima", AutoSize = true };
    private readonly Button previousCyclePage = new() { Text = "Anterior", AutoSize = true };
    private readonly Button nextCyclePage = new() { Text = "Próxima", AutoSize = true };
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly LatencyTrendControl chart = new();
    private readonly TabControl detailTabs = new() { Dock = DockStyle.Fill };
    private bool cyclesLoaded;
    private bool cyclesLoading;
    private bool chartDirty = true;
    private readonly ComboBox chartStage = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly FlowLayoutPanel exportActions = new() { Dock = DockStyle.Fill, AutoSize = true };
    private RunHistoryDetails? selectedDetails;
    private IReadOnlyList<PersistedCycle> visibleCycles = [];
    private int runPage = 1;
    private long runPages;
    private int cyclePage = 1;
    private long cyclePages;
    private bool bindingRuns;
    private bool runsLoading;
    private readonly LatestUiRequest refreshRequests = new();
    private readonly LatestUiRequest runsRequests = new();
    private readonly LatestUiRequest detailsRequests = new();
    private readonly LatestUiRequest cyclesRequests = new();

    public HistoryPage(
        IRunHistoryRepository history,
        IConnectionProfileRepository profilesRepository)
    {
        this.history = history;
        this.profilesRepository = profilesRepository;
        exporter = new RunExportService(history);
        Dock = DockStyle.Fill;
        Padding = new Padding(16);
        ConfigureRunsGrid();
        ConfigureCyclesGrid();
        status.DataSource = new[] { new StatusChoice("Todos", null) }
            .Concat(Enum.GetValues<PersistedRunStatus>().Select(value => new StatusChoice(value.ToString(), value))).ToList();
        chartStage.DataSource = new[] { "Dns", "Ping", "Tcp", "DatabaseConnect", "DatabaseQuery" };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 235));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildFilters(), 0, 0);
        root.Controls.Add(runs, 0, 1);
        root.Controls.Add(BuildRunPager(), 0, 2);
        root.Controls.Add(BuildDetails(), 0, 3);
        Controls.Add(root);

        search.Click += async (_, _) => await HandleRunsLoadAsync(1);
        previousRunPage.Click += async (_, _) => await HandleRunsLoadAsync(Math.Max(1, runPage - 1));
        nextRunPage.Click += async (_, _) => await HandleRunsLoadAsync(runPage + 1);
        previousCyclePage.Click += async (_, _) => await HandleLoadAsync(() => LoadCyclesAsync(Math.Max(1, cyclePage - 1)));
        nextCyclePage.Click += async (_, _) => await HandleLoadAsync(() => LoadCyclesAsync(cyclePage + 1));
        runs.SelectionChanged += async (_, _) => await HandleLoadAsync(LoadSelectedRunAsync);
        detailTabs.SelectedIndexChanged += async (_, _) => await HandleLoadAsync(EnsureCyclesAsync);
        VisibleChanged += async (_, _) => await HandleLoadAsync(EnsureCyclesAsync);
        chartStage.SelectedIndexChanged += (_, _) => { chartDirty = true; UpdateChart(); };
        chart.VisibleChanged += (_, _) => UpdateChart();
        AddExportButton("CSV", RunExportFormat.Csv, "csv");
        AddExportButton("TXT", RunExportFormat.Text, "txt");
        AddExportButton("JSON", RunExportFormat.Json, "json");
        var zip = new Button { Text = "Exportar ZIP", AutoSize = true };
        zip.Click += async (_, _) => await ExportZipAsync();
        exportActions.Controls.Add(zip);
        ClearDetails();
    }

    public async Task RefreshAsync(CancellationToken token = default)
    {
        using var request = refreshRequests.Start(token);
        runsRequests.Cancel();
        InvalidateSelection();
        runsLoading = true;
        previousRunPage.Enabled = nextRunPage.Enabled = false;
        try
        {
            var profileItems = await Task.Run(() => profilesRepository.ListAsync(request.Token), request.Token);
            if (!request.IsCurrent) return;
            var selectedProfile = (profile.SelectedItem as ProfileChoice)?.Id;
            profile.DataSource = new[] { new ProfileChoice("Todos", null) }
                .Concat(profileItems.Select(item => new ProfileChoice(item.Name, item.ProfileId))).ToList();
            profile.SelectedItem = ((IEnumerable<ProfileChoice>)profile.DataSource)
                .FirstOrDefault(item => item.Id == selectedProfile) ?? profile.Items[0];
            await LoadRunsAsync(runPage, request.Token);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (ApplicationStoreException) when (!request.IsCurrent) { }
        finally
        {
            if (request.IsCurrent)
            {
                runsLoading = false;
                previousRunPage.Enabled = runPage > 1;
                nextRunPage.Enabled = runPage < runPages;
            }
        }
    }

    private Task HandleRunsLoadAsync(int page)
    {
        // An explicit search supersedes any startup/profile refresh still pending.
        refreshRequests.Cancel();
        return HandleLoadAsync(() => LoadRunsAsync(page));
    }

    private async Task HandleLoadAsync(Func<Task> load)
    {
        try { await load(); }
        catch (ApplicationStoreException exception)
        {
            if (!IsDisposed)
                MessageBox.Show(this, exception.Message, "Histórico", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void InvalidateSelection()
    {
        detailsRequests.Cancel();
        cyclesRequests.Cancel();
        ClearDetails();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refreshRequests.Dispose(); runsRequests.Dispose();
            detailsRequests.Dispose(); cyclesRequests.Dispose();
        }
        base.Dispose(disposing);
    }

    private Control BuildFilters()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        panel.Controls.AddRange(new Control[]
        {
            LabelFor("De"), from, LabelFor("Até"), until, LabelFor("Perfil"), profile,
            LabelFor("Destino"), target, LabelFor("Estado"), status,
            LabelFor("Diagnóstico"), diagnostic, search
        });
        return panel;
    }

    private Control BuildRunPager()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        panel.Controls.Add(previousRunPage);
        panel.Controls.Add(nextRunPage);
        panel.Controls.Add(runPageLabel);
        panel.Controls.Add(exportActions);
        return panel;
    }

    private Control BuildDetails()
    {
        var tabs = detailTabs;
        var overview = new TabPage("Detalhes e diagnósticos");
        overview.Controls.Add(details);
        var cycleTab = new TabPage("Ciclos");
        var cycleLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        cycleLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cycleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cycleLayout.Controls.Add(cycles);
        var pager = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        pager.Controls.AddRange(new Control[] { previousCyclePage, nextCyclePage, cyclePageLabel });
        cycleLayout.Controls.Add(pager, 0, 1);
        cycleTab.Controls.Add(cycleLayout);
        var graphTab = new TabPage("Gráfico");
        var graphLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        graphLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        graphLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var selector = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        selector.Controls.Add(LabelFor("Etapa")); selector.Controls.Add(chartStage);
        graphLayout.Controls.Add(selector); graphLayout.Controls.Add(chart, 0, 1);
        graphTab.Controls.Add(graphLayout);
        tabs.TabPages.AddRange(new[] { overview, cycleTab, graphTab });
        return tabs;
    }

    private async Task LoadRunsAsync(int requestedPage, CancellationToken token = default)
    {
        using var request = runsRequests.Start(token);
        InvalidateSelection();
        runsLoading = true;
        previousRunPage.Enabled = nextRunPage.Enabled = false;
        var filter = new RunHistoryFilter(
            from.Checked ? new DateTimeOffset(from.Value.Date).ToUniversalTime() : null,
            until.Checked ? new DateTimeOffset(until.Value.Date.AddDays(1).AddTicks(-1)).ToUniversalTime() : null,
            (profile.SelectedItem as ProfileChoice)?.Id, target.Text,
            (status.SelectedItem as StatusChoice)?.Value, diagnostic.Text);
        try
        {
            var result = await Task.Run(() => history.SearchAsync(filter, new PageRequest(requestedPage, RunsPageSize), request.Token), request.Token);
            if (!request.IsCurrent) return;
            // A deletion or a narrower filter can invalidate the current page.
            var lastPage = (int)Math.Max(1, result.TotalPages);
            if (requestedPage > lastPage)
            {
                await LoadRunsAsync(lastPage, token);
                return;
            }
            runPage = requestedPage;
            runPages = result.TotalPages;
            bindingRuns = true;
            runs.SuspendLayout();
            try
            {
                runs.Rows.Clear();
                foreach (var item in result.Items)
                    runs.Rows.Add(item.RunId, item.StartedAt.ToLocalTime().ToString("g"), item.ProfileName ?? "—", item.DatabaseType,
                        item.Target, item.Status, item.CompletedCycles, item.FailureMessage ?? "");
                if (runs.Rows.Count > 0) runs.Rows[0].Selected = true;
            }
            finally { runs.ResumeLayout(); bindingRuns = false; }
            runPageLabel.Text = $"Página {runPage} de {Math.Max(1, runPages)} · {result.TotalItems:N0} execuções";
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (ApplicationStoreException) when (!request.IsCurrent) { }
        finally
        {
            if (request.IsCurrent)
            {
                runsLoading = false;
                previousRunPage.Enabled = runPage > 1;
                nextRunPage.Enabled = runPage < runPages;
            }
        }
        if (request.IsCurrent) await LoadSelectedRunAsync();
    }

    private async Task LoadSelectedRunAsync()
    {
        if (bindingRuns || runsLoading || IsDisposed) return;
        InvalidateSelection();
        if (runs.SelectedRows.Count == 0 || runs.SelectedRows[0].Cells[0].Value is not Guid runId) return;
        using var request = detailsRequests.Start();
        try
        {
            var result = await Task.Run(() => history.GetDetailsAsync(runId, request.Token), request.Token);
            if (!request.IsCurrent) return;
            selectedDetails = result;
            RenderDetails();
            await EnsureCyclesAsync();
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (ApplicationStoreException) when (!request.IsCurrent) { }
    }

    private Task EnsureCyclesAsync()
    {
        if (!Visible || detailTabs.SelectedIndex == 0 || selectedDetails is null || cyclesLoaded || cyclesLoading)
            return Task.CompletedTask;
        return LoadCyclesAsync(cyclePage);
    }

    private async Task LoadCyclesAsync(int requestedPage)
    {
        if (selectedDetails is null || IsDisposed) return;
        var runId = selectedDetails.Run.RunId;
        using var request = cyclesRequests.Start();
        cyclesLoading = true;
        previousCyclePage.Enabled = nextCyclePage.Enabled = false;
        try
        {
            var result = await Task.Run(() => history.GetCyclesAsync(runId, new PageRequest(requestedPage, CyclesPageSize), request.Token), request.Token);
            if (!request.IsCurrent || selectedDetails?.Run.RunId != runId) return;
            cyclesLoaded = true;
            chartDirty = true;
            cyclePage = requestedPage;
            visibleCycles = result.Items;
            cyclePages = result.TotalPages;
            cycles.Rows.Clear();
            foreach (var cycle in result.Items)
            {
                var stages = cycle.Stages.ToDictionary(item => item.Stage);
                string Cell(string stage) => stages.TryGetValue(stage, out var value)
                    ? $"{value.Status.ToOutputText()} · {value.ElapsedMs} ms" : "N/A";
                var firstDiagnostic = cycle.Stages.FirstOrDefault(item => item.DiagnosticCode is not null);
                var rowIndex = cycles.Rows.Add(cycle.Number, cycle.StartedAt.ToLocalTime().ToString("HH:mm:ss.fff"), Cell("Dns"), Cell("Ping"), Cell("Tcp"),
                    Cell("DatabaseConnect"), Cell("DatabaseQuery"), firstDiagnostic?.DiagnosticCode ?? "");
                if (firstDiagnostic is not null)
                    cycles.Rows[rowIndex].Cells[7].ToolTipText = string.Join(Environment.NewLine,
                        cycle.Stages.Where(item => item.DiagnosticCode is not null)
                            .Select(item => $"{item.Stage} [{item.DiagnosticCode}] {item.UserMessage}\n{item.TechnicalMessage}"));
            }
            cyclePageLabel.Text = $"Página {cyclePage} de {Math.Max(1, cyclePages)} · {result.TotalItems:N0} ciclos";
            UpdateChart();
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (ApplicationStoreException) when (!request.IsCurrent) { }
        finally
        {
            if (request.IsCurrent)
            {
                cyclesLoading = false;
                previousCyclePage.Enabled = cyclePage > 1;
                nextCyclePage.Enabled = cyclePage < cyclePages;
            }
        }
    }

    private void RenderDetails()
    {
        if (selectedDetails is null) { details.Clear(); return; }
        var run = selectedDetails.Run;
        var lines = new List<string>
        {
            $"RunId: {run.RunId:D}", $"Estado: {run.Status} · {run.TerminationReason}",
            $"Início: {run.StartedAt.ToLocalTime():F}", $"Fim: {(run.FinishedAt?.ToLocalTime().ToString("F") ?? "N/A")}",
            $"Destino: {run.DatabaseType} · {run.Target}", $"Máquina: {run.MachineName} · Versão: {run.ApplicationVersion}",
            $"Ciclos persistidos: {run.CompletedCycles:N0}"
        };
        if (!string.IsNullOrWhiteSpace(run.FailureMessage)) lines.Add("Falha: " + run.FailureMessage);
        lines.Add(""); lines.Add("Resumo por etapa:");
        lines.AddRange(selectedDetails.StageSummaries.Select(item =>
            $"  {item.Stage}: {item.Statistics.Successes:N0}/{item.Statistics.Attempts:N0} OK · " +
            $"falhas {item.Statistics.Failures:N0} · média {FormatMs(item.Statistics.AverageMs)} · P95 {FormatMs(item.Statistics.P95Ms)}"));
        if (selectedDetails.Warnings.Count > 0)
        {
            lines.Add(""); lines.Add("Avisos:");
            lines.AddRange(selectedDetails.Warnings.Select(item => $"  [{item.Code}] {item.Message}"));
        }
        details.Lines = lines.ToArray();
        foreach (Control control in exportActions.Controls)
            control.Enabled = run.Status != PersistedRunStatus.Running;
    }

    private void UpdateChart()
    {
        if (!chart.Visible || !chartDirty) return;
        chartDirty = false;
        var stageName = chartStage.SelectedItem as string ?? "DatabaseConnect";
        var points = visibleCycles.Select(cycle =>
        {
            var stage = cycle.Stages.FirstOrDefault(item => item.Stage == stageName);
            return new TrendPoint(cycle.Number, stage?.Status ?? StepStatus.Skipped, stage?.ElapsedMs ?? 0);
        }).ToArray();
        var summary = selectedDetails?.StageSummaries.FirstOrDefault(item => item.Stage == stageName)?.Statistics;
        chart.SetData(points, summary?.MedianMs, summary?.P95Ms);
    }

    private void AddExportButton(string label, RunExportFormat format, string extension)
    {
        var button = new Button { Text = "Exportar " + label, AutoSize = true };
        button.Click += async (_, _) => await ExportAsync(format, extension);
        exportActions.Controls.Add(button);
    }

    private async Task ExportAsync(RunExportFormat format, string extension)
    {
        if (selectedDetails is null) return;
        var runId = selectedDetails.Run.RunId;
        using var dialog = new SaveFileDialog { Filter = $"{extension.ToUpperInvariant()} (*.{extension})|*.{extension}", FileName = $"run-{runId:D}.{extension}" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var destination = dialog.FileName;
        await RunExportAsync(() => exporter.ExportAsync(runId, format, destination));
    }

    private async Task ExportZipAsync()
    {
        if (selectedDetails is null) return;
        var runId = selectedDetails.Run.RunId;
        using var dialog = new SaveFileDialog { Filter = "ZIP (*.zip)|*.zip", FileName = $"run-{runId:D}.zip" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var destination = dialog.FileName;
        await RunExportAsync(() => exporter.ExportZipAsync(runId, destination));
    }

    private async Task RunExportAsync(Func<Task<RunExportResult>> action)
    {
        try
        {
            var result = await Task.Run(action);
            if (!IsDisposed)
                MessageBox.Show(this, $"Exportação concluída:\n{result.Files[0]}", "Histórico", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) when (exception is IOException or ApplicationStoreException or KeyNotFoundException)
        {
            if (!IsDisposed)
                MessageBox.Show(this, exception.Message, "Exportação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ClearDetails()
    {
        selectedDetails = null; visibleCycles = []; details.Clear(); cycles.Rows.Clear(); chart.SetData([], null, null);
        cyclesLoaded = cyclesLoading = false; chartDirty = true;
        cyclePage = 1; cyclePages = 0; cyclePageLabel.Text = "";
        previousCyclePage.Enabled = nextCyclePage.Enabled = false;
        foreach (Control control in exportActions.Controls) control.Enabled = false;
    }

    private void ConfigureRunsGrid()
    {
        ConfigureGrid(runs);
        runs.Columns.Add(new DataGridViewTextBoxColumn { Name = "RunId", Visible = false });
        runs.Columns.Add("StartedAt", "Início"); runs.Columns.Add("Profile", "Perfil"); runs.Columns.Add("Type", "Tipo");
        runs.Columns.Add("Target", "Destino"); runs.Columns.Add("Status", "Estado"); runs.Columns.Add("Cycles", "Ciclos"); runs.Columns.Add("Failure", "Falha");
    }

    private void ConfigureCyclesGrid()
    {
        ConfigureGrid(cycles);
        foreach (var (name, title) in new[] { ("Cycle", "Ciclo"), ("Time", "Horário"), ("Dns", "DNS"), ("Ping", "Ping"),
                     ("Tcp", "TCP"), ("Connect", "Conexão DB"), ("Query", "SELECT 1"), ("Diagnostic", "Diagnóstico") })
            cycles.Columns.Add(name, title);
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false; grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.EnableHeadersVisualStyles = false;
    }

    private static Label LabelFor(string text) => new() { Text = text + ":", AutoSize = true, Margin = new Padding(8, 7, 2, 0) };
    private static string FormatMs(double? value) => value is null ? "N/A" : $"{value:0.0} ms";
    private sealed record StatusChoice(string Text, PersistedRunStatus? Value) { public override string ToString() => Text; }
    private sealed record ProfileChoice(string Text, Guid? Id) { public override string ToString() => Text; }
}
